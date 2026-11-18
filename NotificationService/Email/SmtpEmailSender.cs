using System.Net;
using System.Net.Mail;
using NotificationService.Configuration;
using Microsoft.Extensions.Options;

namespace NotificationService.Email;

/// <summary>Sends email through the SMTP server configured in the "Email" section of appsettings.json.</summary>
public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    IHostEnvironment environment,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<bool> SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        var smtp = options.Value;

        if (string.IsNullOrWhiteSpace(smtp.Host) || string.IsNullOrWhiteSpace(smtp.FromAddress))
        {
            // No mail server is configured. In Development only, write the message to the log so sign-in can be
            // tested without email. This log line contains the code, so it must never run in Production.
            if (environment.IsDevelopment())
            {
                logger.LogWarning("Email is not configured. Development only: message to {To}: {Body}", to, body);
                return true;
            }

            logger.LogError("Email is not configured. Set Email:Host and Email:FromAddress.");
            return false;
        }

        using var message = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, smtp.FromName),
            Subject = subject,
            Body = body
        };
        message.To.Add(to);

        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.UseSsl,
            Timeout = 15000,
            Credentials = string.IsNullOrEmpty(smtp.UserName) ? null : new NetworkCredential(smtp.UserName, smtp.Password)
        };

        try
        {
            await client.SendMailAsync(message, cancellationToken);
            return true;
        }
        catch (SmtpException ex)
        {
            logger.LogError(ex, "Sending email to {To} failed.", to);
            return false;
        }
    }
}
