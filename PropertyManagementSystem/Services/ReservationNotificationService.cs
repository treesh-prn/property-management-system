using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services;

public interface IReservationNotificationService
{
    Task QueueTransitionAsync(GFMS_ReservationHeader reservation, string action, string? previousStatus, string eventId);
}

public sealed class ReservationNotificationService : IReservationNotificationService
{
    private readonly DAL _context;
    private readonly UserManager<ApplicationUser> _users;
    private readonly EmailService _email;
    private readonly IReservationProcessingPolicy _processing;
    private readonly ILogger<ReservationNotificationService> _logger;
    public ReservationNotificationService(DAL context, UserManager<ApplicationUser> users, EmailService email,
        IReservationProcessingPolicy processing, ILogger<ReservationNotificationService> logger)
    {
        _context = context; _users = users; _email = email; _processing = processing; _logger = logger;
    }

    public static bool ShouldNotify(string action, string? previousStatus, string? status) =>
        action.StartsWith("SUBMIT", StringComparison.Ordinal) || action.StartsWith("RESCHEDULE_AND_RESUBMIT", StringComparison.Ordinal) ||
        action is "SAVE_ROOMS" or "RETURN_FOR_RESCHEDULING" ||
        (previousStatus != status && (action is "APPROVE" or "REJECT" or "CANCEL"));

    public async Task QueueTransitionAsync(GFMS_ReservationHeader reservation, string action, string? previousStatus, string eventId)
    {
        if (reservation.ReservationType is not "FN" and not "GH" || !ShouldNotify(action, previousStatus, reservation.Status)) return;
        var details = await GetDetailsAsync(reservation);
        var approved = reservation.Status == ReservationStatuses.Approved;
        var title = reservation.Status switch
        {
            ReservationStatuses.Disapproved => "Your reservation was not approved",
            ReservationStatuses.Cancelled => "Your reservation was cancelled",
            ReservationStatuses.ForRescheduling => "Your reservation needs a new schedule",
            _ when action == "SAVE_ROOMS" => "Your guest room assignments are ready",
            _ when approved => "Your reservation is approved",
            _ when action == "APPROVE" => "Your reservation review has progressed",
            _ => "Your reservation was submitted"
        };
        var message = reservation.Status switch
        {
            ReservationStatuses.Disapproved => "Review the remarks below for the reason your reservation was not approved.",
            ReservationStatuses.Cancelled => "Your reservation has been cancelled and is no longer scheduled for approval or use.",
            ReservationStatuses.ForRescheduling => "Choose a new future schedule and resubmit your reservation.",
            _ when action == "SAVE_ROOMS" => "The guest room assignments have been saved. Your reservation remains approved.",
            _ when approved && reservation.ReservationType == "GH" => "Your request is approved. Review the booking details and complete any remaining guest room assignments in the application.",
            _ when approved => "Your reservation has been approved. Review the schedule and assigned location before your booking.",
            _ => "Your reservation is recorded and waiting for the approval stage shown below. You can track its progress in the application."
        };
        if (EmailAddresses.TryParse(reservation.RequestorEmail, out _))
            await _email.QueueNotificationAsync(new(reservation, reservation.RequestorEmail!,
                $"{title}: {reservation.ReservationCode}", title, message, reservation.Requestor, details),
                deduplicationKey: eventId + ":requestor");
        else _logger.LogWarning("Reservation {ReservationId} has no deliverable requestor email.", reservation.ReservationId);

        if (reservation.Status is not ReservationStatuses.DepartmentHeadApproval and
            not ReservationStatuses.AdminApproverApproval and not ReservationStatuses.AdminManagerApproval) return;
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reservation.Status == ReservationStatuses.DepartmentHeadApproval)
        {
            if (EmailAddresses.TryParse(reservation.ApproverEmail, out var head)) recipients.Add(head.Address);
        }
        else
        {
            foreach (var user in await _users.GetUsersInRoleAsync(ReservationRoles.AdminApprover))
            {
                if (EmailAddresses.TryParse(user.Email, out var address) ||
                    EmailAddresses.TryParse(user.UserName, out address)) recipients.Add(address.Address);
            }
        }
        if (recipients.Count == 0)
            _logger.LogError("Reservation {ReservationId} has no email recipient for approval stage {Status}.", reservation.ReservationId, reservation.Status);
        var now = DateTime.Now;
        var due = await _processing.IsEligibleAsync(reservation, now) ? DateTime.UtcNow :
            (reservation.DateSubmitted ?? reservation.DateFiled ?? now).Date.AddDays(1).ToUniversalTime();
        foreach (var recipient in recipients)
            await _email.QueueNotificationAsync(new(reservation, recipient, $"Action required: {reservation.ReservationCode}",
                "Reservation approval requested", "A reservation is ready for your review. Open the request to review its details and available actions.",
                reservation.Status == ReservationStatuses.DepartmentHeadApproval ? reservation.Approver : "Approver", details, "Review reservation"),
                due, eventId + ":approval:" + recipient.ToLowerInvariant(),
                new EmailQueueContext(reservation.ReservationId, reservation.Status, reservation.DateSubmitted));
    }

    private async Task<List<EmailDetail>> GetDetailsAsync(GFMS_ReservationHeader reservation)
    {
        var schedules = await _context.ReservationDetails.AsNoTracking().Where(detail => detail.ReservationId == reservation.ReservationId)
            .Include(detail => detail.Facility).Include(detail => detail.Room).OrderBy(detail => detail.StartDate).ToListAsync();
        var first = schedules.FirstOrDefault();
        var fields = new List<EmailDetail>();
        if (first != null)
        {
            fields.Add(new("Start", ReservationEmailTemplate.FormatDate(first.StartDate?.ToString("O"))));
            fields.Add(new("End", ReservationEmailTemplate.FormatDate(schedules.Max(detail => detail.EndDate)?.ToString("O"))));
            fields.Add(new(reservation.ReservationType == "GH" ? "Guests" : "Attendees",
                (reservation.ReservationType == "GH" ? schedules.Count : first.NumberOfGuest).ToString()));
            if (reservation.ReservationType == "FN" && schedules.Count > 1) fields.Add(new("Occurrences", schedules.Count.ToString()));
            fields.Add(new("Facility", string.Join(", ", schedules.Select(detail => detail.Facility?.FacilityName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct()) is { Length: > 0 } facilities ? facilities : "For assignment"));
            fields.Add(new("Room", string.Join(", ", schedules.Select(detail => detail.Room?.RoomName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct()) is { Length: > 0 } rooms ? rooms : "For assignment"));
            fields.Add(new("Purpose", first.Purpose));
        }
        if (!string.IsNullOrWhiteSpace(reservation.ApprovedBy)) fields.Add(new("Reviewed by", reservation.ApprovedBy));
        if (!string.IsNullOrWhiteSpace(reservation.Remarks)) fields.Add(new("Remarks", reservation.Remarks));
        return fields;
    }
}
