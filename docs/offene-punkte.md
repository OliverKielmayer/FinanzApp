# Offene Punkte

Stand **29.10.2026**, nach den ersten Tests am eigenen Bestand. Eine Liste, kein Plan: sie sagt,
was noch nicht gebaut ist und woher die Anforderung stammt — die Reihenfolge steht in den
Handoffs selbst, nicht hier.

Die Abschnitte bis „Was beim PDF-Scan offen blieb“ sind am echten Papier des Nutzers entstanden
und nicht am Handoff. Sie zählen beim Weiterbauen am meisten, weil dort steht, woran die
Anwendung in der Hand eines Menschen hängengeblieben ist.

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

## Was an den Statusreporten der fondsgebundenen Versicherung offen bleibt

Alle zwölf Berichte des Vertrags 02 (2012 bis 2025) werden erkannt, ihr Anteilsguthaben und ihr
Stichtag gelesen. Die Fondstabelle geht bei **elf** von zwölf gegen den ausgewiesenen Gesamtwert
auf. Was bleibt:

- **Eine Zeile des Jahrgangs 2023 liest keine Regel.** Sie steht als `1 845 60` da, mit
  Leerzeichen. Das Leerzeichen als Tausenderpunkt zu lesen ist keine Lösung: am Rand desselben
  Jahrgangs stehen verirrte Zahlen aus dem Briefkopf, und im Jahrgang 2020 würde aus
  `152,4300 300 870,45` ein Betrag von 300.870,45 statt 870,45. Die Zeile ist trotzdem da —
  erkannt an Anteilen und Preis — und sagt, dass ihr Betrag fehlt. Eingetragen ergibt die Summe
  auf den Cent den ausgewiesenen Gesamtwert (1.845,60 €), und die Probe sagt, dass eine Zeile
  von Hand kam.
- **Die Vertragsnummer desselben Jahrgangs ist ebenfalls beschädigt:** `015111C4 02`. Der Beleg
  findet seinen Vertrag deshalb nicht von allein und wird im Scaneingang zugeordnet. Aus einer
  verlesenen Nummer eine richtige zu raten hieße, einen Beleg an einen fremden Vertrag zu
  hängen — von allen möglichen Fehlern der teuerste.
- **Berichtigt wird nur unter einer Gegenprobe.** In der Fondstabelle prüft der ausgewiesene
  Gesamtwert jede Vermutung nach. An einem einzelnen Feld gibt es diese Prüfung nicht: die
  Beitragssumme des Jahrgangs 2023 steht als `39 415.80` da und bleibt ungelesen, weil aus ihr
  sonst unbemerkt das Hundertfache würde.

## Stand der Statusreporte aller sieben Verträge

Geprüft an **83 echten Berichten** (Verträge 01 bis 07, 2012 bis 2026): jeder wird erkannt, jeder
gibt Vertragsnummer, Stichtag und erreichten Wert her, und jeder Wert ist an seinem Vertrag
angekommen. Im Bestand stehen daraus **91 Vermögensstände, alle mit Beleg** — 83 aus
Statusreporten, 8 aus der Vermögensübersicht.

Was dabei dazugelernt wurde und jetzt in den Leseregeln steht: der Monatsname als Wort
(„31. Juli 2014“), die zweite Schreibweise eines Kennzeichens, wenn die Texterkennung „Anteils-“
zu „Anteiis-“ verliest, das letzte Trennzeichen als Komma in einem zerlegten Scan, und das
Nachbarzeilen-Lesen, wenn eine Beschriftung allein in ihrer Zeile steht.

Offen bleibt:

- **Vertrag 05, Jahrgang 2017: keine Rechenprobe.** Der Leitwert wird gelesen und ist arithmetisch
  bestätigt, seine Bestandteile sind es nicht: die Textebene zerlegt „Rückkaufswert (exkl.
  Bewertungsreserve)“ so, dass weder Beschriftung noch Wert eine Zeile für sich haben. Ohne
  Bestandteile keine Probe.
- **Neun Berichte haben eine offene Zeilensumme.** Beschädigte Fondstabellen, in denen eine Zeile
  ihren Betrag nicht hergibt. Der ausgewiesene Gesamtwert steht trotzdem da, und die Zeile sagt,
  dass ihr Betrag fehlt — eingetragen geht die Probe auf und nennt die Eintragung.
