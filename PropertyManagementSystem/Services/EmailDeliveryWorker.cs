using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services;

public sealed class EmailDeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<EmailDeliveryWorker> _logger;
    public EmailDeliveryWorker(IServiceScopeFactory scopes, ILogger<EmailDeliveryWorker> logger)
    {
        _scopes = scopes; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = 30;
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                var settings = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<SmtpSettings>>().CurrentValue;
                interval = settings.PollIntervalSeconds;
                if (settings.DeliveryMode != EmailDeliveryMode.Disabled)
                {
                    var context = scope.ServiceProvider.GetRequiredService<DAL>();
                    await scope.ServiceProvider.GetRequiredService<FileEmailOutbox>().ProcessDueAsync(settings,
                        scope.ServiceProvider.GetRequiredService<IEmailTransport>(), DateTime.UtcNow,
                        async (entry, token) =>
                        {
                            var current = await context.ReservationHeaders.AsNoTracking().Where(header => header.ReservationId == entry.ReservationId)
                                .Select(header => new { header.Status, header.DateSubmitted }).SingleOrDefaultAsync(token);
                            return current != null && current.Status == entry.RequiredStatus &&
                                (!entry.SubmittedAt.HasValue || (current.DateSubmitted.HasValue &&
                                 Math.Abs((current.DateSubmitted.Value - entry.SubmittedAt.Value).TotalSeconds) < 1));
                        }, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "Email outbox processing failed. Pending messages are retained for retry."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(interval, 5, 3600)), stoppingToken);
        }
    }
}
