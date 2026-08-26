using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace FinanzApp.Api.Infrastructure;

/// <summary>Zugangsdaten des Postausgangsservers, aus dem Abschnitt <c>Mail</c> der Konfiguration.</summary>
public sealed class MailOptions
{
    public const string SectionName = "Mail";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;

    /// <summary>STARTTLS auf dem Klartext-Port. Für Port 465 auf <c>false</c> setzen.</summary>
    public bool UseStartTls { get; set; } = true;

    public string? User { get; set; }
    public string? Password { get; set; }
    /// <summary>
    /// Absender. Leer heißt: dieselbe Adresse, mit der wir uns anmelden. Die meisten Anbieter,
    /// mail.de eingeschlossen, weisen eine fremde Absenderadresse zurück.
    /// </summary>
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "FinanzApp";

    /// <summary>Was am Ende im Von-Feld steht.</summary>
    public string EffectiveFromAddress =>
        !string.IsNullOrWhiteSpace(FromAddress) ? FromAddress : User ?? string.Empty;

    /// <summary>
    /// Erst wenn Host <em>und</em> Passwort dastehen.
    /// </summary>
    /// <remarks>
    /// Am Host allein zu erkennen reichte nicht: dann würde eine vorbereitete, aber noch
    /// geheimnislose Konfiguration den echten Versand einschalten, jede Nachricht an der
    /// Anmeldung scheitern — und der Link stünde nicht mehr im Protokoll, wo man ihn ohne
    /// Postausgang braucht. Ein Relay ganz ohne Anmeldung ist damit nicht vorgesehen.
    /// </remarks>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Password);
}

public interface IMailSender
{
    Task SendAsync(string toAddress, string toName, string subject, string body, CancellationToken ct = default);
}

/// <summary>Versand über SMTP.</summary>
public sealed class SmtpMailSender(MailOptions options, ILogger<SmtpMailSender> log) : IMailSender
{
    /// <summary>Wird nur registriert, wenn ein Host konfiguriert ist — die Prüfung hält das fest.</summary>
    private string Host => options.Host
                           ?? throw new InvalidOperationException("Mail:Host ist nicht konfiguriert.");

    public async Task SendAsync(
        string toAddress, string toName, string subject, string body, CancellationToken ct = default)
    {
        var message = new MimeMessage
        {
            Subject = subject,
            Body = new TextPart("plain") { Text = body },
        };
        message.From.Add(new MailboxAddress(options.FromName, options.EffectiveFromAddress));
        message.To.Add(new MailboxAddress(toName, toAddress));

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(
                Host,
                options.Port,
                options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect,
                ct);

            if (!string.IsNullOrWhiteSpace(options.User))
            {
                await client.AuthenticateAsync(options.User, options.Password ?? string.Empty, ct);
            }

            await client.SendAsync(message, ct);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, ct);
            }
        }

        log.LogInformation("Mail an {Empfänger} versendet: {Betreff}", toAddress, subject);
    }
}

/// <summary>
/// Ersatz, solange kein Postausgangsserver konfiguriert ist: die Nachricht landet im Protokoll.
/// </summary>
/// <remarks>
/// Damit bleibt der Passwort-Reset auch ohne SMTP-Zugang durchspielbar — der Link steht dann in
/// der Konsole. Im Produktivbetrieb muss <c>Mail:Host</c> gesetzt sein, sonst erreicht kein
/// Zurücksetzen jemals den Empfänger.
/// </remarks>
public sealed class LoggingMailSender(MailOptions options, ILogger<LoggingMailSender> log) : IMailSender
{
    /// <summary>Was genau fehlt — dieselbe Auskunft wie die Startmeldung.</summary>
    private string Missing => string.IsNullOrWhiteSpace(options.Host)
        ? "Mail:Host ist nicht gesetzt"
        : "Mail:Password fehlt";

    public Task SendAsync(
        string toAddress, string toName, string subject, string body, CancellationToken ct = default)
    {
        log.LogWarning(
            "Kein Postausgang aktiv ({Grund}). Nachricht an {Empfänger} nicht versendet.\n" +
            "Betreff: {Betreff}\n{Text}",
            Missing, toAddress, subject, body);

        return Task.CompletedTask;
    }
}