- **Vertrag 01: die Dateinamen gehen um ein Jahr vor.** „statusreport 2024.pdf“ trägt den Stand
  zum 31.07.2025. Kein Fehler der Anwendung — sie liest den Stichtag aus dem Text und nie aus dem
  Dateinamen; aber wer nach Dateinamen sucht, greift daneben.

**Entschieden ist inzwischen, was die Daten nicht entscheiden konnten:** die beiden Verträge 06
und 07 laufen über zweiundzwanzig Jahre auf den Cent gleich, und das ist kein kopiertes
Tabellenblatt. Es sind zwei Verträge aus einem Versorgungsausgleich; jeder trägt seine eigenen
dreizehn Statusreporte, die ihre Nummer selbst nennen.

## Was bei Vertrag 04 (Riester) offen bleibt

Die zehn Statusreporte der staatlich geförderten Altersvorsorge werden gelesen — Vertragsnummer,
Stichtag und erreichter Wert in allen zehn, die Rechenproben gehen bei neun von zehn vollständig
auf. Im selben Ordner liegen weitere Belege, die keine Regel kennt:

- **Der Jahrgang 2023 fällt in der Jahresprobe um 0,80 EUR auseinander.** Derselbe beschädigte
  Scan-Jahrgang wie beim fondsgebundenen Vertrag: eine Ziffer im Jahresergebnis ist verlesen.
  Der erreichte Wert selbst ist doppelt bestätigt — durch die Aufteilung in Deckungskapital und
  Anteilsguthaben und durch die Trennung von den nicht garantierten Posten.
- **Die Bescheinigungen nach § 92 EStG sind nicht integriert.** Sechs Stück, klar aufgebaut, mit
  Beiträgen, Grundzulage, den Summen seit Vertragsbeginn und dem *Stand des
  Altersvorsorgevermögens*. Sie gehören ins Steuerjahr (Anlage AV) und nicht ins Vermögen: ihr
  Vermögensstand ist das **gebildete** Kapital einschließlich der nicht garantierten Posten und
  damit eine andere Größe als die des Statusreports zum selben Stichtag. Beide in denselben
  Vertragsstand zu schreiben hieße, dass der zuletzt eingelesene Beleg gewinnt.
- **Zulagenantrag, Versorgungsausgleich, Bezugsrechtsänderung und Mandatsreferenz** tragen keine
  wiederkehrenden Werte. Sie werden abgelegt und nicht gelesen — richtig so.

## Was bei der Vermögensübersicht offen blieb

Gebaut ist die ganze Strecke: der Leser für das CSV des Maklers, die Vorschau mit einer Zeile je
Vertrag, die Übernahme über dieselbe Stelle wie beim Statusreport, der eigene Weg durch die
Einlieferung aus dem überwachten Ordner und der Prüfschritt unter `/vorsorge/werte/{id}`. Geprüft
an den echten Dateien des Nutzers — der Monatsübersicht vom 23.08.2026 und einer aus
`Wertentwicklung.xlsx` umgewandelten Historie mit **264 Ständen über acht Verträge, 2003 bis
2026**. Was dabei liegen blieb:

- **Die Umwandlung der Tabellenkalkulation ist ein einmaliges Skript, kein Teil der Anwendung.**
  Sie hing an den Eigenheiten genau dieser Datei — acht Blätter mit drei verschiedenen
  Spaltenanordnungen, Monatsüberschriften teils als Text und teils als Datum. Eine zweite
  Tabelle dieser Art gibt es nicht, und eine allgemeine Umwandlung wäre eine Lösung für ein
  Problem, das nur einmal auftrat.
- **Zwei Zellen der Tabellenkalkulation sind nicht übernommen worden.** Der Juli 2024 von
  `77001122-02` trug den Wert des Nachbarvertrags — er lag um ein Drittel unter den Ständen
  davor und danach, eine Kopie über zwei Blätter hinweg. Und die Zeilen 2027 bis 2035 des Raiba-Blatts schreiben
  den letzten Wert fort; ein `PolicyReport` ist ein *gemeldeter* Stand, keine Hochrechnung.
  Beides steht im Umwandlungsprotokoll und nicht in der Datei.
