using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LotamSentinel.Application.Email;

/// <summary>Brevo (formerly Sendinblue) transactional-email HTTP API
/// options. Unlike SMTP, this talks to api.brevo.com over plain HTTPS
/// (port 443) - see BrevoEmailSender's class doc for why that matters.
/// Free tier: 300 emails/day, no credit card, and sending only needs a
/// "single sender" email verified by clicking a confirmation link (no
/// domain/DNS setup required) - see DEPLOYMENT.md "Email delivery"
/// section for the exact signup steps.</summary>
public class BrevoOptions
{
    public bool Enabled { get; set; } = false;
    public string ApiKey { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "LOTAM Sentinel";
}

/// <summary>HTTP-API email sender, as an alternative to SmtpEmailSender for
/// networks where raw SMTP (ports 25/465/587) is blocked or throttled by a
/// corporate firewall - very common on enterprise networks, which
/// typically allow arbitrary outbound HTTPS (port 443, since every browser
/// needs it) while blocking or heavily restricting SMTP specifically to
/// stop spam/data exfiltration via unapproved mail relays. That exact
/// symptom - a raw TCP test to smtp.gmail.com:587 succeeding while the
/// .NET process's own SMTP connection attempt times out - is what sending
/// over HTTPS instead sidesteps entirely, regardless of which network the
/// app happens to run on.
///
/// Selected instead of SmtpEmailSender via the Email:Provider=Brevo config
/// switch (see Program.cs) - SmtpEmailSender is left fully intact as the
/// default/fallback for hosts where SMTP works fine (e.g. a cloud
/// deployment on Render talking to Gmail).</summary>
public class BrevoEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly BrevoOptions _options;
    private readonly ILogger<BrevoEmailSender> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public BrevoEmailSender(HttpClient http, IOptions<BrevoOptions> options, ILogger<BrevoEmailSender> logger)
    {
        _http = http;
        _http.BaseAddress ??= new Uri("https://api.brevo.com/v3/");
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string toEmail, string subject, string htmlBody,
        List<EmailAttachment>? attachments = null, CancellationToken ct = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.FromEmail))
            throw new InvalidOperationException(
                "Brevo email is not configured. Set Brevo:Enabled=true and fill in Brevo:ApiKey/FromEmail in appsettings.json " +
                "(see DEPLOYMENT.md \"Email delivery\").");

        var payload = new BrevoSendRequest
        {
            Sender = new BrevoAddress { Email = _options.FromEmail, Name = _options.FromName },
            To = new List<BrevoAddress> { new() { Email = toEmail } },
            Subject = subject,
            HtmlContent = htmlBody,
            Attachment = attachments is { Count: > 0 }
                ? attachments.Select(a => new BrevoAttachment
                {
                    Name = a.FileName,
                    Content = Convert.ToBase64String(a.Content),
                }).ToList()
                : null,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "smtp/email")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("api-key", _options.ApiKey);

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Brevo API returned {(int)response.StatusCode} {response.StatusCode} sending to {toEmail}: {body}");
        }

        _logger.LogInformation("Email sent via Brevo to {ToEmail} — subject: {Subject}", toEmail, subject);
    }

    private class BrevoSendRequest
    {
        [JsonPropertyName("sender")] public BrevoAddress Sender { get; set; } = new();
        [JsonPropertyName("to")] public List<BrevoAddress> To { get; set; } = new();
        [JsonPropertyName("subject")] public string Subject { get; set; } = "";
        [JsonPropertyName("htmlContent")] public string HtmlContent { get; set; } = "";
        [JsonPropertyName("attachment")] public List<BrevoAttachment>? Attachment { get; set; }
    }

    private class BrevoAddress
    {
        [JsonPropertyName("email")] public string Email { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private class BrevoAttachment
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }
}
