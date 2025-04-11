using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using PropertyManagementSystem.Models;
using Microsoft.Extensions.Options;

namespace PropertyManagementSystem.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly string _baseUrl;

        public EmailService(IConfiguration config)
        {
            _config = config;
            _baseUrl = config["AppSettings:BaseUrl"];
        }

        string emailAdd = "trisha_paran@tecoindustrialpark.com";

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            try
            {
                var emailMessage = new MimeMessage();
                emailMessage.From.Add(new MailboxAddress(_config["SmtpSettings:SenderName"], _config["SmtpSettings:SenderEmail"]));
                emailMessage.To.Add(new MailboxAddress(toEmail, toEmail));
                emailMessage.Subject = subject;

                var bodyBuilder = new BodyBuilder { HtmlBody = body };
                emailMessage.Body = bodyBuilder.ToMessageBody();

                using (var client = new SmtpClient())
                {
                    await client.ConnectAsync(_config["SmtpSettings:Server"], int.Parse(_config["SmtpSettings:Port"]), false);
                    await client.AuthenticateAsync(_config["SmtpSettings:Username"], _config["SmtpSettings:Password"]);
                    await client.SendAsync(emailMessage);
                    await client.DisconnectAsync(true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error sending email: " + ex.Message);
                throw; // rethrow or handle as needed
            }
        }


        public async Task SendReservationApprovalEmail(GFMS_ReservationHeader reservationHeader, string requestorEmail, string startDate, string endDate, int numOfGuests, string facilityName, string roomName, string purpose, string guid, string controllerName)
        {
            string emailBody;

            if (roomName == null && facilityName == null)
            {
                emailBody = $@"
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
                <p>Please review and approve the <a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>reservation</a> at your earliest convenience.</p>
                <p>Thank you,</p>
                <p>Property Management System</p>";
            }
            else
            {
                emailBody = $@"
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
                <p>Please review and approve the <a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>reservation</a> at your earliest convenience.</p>
                <p>Thank you,</p>
                <p>Property Management System</p>";

            }

            await SendEmailAsync(emailAdd, $"Reservation {reservationHeader.ReservationCode} has been created", emailBody);

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

        public async Task SendPendingRoomAssignmentEmail(GFMS_ReservationHeader reservationHeader, string adminApproverEmail, string controllerName)
        {
            var subject = $"Pending Room Assignment and Approval - {reservationHeader.ReservationCode}";
            var baseUrl = _config["AppSettings:BaseUrl"]; // e.g., "https://yourdomain.com/"
            var reservationLink = $"{baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}";

            var emailBody = $@"
                <h1>Action Required: Room Assignment & Approval</h1>
                <p>Dear Admin Approver,</p>
                <p>A reservation is pending room assignment and your approval. Below are the details:</p>
                <ul>
                    <li><strong>Reservation Code:</strong> {reservationHeader.ReservationCode}</li>
                    <li><strong>Requestor:</strong> {reservationHeader.Requestor}</li>
                    <li><strong>Department:</strong> {reservationHeader.Department}</li>
                    <li><strong>Date Requested:</strong> {reservationHeader.DateFiled?.ToString("MMMM dd, yyyy")}</li>
                </ul>
                <p>Please <a href='{reservationLink}'>click here</a> to review and take action.</p>
                <p>Thank you,<br/>Property Management System</p>
            ";

            await SendEmailAsync(adminApproverEmail, subject, emailBody);
        }

    }
}