- **Die einmalig umgewandelte Historie steht nicht im Bestand.** Die 264 Stände aus der
  Tabellenkalkulation sind in der heutigen Datenbank nicht enthalten; was dort steht, kommt aus
  Belegen. Wer die Monatsreihe vor 2026 wiederhaben will, muss die Umwandlung erneut laufen
  lassen — das Skript liegt außerhalb der Anwendung.
- **Ein geänderter Beitrag wird gemeldet, nicht übernommen.** Nennt die Datei einen anderen
  Beitrag als der Vertrag, sagt die Vorschau das und lässt ihn stehen. Für eine Dynamik gibt es
  keinen Weg, der ohne Zutun richtig läge — eine monatlich unbeaufsichtigt hereinkommende Datei
  soll keine Angabe umschreiben, um die niemand gebeten hat.
- **Konten, Tagesgeld und Depot bleiben liegen.** Dieselbe Datei führt sie mit; übernommen
  werden nur Vorsorgeverträge. Die Kontostände darin sind älter als das, was die App aus den
  Kontoauszügen ohnehin weiß, und ein Depotwert ohne Positionen wäre eine Zahl ohne Bestand.

## Was die ersten Tests an den Wegen herein gezeigt haben

Vier Wege führen Daten in die Anwendung: der Beleg **von Hand** (`/scannen`), der **Ordnerdienst**
aus dem überwachten Ordner, die **Vermögensübersicht** des Maklers (`/vorsorge/werte`) und der
**Kontoauszug** über den Import. Dass der Ordnerdienst vor der Übernahme anhält, ist Absicht —
ein Dienst, dem niemand zusieht, verändert keine Vermögenszahl. Was die Tests gezeigt haben,
ist nicht diese Entscheidung, sondern wie schlecht der Weg zurück markiert war:

- **Zweimal ist der Nutzer im Eingang steckengeblieben.** „Das Übertragen über den Dienst hat
  nicht die gleiche Auswirkung, wie wenn ich einen Statusreport direkt erfasse“ und später „Ich
  finde den Schalter *Werte übernehmen* nicht“. Beide Male fehlte nicht die Funktion, sondern
  ihr Weg. Der Prüfschritt holt die Übernahme inzwischen nach, der Beleg sagt selbst, wie viele
  Angaben an ihm offen sind, und das Wegräumen fragt nach. **Wie gut das trägt, muss der
  nächste Test zeigen** — gebaut ist es, bewährt noch nicht.
- **Die Dublettenprüfung saß nur am Ordnerdienst.** Von Hand ließ sich dieselbe Datei beliebig
  oft einlesen; im Bestand lagen davon drei Abzüge desselben Berichts, einer an keinem Vertrag.
  Beide Stufen gelten jetzt auf beiden Wegen.
- **Ein Kontoauszug lief in den PDF-Leser.** Eine camt-Datei aus dem überwachten Ordner fand dort
  keine Textebene und lag als unbekannter Zettel in „Sonstiges“. Sie geht jetzt denselben Weg wie
  ein Beleg — abgelegt unter `Finanzen/Kontoauszüge/{Jahr}`, im Eingang mit dem Schalter
  „Buchungen einlesen“, und die Vorschau liest die *abgelegte* Datei neu.
- **Und er kam nicht wieder heraus.** Der zweite Befund desselben Weges, einen Test später: lesen
  ging, herauskommen nicht. „Wegräumen“ verlangt Dokumenttyp *und* Verknüpfung, und für einen
  Kontoauszug gibt es keinen Dokumenttyp. Jetzt erledigt ihn die Übernahme der Buchungen selbst —
  sie verknüpft ihn mit dem Konto, in das gebucht wurde, und räumt ihn aus dem Eingang, sobald von
  seinen Sätzen nichts Neues mehr offen ist. Dieselbe Regel wie bei der Vermögensübersicht:
  übernommen ist mehr als abgelegt.
- **Die Uhr war eingefroren, und das hing nicht an der Demo.** `Demo:Today` setzt den Stichtag
  der *ganzen* Anwendung: jeder eingelesene Beleg bekam den 23.08.2026 als Ablagetag, und wo das
  Papier kein Dokumentdatum nennt, stand dieses Datum als Datum des Schreibens — drei Berichte
  aus drei Jahren trugen dasselbe. Der Wert ist jetzt leer, die Anwendung läuft auf der echten
  Uhr. Die Monatssummen des Demo-Haushalts zeigen seitdem den laufenden Monat; die Beispieldaten
  hängen am August 2026, weil sie so geschrieben sind.

