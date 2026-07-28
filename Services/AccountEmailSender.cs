using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;

namespace NinjaTrader_Xen.Services;

public sealed class AccountEmailSender(IConfiguration configuration)
{
    public async Task SendFeedbackAsync(
        string type,
        string comment,
        int subscriberId,
        string userEmail,
        string browser,
        FeedbackDiagnostics? diagnostics)
    {
        var section = configuration.GetSection("Email");
        string Required(string key) =>
            section[key] ?? throw new InvalidOperationException($"Email:{key} is not configured.");
        static string Safe(string? value) =>
            HtmlEncoder.Default.Encode(value ?? "Not included");

        using var client = new SmtpClient(Required("Host"), int.Parse(Required("Port")))
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(Required("Username"), Required("Password"))
        };

        var reportName = type.Equals("bug", StringComparison.OrdinalIgnoreCase)
            ? "Bug or code problem"
            : "Feedback or suggestion";
        var diagnosticHtml = diagnostics is null
            ? "<p><em>The user chose not to include workspace diagnostics.</em></p>"
            : $"""
                <p><strong>Project ID:</strong> {Safe(diagnostics.ProjectId?.ToString())}<br />
                <strong>Task:</strong> {Safe(diagnostics.Task)}<br />
                <strong>Model:</strong> {Safe(diagnostics.Model)}<br />
                <strong>Build version:</strong> {Safe(diagnostics.AppVersion)}</p>
                <h3>Latest user prompt</h3>
                <pre style="white-space:pre-wrap">{Safe(diagnostics.UserPrompt)}</pre>
                <h3>Latest Xen response</h3>
                <pre style="white-space:pre-wrap">{Safe(diagnostics.AssistantOutput)}</pre>
                <h3>Latest generated NinjaScript C#</h3>
                <pre style="white-space:pre-wrap">{Safe(diagnostics.LatestCode)}</pre>
                """;

        using var message = new MailMessage
        {
            From = new MailAddress(Required("From")),
            Subject = $"NinjaTrader Xen: {reportName}",
            Body = $"""
                <h2>{Safe(reportName)}</h2>
                <p>{Safe(comment)}</p>
                <hr />
                <p><strong>Subscriber ID:</strong> {subscriberId}<br />
                <strong>User email:</strong> {Safe(userEmail)}<br />
                <strong>Browser:</strong> {Safe(browser)}</p>
                {diagnosticHtml}
                """,
            IsBodyHtml = true
        };
        message.To.Add(Required("NotificationEmail"));
        await client.SendMailAsync(message);
    }

    public async Task SendVerificationAsync(string recipient, string verificationUrl)
    {
        var section = configuration.GetSection("Email");

        string Required(string key) =>
            section[key] ?? throw new InvalidOperationException($"Email:{key} is not configured.");

        using var client = new SmtpClient(Required("Host"), int.Parse(Required("Port")))
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(Required("Username"), Required("Password"))
        };

        var safeUrl = HtmlEncoder.Default.Encode(verificationUrl);
        using var message = new MailMessage
        {
            From = new MailAddress(Required("From")),
            Subject = "Verify your NinjaTrader Xen account",
            Body = $"""
                <p>Welcome to ClickAlgo NinjaTrader Xen.</p>
                <p>Please verify your email address:</p>
                <p><a href="{safeUrl}">{safeUrl}</a></p>
                <p>This link expires in 24 hours.</p>
                """,
            IsBodyHtml = true
        };

        message.To.Add(recipient);
        await client.SendMailAsync(message);
    }

    public async Task SendFreeTrialActivatedAsync(
        string recipient,
        decimal amountGbp,
        DateTime expiresUtc)
    {
        var section = configuration.GetSection("Email");

        string Required(string key) =>
            section[key] ?? throw new InvalidOperationException($"Email:{key} is not configured.");

        using var client = new SmtpClient(Required("Host"), int.Parse(Required("Port")))
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(Required("Username"), Required("Password"))
        };

        using var message = new MailMessage
        {
            From = new MailAddress(Required("From")),
            Subject = "Your NinjaTrader Xen free credit is active",
            Body = $"""
                <p>Your £{amountGbp:0.00} NinjaTrader Xen introductory credit is now active.</p>
                <p>It expires at {expiresUtc:dd MMMM yyyy HH:mm} UTC.</p>
                <p>Sign in to Xen to start your first NinjaScript project.</p>
                """,
            IsBodyHtml = true
        };

        message.To.Add(recipient);
        await client.SendMailAsync(message);
    }
}

public sealed record FeedbackDiagnostics(
    Guid? ProjectId,
    string? Model,
    string? Task,
    string? AppVersion,
    string? UserPrompt,
    string? AssistantOutput,
    string? LatestCode);
