using MailKit.Net.Smtp;
using MimeKit;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

namespace PropertyManagementSystem.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;

        public EmailService(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
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
    }
}