Offen:

- **Der Import liest camt, das Papier kommt als Tabelle.** `camt.052` und `camt.053` versteht er,
  einzeln und als ZIP. Im Bankordner des Nutzers liegen über alle Institute hinweg zwei solche
  Dateien — die Umsätze des Girokontos dagegen als vierzehn CSV und acht XLSX. Ob der Weg im
  Alltag trägt, hängt daran, was die Bank monatlich hergibt; ein Leser für ihren CSV-Export wäre
  die Antwort, wenn nicht.
- **Eine Ablage lässt sich nicht von Hand wählen** (§17.3). Der Pfad entsteht bei der Analyse aus
  Bereich, Objekt und Jahr. Die Zuordnung im Eingang legt den Beleg inzwischen um — ein Beleg,
  dessen Vertrag erst dort gefunden wurde, liegt danach bei seinem Vertrag und nicht mehr unter
  „Unbekannt“. Was fehlt, ist der Griff, mit dem jemand einen Ablageort *anders* bestimmt als die
  Regel.
- **Ein Dokumentdatum wird weiter ersetzt, wenn das Papier keines nennt.** Dann steht der
  Ablagetag dort. Er ist seit der echten Uhr wenigstens der wahre Tag, aber er ist nicht das
  Datum des Schreibens, und der Beleg sagt das nicht.

## Was beim PDF-Scan (§14) offen blieb

Gebaut ist der ganze Abschnitt: Dokumenttyp-Modell, beide Arten, Analyse-Schrittkette,
Vorschlag, Werteprüfung mit Herkunftsseite und die Bestätigung, die die Wirkung nennt. Geprüft
an den echten PDFs des Nutzers — **elf Jahresberichte desselben Vertrags, 2014 bis 2024**. Aus
den Jahrgängen bis 2018 kam zuerst kein einziger Wert heraus; die Leseregeln kennen deren
Schreibweise jetzt (ausgeschriebenes „Euro“, kürzere Abschnittsnamen, Druckmarken links,
Stichtag mit Monatsnamen). Was dabei liegen blieb:

- **Keine Texterkennung.** Beide Originale tragen eine Textebene — der Statusreport eine
  unsichtbare hinter vier Seitenbildern. Ein abfotografierter Beleg wird deshalb erkannt,
  abgelegt und mit leerer Maske gemeldet, aber nicht gelesen. Die Schnittstelle
  `IPdfTextReader` ist der Platz, an dem eine Erkennung andockt.
- **Die gelernte Ablageregel wird angezeigt, aber nicht gespeichert.** „Absender X + Typ Y →
  künftig automatisch hierher“ steht auf der Bestätigungsseite; beim nächsten Beleg entsteht
  der Vorschlag wieder aus dem Typ-Datensatz und nicht aus einer gelernten Regel.
- **Kein Weg, die vorgeschlagene Ablage zu ändern.** Der Prototyp zeigt den Knopf und lässt ihn
  ins Leere laufen; hier fehlt er ganz. Der Pfad entsteht aus Bereich, Objekt und Jahr.
- **Bewertungsreserven und Schlussüberschüsse liest der ältere Jahrgang nicht.** Er schreibt sie
  als Fließtext über zwei Zeilen („… belief sich die Bewertungsreserve auf“ / „256,78 Euro“)
  statt als beschriftete Wertzeile. Beide sind ohnehin nicht garantiert und in keiner
  Vermögenssumme; sie bleiben leer, statt geraten zu werden.
- **Das Dokumentdatum fehlt in den älteren Berichten.** Dort steht nur „August 2014“ — ein Monat
  ohne Tag. Aus ihm einen Ersten zu machen wäre ein erfundenes Datum; der Stichtag, an dem der
  Wert hängt, wird dagegen gelesen.

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

## Was aus [Handoff 21](design-handoff-v5j/design_handoff_immobilie/README.md) noch fehlt

Gebaut sind die Beteiligung am Objekt (§3.1/§3.2) mit einer Stelle, die Quote, Einlagen und
Ausgleich rechnet, die Buchungsart „Einlage“ (§3.4), das **Gemeinschaftskonto** als vierte
Freigabestufe (§3.3) samt Soll-gegen-Eingang je Person und Jahresstand, und das **Kennzeichen
„objektbezogen“** an Kategorie und Vertrag (§3.4) mit der objektbezogenen Teilmenge des
Kontoabflusses.

