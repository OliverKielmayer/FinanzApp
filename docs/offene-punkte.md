# Offene Punkte

Stand **28.08.2026**, nach `3b2f1b5` (PKV-Bilanz aus Handoff 14). Eine Liste, kein Plan: sie sagt,
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
PKV-Bilanz (§12).

## In Arbeit: PDF-Dokumente einlesen (Handoff 15, §14)

[Handoff 15](design-handoff-v5e/design_handoff_v5/README.md) liegt vor und ist analysiert; gebaut
ist noch nichts. **Die beiden Beispiel-PDFs stehen aus** — der Nutzer liefert sie nach. Ohne sie
ließe sich die Feldzuordnung nur raten, und geratene Zuordnungen an echten Dokumenten zu
korrigieren ist teurer, als einmal zu warten.

### Was der Handoff verlangt

Der Beleg-Flow ist heute auf **einen** Einzelfall gebaut (Statusreport, feste Werte). Er soll ein
**Dokumenttyp-Modell** tragen: jede Art ein Datensatz mit Bezeichnung, Zielobjekt,
Ablagepfad-Vorlage, Dokumentdatum, fachlichem Stichtag, Seitenzahl, Analyseschritten und
Feldliste. Vorschlagsschritt, Werteprüfung, Ablagepfad, Bestätigungsseite und Speicherlogik
entstehen daraus — nichts je Typ hartkodiert. Eine dritte Art wäre dann ein Datensatz, kein
Screen.

Zwei Arten sind beschrieben: **Statusreport Lebensversicherung** (zehn Felder, §14.3) und
**Quartalsaufstellung MiFID II** (acht Felder, §14.4). Die zweite speist den Bestandsabgleich
aus §11.3, der schon steht — sie ersetzt keinen Depotwert, sondern belegt ihn.

### Was schon da ist

- `DocumentExtraction` speichert bereits Feldschlüssel, Wert, **Herkunftsseite**, Konfidenz und
  `Confirmed`. Damit ist §14.6 im Modell erfüllt: nichts Unbestätigtes verändert Vermögenszahlen.
- `IBillTextExtractor` ist die austauschbare Schnittstelle; `NoBillTextExtractor` liefert leer.
  Der Handoff sagt ausdrücklich, dass das genügt: „fehlt sie, erscheint dieselbe Maske leer".
- `DepotStatement` samt Abgleich existiert seit §11.3 — das Ziel für die Quartalsaufstellung.

### Die eine offene Entscheidung

§14.1 macht **Textebene gegen Bild** zum sichtbaren Kern: ein Original-PDF der Versicherung oder
Bank ist maschinenlesbar, nur Scans brauchen OCR. Das *Erkennen* einer Textebene geht ohne
fremde Bibliothek (Objekte lesen, Flate-Ströme entpacken, nach Textoperatoren suchen). Das
*Auslesen* der Felder geht damit nicht — dafür braucht es einen PDF-Leser als Abhängigkeit
(PdfPig wäre der naheliegende, Apache 2.0). Zu klären, bevor gebaut wird; eine PDF-Abhängigkeit
war bei der Druckausgabe schon einmal abgelehnt worden, dort allerdings fürs Erzeugen.

### Regeln, die beim Bau tragen

- Der **erreichte Wert gesamt** (Rückkaufswert + Ansammlungsguthaben) ist der Vermögenswert —
  nicht der Rückkaufswert allein, nicht die Ablaufleistung.
- **Bewertungsreserven und Schlussüberschüsse sind nicht garantiert**: als `soft` kennzeichnen
  und in keine Vermögenssumme aufnehmen. Das Dokument sagt es in drei Fußnoten.
- Die drei Leistungsszenarien nicht vermischen — übernommen wird aus „Wert der Versicherung".
- **Metadaten aus dem Inhalt, nie aus dem Dateinamen**: im Beispiel heißt die Datei
  „statusreport 2024", der Inhalt sagt Stichtag 31.07.2025.
- Die Bestätigungsseite nennt die **Wirkung**, nicht den Vorgang.
- Der Demoschalter „Beispieldokument" aus dem Prototyp wird **nicht** mitgeliefert.

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

## Die vier verbliebenen Berichte (v4-Handoff 10b)

Gebaut sind Kostentrend, Fixkosten & vertragliche Bindung, Depot-G/V, Datenqualität, die
PKV-Bilanz, gespeicherte Ansichten, CSV und Druckansicht. Der Handoff markiert die folgenden
vier als **„vor dem Bau anfragen"**:

1. **Vermögensentwicklung nach Klasse** — mit dem Stichtagsproblem: Depotkurse sind
   tagesaktuell, Lebensversicherungswerte bis zu ein Jahr alt. Eine Kurve, die beides in einer
   Linie führt, behauptet eine Gleichzeitigkeit, die es nicht gibt.
2. **Objektkosten** — Immobilie €/Monat und €/m², Fahrzeuge Gesamtkosten und €/km.
3. **Steuerjahr-Paket** — Beiträge, Handwerkerleistungen, Werbungskosten-Kandidaten mit
   Dokumentbezug. Die PKV-Bilanz nennt mit „potenziell absetzbar" schon den Einstieg.
4. **Liquiditätsprognose 3–6 Monate.**

## Aus der Dateiablage ersichtlich, nirgends abgebildet

- **Steuer nach Jahr** als eigener Bereich.
- **Unterhalt / Scheidung** als Vorgangstyp mit Zahlungsverfolgung.

## Gebaut, aber nie angesehen

- **Druckansicht der Auswertungen.** Umgesetzt als Druck-Stylesheet plus `window.print()` —
  ausdrücklich statt einer neuen PDF-Abhängigkeit. Wie das Blatt tatsächlich aussieht, hat
  bisher niemand geprüft.

## Entscheidungen, die eine zweite Meinung vertragen

- **Netto-Schätzfaktor 0,62** (`EmploymentService.NetFactor`). Steuerklasse, Kirche,
  Kinderfreibetrag und Beitragsbemessungsgrenze sind nicht bekannt, die Zahl kann nur eine
  Hausnummer sein. Sie wird überall als Schätzung ausgewiesen und ist überschreibbar — aber
  eine bessere Näherung wäre eine bessere Näherung.
- **Lohnabrechnung erfassen** ist eine Maske im Screen `/arbeit`. Der v5-Handoff beschreibt die
  Liste, aber keinen Weg hinein; ohne die Maske bliebe der Abschnitt für immer leer. Wenn
  Abrechnungen später aus dem Scaneingang kommen, gehört sie überdacht.
- **Vertragsende** ist ein Feld des Arbeitsverhältnisses, das der Handoff nicht nennt. Ohne es
  könnte nie etwas „beendet" werden, und die Regel „Beendetes zählt nicht als laufende Last"
  bliebe unbedienbar.
- **Quartalsaufstellungen werden abgetippt, nicht ausgelesen.** Der Handoff nennt sie als PDF;
  eine Texterkennung gibt es in dieser Anwendung nicht (`NoBillTextExtractor`), und der
  Beleg-Scan-Flow, auf den §11.5 verweist, lässt die Maske ohnehin von Hand füllen — wie bei
  den PKV-Belegen. Sobald eine Erkennung angebunden ist, füllt sie dieselbe Maske vor.
- **Der Bestandsabgleich rechnet den Mindermengenzuschlag in den Einstand.** Die Beispieltabelle
  in §11.3 nennt 28.413 € und lässt ihn damit weg; §11.1 verlangt ausdrücklich, ihn in die
  Anschaffungskosten zu nehmen. Umgesetzt ist §11.1 — an den echten Orders sind es 28.414,45 €.
