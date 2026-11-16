using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace FinanzApp.Ordnerdienst;

/// <summary>Ein Anhang, so wie er aus der Mail kommt.</summary>
/// <remarks>
/// Die Bytes und nicht ein Pfad: Ein Anhang lag nie auf der Platte, und ihn erst dorthin zu
/// schreiben hieße, ein zweites Original anzulegen, das danach jemand aufräumen muss.
/// </remarks>
public sealed record MailAttachment(string FileName, byte[] Content);

/// <summary>Eine Mail, reduziert auf das, was der Eingang braucht.</summary>
public sealed record MailItem(
    string Id,
    string Subject,
    string From,
    DateTimeOffset Date,
    IReadOnlyList<MailAttachment> Attachments)
{
    /// <summary>Woher ein Beleg kam, in einem Satz — steht danach am Dokument.</summary>
    public string Source => $"Postfach: „{Subject}“ von {From} ({Date:dd.MM.yyyy})";
}

/// <summary>Ein geöffnetes Postfach. Die Verbindung lebt, solange die Sitzung lebt.</summary>
public interface IMailSession : IAsyncDisposable
{
    /// <summary>Was in diesem Durchgang mitgenommen wird.</summary>
    IReadOnlyList<MailItem> Messages { get; }

    /// <summary>Legt eine Mail in den Erledigt-Ordner.</summary>
    Task DoneAsync(MailItem item, CancellationToken ct);
}

/// <summary>
/// Der Weg zum Postfach.
/// </summary>
/// <remarks>
/// Eine Schnittstelle für eine Umsetzung, und aus demselben Grund wie bei
/// <see cref="IIntakeClient"/>: Was der Durchgang entscheidet — welcher Anhang hinausgeht, wann
/// eine Mail als erledigt gilt, was bei einem Fehler liegen bleibt — ließe sich sonst nur mit
/// einem echten IMAP-Server prüfen, der auf Kommando das Richtige tut. So einen Server gibt es
/// nicht.
/// </remarks>
public interface IMailbox
{
    /// <summary>Öffnet das Postfach und holt den nächsten Schwung.</summary>
    Task<IMailSession> OpenAsync(CancellationToken ct);
}

