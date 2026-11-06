using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class EmailOutboxTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Mime_bytes_preserve_non_utf8_characters_and_sent_receipts_remove_content()
    {
        using var environment = new EmailTestEnvironment();
        var message = Message();
        var body = new TextPart("plain");
        body.SetText(System.Text.Encoding.GetEncoding("iso-8859-1"), "José - Niño");
        message.Body = body;
        var queue = Queue(environment);
        await queue.QueueAsync(message, EmailDeliveryMode.Test, "unicode", Now);
        Assert.NotEmpty(Read(environment, "Pending").MessageBase64!);
        var transport = new FakeTransport();
        Assert.Equal(1, await queue.ProcessDueAsync(EmailTestSupport.Settings(), transport, Now));
        Assert.Equal("José - Niño", transport.Bodies.Single()!.TrimEnd('\r', '\n'));
        Assert.Null(Read(environment, "Sent").MessageBase64);
        Assert.Empty(Read(environment, "Sent").Message);
    }

    [Fact]
    public async Task Legacy_text_queue_entries_remain_deliverable()
    {
        using var environment = new EmailTestEnvironment();
        var queue = Queue(environment);
        await queue.QueueAsync(Message(), EmailDeliveryMode.Test, "legacy", Now);
        var path = Directory.GetFiles(Path.Combine(environment.ContentRootPath, "outbox", "Pending"), "*.json").Single();
        var entry = Read(environment, "Pending");
        entry.Message = Message().ToString();
        entry.MessageBase64 = null;
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(entry));
        Assert.Equal(1, await queue.ProcessDueAsync(EmailTestSupport.Settings(), new FakeTransport(), Now));
    }

    [Fact]
    public async Task Shutdown_after_smtp_acceptance_still_records_delivery()
    {
        using var environment = new EmailTestEnvironment();
        using var cancellation = new CancellationTokenSource();
        var queue = Queue(environment);
        var transport = new FakeTransport { AfterAccepted = cancellation.Cancel };
        await queue.QueueAsync(Message(), EmailDeliveryMode.Test, "shutdown-after-acceptance", Now);
        Assert.Equal(1, await queue.ProcessDueAsync(EmailTestSupport.Settings(), transport, Now, cancellationToken: cancellation.Token));
        Assert.NotNull(Read(environment, "Sent").DeliveredUtc);
        Assert.Single(transport.MessageIds);
    }

    [Fact]
    public async Task Failed_delivery_survives_restart_and_retries_without_changing_message_id()
    {
        using var environment = new EmailTestEnvironment();
        var queue = Queue(environment);
        var message = Message();
        var transport = new FakeTransport { FailuresRemaining = 1 };
        await queue.QueueAsync(message, EmailDeliveryMode.Test, "event-1", Now);
        Assert.Equal(0, await queue.ProcessDueAsync(EmailTestSupport.Settings(), transport, Now));
        Assert.Equal(1, Read(environment, "Pending").Attempts);
        Assert.Equal(0, await Queue(environment).ProcessDueAsync(EmailTestSupport.Settings(), transport, Now.AddSeconds(59)));
        Assert.Equal(1, await Queue(environment).ProcessDueAsync(EmailTestSupport.Settings(), transport, Now.AddSeconds(61)));
        Assert.Equal(2, transport.MessageIds.Count);
        Assert.All(transport.MessageIds, id => Assert.Equal(message.MessageId, id));
        var receipt = Read(environment, "Sent");
        Assert.Equal(2, receipt.Attempts);
        Assert.NotNull(receipt.DeliveredUtc);
        Assert.Empty(receipt.Message);
    }

    [Fact]
    public async Task Approval_notice_waits_until_due_then_is_sent_once()
    {
        using var environment = new EmailTestEnvironment();
        var queue = Queue(environment);
        var transport = new FakeTransport();
        await queue.QueueAsync(Message(), EmailDeliveryMode.Test, "delayed-approval", Now.AddDays(1));
        Assert.Equal(0, await queue.ProcessDueAsync(EmailTestSupport.Settings(), transport, Now));
        Assert.Empty(transport.MessageIds);
        Assert.Equal(1, await queue.ProcessDueAsync(EmailTestSupport.Settings(), transport, Now.AddDays(1)));
        await queue.QueueAsync(Message(), EmailDeliveryMode.Test, "delayed-approval", Now);
        Assert.Equal(0, await queue.ProcessDueAsync(EmailTestSupport.Settings(), transport, Now.AddDays(2)));
        Assert.Single(transport.MessageIds);
    }

    [Fact]
    public async Task Superseded_approval_stage_is_not_emailed()
    {
        using var environment = new EmailTestEnvironment();
        var queue = Queue(environment);
        var transport = new FakeTransport();
        await queue.QueueAsync(Message(), EmailDeliveryMode.Test, "old-stage", Now,
            new EmailQueueContext(42, ReservationStatuses.DepartmentHeadApproval));
        Assert.Equal(0, await queue.ProcessDueAsync(EmailTestSupport.Settings(), transport, Now, (_, _) => Task.FromResult(false)));
        Assert.Empty(transport.MessageIds);
        Assert.NotNull(Read(environment, "Superseded"));
    }

    [Fact]
    public async Task Switching_to_production_does_not_deliver_old_test_queue()
    {
        using var environment = new EmailTestEnvironment();
        var queue = Queue(environment);
        var transport = new FakeTransport();
        await queue.QueueAsync(Message(), EmailDeliveryMode.Test, "test-only", Now);
        Assert.Equal(0, await queue.ProcessDueAsync(EmailTestSupport.Settings(EmailDeliveryMode.Production), transport, Now));
        Assert.Empty(transport.MessageIds);
        Assert.Equal(EmailDeliveryMode.Test, Read(environment, "Held").Mode);
    }

    [Fact]
    public async Task Retries_stop_at_limit_and_failed_payload_is_kept_for_review()
    {
        using var environment = new EmailTestEnvironment();
        var queue = Queue(environment);
        var settings = EmailTestSupport.Settings();
        settings.MaxAttempts = 2;
        var transport = new FakeTransport { FailuresRemaining = 10 };
        await queue.QueueAsync(Message(), EmailDeliveryMode.Test, "permanent-failure", Now);
        await queue.ProcessDueAsync(settings, transport, Now);
        await queue.ProcessDueAsync(settings, transport, Now.AddMinutes(2));
        var failed = Read(environment, "Failed");
        Assert.Equal(2, failed.Attempts);
        Assert.Equal(nameof(IOException), failed.LastError);
        Assert.NotEmpty(failed.MessageBase64!);
        await queue.ProcessDueAsync(settings, transport, Now.AddDays(1));
        Assert.Equal(2, transport.MessageIds.Count);
    }

    [Fact]
    public async Task Malformed_entry_does_not_block_other_messages()
    {
        using var environment = new EmailTestEnvironment();
        var queue = Queue(environment);
        await queue.QueueAsync(Message(), EmailDeliveryMode.Test, "valid-message", Now);
        await File.WriteAllTextAsync(Path.Combine(environment.ContentRootPath, "outbox", "Pending", "bad.json"), "{");
        var transport = new FakeTransport();
        Assert.Equal(1, await queue.ProcessDueAsync(EmailTestSupport.Settings(), transport, Now));
        Assert.Single(Directory.GetFiles(Path.Combine(environment.ContentRootPath, "outbox", "Failed"), "*.json"));
    }

    [Fact]
    public async Task Concurrent_workers_do_not_send_one_entry_twice()
    {
        using var environment = new EmailTestEnvironment();
        var queue = Queue(environment);
        var transport = new FakeTransport { DelayMilliseconds = 50 };
        await queue.QueueAsync(Message(), EmailDeliveryMode.Test, "concurrent", Now);
        var results = await Task.WhenAll(queue.ProcessDueAsync(EmailTestSupport.Settings(), transport, Now),
            Queue(environment).ProcessDueAsync(EmailTestSupport.Settings(), transport, Now));
        Assert.Equal(1, results.Sum());
        Assert.Single(transport.MessageIds);
    }

    [Fact]
    public void Outbox_cannot_be_placed_under_public_webroot()
    {
        using var environment = new EmailTestEnvironment();
        Assert.Throws<InvalidOperationException>(() => new FileEmailOutbox(environment,
            EmailTestSupport.Configuration(path: Path.Combine("wwwroot", "emails")), NullLogger<FileEmailOutbox>.Instance));
    }

    private static FileEmailOutbox Queue(EmailTestEnvironment environment) =>
        new(environment, EmailTestSupport.Configuration(), NullLogger<FileEmailOutbox>.Instance);
    private static MimeMessage Message() => EmailTestSupport.Service(new RecordingEmailOutbox()).CreateMessage(EmailTestSupport.Notification());
    private static QueuedEmail Read(EmailTestEnvironment environment, string folder) =>
        JsonSerializer.Deserialize<QueuedEmail>(File.ReadAllText(Directory.GetFiles(Path.Combine(environment.ContentRootPath, "outbox", folder), "*.json").Single()))!;

    private sealed class FakeTransport : IEmailTransport
    {
        internal int FailuresRemaining { get; set; }
        internal int DelayMilliseconds { get; set; }
        internal Action? AfterAccepted { get; set; }
        internal List<string> MessageIds { get; } = new();
        internal List<string?> Bodies { get; } = new();
        public async Task SendAsync(MimeMessage message, SmtpSettings settings, CancellationToken cancellationToken = default)
        {
            MessageIds.Add(message.MessageId);
            Bodies.Add(message.TextBody);
            if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds, cancellationToken);
            if (FailuresRemaining-- > 0) throw new IOException("Simulated SMTP failure; no network connection was made.");
            AfterAccepted?.Invoke();
        }
    }
}
