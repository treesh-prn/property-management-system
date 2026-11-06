using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using PropertyManagementSystem.Models;

namespace PropertyManagementSystem.Services;

public interface IEmailTransport
{
    Task SendAsync(MimeMessage message, SmtpSettings settings, CancellationToken cancellationToken = default);
}

public sealed class SmtpEmailTransport : IEmailTransport
{
    public async Task SendAsync(MimeMessage message, SmtpSettings settings, CancellationToken cancellationToken = default)
    {
        using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
        var encryption = settings.Encryption switch
        {
            EmailEncryption.SslOnConnect => SecureSocketOptions.SslOnConnect,
            EmailEncryption.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls
        };
        await client.ConnectAsync(settings.Server, settings.Port, encryption, cancellationToken);
        if (settings.UseAuthentication) await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        // SMTP acceptance is delivery's commit point; disconnect failure must not cause a retry.
        try { await client.DisconnectAsync(true, cancellationToken); }
        catch (Exception) { }
    }
}