/// <summary>Das Postfach über IMAP, mit MailKit.</summary>
/// <remarks>
/// <para>Dieselbe Bibliothek, die die Anwendung zum Versenden benutzt — <c>MailKit.Net.Imap</c>
/// liegt ohnehin daneben.</para>
/// <para><b>Gelesen wird nur der eingestellte Ordner.</b> Nicht der Posteingang, außer jemand
/// trägt ihn ausdrücklich ein. Siehe <see cref="MailboxOptions.Folder"/>.</para>
/// </remarks>
public sealed class Mailbox(MailboxOptions options, ILogger<Mailbox> log) : IMailbox
{
    public async Task<IMailSession> OpenAsync(CancellationToken ct)
    {
        var client = new ImapClient();

        try
        {
            await client.ConnectAsync(
                options.Host,
                options.Port,
                options.UseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
                ct);

            await client.AuthenticateAsync(options.User, options.Password, ct);

            var quelle = await ResolveAsync(client, options.Folder.Trim(), create: false, ct);
            await quelle.OpenAsync(FolderAccess.ReadWrite, ct);

            // Der Erledigt-Ordner wird angelegt, wenn er fehlt. Ohne ihn liefe der Dienst
            // durch, könnte aber nichts wegräumen — und brächte beim nächsten Durchgang
            // dieselben Mails noch einmal.
            var erledigt = await ResolveAsync(
                client, options.ResolvedDoneFolder, create: true, ct, unter: quelle);

            var uids = await quelle.SearchAsync(SearchQuery.All, ct);
            var genommen = uids.Take(Math.Max(1, options.MaxPerSweep)).ToList();

            List<MailItem> mails = [];
            foreach (var uid in genommen)
            {
                var message = await quelle.GetMessageAsync(uid, ct);
                mails.Add(Read(uid, message));
            }

            if (uids.Count > genommen.Count)
            {
                log.LogInformation(
                    "Im Postfachordner {Ordner} liegen {Gesamt} Mails; dieser Durchgang nimmt "
                        + "{Genommen} mit.",
                    options.Folder, uids.Count, genommen.Count);
            }

            return new ImapSession(client, quelle, erledigt, mails);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Baut aus der Nachricht das, was der Eingang braucht.
    /// </summary>
    /// <remarks>
    /// <b>Nur echte Anhänge.</b> <see cref="MimeMessage.Attachments"/> lässt den Nachrichtentext
    /// und eingebettete Bilder einer HTML-Mail aussen vor — sonst käme aus jeder Signatur ein
    /// Logo als Beleg in der Ablage an.
    /// </remarks>
    private static MailItem Read(UniqueId uid, MimeMessage message)
    {
        List<MailAttachment> anhaenge = [];

        foreach (var teil in message.Attachments.OfType<MimePart>())
        {
            var name = teil.FileName;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            // Ein Teil ohne Inhalt ist kein Anhang, sondern ein Kopf ohne Rumpf. Er kommt
            // bei beschädigten Nachrichten vor, und eine leere Datei abzulegen wäre die
            // schlechteste Antwort darauf.
            if (teil.Content is not { } inhalt)
            {
                continue;
            }

            using var speicher = new MemoryStream();
            inhalt.DecodeTo(speicher);
            anhaenge.Add(new MailAttachment(name.Trim(), speicher.ToArray()));
        }

        var absender = message.From.Mailboxes.FirstOrDefault();

        return new MailItem(
            uid.ToString(),
            string.IsNullOrWhiteSpace(message.Subject) ? "(ohne Betreff)" : message.Subject.Trim(),
            absender?.Address ?? "(unbekannt)",
            message.Date,
            anhaenge);
    }

    /// <summary>
    /// Findet einen Ordner im Postfach, und legt ihn auf Wunsch an.
    /// </summary>
    /// <remarks>
    /// <para><c>INBOX</c> ist der einzige Name, den jeder Server gleich schreibt. Alles andere
    /// hängt unter dem persönlichen Namensraum, und das Trennzeichen darin ist je nach Server
    /// ein Punkt oder ein Schrägstrich — deshalb wird nicht zusammengesetzt, sondern gesucht.</para>
    /// <para>Der Erledigt-Ordner entsteht als Unterordner des angesehenen: Er gehört dorthin,
    /// und so steht beides nebeneinander, wenn jemand ins Postfach sieht.</para>
    /// </remarks>
    private static async Task<IMailFolder> ResolveAsync(
        ImapClient client, string name, bool create, CancellationToken ct,
        IMailFolder? unter = null)
    {
        if (unter is null && string.Equals(name, "INBOX", StringComparison.OrdinalIgnoreCase))
        {
            return client.Inbox;
        }

        var eltern = unter ?? client.GetFolder(client.PersonalNamespaces[0]);

        try
        {
            return await eltern.GetSubfolderAsync(name, ct);
        }
        catch (FolderNotFoundException)
        {
            if (!create)
            {
                throw;
            }

            // CreateAsync ist als nullbar beschrieben; ein Server, der das Anlegen mit
            // „geht in Ordnung“ und ohne Ordner beantwortet, wäre allerdings kaputt. Die Zeile
            // steht hier, damit der Fall einen Namen hat statt einer NullReferenceException
            // drei Aufrufe später.
            return await eltern.CreateAsync(name, isMessageFolder: true, ct)
                   ?? throw new InvalidOperationException(
                       $"Der Postfachordner „{name}“ liess sich nicht anlegen.");
        }
    }

    private sealed class ImapSession(
        ImapClient client,
        IMailFolder quelle,
        IMailFolder erledigt,
        IReadOnlyList<MailItem> messages) : IMailSession
    {
        public IReadOnlyList<MailItem> Messages { get; } = messages;

        public async Task DoneAsync(MailItem item, CancellationToken ct)
        {
            if (UniqueId.TryParse(item.Id, out var uid))
            {
                await quelle.MoveToAsync(uid, erledigt, ct);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(quit: true, CancellationToken.None);
            }

            client.Dispose();
        }
    }
}
