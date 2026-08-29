# Offene Punkte

Stand **29.08.2026**, nach dem Steuerjahr-Paket aus Handoff 16. Eine Liste, kein Plan: sie sagt,
was noch nicht gebaut ist und woher die Anforderung stammt — die Reihenfolge steht in den
Handoffs selbst, nicht hier.

Fertig und damit hier nicht mehr aufgeführt: der v4-Handoff samt Nachtrag
([`handoff-v4-umsetzung.md`](handoff-v4-umsetzung.md)), die Schritte 1–6 des
[Erweiterungsplans](erweiterungsplan.md), der v5-Navigationsumbau, aus der v5-Erweiterung die
Abschnitte 8 (Arbeit & Beruf) und 9 (Dokumenttypen) sowie aus
[Handoff 11](design-handoff-v5c/design_handoff_v5/README.md) das überarbeitete Vermögensmodell
(§3b) und die Lade-, Leer-, Offline- und Fehlerzustände (§7); aus Handoff 13 der ganze
Depot-Abschnitt (§11) — Transaktionen, abgeleitete Positionen, Quartalsaufstellungen und
Bestandsabgleich; aus [Handoff 14](design-handoff-v5d/design_handoff_v5/README.md) die
PKV-Bilanz (§12); aus [Handoff 15](design-handoff-v5e/design_handoff_v5/README.md) das
Einlesen von PDF-Dokumenten (§14); aus
[Handoff 16](design-handoff-v5f/design_handoff_v5/README.md) das Steuerjahr-Paket mit Druckblatt
und den vier entschiedenen Rückfragen (§15).

## Was beim PDF-Scan (§14) offen blieb

Gebaut ist der ganze Abschnitt: Dokumenttyp-Modell, beide Arten, Analyse-Schrittkette,
Vorschlag, Werteprüfung mit Herkunftsseite und die Bestätigung, die die Wirkung nennt. Geprüft
an den beiden echten PDFs des Nutzers. Was dabei liegen blieb:

- **Eine Position je Quartalsaufstellung.** Der Extraktor nimmt je Feld den ersten Treffer; ein
  Depot mit drei Fonds läse nur den ersten. Für mehrere bräuchte der Typ eine Wiederholgruppe.
  Bis dahin bleibt für solche Aufstellungen die Erfassung von Hand aus §11.2.
- **Keine Texterkennung.** Beide Originale tragen eine Textebene — der Statusreport eine
  unsichtbare hinter vier Seitenbildern. Ein abfotografierter Beleg wird deshalb erkannt,
  abgelegt und mit leerer Maske gemeldet, aber nicht gelesen. Die Schnittstelle
  `IPdfTextReader` ist der Platz, an dem eine Erkennung andockt.
- **Die gelernte Ablageregel wird angezeigt, aber nicht gespeichert.** „Absender X + Typ Y →
  künftig automatisch hierher“ steht auf der Bestätigungsseite; beim nächsten Beleg entsteht
  der Vorschlag wieder aus dem Typ-Datensatz und nicht aus einer gelernten Regel.
- **Kein Weg, die vorgeschlagene Ablage zu ändern.** Der Prototyp zeigt den Knopf und lässt ihn
  ins Leere laufen; hier fehlt er ganz. Der Pfad entsteht aus Bereich, Objekt und Jahr.

Zwei Befunde aus den echten Dateien, die im Handoff so nicht stehen und beim Weiterbauen zählen:

- Der Handoff teilt Statusreporte in „Original mit Textebene“ und „abfotografiert, OCR nötig“.
  Das reale Original ist beides zugleich: vier Seitenbilder mit einer unsichtbaren Textebene
  darunter. Lesbar, aber nicht das Sichtbare — deshalb zählt so ein Wert etwas weniger.
- In der Textebene desselben Dokuments steht die Wertspalte stellenweise um **eine Zeile**
  gegen die Beschriftungen versetzt, an anderen Stellen nicht. Wer Zeilen zählt, liest dort die
  Abschnittsüberschrift als Rückkaufswert. Die Zuordnung hängt deshalb an Abschnitt und
  Beschriftung, und am Ende prüft eine Rechenprobe nach: Rückkaufswert + Ansammlungsguthaben
  muss die ausgewiesene Gesamtleistung ergeben, Nominale × Kurs den Kurswert.

## Aus dem v5-Handoff, Abschnitt 10

