using FinanzApp.Api.Data.Entities;
using FinanzApp.Api.Infrastructure;

namespace FinanzApp.Api.Application;

/// <summary>
/// Was auf dem Blatt steht: sein Datum und die Frist, die es setzt.
/// </summary>
/// <remarks>
/// <para>Zwei Stellen fragen danach — das Einliefern eines neuen Belegs und das Nachziehen am
/// Bestand. Sie unterscheiden sich in genau einem Punkt, und der steht als Parameter da: Woran
/// erkennt man, dass das Dokumentdatum noch niemand gesetzt hat? Beim Einliefern ist das der
/// heutige Tag, beim Nachziehen der Tag der Ablage.</para>
/// <para>Alles andere ist an beiden Stellen gleich und gehört deshalb nur einmal hierher. Die
/// Schranke gegen ein Datum aus der Zukunft zweimal zu pflegen hieße, sie irgendwann nur noch
/// einmal zu pflegen.</para>
/// </remarks>
public static class PaperFacts
{
    /// <summary>Was dabei herauskam — für die Zählung beim Nachziehen.</summary>
    public readonly record struct Result(bool Dated, bool Demanded)
    {
        public bool Nothing => !Dated && !Demanded;
    }

    /// <summary>
    /// Trägt Datum und Frist am Beleg ein, soweit der Text sie hergibt.
    /// </summary>
    /// <param name="dateMarker">
    /// Der Wert, den <see cref="Document.DocumentDate"/> trägt, solange ihn niemand gesetzt hat.
    /// Weicht das Datum davon ab, bleibt es stehen: Dann hat es jemand gesetzt — oder ein
    /// genauerer Weg als ein Briefdatum, etwa der Stichtag eines Statusreports.
    /// </param>
    public static Result Apply(Document document, DateOnly today, DateOnly? dateMarker)
    {
        if (document.ContentText is not { Length: > 0 } text)
        {
            return new Result(false, false);
        }

        var gelesen = BillTextReader.Read(text);
        var dated = false;
        var demanded = false;

        if (dateMarker is { } marke
            && document.DocumentDate == marke
            && BillTextReader.DocumentDate(gelesen, today) is { } vomBlatt
            && vomBlatt != document.DocumentDate)
        {
            document.DocumentDate = vomBlatt;
            dated = true;
        }

        // Die Forderung nur, solange keine dasteht. Eine eingetragene stammt von einem
        // Menschen oder aus einem früheren Lesen — und ein zweites Lesen weiß es nicht besser.
        //
        // Was die Leseregel findet, ist immer eine Zahlungsfrist: Sie erkennt die Forderung am
        // Wort „fällig“ und seinesgleichen, und ohne Termin in der Zeile greift sie gar nicht.
        // Forderungen ohne Datum findet erst das Sprachmodell.
        if (string.IsNullOrEmpty(document.ActionLine)
            && gelesen.DueDate is { } frist
            && gelesen.DueDateSource is { Length: > 0 } zeile)
        {
            document.ActionLine = zeile;
            document.DueDate = frist;
            demanded = true;
        }

        return new Result(dated, demanded);
    }
}
