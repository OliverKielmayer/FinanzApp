# Offene Punkte

Stand **30.08.2026**, nach der Berichtsreihe im Vorsorgebereich. Eine Liste, kein Plan: sie sagt,
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
und den vier entschiedenen Rückfragen (§15); aus
[Handoff 17](design-handoff-v5g/design_handoff_v5/README.md) die Kurszeitreihe samt Abruf über
eine Web-API (§16); aus [Handoff 18](design-handoff-v5h/design_handoff_v5/README.md) die
Wiederholgruppe für Aufstellungen mit mehreren Positionen (§17.2); aus
[Handoff 20](design-handoff-v5i/design_handoff_v5/README.md) die drei Befunde aus §18 und die
fünf Vorsorge-Korrekturen aus §19.

## Was beim PDF-Scan (§14) offen blieb

Gebaut ist der ganze Abschnitt: Dokumenttyp-Modell, beide Arten, Analyse-Schrittkette,
Vorschlag, Werteprüfung mit Herkunftsseite und die Bestätigung, die die Wirkung nennt. Geprüft
an den beiden echten PDFs des Nutzers. Was dabei liegen blieb:

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

## Aus Handoff 18 (§17) noch nicht gebaut

Umgesetzt ist nur §17.2 — die Wiederholgruppe. Offen und der Reihe nach das Nächste:

- **§17.1 Vermögensentwicklung nach Klasse.** Der letzte große Bericht aus §10b: gestapelte
  Balken je Stichtag statt einer Linie, gestrichelte Oberkante für fortgeschriebene Werte, und
  der Block „Wie belastbar ist diese Kurve?". Braucht eine gespeicherte Zeitreihe je Stichtag
  und Klasse mit dem Kennzeichen *gemessen* oder *fortgeschrieben* — ohne das lässt sich die
  gestrichelte Kante nicht zeichnen.
- **§17.3 Ablage ändern.** Der Knopf im Vorschlagsschritt läuft weiter ins Leere. Gedacht sind
  drei Vorschläge als Chips plus ein freies Feld; nur der Ordner ist änderbar, nicht der
  Dateiname.
- **§17.4 Gelernte Ablageregel speichern.** Die Bestätigungsseite zeigt „Absender X + Typ Y →
  künftig automatisch hierher", ohne dass etwas gespeichert wird. Die Regel gehört in den
  Kategorieregeln-Schirm, in einen eigenen Block, einzeln löschbar.
- **§17.5** bleibt offen wie gemeldet: keine Texterkennung für reine Bilder.

## Was beim Kursabruf (§16) offen blieb

Gebaut ist die gespeicherte Kurszeitreihe, der Abruf hinter `IQuoteSource` mit der Börse
Frankfurt als erster Quelle, Zeitplan und Knopf, das Kursband mit seinen vier Zuständen und der
Kursverlauf je Position samt Einstandslinie. Geprüft mit echten Abrufen gegen die reale
Schnittstelle.

**Der Befund, der den Bau bestimmt hat:** die frei zugängliche Schnittstelle gibt **keine
Vergangenheit** heraus. Der Endpunkt `quote_box` liefert den zuletzt festgestellten Kurs und
verlangt nichts weiter; `price_history` antwortet leer, weil er eine Signatur erwartet, die der
Anbieter nur seiner eigenen Oberfläche mitgibt. Sie nachzubauen hieße, eine Zugangssperre zu
umgehen — das ist unterblieben. Die Reihe wächst deshalb Tag für Tag mit den eigenen Abrufen,
und was an Vergangenheit im Haus ist, wird nachgetragen: Ausführungen, Bestandsnachweise und
erfasste Positionen tragen alle einen Kurs mit Datum.

Offen:

- **Keine Fremdwährung.** Die Reihe führt eine Währung mit, aber es gibt keinen
  Umrechnungskurs — der wäre selbst eine Zeitreihe. Xetra und Frankfurt notieren in Euro; ein
  in Dollar notiertes Papier bewertet die Anwendung derzeit falsch.
- **Keine Splits und Ausschüttungen.** Ein Split bricht den Verlauf optisch ein, ohne dass
  Vermögen verloren ging. Erkannt wird er nicht.
