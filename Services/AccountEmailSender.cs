using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;

namespace NinjaTrader_Xen.Services;

public sealed class AccountEmailSender(IConfiguration configuration)
{
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
}
