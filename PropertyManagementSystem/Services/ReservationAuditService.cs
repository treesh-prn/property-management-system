using System.Security.Claims;
using System.Text.Json;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public interface IReservationAuditService
    {
        Task AddAsync(
            GFMS_ReservationHeader reservation,
            ClaimsPrincipal user,
            string action,
            string? previousStatus,
            string? newStatus,
            string? details = null);
    }

    public sealed class ReservationAuditService : IReservationAuditService
    {
        private static readonly SemaphoreSlim WriteLock = new(1, 1);
        private readonly IReservationAuthorizationService _authorization;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ReservationAuditService> _logger;

        public ReservationAuditService(
            IReservationAuthorizationService authorization,
            IWebHostEnvironment environment,
            ILogger<ReservationAuditService> logger)
        {
            _authorization = authorization;
            _environment = environment;
            _logger = logger;
        }

        public async Task AddAsync(
            GFMS_ReservationHeader reservation,
            ClaimsPrincipal user,
            string action,
            string? previousStatus,
            string? newStatus,
            string? details = null)
        {
            var actor = await _authorization.GetActorAsync(user);
            var entry = new
            {
                reservationId = reservation.ReservationId,
                reservationType = reservation.ReservationType,
                action,
                actorUserId = actor.UserId,
                actorEmail = actor.Email,
                activeRole = actor.ActiveRole,
                previousStatus,
                newStatus,
                details,
                occurredUtc = DateTime.UtcNow
            };

            var directory = Path.Combine(_environment.ContentRootPath, "App_Data", "ReservationAudit");
            var path = Path.Combine(directory, $"audit-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            var lockTaken = false;

            try
            {
                await WriteLock.WaitAsync();
                lockTaken = true;
                Directory.CreateDirectory(directory);
                await File.AppendAllTextAsync(path, line);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to persist reservation audit {Action} for {ReservationId}.",
                    action,
                    reservation.ReservationId);
            }
            finally
            {
                if (lockTaken) WriteLock.Release();
            }

            _logger.LogInformation(
                "Reservation audit {Action} on {ReservationId} by {ActorUserId} as {ActiveRole}: {PreviousStatus} -> {NewStatus}",
                action,
                reservation.ReservationId,
                actor.UserId,
                actor.ActiveRole,
                previousStatus,
                newStatus);
        }
    }
}
