using System.Globalization;
using FinanzApp.Shared.Contracts;

namespace FinanzApp.Api.Application;

/// <summary>
/// Was ein gelesener Beleg einem Anlegeformular vorschlägt.
/// </summary>
/// <remarks>
/// <para>Der Lieferschein liegt vor einem, und das Objekt dazu gibt es noch nicht. Was dann
/// folgte, war Abtippen: Händler, Kaufdatum und Preis stehen auf dem Blatt und standen auch
/// schon im gelesenen Text — nur führte von dort kein Weg ins Formular.</para>
/// <para><b>Vorschlag und nicht Übernahme.</b> Die Felder stehen vorbefüllt da und werden
/// gespeichert, wenn jemand auf Anlegen drückt. Das ist der Unterschied zur Werteübernahme bei
/// Versicherungsbelegen, die eine Bestätigung je Wert verlangt: Dort wandert eine Zahl in eine
/// Vermögenssumme, hier in ein Objekt, das der Mensch gerade selbst anlegt und vor sich sieht.</para>
/// <para><b>Was nicht gelesen wurde, bleibt leer.</b> Kein Vorschlag ist besser als ein
/// schlechter — ein falsch geratener Preis fällt niemandem auf, ein leeres Feld schon.</para>
/// </remarks>
public static class BillFormProposals
{
    /// <summary>
    /// Die Vorschläge für ein Formular, nach Feldschlüssel.
    /// </summary>
    /// <remarks>
    /// <para>Nur die Anschaffung. Sie ist der Typ, der aus einem Beleg entsteht — eine
    /// Immobilie oder ein Darlehen entsteht aus einem Vertrag, und was dort auf dem Blatt steht,
    /// hat mit den Feldern des Formulars wenig zu tun.</para>
    /// <para>Die Bezeichnung fehlt bewusst. Sie ist das eine Feld, das ein Mensch besser weiß:
    /// Auf dem Blatt steht die Artikelzeile des Herstellers, und eine reine Artikelnummer ist kein Name,
    /// unter dem jemand das Gerät wiederfindet.</para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> For(
        CreateObjectType type, ExtractedBillDto gelesen)
    {
        if (type != CreateObjectType.Purchase || !gelesen.HasContent)
        {
            return new Dictionary<string, string>();
        }

        var vorschlaege = new Dictionary<string, string>(StringComparer.Ordinal);

        if (gelesen.Provider is { Length: > 0 } haendler)
        {
            vorschlaege["vendor"] = haendler;
        }

        if (gelesen.BillDate is { } belegdatum)
        {
            vorschlaege["purchase"] = belegdatum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (gelesen.GrossAmount is { } brutto and > 0)
        {
            vorschlaege["price"] = brutto.ToString("0.00", CultureInfo.GetCultureInfo("de-DE"));
        }

        // Die Belegnummer hat kein eigenes Feld und gehört trotzdem an das Objekt: Ohne sie
        // steht später eine Anschaffung da, zu der sich der Beleg nicht mehr zuordnen lässt,
        // falls die Verknüpfung einmal verloren geht.
        if (gelesen.BillNumber is { Length: > 0 } nummer)
        {
            vorschlaege["note"] = $"Beleg-Nr. {nummer}";
        }

        return vorschlaege;
    }
}
