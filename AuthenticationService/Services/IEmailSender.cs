namespace AuthenticationService.Services;

/// <summary>Sends an email. Returns false when the message could not be sent.</summary>
public interface IEmailSender
{
    Task<bool> SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}
