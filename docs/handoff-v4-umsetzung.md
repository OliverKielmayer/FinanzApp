# Umsetzung Handoff v4

Arbeitsliste zu [`docs/design-handoff-v4/handoff.md`](design-handoff-v4/handoff.md). Reihenfolge
aus Abschnitt 11 des Handoffs — sie ist nicht beliebig: Schritt 1 verschiebt jede Maßzahl, alles
danach würde sonst zweimal gebaut.

Stand 25.08.2026: **Schritte 1 bis 3 umgesetzt**, Rest offen.

Entschieden: beim Umbau des Datenmodells in Schritt 3 wird **nicht migriert**, sondern neu
aufgesetzt — bestehende `finanzapp.db` löschen. Damit muss die Schema-Prüfung beim Start
(`SchemaStartup`) in Schritt 3 auch für diesen Fall etwas Sinnvolles sagen; heute kennt sie nur
den Fall „Tabellen ohne Migrationshistorie".

## Was der Handoff ersetzt

Die Gestaltungsvorgaben aus `design-handoff/` und `design-handoff-erweiterung/` sind überholt.
Ihre **fachlichen** Beschreibungen — Dokumentmodell, PKV-Regeln, Verknüpfungen, Tests — gelten
weiter. Die beiden Ordner bleiben deshalb liegen.

## Die acht Schritte

| # | Schritt | Was im Code betroffen ist | Umfang |
| --- | --- | --- | --- |
| ~~1~~ | ~~Stylesheet Industry + Typo-Skala~~ | **erledigt** | groß |
| ~~2~~ | ~~Responsiver Rahmen~~ | **erledigt** bis auf das Formular am Stück (siehe unten) | mittel |
| ~~3~~ | ~~Vorsorge / Absicherung trennen~~ | **erledigt** | mittel |
| 4 | Anlege-Flows | eine Formularkomponente + Feldliste je Typ (8 Typen), Endpunkte | groß |
| 5 | Buchungstabelle, Filter, Summen, Leerzustände | `Accounts.razor`, `TransactionService`, Kategorie-Panel | mittel |
| 6 | Dokumente Master/Detail, Scan & PKV zweispaltig | `Documents`, `DocumentDetail`, `ScanBill`, `MedicalBillDetail` | mittel |
| 7 | Police-Import hinter der Analyse-Schnittstelle | `IBillTextExtractor` erweitern, Import-Panel, Herkunft je Feld | mittel |
| 8 | Fahrzeuge, Scaneingang | 2 neue Entitäten + Bereiche | mittel |

## Was schon da ist und nur zusammengeführt werden muss

Schritt 3 ist **kein** Neubau. Die Trennung existiert bereits als zwei halbe Modelle:

- `InsurancePolicy` (Provider, `SurrenderValue`, `ValuationDate`) — das ist **Vorsorge & Kapital**,
  speist heute schon die Kachel „Lebensversicherung" auf dem Dashboard.
- `Insurance` (Insurer, `Premium`, `PremiumInterval`, `EndsOn`, `NoticePeriodMonths`) — das ist
  **Absicherung**.

Der Handoff verlangt **ein** Modell mit Flag `kapitalbildend` und zwei Einstiegen. Die Arbeit ist
also Zusammenführen, nicht Erfinden — und ohne Migration, siehe oben.

Ebenfalls vorhanden und wiederverwendbar: `Property`/`Contract`/`Invoice` als Vorbild für
Fahrzeuge (strukturgleich laut Handoff), `DocumentLink` für die Verknüpfungen, `IBillTextExtractor`
als bereits gezogene Schnittstelle für den Police-Import.

Neu anzulegen: `Vehicle`, Scaneingang (Posteingang), das Flag, die Feldlisten der Anlege-Flows.

## Zwei Widersprüche im Handoff

Beide betreffen Schritt 1 und müssen entschieden sein, bevor `app.css` angefasst wird.

**Radius.** Der Fließtext sagt „4px aus `--radius-*` (Tokens nutzen, nicht hart 0)". Die
mitgelieferte `styles.css` setzt am Ende jedoch ausdrücklich

```css
.card, .btn, .input, .tag, .seg, .dialog { border-radius: 0; }
```

Da die CSS als „verbindliche Token- und Komponentenquelle" bezeichnet wird, gilt sie: diese sechs
Komponenten sind eckig, die Tokens greifen überall sonst. So umgesetzt, im Prototyp gegengeprüft.

**Linksbündige Button-Labels.** Der Handoff führt sie unter „weiter gültig". Industrys `.btn-block`
hat `justify-content: flex-start; text-align: left` gegenüber Modernist aber **verloren**. Die
Ausrichtung muss daher aus `app.css` kommen, nicht aus dem System.

