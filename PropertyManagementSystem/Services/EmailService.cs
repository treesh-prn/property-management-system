using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        string emailAdd = "trisha_paran@tecoindustrialpark.com";

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var recipientEmail = _config["SmtpSettings:RecipientEmail"] ?? toEmail;
            var emailMessage = new MimeMessage();
            emailMessage.From.Add(new MailboxAddress(_config["SmtpSettings:SenderName"], _config["SmtpSettings:SenderEmail"]));
            emailMessage.To.Add(new MailboxAddress(recipientEmail, recipientEmail));
            emailMessage.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = body };
            emailMessage.Body = bodyBuilder.ToMessageBody();

            try
            {
                using var client = new SmtpClient();
                await client.ConnectAsync(_config["SmtpSettings:Server"], int.Parse(_config["SmtpSettings:Port"]), false);
                await client.AuthenticateAsync(_config["SmtpSettings:Username"], _config["SmtpSettings:Password"]);
                await client.SendAsync(emailMessage);
                await client.DisconnectAsync(true);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Email delivery failed for subject {Subject} to {Recipient}. The originating operation will continue.",
                    subject,
                    recipientEmail);
            }
        }

        public async Task SendReservationApprovalEmail(GFMS_ReservationHeader reservationHeader, string requestorEmail, string startDate, string endDate, int numOfGuests, string facilityName, string roomName, string purpose)
        {
            if (roomName == null && facilityName == null)
            {
                var emailBody = $@"
                <h1>Reservation Approval Needed</h1>
                <p>Hello <b>{reservationHeader.Requestor}</b>,</p>
                <p>A new reservation has been created and requires your approval. The details of the reservation are as follows:</p>
                <ul>
                    <li><strong>Requestor:</strong> {reservationHeader.Requestor}</li>
                    <li><strong>Start Date:</strong> {startDate}</li>
                    <li><strong>End Date:</strong> {endDate}</li>
                    <li><strong>Number of Guests:</strong> {numOfGuests}</li>
                    <li><strong>Purpose:</strong> {purpose}</li>
                </ul>
                <p>Please review and approve the <a href='https://localhost:7035/Identity/Account/Login'>reservation</a> at your earliest convenience.</p>
                <p>Thank you,</p>
                <p>Property Management System</p>";
            }
            else
            {
                var emailBody = $@"
                <h1>Reservation Approval Needed</h1>
                <p>Hello <b>{reservationHeader.Requestor}</b>,</p>
                <p>A new reservation has been created and requires your approval. The details of the reservation are as follows:</p>
                <ul>
                    <li><strong>Requestor:</strong> {reservationHeader.Requestor}</li>
                    <li><strong>Start Date:</strong> {startDate}</li>
                    <li><strong>End Date:</strong> {endDate}</li>
                    <li><strong>Number of Guests:</strong> {numOfGuests}</li>
                    <li><strong>Facility Name:</strong> {facilityName}</li>
                    <li><strong>Room Name:</strong> {roomName}</li>
                    <li><strong>Purpose:</strong> {purpose}</li>
                </ul>
                <p>Please review and approve the <a href='https://localhost:7035/Identity/Account/Login'>reservation</a> at your earliest convenience.</p>
                <p>Thank you,</p>
                <p>Property Management System</p>";

                await SendEmailAsync(emailAdd, $"Reservation {reservationHeader.ReservationCode} has been created", emailBody);
            }


        }

        public async Task RoomAssignmentApprovalEmail(GFMS_ReservationHeader reservationHeader, string requestorEmail, string startDate, string endDate, int numOfGuests, string facilityName, string roomName, string purpose)
        {
            var emailBody = $@"
                <h1>{reservationHeader.ReservationCode} Facility Assignment</h1>
                <p>Hello <b>{reservationHeader.Requestor}</b>,</p>
                <p>{reservationHeader.ApprovedBy} has approved and assigned a facility to your request. The details of the reservation are as follows:</p>
                <ul>
                    <li><strong>Requestor:</strong> {reservationHeader.Requestor}</li>
                    <li><strong>Start Date:</strong> {startDate}</li>
                    <li><strong>End Date:</strong> {endDate}</li>
                    <li><strong>Number of Guests:</strong> {numOfGuests}</li>
                    <li><strong>Facility Name:</strong> {facilityName}</li>
                    <li><strong>Room Name:</strong> {roomName}</li>
                    <li><strong>Purpose:</strong> {purpose}</li>
                </ul>
                <p>Click here to <a href='https://localhost:7035/Identity/Account/Login'>login</a> and view the complete reservation details</p>
                <p>Thank you,</p>
                <p>Property Management System</p>";
            await SendEmailAsync(emailAdd, $"Reservation {reservationHeader.ReservationCode} facility assignment", emailBody);
        }

        public async Task SendApprovalNotification(GFMS_ReservationHeader reservationHeader, string requestorEmail)
        {
            if (reservationHeader.ApprovedBy != null)
            {
                var emailBody = $@"
                <p>Hello <b>{reservationHeader.Requestor}</b>,</p>
                <p>{reservationHeader.ApprovedBy} approved your reservation({reservationHeader.ReservationCode}) request</p>
                <p>Click here to <a href='https://localhost:7035/Identity/Account/Login'>login</a> and view reservation details</p>
                
                <p>Thank you,</p>
                <p>Property Management System</p>";
                await SendEmailAsync(emailAdd, $"Reservation {reservationHeader.ReservationCode} has been approved", emailBody);
            }
            else
            {
                var emailBody = $@"
                <h1>Reservation Approval Needed</h1>
                <p>Hello <b>{reservationHeader.Requestor}</b>,</p>
                <p>{reservationHeader.Approver} approved your reservation({reservationHeader.ReservationCode}) request</p>
                <p>Click here to <a href='https://localhost:7035/Identity/Account/Login'>login</a> and view reservation details</p>
                
                <p>Thank you,</p>
                <p>Property Management System</p>";
                await SendEmailAsync(emailAdd, $"Reservation {reservationHeader.ReservationCode} has been approved", emailBody);
            }
        }

        public async Task SendDisapprovalNotification(GFMS_ReservationHeader reservationHeader, string requestorEmail)
        {
            var emailBody = $@"
                <p>Hello <b>{reservationHeader.Requestor}</b>,</p>
                <p>{reservationHeader.ApprovedBy} disapproved your reservation({reservationHeader.ReservationCode}) request. The details of the reservation are as follows:</p>
                <p><b>Remarks:</b> {reservationHeader.Remarks}</p>
                <p>Click here to <a href='https://localhost:7035/Identity/Account/Login'>login</a> and view reservation details</p>
                
                <p>Thank you,</p>
                <p>Property Management System</p>";
            await SendEmailAsync(emailAdd, $"Reservation {reservationHeader.ReservationCode} has been disapproved", emailBody);
        }

        public async Task SendScheduleStatusNotification(
            GFMS_ReservationHeader reservationHeader,
            string statusMessage)
        {
            if (string.IsNullOrWhiteSpace(reservationHeader.RequestorEmail)) return;

            var requestor = System.Net.WebUtility.HtmlEncode(
                reservationHeader.Requestor ?? "Requestor");
            var reservationCode = System.Net.WebUtility.HtmlEncode(
                reservationHeader.ReservationCode ?? "Reservation request");
            var message = System.Net.WebUtility.HtmlEncode(statusMessage);
            var emailBody = $@"
                <p>Hello <b>{requestor}</b>,</p>
                <p>The status of <b>{reservationCode}</b> was updated automatically.</p>
                <p>{message}</p>
                <p>Please sign in to the Property Management System to review the request.</p>
                <p>Thank you,<br/>Property Management System</p>";

            var subject = reservationHeader.Status == ReservationStatuses.Expired
                ? $"Reservation {reservationHeader.ReservationCode} has expired"
                : $"Reservation {reservationHeader.ReservationCode} requires rescheduling";

            await SendEmailAsync(
                reservationHeader.RequestorEmail,
                subject,
                emailBody);
        }
    }
}
