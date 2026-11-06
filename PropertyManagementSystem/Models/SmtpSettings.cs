namespace PropertyManagementSystem.Models
{
    public class SmtpSettings
    {
        public EmailDeliveryMode DeliveryMode { get; set; } = EmailDeliveryMode.Test;
        public string ApplicationBaseUrl { get; set; } = "https://localhost:7035";
        public string Server { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string SenderName { get; set; } = "Property Management System";
        public string SenderEmail { get; set; } = string.Empty;
        // Kept for compatibility with the existing test inbox configuration.
        public string RecipientEmail { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool UseAuthentication { get; set; } = true;
        public EmailEncryption Encryption { get; set; } = EmailEncryption.StartTls;
        public int TimeoutSeconds { get; set; } = 30;
        public int MaxAttempts { get; set; } = 5;
        public int RetryDelaySeconds { get; set; } = 60;
        public int PollIntervalSeconds { get; set; } = 30;
        public string OutboxPath { get; set; } = "App_Data/EmailOutbox";
    }

    public enum EmailDeliveryMode { Test, Production, Disabled }
    public enum EmailEncryption { StartTls, SslOnConnect, None }
}
