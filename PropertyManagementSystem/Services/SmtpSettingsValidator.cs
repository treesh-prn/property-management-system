using Microsoft.Extensions.Options;
using MimeKit;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services;

public sealed class SmtpSettingsValidator : IValidateOptions<SmtpSettings>
{
    public ValidateOptionsResult Validate(string? name, SmtpSettings settings)
    {
        var errors = new List<string>();
        if (!Enum.IsDefined(typeof(EmailDeliveryMode), settings.DeliveryMode)) errors.Add("SmtpSettings:DeliveryMode must be Test, Production or Disabled.");
        if (settings.DeliveryMode == EmailDeliveryMode.Disabled) return ValidateOptionsResult.Success;
        if (string.IsNullOrWhiteSpace(settings.Server)) errors.Add("SmtpSettings:Server is required.");
        if (settings.Port is < 1 or > 65535) errors.Add("SmtpSettings:Port must be between 1 and 65535.");
        if (!EmailAddresses.TryParse(settings.SenderEmail, out _)) errors.Add("SmtpSettings:SenderEmail must be a valid email address.");
        if (settings.UseAuthentication && (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password)))
            errors.Add("SMTP authentication requires Username and Password.");
        if (!Enum.IsDefined(typeof(EmailEncryption), settings.Encryption)) errors.Add("SmtpSettings:Encryption must be StartTls, SslOnConnect or None.");
        if (settings.TimeoutSeconds is < 1 or > 300) errors.Add("SmtpSettings:TimeoutSeconds must be between 1 and 300.");
        if (settings.MaxAttempts is < 1 or > 20 || settings.RetryDelaySeconds is < 1 or > 3600)
            errors.Add("Configure MaxAttempts between 1 and 20 and RetryDelaySeconds between 1 and 3600.");
        if (settings.PollIntervalSeconds is < 5 or > 3600) errors.Add("SmtpSettings:PollIntervalSeconds must be between 5 and 3600.");
        if (string.IsNullOrWhiteSpace(settings.OutboxPath)) errors.Add("SmtpSettings:OutboxPath is required.");
        if (settings.DeliveryMode == EmailDeliveryMode.Test && !EmailAddresses.TryParse(settings.RecipientEmail, out _))
            errors.Add("Test mode requires a valid SmtpSettings:RecipientEmail test inbox.");

        var validUrl = Uri.TryCreate(settings.ApplicationBaseUrl, UriKind.Absolute, out var url) &&
            (url.Scheme == "http" || url.Scheme == "https") && string.IsNullOrEmpty(url.UserInfo) &&
            string.IsNullOrEmpty(url.Query) && string.IsNullOrEmpty(url.Fragment);
        if (!validUrl) errors.Add("SmtpSettings:ApplicationBaseUrl must be an absolute HTTP(S) application URL without credentials, query or fragment.");
        if (settings.DeliveryMode == EmailDeliveryMode.Production)
        {
            if (!string.IsNullOrWhiteSpace(settings.RecipientEmail)) errors.Add("Clear SmtpSettings:RecipientEmail before enabling Production delivery.");
            if (!validUrl || url!.Scheme != "https" || url.IsLoopback) errors.Add("Production email requires a public or intranet HTTPS ApplicationBaseUrl, not localhost.");
            if (settings.Encryption == EmailEncryption.None) errors.Add("Production email requires StartTls or SslOnConnect.");
        }
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

internal static class EmailAddresses
{
    private static readonly ParserOptions Options = new() { AllowAddressesWithoutDomain = false };
    public static bool TryParse(string? value, out MailboxAddress address) =>
        MailboxAddress.TryParse(Options, value ?? string.Empty, out address);
}