Die Zeile „Ladezustände, Offline, Fehlerdialoge“ steht dort noch, obwohl Handoff 11 sie in §7
ausführt und sie inzwischen gebaut sind — im Handoff stehengeblieben, hier gestrichen.

| Punkt | Was fehlt |
| --- | --- |
| Zwei-Faktor-Anmeldung | Die Einstellungen zeigen „2FA aus" als Zustand an, es gibt nichts dahinter. |
| Rechtematrix im Detail | Es gibt drei Rollen (Owner, Member, ReadOnly) und Kontofreigaben. Was genau jede Rolle je Bereich darf, ist nirgends niedergeschrieben. |
| Split-Buchung | Eine Buchung auf mehrere Kategorien aufteilen. |
| Sondertilgung | Beim Darlehen: außerplanmäßige Tilgung samt Wirkung auf Restschuld und Laufzeit. |
| CSV-Spalten-Mapping | Der Import kennt feste Profile. Ein unbekanntes Format lässt sich nicht von Hand zuordnen. |
| Ablaufende Dokumente als Fristquelle | Schritt 7 des Erweiterungsplans, die eine Hälfte, die nicht mitkam: `TaskItem` leitet Fristen aus Verträgen, Rechnungen und Erstattungen ab, aber nicht aus Dokumenten mit Ablaufdatum. |

## Die drei verbliebenen Berichte (v4-Handoff 10b)

Gebaut sind Kostentrend, Fixkosten & vertragliche Bindung, Depot-G/V, Datenqualität, die
PKV-Bilanz, das Steuerjahr, gespeicherte Ansichten, CSV und Druckansicht. Der Handoff markiert
die folgenden drei als **„vor dem Bau anfragen"**:

1. **Vermögensentwicklung nach Klasse** — mit dem Stichtagsproblem: Depotkurse sind
   tagesaktuell, Lebensversicherungswerte bis zu ein Jahr alt. Eine Kurve, die beides in einer
   Linie führt, behauptet eine Gleichzeitigkeit, die es nicht gibt.
2. **Objektkosten** — Immobilie €/Monat und €/m², Fahrzeuge Gesamtkosten und €/km.
3. **Liquiditätsprognose 3–6 Monate.**

## Aus der Dateiablage ersichtlich, nirgends abgebildet

- **Steuer nach Jahr** als eigener Bereich. Der Bericht aus §15 ist die Auswertung, nicht die
  Ablage — er sammelt Kandidaten, verwaltet aber keine Steuerjahre mit Bescheiden und Fristen.
- **Unterhalt / Scheidung** als Vorgangstyp mit Zahlungsverfolgung.

## Was beim Steuerjahr-Paket (§15) offen blieb

Gebaut ist der Bericht mit seinen vier Abschnitten, den beiden getrennten Kennzeichen, dem
Druckblatt, der Brücke aus der PKV-Bilanz und der sichtbaren Rechenprobe beim Einlesen. Was
liegen blieb:

- **Die steuerliche Einordnung einer Kategorie lässt sich nur im Seed setzen.** `Category`
  trägt jetzt `TaxCategory`, aber der Kategorienschirm zeigt das Feld nicht. Ohne eine
  eingeordnete Kategorie bleiben Handwerkerleistungen und Werbungskosten aus Buchungen leer.
- **Entfernung und Arbeitstage lassen sich nur beim Anlegen mitgeben**, nicht nachträglich am
  Arbeitsverhältnis ändern — die Felder stehen im Modell, aber nicht in der Bearbeitenmaske.
- **Handwerkerleistungen trennen Arbeitslohn und Material nicht.** Der Bericht sagt das im
  Klartext; die Trennung selbst bräuchte eine Angabe je Rechnung.
- **Der Druck geht über `window.print()`.** Das Blatt ist gestaltet und geprüft, gedruckt hat es
  noch niemand — das Ergebnis hängt am Browser und seinen Rändern.

Eine Abweichung vom Prototyp, die bewusst ist: **der Bericht zeigt Cent**, nicht gerundete Euro.
Der Sprung aus der PKV-Bilanz stellte sonst 562 € neben 561,60 € für dieselbe Zahl. Was in ein
Formular abgetippt wird, gehört genau hin.

## Gebaut, aber nie angesehen

Nichts mehr — das Druckblatt ist gestaltet und in der Vorschau geprüft (§15.3).
