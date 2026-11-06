using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MimeKit;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services;

public sealed record EmailQueueContext(int ReservationId, string? RequiredStatus = null, DateTime? SubmittedAt = null);
public sealed class QueuedEmail
{
    public string Id { get; set; } = string.Empty;
    public EmailDeliveryMode Mode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? MessageBase64 { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public DateTime NextAttemptUtc { get; set; }
    public DateTime? DeliveredUtc { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public EmailQueueContext? Context { get; set; }
}

public interface IEmailOutbox
{
    Task QueueAsync(MimeMessage message, EmailDeliveryMode mode, string id, DateTime notBeforeUtc, EmailQueueContext? context = null);
}

public sealed class FileEmailOutbox : IEmailOutbox
{
    private readonly string _root;
    private readonly ILogger<FileEmailOutbox> _logger;
    public FileEmailOutbox(IWebHostEnvironment environment, IConfiguration configuration, ILogger<FileEmailOutbox> logger)
    {
        var configuredPath = configuration["SmtpSettings:OutboxPath"] ?? "App_Data/EmailOutbox";
        _root = Path.GetFullPath(Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(environment.ContentRootPath, configuredPath));
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        var relative = Path.GetRelativePath(webRoot, _root);
        if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar)))
            throw new InvalidOperationException("The email outbox must be outside wwwroot.");
        _logger = logger;
    }

    public async Task QueueAsync(MimeMessage message, EmailDeliveryMode mode, string id, DateTime notBeforeUtc, EmailQueueContext? context = null)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        if (new[] { "Pending", "Sent", "Failed", "Held", "Superseded" }.Any(folder => File.Exists(Path.Combine(_root, folder, hash + ".json")))) return;
        var folderPath = Path.Combine(_root, "Pending");
        Directory.CreateDirectory(folderPath);
        await using var serialized = new MemoryStream();
        await message.WriteToAsync(serialized);
        var entry = new QueuedEmail
        {
            Id = hash, Mode = mode, MessageBase64 = Convert.ToBase64String(serialized.ToArray()), Recipient = message.To.Mailboxes.Single().Address,
            NextAttemptUtc = notBeforeUtc, Context = context
        };
        var destination = Path.Combine(folderPath, hash + ".json");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entry));
        try { File.Move(temporary, destination); }
        catch (IOException) when (File.Exists(destination)) { File.Delete(temporary); }
    }

    public async Task<int> ProcessDueAsync(SmtpSettings settings, IEmailTransport transport, DateTime nowUtc,
        Func<EmailQueueContext, CancellationToken, Task<bool>>? isCurrent = null, CancellationToken cancellationToken = default)
    {
        if (settings.DeliveryMode == EmailDeliveryMode.Disabled) return 0;
        var pending = Path.Combine(_root, "Pending");
        if (!Directory.Exists(pending)) return 0;
        var delivered = 0;
        var attempted = 0;
        foreach (var path in Directory.EnumerateFiles(pending, "*.json").OrderBy(File.GetLastWriteTimeUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream lease;
            try { lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { continue; }
            await using (lease)
            {
                if (!File.Exists(path)) continue;
                QueuedEmail entry;
                try
                {
                    entry = JsonSerializer.Deserialize<QueuedEmail>(await File.ReadAllTextAsync(path, cancellationToken))
                        ?? throw new JsonException("An email outbox entry is empty.");
                }
                catch (JsonException exception)
                {
                    Move(path, "Failed");
                    _logger.LogError(exception, "A malformed outbox entry was quarantined: {FileName}.", Path.GetFileName(path));
                    continue;
                }
                if (entry.DeliveredUtc.HasValue) { Move(path, "Sent"); continue; }
                if (entry.Mode != settings.DeliveryMode)
                {
                    Move(path, "Held");
                    _logger.LogWarning("Email {EmailId} held because its test/production mode differs from the current configuration.", entry.Id);
                    continue;
                }
                if (entry.NextAttemptUtc > nowUtc) continue;
                if (entry.Context?.RequiredStatus != null && isCurrent != null && !await isCurrent(entry.Context, cancellationToken))
                {
                    Move(path, "Superseded");
                    continue;
                }
                var accepted = false;
                attempted++;
                try
                {
                    // MIME declares its own charset. Preserve its original bytes rather than re-encoding raw text.
                    // Keep reading legacy queue entries created before byte-preserving serialization was added.
                    await using var bytes = new MemoryStream(entry.MessageBase64 != null
                        ? Convert.FromBase64String(entry.MessageBase64) : Encoding.UTF8.GetBytes(entry.Message));
                    var message = await MimeMessage.LoadAsync(bytes, cancellationToken);
                    await transport.SendAsync(message, settings, cancellationToken);
                    accepted = true;
                    entry.Attempts++;
                    entry.DeliveredUtc = nowUtc;
                    entry.LastError = null;
                    entry.Message = string.Empty;
                    entry.MessageBase64 = null;
                    // Persist SMTP acceptance even if the host starts shutting down now.
                    await SaveAsync(path, entry, CancellationToken.None);
                    Move(path, "Sent");
                    delivered++;
                    _logger.LogInformation("Email {EmailId} accepted by SMTP after {Attempts} attempt(s).", entry.Id, entry.Attempts);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) when (accepted)
                {
                    Move(path, "Held");
                    _logger.LogCritical(exception, "SMTP accepted email {EmailId}, but its receipt could not be saved. The entry is held for operator review to avoid an automatic duplicate.", entry.Id);
                }
                catch (Exception exception)
                {
                    entry.Attempts++;
                    entry.LastError = exception.GetType().Name;
                    entry.NextAttemptUtc = nowUtc.AddSeconds(Math.Min(settings.RetryDelaySeconds * Math.Pow(2, entry.Attempts - 1), 3600));
                    await SaveAsync(path, entry, cancellationToken);
                    if (entry.Attempts >= settings.MaxAttempts) Move(path, "Failed");
                    _logger.LogError(exception, "Email {EmailId} attempt {Attempts} failed. State: {State}.",
                        entry.Id, entry.Attempts, entry.Attempts >= settings.MaxAttempts ? "Failed" : "Retry scheduled");
                }
            }
            if (attempted >= 25) break;
        }
        return delivered;
    }

    private static async Task SaveAsync(string path, QueuedEmail entry, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(entry), cancellationToken);
        File.Move(temporary, path, true);
    }
    private void Move(string source, string folder)
    {
        var destination = Path.Combine(_root, folder);
        Directory.CreateDirectory(destination);
        File.Move(source, Path.Combine(destination, Path.GetFileName(source)), true);
    }
}
