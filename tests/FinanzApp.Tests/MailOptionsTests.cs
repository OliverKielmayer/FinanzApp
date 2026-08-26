using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Tests;

/// <summary>
/// Wann der echte Postausgang benutzt wird und wer als Absender dasteht.
/// </summary>
/// <remarks>
/// Am Host allein zu erkennen, ob Mail konfiguriert ist, war zu wenig: eine vorbereitete, aber
/// noch geheimnislose Konfiguration hätte den SMTP-Versand eingeschaltet, jede Nachricht wäre an
/// der Anmeldung gescheitert — und der Link hätte nicht mehr im Protokoll gestanden, wo man ihn
/// ohne Postausgang braucht.
/// </remarks>
public sealed class MailOptionsTests
{
    [Fact]
    public void Ohne_alles_bleibt_es_beim_Protokoll()
        => Assert.False(new MailOptions().IsConfigured);

    [Fact]
    public void Ein_Host_allein_schaltet_den_Versand_noch_nicht_ein()
    {
        // Genau der Zustand, den appsettings.json mitbringt: vorbereitet, aber ohne Geheimnis.
        var options = new MailOptions { Host = "smtp.mail.de", Port = 587 };

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Host_und_Passwort_zusammen_schalten_ihn_ein()
    {
        var options = new MailOptions
        {
            Host = "smtp.mail.de",
            User = "wer@mail.de",
            Password = "steht-nur-im-Geheimnisspeicher",
        };

        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Ohne_eigene_Absenderadresse_gilt_die_Anmeldeadresse()
    {
        // mail.de weist eine fremde Absenderadresse zurück — der Rückfall erspart genau den Fehler.
        var options = new MailOptions { User = "wer@mail.de" };

        Assert.Equal("wer@mail.de", options.EffectiveFromAddress);
    }

    [Fact]
    public void Eine_gesetzte_Absenderadresse_bleibt_stehen()
    {
        var options = new MailOptions { User = "wer@mail.de", FromAddress = "noreply@eigene-domain.de" };

        Assert.Equal("noreply@eigene-domain.de", options.EffectiveFromAddress);
    }
}