## Zahlen, die sich mitverschieben

Der Prototyp zeigt **Nettovermögen 99.879,95 €** (Brutto 248.179,95 €, Verbindlichkeiten
−148.300,00 €) gegenüber bisher 125.839,95 € / 274.139,95 €. Die Differenz von 25.960 € entsteht
dadurch, dass Risikoverträge nicht mehr ins Vermögen zählen (Abschnitt 4). Vorsorge summiert auf
58.940 €, Absicherung auf 12.330 €/Jahr.

Die Beispieldaten müssen also mit Schritt 3 nachgezogen werden — sonst zeigt die App weiter die
alte Bilanz. „Bleibt übrig" bleibt bei 1.628 €.

## Nicht bauen, ohne zu fragen

Der Handoff nennt sie selbst: Ladezustände, Offline, Fehlerdialoge, 2FA, Rechtematrix, Auswertungen,
Split-Buchung, Sondertilgung, CSV-Spalten-Mapping, Arbeit & Beruf, Administration. Dazu neu
angedeutet, aber nicht gestaltet: **Steuer nach Jahr** und **Unterhalt / Scheidung**.

## Nebenbefund zum Design-System

Industry (`c7818bc7-9e12-4bae-b37e-4860de5fd288`) ist **kein** Design-System-Projekt des Nutzers —
die Liste über `DesignSync` kennt nur *Modernist* und ein leeres *Design System*. Die Tokens kommen
also ausschließlich über die Handoff-ZIP; der Abgleichweg, der bei Modernist offenstand, existiert
für Industry vorerst nicht. Siehe die Notiz zum Zusammenspiel beider Plattformen.

## Was Schritt 1 und 2 konkret geändert haben

**Schritt 1.** `modernist.css` ist raus, `industry.css` liegt byte-identisch zur Lieferung im
Projekt, `index.html` und die Theme-Farbe zeigen darauf. In `app.css`:

- 25 Trennlinien von 2px auf Haarlinien — 19 als Bereichsgrenze in `--color-text`, 6 als
  Zeilentrenner in `--color-divider`. Die **neun Akzentbalken bleiben bei 2px**; so hält es der
  Prototyp (`border-left: 2px solid var(--color-accent)`, dort mehrfach belegt).
- 29 × hartkodiertes `font-weight: 800` (Modernists Überschriftengewicht) auf
  `var(--font-heading-weight)` — Industry setzt 600, sonst wirkte jede Zahl zu fett.
- Displaygrade angehoben, weil Barlow Condensed schmal läuft: Hero 42 → 52, Betragsanzeige
  46 → 52, Wortmarke 34 → 40, Budgetsumme 32 → 38, Kachelwerte 16 → 19, Schrittnummer 9 → 10.
- `.btn-block` bekommt seine linksbündigen Labels aus `app.css` zurück.

Hartkodierte Farben gab es keine — der Farbwechsel fiel allein durch den Dateitausch an.

**Schritt 2.** Umschaltpunkte jetzt 768 und **1200** (vorher 1024, wie der Handoff es verlangt).

- Kacheln 2 / 3 / 4 Spalten, Hero 52 / 64 / 76 px, Hülle voll / 860 px / voll.
- Seitennavigation: Benutzerblock oben (Name, Haushalt, Sitzungsbeginn), flache Bereichsliste in
  der Reihenfolge aus Abschnitt 3, Kennzahl je Zeile, Erfassen fest am Fuß. Breite 240 → 280 px.
- Das Sheet wird ab 768 ein rechtes Panel (420 px, ab 1200 480 px), volle Höhe, Bewegung in X.
- Chart und Bilanz stehen ab 768 nebeneinander; die Kacheln sind dafür unter sie gerückt.

Gegengeprüft im Browser bei 390, 900, 1280 und 1400 px: Umschaltpunkte, Spaltenzahl, Hero-Grade,
Panel-Geometrie und die feste Position des Erfassen-Knopfes stimmen.

## Bewusst offen geblieben

- **Formular am Stück ab Tablet** (Erfassen, Scan). Der Handoff blendet dort die Schrittleisten
  aus, weil das Formular in einem Zug passt. Das ist keine Layout-, sondern eine Ablaufänderung:
  die Komponente müsste die Fensterbreite kennen, und dieselbe Frage stellt sich in Schritt 4 für
  alle acht Anlege-Typen. Deshalb dort, nicht hier — die Schrittleiste bleibt solange sichtbar.