Zwei Dinge fielen dabei auf und wurden gleich miterledigt: das Vorzeichen einer Einlage hing
nicht an ihrer Richtung — auf dem Gemeinschaftskonto kommt sie an, vom eigenen Konto geht sie ab,
gebucht wurde immer als Abfluss —, und der Monatsvergleich hatte kein Ende, sodass eine
vordatierte Einlage in jeden Monat vor ihr zählte.

Dazu der **achte Bericht „Objekt & Beteiligung“ (§3.5)**, der dritte Reiter der Auswertungen:
drei Kacheln (angefallen / hochgerechnet / Ausgleichsstand), die Postentabelle mit der Spalte Art,
der Ausschlussblock, der Proportionsbalken mit seiner Bezugsgröße, die Beteiligungstabelle mit der
Ausgleichszeile, CSV mit beiden Tabellen. Er löst den offenen Bericht „Objektkosten“ aus §10b mit.

Zwei Größen tragen dort ausdrücklich zwei Namen: **angefallen** ist gemessen (Buchungen auf
objektbezogene Kategorien im laufenden Jahr), **hochgerechnet** ist fortgeschrieben (Darlehen,
Verträge, Policen, Rücklage). Sie stimmen nicht überein und sollen es nicht — die eine kommt aus
dem Kontoauszug, die andere aus dem Bestand. Zwei Angaben am Objekt kamen dafür neu hinzu:
Wohnfläche (Nenner der €/m²-Zahl) und Instandhaltungsrücklage.

Zwei Befunde fielen dabei auf und sind mit erledigt: **Zahlenfelder wurden mit Komma
vorbelegt** — ein `input type="number"` verwirft „38,5“, das Feld stand leer da und das nächste
Speichern löschte die Angabe (betroffen waren Arbeitszeit, Entfernung, Eigentumsanteil) —, und
**gespeicherte Ansichten hießen „Kostentrend“**, auch wenn sie aus dem Steuerjahr kamen.

Offen:

- **Die Forderung als eigene Vermögensklasse (§9.4)** hängt an §17.1 — der Vermögensbericht nach
  Klasse ist selbst noch nicht gebaut.
- **Im Demo-Bestand ist die objektbezogene Teilmenge des Kontoabflusses nicht zu sehen.** Das
  Gemeinschaftskonto ist das Tagesgeld — das Girokonto umzustellen nähme dem Steuerbüro die Sicht
  darauf —, und von dort geht im Demo-Monat nichts ab. Die Zeile erscheint erst, wenn etwas
  abgeht; geprüft ist sie mit zwei Probebuchungen im laufenden Programm.
- **Die gemessene Zahl mischt im Demo-Bestand zwei Wohnsituationen.** Die Kategorie „Wohnen“
  trägt dort die Miete der Wohnung <em>und</em> die Kosten des Hauses. Das Kennzeichen sitzt an
  der Kategorie; wer beides trennen will, teilt sie im Bestand. Der Bericht sagt das auch selbst.
- **Wohngebäude- und Hausratversicherung hängen an keinem Objekt.** Der Bericht liest sie über
  ihre Art und schreibt „keinem Objekt zugeordnet“ in die Zeile. Bei zwei Immobilien zählen sie
  bei beiden mit.
- **Der Knopf „Ausgleich verrechnen“ aus dem Prototyp fehlt.** Es gibt keine Buchungsart für eine
  Ausgleichszahlung, und §4 sagt ausdrücklich, dass die Anwendung den Stand führt und nicht
  eintreibt. Eine Zahlung lässt sich heute nur als gewöhnliche Umbuchung erfassen.
- **Eine gespeicherte Ansicht merkt sich das Objekt nicht.** Sie stellt den Reiter wieder her, aber
  bei mehreren Immobilien immer die erste — wie beim Steuerjahr und der PKV-Bilanz, die sich ihr
  Jahr auch nicht merken.
- **Mehr als zwei Beteiligte, Verkauf und Trennung** sind im Handoff selbst offen (§7) und hier
  nur der Vollständigkeit halber genannt.

## Gebaut, aber nie angesehen

Nichts mehr — das Druckblatt ist gestaltet und in der Vorschau geprüft (§15.3), die vier
Vorsorgeverträge sind im laufenden Programm nachgesehen (§19).