- **Keine Zweitquelle und kein Kurs von Hand.** Beides ist als Implementierung derselben
  Schnittstelle vorgesehen und nicht gebaut. Für Papiere, die keine Quelle kennt — Anteile an
  geschlossenen Fonds, Belegschaftsaktien — bleibt es beim Kurs aus der erfassten Position.
- **Die Einstellungszeile „Kursquelle" zeigt nur an.** Quelle wählen, Abrufzeit ändern und die
  Zweitquelle einrichten steht in der Konfigurationsdatei, nicht in der Oberfläche.
- **Kein Intraday.** Gespeichert wird der letzte Kurs eines Tages. Für eine Vermögensübersicht
  ist alles darunter Rauschen.

## Was beim Steuerjahr-Paket (§15) offen blieb

Gebaut ist der Bericht mit seinen vier Abschnitten, den beiden getrennten Kennzeichen, dem
Druckblatt, der Brücke aus der PKV-Bilanz und der sichtbaren Rechenprobe beim Einlesen. Was
liegen blieb:

- **Handwerkerleistungen trennen Arbeitslohn und Material nicht.** Der Bericht sagt das im
  Klartext; die Trennung selbst bräuchte eine Angabe je Rechnung.
- **Der Druck geht über `window.print()`.** Das Blatt ist gestaltet und geprüft, gedruckt hat es
  noch niemand — das Ergebnis hängt am Browser und seinen Rändern.

Eine Abweichung vom Prototyp, die bewusst ist: **der Bericht zeigt Cent**, nicht gerundete Euro.
Der Sprung aus der PKV-Bilanz stellte sonst 562 € neben 561,60 € für dieselbe Zahl. Was in ein
Formular abgetippt wird, gehört genau hin.

## Was bei den Vorsorge-Korrekturen (§18/§19) offen blieb

Gebaut sind alle acht Befunde: die Kurslinie nach Datum statt nach Nummer (§18.2), die
steuerliche Einordnung im Kategorienschirm und Entfernung und Arbeitstage in der
Bearbeitenmaske (§18.3), die Zahlungszuordnung über die Vertragsnummer samt Begründung,
Gegenprobe und ehrlichem Leerzustand (§19.2), die Dokumentliste je Vertrag mit Typ-Tag (§19.3),
das Bearbeiten vom Detailschirm aus mit allen Rohfeldern (§19.4) und der Block „So entsteht der
Wert" samt Berichtsreihe (§19.5 bis §19.7). Zwei Dinge fielen dabei auf und wurden gleich
miterledigt: der Detailschirm lud nur beim Aufbau — von `/police/1` auf `/police/2` blieben die
Zahlen des ersten Vertrags stehen —, und der Anlegeweg rechnete die Wertbestandteile nicht
zusammen, während der Änderungsweg es tat.

Nachgetragen wurde danach die Berichtsreihe als Liste: jeder gemeldete Stand steht mit
Stichtag, Betrag und Quelle da, lässt sich wieder entfernen, und wo er aus einem Beleg stammt,
lassen sich die ausgelesenen Werte einblenden — mit Seite und Sicherheit. Der erreichte Wert
des Vertrags kommt seitdem **aus dem neuesten Bericht** und ist keine eigene Größe mehr.

Offen:

- **Ein Bericht lässt sich nicht bearbeiten**, nur entfernen oder durch erneutes Speichern zum
  selben Stichtag berichtigen. Für einen falsch gelesenen Stichtag heißt das: entfernen und neu
  einlesen.
- **Der Verlauf beschriftet seine Punkte nicht.** Die Beträge stehen in der Liste darunter, die
  Linie zeigt weiter nur ihre beiden Enden.
- **Nur der Vorsorge-Detailschirm führt „bearbeiten".** Immobilie, Fahrzeug und Vertrag haben
  denselben Aufbau und dieselbe Lücke — dort ist das Bearbeiten weiter nur aus der Liste
  erreichbar.

## Gebaut, aber nie angesehen

Nichts mehr — das Druckblatt ist gestaltet und in der Vorschau geprüft (§15.3), die vier
Vorsorgeverträge sind im laufenden Programm nachgesehen (§19).