- **Kennzahlen an allen Bereichen.** Geliefert werden die, die `AreaCountsDto` schon führt
  (Dokumente, Vorgänge, PKV, Objekte, Benutzer). Konten, Budgets, Depot, Darlehen und Import
  bekommen ihre, wenn die zugehörigen Zahlen bereitstehen. Lieber keine Zahl als eine erfundene.
- **Vier Navigationseinträge fehlen** — Vorsorge & Kapital, Absicherung (Schritt 3), Scaneingang,
  Fahrzeuge (Schritt 8). Ein Eintrag, der ins Leere führt, wäre schlechter als keiner.
- **Dashboard-Reihenfolge** aus Abschnitt 10 („Bleibt übrig" zuoberst, Nettovermögen darunter)
  gehört in keinen der acht Schritte. Vorschlag: zusammen mit Schritt 5.

## Was Schritt 3 geändert hat

**Ein Modell statt zwei halber.** `Insurance` und `InsurancePolicy` sind zu **`Policy`**
zusammengeführt, unterschieden allein durch `IsCapitalForming`. Die Regel, an der die alte
Sammelkategorie gescheitert ist, steht jetzt an einer einzigen Stelle:

```csharp
public decimal? AssetValue => IsCapitalForming ? CurrentValue : null;
```

Damit kann ein Risikoleben-Vertrag nicht mehr ins Nettovermögen geraten, auch wenn versehentlich
ein Wert eingetragen wäre. `PolicyTests` hält das fest.

**Zwei Einstiege, eine Seite.** `/vorsorge` und `/absicherung` sind dieselbe Komponente; was sie
unterscheidet, ist die Kopfzahl — Wert mit Stichtag gegen Jahresbeitrag. Die Detailseite
`/police/{id}` gilt für beide. Aus `LinkTargetType.Insurance` und `LifeInsurance` wurde ein
`Policy`.

**Wo die Trennung sonst noch wirkt:** Vorsorgebeiträge zählen nicht mehr als Kosten (sie sind
Sparen, Abschnitt 10), Vorsorgeverträge tauchen nicht mehr im Sparpotential auf (eine Kapital-LV
zu kündigen ist ein Verlust, kein Potential), und die Immobilienkosten ziehen Gebäude- und
Hausratbeitrag jetzt über die Vertragsart statt über den Namen zu raten.

### Termin und Erinnerung sind zweierlei

Der Prototyp führt bei Hausrat „Kündigung bis 30.09.2027“ **und** „in 18 Tagen erinnern“ und
markiert den Vertrag trotzdem als laufende Frist. Beides zugleich geht nur, wenn Termin und
Erinnerung getrennt sind — mit dem abgeleiteten Termin allein wäre der Vertrag 403 Tage entfernt
und damit unauffällig, und die Demo hätte kein einziges Beispiel für den Zustand „Frist läuft“.
Deshalb trägt `Policy` ein `NoticeReminderOn`. Es zählt, sobald es in Sicht ist, nicht erst am Tag
selbst — ein Vergleich braucht Vorlauf.

### Die Zahlen

Gegengeprüft über die laufende API, nicht geschätzt:

| | Soll (Prototyp) | Ist |
| --- | --- | --- |
| Vorsorge, 4 Verträge | 58.940,00 € | 58.940,00 € |
| Absicherung, 8 Verträge | 12.330 €/Jahr | 12.330 €/Jahr |
| Bruttovermögen | 248.179,95 € | 248.179,95 € |
| Nettovermögen | 99.879,95 € | 99.879,95 € |
| zum Vormonat | +2.140,80 € | +2.140,80 € |

Die Verlaufsreihe ist um 25.960 € nach unten **verschoben**, nicht skaliert: der Unterschied
steckte schon immer darin, er war nur falsch zugeordnet. Dadurch bleibt der Monatszuwachs
erhalten. Die Jahresangabe wandert damit von +11,4 % auf **+14,8 %** — der Prototyp zeigt zwar
weiter „+11,4 %“, aber das ist eine aus der Vorfassung übernommene feste Zeichenkette, die
schon zu seiner eigenen Kopfzahl nicht mehr passt.

### Datenbank

Wie entschieden **nicht migriert, sondern neu aufgesetzt**: die Migrationen sind neu erzeugt.
`SchemaStartup` erkennt jetzt zwei Fälle statt einem — eine Datenbank ohne Migrationshistorie
(wie bisher) und eine aus einer älteren Migrationslinie (neu). Beide bekommen dieselbe klare
Ansage samt Dateipfad; löschen darf die Anwendung sie weiterhin nicht.

59 Tests, davon 11 neue zu den Vorsorge-/Absicherungsregeln.
