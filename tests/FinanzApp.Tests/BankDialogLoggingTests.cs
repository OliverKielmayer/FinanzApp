using libfintx.FinTS;
using libfintx.FinTS.Data;
using Microsoft.Extensions.Logging;

namespace FinanzApp.Tests;

/// <summary>
/// Der Bankdialog darf ins Protokoll — die Zugangsdaten nicht.
/// </summary>
/// <remarks>
/// <para><b>Warum das überhaupt protokolliert wird.</b> Scheitert ein Abruf beim Zerlegen der
/// Bankantwort, sagt das Protokoll der Anwendung bewusst nichts über den Text der Bibliothek:
/// In deren Nachrichtenstrom kann die PIN stehen. Damit sieht man, <em>dass</em> die Antwort
/// nicht lesbar war, nie <em>was</em> die Bank geschickt hat — und kommt nicht weiter.</para>
/// <para><b>Deshalb bekommt die Bibliothek eine <see cref="ILoggerFactory"/>.</b> Sie ersetzt
/// PIN und Benutzerkennung durch <c>XXXXXX</c>, bevor sie eine Zeile abgibt. Das ist eine
/// Eigenschaft der Bibliothek, keine der Anwendung — also wird sie hier gemessen und nicht
/// angenommen. Hört sie mit einer neuen Fassung auf zu maskieren, fällt dieser Test, und nicht
/// erst jemandem das Protokoll auf.</para>
/// <para><b>Ohne Bank.</b> Der Versuch zeigt auf eine Gegenstelle, die niemand bedient — eine
/// Adresse im eigenen Rechner, kein Netz. Gebaut und protokolliert wird die <em>ausgehende</em>
/// Nachricht trotzdem, und genau sie trägt die Zugangsdaten.</para>
/// <para><b>Er kostet rund vier Sekunden.</b> Die Bibliothek gibt nicht sofort auf, auch wenn
/// die Verbindung abgewiesen wird. Deshalb läuft er einmal für beide Fragen. Vier Sekunden je
/// Lauf sind der Preis dafür, dass ein Fassungswechsel der Bibliothek nicht unbemerkt die PIN
/// ins Protokoll schreibt.</para>
/// </remarks>
public sealed class BankDialogLoggingTests
{
    /// <summary>Eine Zeichenkette, die nirgendwo sonst vorkommt — sonst bewiese der Test nichts.</summary>
    private const string ErfundenePin = "PINPINPIN987654";

    private const string ErfundeneKennung = "BENUTZER4711";

    /// <summary>Port 1 bedient niemand; die Verbindung scheitert sofort und ohne Netz.</summary>
    private const string TotesZiel = "http://127.0.0.1:1/keine-bank";

    /// <summary>
    /// Ein Versuch für alle Fragen daran.
    /// </summary>
    /// <remarks>
    /// xUnit baut die Klasse je Test neu, ein Feld am Objekt würde also nichts teilen. Der
    /// Dialog kostet ein paar Sekunden — zweimal dasselbe zu fahren, um zwei Fragen an
    /// dieselben Zeilen zu stellen, wäre verschenkte Zeit in jedem Lauf.
    /// </remarks>
    private static readonly Lazy<Task<IReadOnlyList<string>>> Versuch =
        new(DialogVersuchenAsync);

    [Fact]
    public async Task Das_Protokoll_der_Bibliothek_zeigt_weder_PIN_noch_Kennung()
    {
        var zeilen = await Versuch.Value;

        Assert.NotEmpty(zeilen);
        Assert.DoesNotContain(zeilen, z => z.Contains(ErfundenePin, StringComparison.Ordinal));
        Assert.DoesNotContain(zeilen, z => z.Contains(ErfundeneKennung, StringComparison.Ordinal));
    }

    /// <summary>
    /// Und es zeigt, dass maskiert wurde — nicht bloß, dass nichts dastand.
    /// </summary>
    /// <remarks>
    /// Ohne diese zweite Frage bestünde der Test auch dann, wenn die Bibliothek gar nichts mehr
    /// protokollierte. Schweigen ist nicht dasselbe wie Maskieren: Das eine nähme uns die
    /// Auskunft, für die der Weg gebaut ist.
    /// </remarks>
    [Fact]
    public async Task Das_Protokoll_zeigt_die_Maske_an_ihrer_Stelle()
    {
        var zeilen = await Versuch.Value;

        Assert.Contains(zeilen, z => z.Contains("XXXXXX", StringComparison.Ordinal));
    }

    /// <summary>
    /// Ein Abruf gegen eine tote Gegenstelle, mit erfundenen Zugangsdaten.
    /// </summary>
    private static async Task<IReadOnlyList<string>> DialogVersuchenAsync()
    {
        var zeilen = new List<string>();

        using var fabrik = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(new SammelProvider(zeilen));
        });

        var verbindung = new ConnectionDetails
        {
            Blz = 12345678,
            Url = TotesZiel,
            UserId = ErfundeneKennung,
            Pin = ErfundenePin,
            Iban = "DE02120300000000202051",
            Account = "202051",
            FinTSVersion = FinTsVersion.v300,
        };

        var kunde = new FinTsClient(verbindung, loggerFactory: fabrik);

        try
        {
            await kunde.Balance(new TANDialog(_ => Task.FromResult(string.Empty)));
        }
        catch (Exception)
        {
            // Erwartet: Die Gegenstelle antwortet nicht. Geprüft wird, was bis dahin ins
            // Protokoll ging — die ausgehende Nachricht ist da längst gebaut.
        }

        lock (zeilen)
        {
            return [.. zeilen];
        }
    }

    /// <summary>Sammelt, was die Bibliothek protokolliert, statt es auszugeben.</summary>
    private sealed class SammelProvider(List<string> ziel) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Sammler(ziel);

        public void Dispose()
        {
        }

        private sealed class Sammler(List<string> ziel) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
                => new Nichts();

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var text = formatter(state, exception);
                if (exception is not null)
                {
                    text += $" || {exception.Message}";
                }

                lock (ziel)
                {
                    ziel.Add(text);
                }
            }

            private sealed class Nichts : IDisposable
            {
                public void Dispose()
                {
                }
            }
        }
    }
}
