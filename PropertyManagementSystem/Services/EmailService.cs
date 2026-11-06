using MimeKit;
using MimeKit.Utils;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services;

public class EmailService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<EmailService> _logger;
    private readonly IEmailOutbox _outbox;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, IEmailOutbox outbox)
    {
        _settings = config.GetSection("SmtpSettings").Get<SmtpSettings>() ?? new();
        _logger = logger;
        _outbox = outbox;
    }

    public MimeMessage CreateMessage(ReservationEmailModel notification)
    {
        var validation = new SmtpSettingsValidator().Validate(null, _settings);
        if (validation.Failed) throw new InvalidOperationException(string.Join(" ", validation.Failures));
        var rendered = ReservationEmailTemplate.Render(notification, _settings);
        if (!EmailAddresses.TryParse(notification.RecipientEmail, out var recipient))
            throw new ArgumentException("An intended recipient email is required.", nameof(notification));
        var destination = _settings.DeliveryMode == EmailDeliveryMode.Test ? MailboxAddress.Parse(_settings.RecipientEmail) : recipient;
        var message = new MimeMessage { Subject = rendered.Subject, MessageId = MimeUtils.GenerateMessageId(), Date = DateTimeOffset.Now };
        message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
        message.To.Add(destination);
        message.Body = new BodyBuilder { HtmlBody = rendered.HtmlBody, TextBody = rendered.TextBody }.ToMessageBody();
        return message;
    }

    public async Task QueueNotificationAsync(ReservationEmailModel notification, DateTime? notBeforeUtc = null,
        string? deduplicationKey = null, EmailQueueContext? context = null)
    {
        if (_settings.DeliveryMode == EmailDeliveryMode.Disabled)
        {
            _logger.LogInformation("Email disabled for reservation {ReservationId}.", notification.Reservation.ReservationId);
            return;
        }
        var message = CreateMessage(notification);
        await _outbox.QueueAsync(message, _settings.DeliveryMode,
            deduplicationKey ?? message.MessageId, notBeforeUtc ?? DateTime.UtcNow, context);
    }

    public Task SendReservationApprovalEmail(GFMS_ReservationHeader reservation, string approverEmail,
        string startDate, string endDate, int numOfGuests, string facilityName, string roomName, string purpose) =>
        QueueNotificationAsync(new(reservation, approverEmail, $"Action required: {reservation.ReservationCode}",
            "Reservation approval requested", "A reservation is ready for your review. Open the request to review its details and available actions.",
            reservation.Approver, ScheduleDetails(startDate, endDate, numOfGuests, facilityName, roomName, purpose), "Review reservation"));

    public Task RoomAssignmentApprovalEmail(GFMS_ReservationHeader reservation, string requestorEmail,
        string startDate, string endDate, int numOfGuests, string facilityName, string roomName, string purpose) =>
        QueueNotificationAsync(new(reservation, requestorEmail, $"Room assignment: {reservation.ReservationCode}",
            "Your room assignment is ready", "The facility and room assignment for your reservation has been saved.",
            reservation.Requestor, ScheduleDetails(startDate, endDate, numOfGuests, facilityName, roomName, purpose)));

    public Task SendApprovalNotification(GFMS_ReservationHeader reservation, string requestorEmail) =>
        QueueNotificationAsync(new(reservation, requestorEmail, $"Approval update: {reservation.ReservationCode}",
            reservation.Status == ReservationStatuses.Approved ? "Your reservation is approved" : "Your reservation review has progressed",
            reservation.Status == ReservationStatuses.Approved
                ? "Your reservation has been approved. Review the schedule and assigned location before your booking."
                : "Your reservation has moved to the next approval stage. You can track its progress in the application.",
            reservation.Requestor, new[] { new EmailDetail("Approved by", reservation.ApprovedBy ?? reservation.Approver) }));

    public Task SendDisapprovalNotification(GFMS_ReservationHeader reservation, string requestorEmail) =>
        QueueNotificationAsync(new(reservation, requestorEmail, $"Disapproved: {reservation.ReservationCode}",
            "Your reservation was not approved", "Review the remarks below for the reason your request was not approved.",
            reservation.Requestor, new[] { new EmailDetail("Reviewed by", reservation.ApprovedBy), new("Remarks", reservation.Remarks) }));

    public Task SendScheduleStatusNotification(GFMS_ReservationHeader reservation, string statusMessage)
    {
        if (string.IsNullOrWhiteSpace(reservation.RequestorEmail)) return Task.CompletedTask;
        var expired = reservation.Status == ReservationStatuses.Expired;
        return QueueNotificationAsync(new(reservation, reservation.RequestorEmail,
            $"{(expired ? "Expired" : "Rescheduling required")}: {reservation.ReservationCode}",
            expired ? "Your reservation request has expired" : "Your reservation needs a new schedule",
            statusMessage, reservation.Requestor));
    }

    private static IReadOnlyList<EmailDetail> ScheduleDetails(string start, string end, int count,
        string? facility, string? room, string? purpose) => new[]
    {
        new EmailDetail("Start", ReservationEmailTemplate.FormatDate(start)), new("End", ReservationEmailTemplate.FormatDate(end)),
        new("Guests / attendees", count.ToString()), new("Facility", facility ?? "For assignment"),
        new("Room", room ?? "For assignment"), new("Purpose", purpose)
    };
}
