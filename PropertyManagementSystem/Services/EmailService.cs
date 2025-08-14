using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using PropertyManagementSystem.Models;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;

namespace PropertyManagementSystem.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly string _baseUrl;
        private readonly DAL _context;



        public EmailService(IConfiguration config, DAL context)
        {
            _config = config;
            _baseUrl = config["AppSettings:BaseUrl"];
            _context = context;
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


        public async Task SendFNReservationApprovalEmail(GFMS_ReservationHeader reservationHeader, string approverEmail, string startDate, string endDate, int numOfGuests, string facilityName, string roomName, string purpose, string guid, string controllerName)
        {
            string emailBody;
            if (roomName == null && facilityName == null)
            {
                emailBody = $@"
                <h1>Reservation Approval Needed</h1>
                <p>Hello,</p>
                <p>A new reservation submitted by <b>{reservationHeader.Requestor}</b> requires approval.</p>
                <p>Please review and take action by clicking the link below:</p>
                <p><a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>View Reservation Details</a></p>
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
            
            await SendEmailAsync(approverEmail, $"Reservation {reservationHeader.ReservationCode} has been created", emailBody);

        }
        public async Task SendGHReservationApprovalEmail(GFMS_ReservationHeader reservationHeader, string requestorEmail, string approverEmail, string approverName, string guid, string controllerName)
        {
            string emailBody;
            emailBody = $@"
                <h1>Reservation Approval Needed</h1>
                <p>Hello, {approverName}</p>
                <p>A new reservation submitted by <b>{reservationHeader.Requestor}</b> requires approval.</p>
                <p>Please review and take action by clicking the link below:</p>
                <p><a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>View Reservation Details</a></p>
                <p>Thank you,</p>
                <p>Property Management System</p>";


            await SendEmailAsync(approverEmail, $"Reservation {reservationHeader.ReservationCode} has been created", emailBody);

        }

        public async Task SendDRMReservationApprovalEmail(GFMS_ReservationHeader reservationHeader, string requestorEmail, string approverEmail, string approverName,string guid, string controllerName)
        {
            string emailBody;
            emailBody = $@"
                <h1>Reservation Approval Needed</h1>
                <p>Hello,</p>
                <p>A new reservation submitted by <b>{reservationHeader.Requestor}</b> requires approval.</p>
                <p>Please review and take action by clicking the link below:</p>
                <p><a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>View Reservation Details</a></p>
                <p>Thank you,</p>
                <p>Property Management System</p>";


            await SendEmailAsync(emailAdd, $"Reservation {reservationHeader.ReservationCode} has been created", emailBody);

        }

        public async Task RoomAssignmentApprovalEmail(GFMS_ReservationHeader reservationHeader, string requestorEmail, string startDate, string endDate, int numOfGuests, string facilityName, string roomName, string purpose, string guid, string controllerName, List<string> adminManagerEmail)
        {
            foreach (var recipients in adminManagerEmail)
            {
                var emailBody = $@"
                <h1>{reservationHeader.ReservationCode} Facility Assignment</h1>
                <p>Hello,</p>
                <p>{reservationHeader.ApprovedBy} has approved and assigned a facility to the reservation request submitted by {reservationHeader.Requestor}. The details of the reservation are as follows:</p>
                <ul>
                    <li><strong>Requestor:</strong> {reservationHeader.Requestor}</li>
                    <li><strong>Start Date:</strong> {startDate}</li>
                    <li><strong>End Date:</strong> {endDate}</li>
                    <li><strong>Number of Guests:</strong> {numOfGuests}</li>
                    <li><strong>Facility Name:</strong> {facilityName}</li>
                    <li><strong>Room Name:</strong> {roomName}</li>
                    <li><strong>Purpose:</strong> {purpose}</li>
                </ul>
                <p>To view the complete reservation details, please <a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>click here to log in</a>.</p>
                <p>Thank you,</p>
                <p>Property Management System</p>";

                await SendEmailAsync(recipients, $"Reservation {reservationHeader.ReservationCode} facility assignment", emailBody);
            }
        }

        public async Task SendGuestRoomAssignmentEmail(GFMS_ReservationHeader reservationHeader, IEnumerable<GFMS_ReservationDetails> reservationDetails, List<string> adminManagerEmail, string controllerName)
        {
            var emailSubject = "Room Assignment Summary";
            foreach (var recipients in adminManagerEmail)
            {
                string emailBody = $@"
                <h2>Room Assignment Summary for Reservation Code: {reservationHeader.ReservationCode}</h2>
                <p>Below are the details of each guest and their assigned facility and room:</p>
                <table border='1' cellpadding='8' cellspacing='0' style='border-collapse:collapse;'>
                    <thead>
                        <tr>
                            <th>Guest Name</th>
                            <th>Facility</th>
                            <th>Room</th>
                        </tr>
                    </thead>
                    <tbody>
                    ";

                        foreach (var detail in reservationDetails)
                        {
                            var facility = _context.Facilities.FirstOrDefault(f => f.FacilityId == detail.FacilityId);
                            var room = _context.Rooms.FirstOrDefault(r => r.RoomId == detail.RoomId);

                            emailBody += $@"
                    <tr>
                        <td>{detail.GuestName}</td>
                        <td>{facility?.FacilityName ?? "N/A"}</td>
                        <td>{room?.RoomName ?? "N/A"}</td>
                    </tr>";
                        }

                emailBody += @"
                    </tbody>
                </table>
                <p>To view the complete reservation details, please <a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>click here to log in</a>.</p>
                <p>Thank you,</p>
                <p>Property Management System</p>";

                await SendEmailAsync(recipients, emailSubject, emailBody);
            }

        }

        public async Task DRMRoomAssignmentApprovalEmail(GFMS_ReservationHeader reservationHeader, string appliedUnit, string scheduleType, string facilityName, string roomName, string guid, string controllerName, List<string> adminManagerEmail)
        {
            foreach (var recipients in adminManagerEmail)
            {
                var emailBody = $@"
                <h1>{reservationHeader.ReservationCode} Facility Assignment</h1>
                <p>Hello,</p>
                <p>{reservationHeader.ApprovedBy} has approved and assigned a facility to the reservation request submitted by {reservationHeader.Requestor}. The details of the reservation are as follows:</p>
                <ul>
                    <li><strong>Requestor:</strong> {reservationHeader.Requestor}</li>
                    <li><strong>Applied Unit:</strong> {appliedUnit}</li>
                    <li><strong>Schedule Type:</strong> {scheduleType}</li>
                    <li><strong>Facility Name:</strong> {facilityName}</li>
                    <li><strong>Room Name:</strong> {roomName}</li>
                    
                </ul>
                <p>To view the complete reservation details, please <a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>click here to log in</a>.</p>
                <p>Thank you,</p>
                <p>Property Management System</p>";

                await SendEmailAsync(recipients, $"Reservation {reservationHeader.ReservationCode} facility assignment", emailBody);
            }
        }

        public async Task SendApprovalNotification(GFMS_ReservationHeader reservationHeader, string requestorEmail, string guid, string controllerName)
        {
            if (reservationHeader.ApprovedBy != null)
            {
                var emailBody = $@"
                <p>Hello <b>{reservationHeader.Requestor}</b>,</p>
                <p>{reservationHeader.ApprovedBy} approved your reservation({reservationHeader.ReservationCode}) request</p>
                <p>Click here to <a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>login</a> and view reservation details</p>
                
                <p>Thank you,</p>
                <p>Property Management System</p>";
                await SendEmailAsync(emailAdd, $"Reservation {reservationHeader.ReservationCode} has been approved", emailBody);
            }
            else
            {
                var emailBody = $@"
                <h1>Reservation Approval Notification</h1>
                <p>Hello <b>{reservationHeader.Requestor}</b>,</p>
                <p>The reservation request with code ({reservationHeader.ReservationCode}) submitted by {reservationHeader.Requestor} has been approved by {reservationHeader.Approver}.</p>
                <p>Click here to <a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>login</a> and view the reservation details.</p>
                
                <p>Thank you,</p>
                <p>Property Management System</p>";
                await SendEmailAsync(requestorEmail, $"Reservation {reservationHeader.ReservationCode} has been approved", emailBody);

            }
        }

        public async Task SendDisapprovalNotification(GFMS_ReservationHeader reservationHeader, string requestorEmail, string guid, string controllerName)
        {
            var emailBody = $@"
                <p>Hello <b>{reservationHeader.Requestor}</b>,</p>
                <p>{reservationHeader.ApprovedBy} disapproved your reservation({reservationHeader.ReservationCode}) request. The details of the reservation are as follows:</p>
                <p><b>Remarks:</b> {reservationHeader.Remarks}</p>
                <p>Click here to <a href='{_baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}'>reservation</a> and view reservation details</p>
                
                <p>Thank you,</p>
                <p>Property Management System</p>";
            await SendEmailAsync(requestorEmail, $"Reservation {reservationHeader.ReservationCode} has been disapproved", emailBody);
        }

        public async Task SendPendingRoomAssignmentEmail(GFMS_ReservationHeader reservationHeader, List<string> recipients, string controllerName, string departmentCode)
        {
            foreach (var recipient in recipients)
            {
                var subject = $"Pending Room Assignment and Approval - {reservationHeader.ReservationCode}";
                var baseUrl = _config["AppSettings:BaseUrl"];
                var reservationLink = $"{baseUrl}{controllerName}/ViewReservationDetails/{reservationHeader.GUID}";

                var emailBody = $@"
                <h1>Action Required: Room Assignment & Approval</h1>
                <p>Dear Admin Approver,</p>
                <p>A reservation is pending room assignment and your approval. Below are the details:</p>
                <ul>
                    <li><strong>Reservation Code:</strong> {reservationHeader.ReservationCode}</li>
                    <li><strong>Requestor:</strong> {reservationHeader.Requestor}</li>
                    <li><strong>Department:</strong> {departmentCode}</li>
                    <li><strong>Date Requested:</strong> {reservationHeader.DateFiled?.ToString("MMMM dd, yyyy")}</li>
                </ul>
                <p>Please <a href='{reservationLink}'>click here</a> to review and take action.</p>
                <p>Thank you,<br/>Property Management System</p>
            ";

                await SendEmailAsync(recipient, subject, emailBody);
            }
        }

    }
}
