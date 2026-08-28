# Offene Punkte

Stand **28.08.2026**, nach `603d8bb` (Zustände aus Handoff 11). Eine Liste, kein Plan: sie sagt,
was noch nicht gebaut ist und woher die Anforderung stammt — die Reihenfolge steht in den
Handoffs selbst, nicht hier.

Fertig und damit hier nicht mehr aufgeführt: der v4-Handoff samt Nachtrag
([`handoff-v4-umsetzung.md`](handoff-v4-umsetzung.md)), die Schritte 1–6 des
[Erweiterungsplans](erweiterungsplan.md), der v5-Navigationsumbau, aus der v5-Erweiterung die
Abschnitte 8 (Arbeit & Beruf) und 9 (Dokumenttypen) sowie aus
[Handoff 11](design-handoff-v5c/design_handoff_v5/README.md) das überarbeitete Vermögensmodell
(§3b) und die Lade-, Leer-, Offline- und Fehlerzustände (§7); aus Handoff 13 der ganze
Depot-Abschnitt (§11) — Transaktionen, abgeleitete Positionen, Quartalsaufstellungen und
Bestandsabgleich.

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

## Die fünf offenen Berichte (v4-Handoff 10b)

Gebaut sind Kostentrend, Fixkosten & vertragliche Bindung, Depot-G/V, Datenqualität,
gespeicherte Ansichten, CSV und Druckansicht. Der Handoff markiert die folgenden fünf als
**„vor dem Bau anfragen"**:

1. **Vermögensentwicklung nach Klasse** — mit dem Stichtagsproblem: Depotkurse sind
   tagesaktuell, Lebensversicherungswerte bis zu ein Jahr alt. Eine Kurve, die beides in einer
   Linie führt, behauptet eine Gleichzeitigkeit, die es nicht gibt.
2. **Objektkosten** — Immobilie €/Monat und €/m², Fahrzeuge Gesamtkosten und €/km.
3. **Gesundheit / PKV-Bilanz** — Eigenanteil pro Jahr, Erstattungsquote, Bearbeitungsdauer.
4. **Steuerjahr-Paket** — Beiträge, Handwerkerleistungen, Werbungskosten-Kandidaten mit
   Dokumentbezug.
5. **Liquiditätsprognose 3–6 Monate.**

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
