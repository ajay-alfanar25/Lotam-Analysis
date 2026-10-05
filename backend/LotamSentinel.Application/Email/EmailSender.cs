using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace LotamSentinel.Application.Email;

/// <summary>SMTP connection details - fill these in appsettings.json (see
/// README "Email / SMTP setup" section). Nothing here is hardcoded to a
/// particular provider (Office 365, Gmail, an internal relay all work) -
/// it's a plain SMTP client either way.</summary>
public class SmtpOptions
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "LOTAM Sentinel";
    /// <summary>True for STARTTLS on port 587 (the common case) or implicit
    /// TLS on port 465. False only for an internal relay that genuinely
    /// doesn't use TLS.</summary>
    public bool UseSsl { get; set; } = true;
    /// <summary>Milliseconds to wait for the connect/handshake/send before
    /// giving up. MailKit honors this per-operation, so a blocked network
    /// path fails fast with a clear message instead of hanging.</summary>
    public int TimeoutMs { get; set; } = 20000;
}

public record EmailAttachment(string FileName, byte[] Content, string ContentType);

public interface IEmailSender
{
    Task SendAsync(
        string toEmail, string subject, string htmlBody,
        List<EmailAttachment>? attachments = null, CancellationToken ct = default);
}

/// <summary>MailKit-based SMTP sender. Used for: password-reset OTPs,
/// exported reports sent "to email" from any export button, and (once
/// notification subscriptions produce something to send) change-alert
/// digests.
///
/// Previously this used the built-in System.Net.Mail.SmtpClient. That type
/// is legacy (Microsoft's own docs say "we don't recommend that you use the
/// SmtpClient class for new development") and, on some networks, its
/// connect logic hangs indefinitely instead of failing cleanly - which is
/// exactly what a SocketException 10060 after a long wait usually means,
/// even when a plain TCP test (e.g. PowerShell's Test-NetConnection) to the
/// same host/port succeeds instantly. MailKit's SmtpClient has more robust,
/// actively-maintained connection handling and (via Timeout below) an
/// explicit fast-fail instead of an indefinite hang.</summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string toEmail, string subject, string htmlBody,
        List<EmailAttachment>? attachments = null, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Host) || string.IsNullOrWhiteSpace(_options.FromEmail))
            throw new InvalidOperationException(
                "SMTP is not configured. Set Smtp:Enabled=true and fill in Smtp:Host/Username/Password/FromEmail in appsettings.json.");

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        if (attachments is { Count: > 0 })
        {
            foreach (var a in attachments)
                bodyBuilder.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        }
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient { Timeout = _options.TimeoutMs };

        try
        {
            // Port 465 is implicit TLS; 587 (the common case) is STARTTLS.
            // SslOnConnect picks itself correctly for either when Port is
            // set accurately, so we only need to decide TLS vs. none here.
            var socketOptions = _options.UseSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None;
            await client.ConnectAsync(_options.Host, _options.Port, socketOptions, ct);

            if (!string.IsNullOrWhiteSpace(_options.Username))
                await client.AuthenticateAsync(_options.Username, _options.Password, ct);

            await client.SendAsync(message, ct);
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true, ct);
        }

        _logger.LogInformation("Email sent to {ToEmail} — subject: {Subject}", toEmail, subject);
    }
}
