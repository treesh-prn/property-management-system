using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public sealed record ReservationScheduleTransition(string NewStatus, string Message);

    public interface IReservationScheduleStatusService
    {
        Task<ReservationScheduleTransition?> RefreshAsync(
            GFMS_ReservationHeader reservation,
            DateTime asOf,
            CancellationToken cancellationToken = default);

        Task<int> RefreshAllAsync(
            DateTime asOf,
            CancellationToken cancellationToken = default);
    }

    public sealed class ReservationScheduleStatusService : IReservationScheduleStatusService
    {
        private static readonly string[] PendingStatuses =
        {
            ReservationStatuses.DepartmentHeadApproval,
            ReservationStatuses.AdminApproverApproval,
            ReservationStatuses.AdminManagerApproval
        };

        private readonly DAL _context;
        private readonly ILogger<ReservationScheduleStatusService> _logger;
        private readonly EmailService _emailService;
        private readonly IReservationAuthorizationService _authorization;

        public ReservationScheduleStatusService(
            DAL context,
            ILogger<ReservationScheduleStatusService> logger,
            EmailService emailService,
            IReservationAuthorizationService authorization)
        {
            _context = context;
            _logger = logger;
            _emailService = emailService;
            _authorization = authorization;
        }

        public async Task<ReservationScheduleTransition?> RefreshAsync(
            GFMS_ReservationHeader reservation,
            DateTime asOf,
            CancellationToken cancellationToken = default)
        {
            if (!PendingStatuses.Contains(reservation.Status, StringComparer.Ordinal))
                return null;

            var schedules = await _context.ReservationDetails
                .Where(detail => detail.ReservationId == reservation.ReservationId)
                .Select(detail => new { detail.StartDate, detail.EndDate })
                .ToListAsync(cancellationToken);

            var transition = Evaluate(
                reservation.ReservationType,
                schedules.Select(schedule => schedule.StartDate),
                schedules.Select(schedule => schedule.EndDate),
                asOf);
            if (transition == null)
            {
                if (await NormalizeApprovalRouteAsync(reservation, cancellationToken))
                {
                    await _context.SaveChangesAsync(cancellationToken);
                }

                return null;
            }

            ApplyTransition(reservation, transition);
            await _context.SaveChangesAsync(cancellationToken);
            LogTransition(reservation, transition);
            await SendNotificationSafelyAsync(reservation, transition.Message);
            return transition;
        }

        public async Task<int> RefreshAllAsync(
            DateTime asOf,
            CancellationToken cancellationToken = default)
        {
            var reservations = await _context.ReservationHeaders
                .Where(header => PendingStatuses.Contains(header.Status!))
                .ToListAsync(cancellationToken);
            if (reservations.Count == 0) return 0;

            var reservationIds = reservations.Select(header => header.ReservationId).ToList();
            var schedules = await _context.ReservationDetails
                .Where(detail => reservationIds.Contains(detail.ReservationId))
                .Select(detail => new { detail.ReservationId, detail.StartDate, detail.EndDate })
                .ToListAsync(cancellationToken);
            var schedulesByReservation = schedules
                .GroupBy(schedule => schedule.ReservationId)
                .ToDictionary(group => group.Key, group => group.ToList());

            var changed = new List<(GFMS_ReservationHeader Reservation, ReservationScheduleTransition Transition)>();
            var rerouted = new List<GFMS_ReservationHeader>();
            foreach (var reservation in reservations)
            {
                schedulesByReservation.TryGetValue(reservation.ReservationId, out var reservationSchedules);

                var transition = Evaluate(
                    reservation.ReservationType,
                    reservationSchedules?.Select(schedule => schedule.StartDate)
                        ?? Enumerable.Empty<DateTime?>(),
                    reservationSchedules?.Select(schedule => schedule.EndDate)
                        ?? Enumerable.Empty<DateTime?>(),
                    asOf);
                if (transition == null)
                {
                    if (await NormalizeApprovalRouteAsync(reservation, cancellationToken))
                    {
                        rerouted.Add(reservation);
                    }

                    continue;
                }

                ApplyTransition(reservation, transition);
                changed.Add((reservation, transition));
            }

            if (changed.Count == 0 && rerouted.Count == 0) return 0;

            await _context.SaveChangesAsync(cancellationToken);
            foreach (var item in changed)
            {
                LogTransition(item.Reservation, item.Transition);
                await SendNotificationSafelyAsync(item.Reservation, item.Transition.Message);
            }
            return changed.Count + rerouted.Count;
        }

        private async Task<bool> NormalizeApprovalRouteAsync(
            GFMS_ReservationHeader reservation,
            CancellationToken cancellationToken)
        {
            if (reservation.Status != ReservationStatuses.DepartmentHeadApproval ||
                !await _authorization.ShouldBypassDepartmentHeadAsync(reservation))
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();
            reservation.Status = ReservationStatuses.AdminApproverApproval;
            _logger.LogInformation(
                "Reservation {ReservationId} bypassed Department Head review because assigned approver {ApproverEmail} also has the Admin Manager role.",
                reservation.ReservationId,
                reservation.ApproverEmail);
            return true;
        }

        private async Task SendNotificationSafelyAsync(
            GFMS_ReservationHeader reservation,
            string message)
        {
            try
            {
                await _emailService.SendScheduleStatusNotification(reservation, message);
            }
            catch (Exception exception)
            {
                // Expiration is a data-integrity transition and must not be rolled
                // back or hidden merely because the notification provider is down.
                _logger.LogError(
                    exception,
                    "Reservation {ReservationId} changed schedule status, but its notification could not be sent.",
                    reservation.ReservationId);
            }
        }

        public static ReservationScheduleTransition? Evaluate(
            string? reservationType,
            IEnumerable<DateTime?> startDates,
            IEnumerable<DateTime?> endDates,
            DateTime asOf)
        {
            var starts = startDates.Where(date => date.HasValue).Select(date => date!.Value).ToList();
            var ends = endDates.Where(date => date.HasValue).Select(date => date!.Value).ToList();

            if (ends.Count > 0 && ends.Max() <= asOf)
            {
                return new ReservationScheduleTransition(
                    ReservationStatuses.Expired,
                    "The reservation period has already ended. The request was moved to history as expired.");
            }

            if (starts.Count > 0 && starts.Min() <= asOf)
            {
                if (reservationType == "DRM")
                {
                    return new ReservationScheduleTransition(
                        ReservationStatuses.Expired,
                        "The housing schedule has already started and can no longer be approved.");
                }

                return new ReservationScheduleTransition(
                    ReservationStatuses.ForRescheduling,
                    "The reservation start time has passed. The requestor must choose a future schedule and resubmit.");
            }

            return null;
        }

        private static void ApplyTransition(
            GFMS_ReservationHeader reservation,
            ReservationScheduleTransition transition)
        {
            reservation.Status = transition.NewStatus;
            // The legacy production column is varchar(50). Keep the persisted
            // system note concise; the complete explanation is still used for
            // the alert, log, and notification.
            reservation.Remarks = GetStoredRemark(transition.NewStatus);
            reservation.ApprovedBy = "SYSTEM";
        }

        public static string GetStoredRemark(string status) => status switch
        {
            ReservationStatuses.Expired => "Schedule expired.",
            ReservationStatuses.ForRescheduling => "Reschedule required.",
            _ => "Schedule status updated."
        };

        private void LogTransition(
            GFMS_ReservationHeader reservation,
            ReservationScheduleTransition transition)
        {
            _logger.LogInformation(
                "Reservation {ReservationId} ({ReservationType}) automatically changed to {Status}: {Message}",
                reservation.ReservationId,
                reservation.ReservationType,
                transition.NewStatus,
                transition.Message);
        }
    }

    public sealed class ReservationScheduleStatusWorker : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReservationScheduleStatusWorker> _logger;

        public ReservationScheduleStatusWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<ReservationScheduleStatusWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider
                        .GetRequiredService<IReservationScheduleStatusService>();
                    await service.RefreshAllAsync(DateTime.Now, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Failed to refresh overdue reservation statuses.");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }
    }
}
