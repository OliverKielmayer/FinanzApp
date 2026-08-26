# Umsetzung Handoff v4

Arbeitsliste zu [`docs/design-handoff-v4/handoff.md`](design-handoff-v4/handoff.md). Reihenfolge
aus Abschnitt 11 des Handoffs — sie ist nicht beliebig: Schritt 1 verschiebt jede Maßzahl, alles
danach würde sonst zweimal gebaut.

Stand 26.08.2026: alle acht Schritte umgesetzt, dazu der **Nachtrag vom 26.08.** (Abschnitte 6b und 8b).

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
| ~~4~~ | ~~Anlege-Flows~~ | **erledigt**, alle acht Typen (Fahrzeug kam mit Schritt 8) | groß |
| ~~5~~ | ~~Buchungstabelle, Filter, Summen, Leerzustände~~ | **erledigt** | mittel |
| ~~6~~ | ~~Dokumente Master/Detail, Scan & PKV zweispaltig~~ | **erledigt** | mittel |
| ~~7~~ | ~~Police-Import hinter der Analyse-Schnittstelle~~ | **erledigt** | mittel |
| ~~8~~ | ~~Fahrzeuge, Scaneingang~~ | **erledigt** | mittel |

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

- ~~**Formular am Stück ab Tablet**~~ — mit Schritt 4 erledigt, siehe unten.
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

## Was Schritt 4 gebracht hat

**Ein Formular für alle Typen, vom Server beschrieben.** `CreateFormService` liefert die Feldliste
— Schlüssel, Beschriftung, Art, Pflicht, Auswahlwerte — und prüft die Eingabe gegen *dieselbe*
Liste. Der Client rendert sie generisch. Daraus folgt zweierlei: die Meldung nennt das fehlende
Feld bei dem Namen, den der Benutzer gesehen hat („Versicherer fehlt“), und ein neuer Objekttyp
kostet einen Listeneintrag statt einer neuen Seite.

Umgesetzt sind sieben Typen: Konto, Depot, Vorsorgevertrag, Versicherung, Immobilie, Vertrag,
Budget. **Fahrzeug fehlt bewusst** — die Entität entsteht erst mit Schritt 8; bis dahin liefert
der Dienst für diesen Typ `null` und die Auswahl führt ihn gar nicht auf.

**Einstieg dort, wo das Objekt hingehört.** Jede Liste endet auf einer „+“-Zeile
(`AddRow`-Komponente, mit Lesezugriff unsichtbar); dazu der Sammeleintrag im Erfassen-Fenster, der
auf `/neu` führt und fragt, was es sein soll. Aus dem Platzhalter „Neues Budget anlegen“ ist
damit ein echter Weg geworden.

**Es wird wirklich geschrieben.** Gegen die laufende API geprüft: ein angelegtes Konto steht
danach in der Kontoliste **und** ist in der Vertragsanlage wählbar; ein Budget über 360 € im
Quartal landet als 120 € je Monat im Plan; ein zweites Budget auf dieselbe Kategorie wird mit
„Budget für Auto besteht bereits“ abgelehnt.

**„Depot“ im Konto-Formular legt kein Konto an**, sondern führt in den Depot-Flow — so verlangt
es der Handoff. Dasselbe gilt für „Darlehen“: es ist eine Verbindlichkeit mit Tilgungsplan, kein
Konto, und `AccountKind` kennt dafür auch keinen Wert. Der Handoff nennt nur das Depot; die
Weiterleitung des Darlehens ist die einzige Möglichkeit, an dieser Stelle nichts Falsches
anzulegen.

### Neue Felder

Für drei Typen verlangt der Handoff Felder, die es im Modell noch nicht gab. Ergänzt sind genau
diese, per additiver Migration `AnlegeFelder`:

- **Budget**: `Period`, `ValidFrom`, `WarnThresholdPercent`. Intern wird weiter je Monat geführt;
  Quartal und Jahr rechnen beim Anlegen herunter.
- **Depot**: `Broker`, `Number`, `DepotKind`, `StatedValue` + `ValuationDate`, `AccountId`,
  `QuoteSource`. Der angegebene Wert zählt nur, solange keine Positionen erfasst sind — danach
  rechnet der Bestand.
- **Immobilie**: `Kind` (Haus, Wohnung, Grundstück).

### Das Formular am Stück — der offene Punkt aus Schritt 2

Jetzt eingelöst, und ohne dass die Komponente die Fensterbreite kennen muss: alle drei Schritte
der Erfassung stehen im Dokument, die CSS zeigt auf dem Telefon nur den aktiven und ab Tablet
alle. Dazu zwei Aktionsleisten, von denen je eine sichtbar ist — der Assistent führt durch die
Schritte, am großen Bildschirm wird in einem Zug gespeichert. Erst dadurch darf die Schrittleiste
ab Tablet verschwinden, wie der Handoff es will.

Nachgemessen: bei 390 px eine Schrittleiste, ein sichtbarer Schritt, Knopf „Weiter“; bei 1280 px
keine Schrittleiste, drei sichtbare Schritte, Knopf „Buchung speichern“.

Der Scan-Flow behält seine Schritte vorerst — er wird in Schritt 6 ohnehin zweispaltig umgebaut,
und zweimal umbauen wäre verschwendet.

70 Tests, davon 11 neue zu den Anlege-Flows.

## Was Schritt 5 gebracht hat

**Ab Tablet eine Tabelle**, mit den Spalten aus Abschnitt 8 — nachgemessen im Browser:
`28px 56px 552px 118px 110px 104px`, also Auswahl, Datum, Empfänger als breiteste Spalte,
Kategorie, Konto, Betrag rechtsbündig. Auf dem Telefon bleibt die kompakte Zeile; die
Tabellenspalten und die Auswahlspalte sind dort schlicht nicht da.

**Stapelvergabe mit einer fachlichen Regel.** Gewählte Zeilen liegen im Akzent, darüber steht
„N Buchungen ausgewählt“. „Kategorie zuweisen“ schreibt **nicht direkt**, sondern öffnet das
Kategorie-Panel — und dessen Kopf sagt vorab, was nicht angefasst wird: „Stapelvergabe · 3
Buchungen · Umbuchungen bleiben unverändert“. Danach nennt die Meldung beides:
„2 × Wohnen · 1 Umbuchung geschützt“.

Das ist keine Bequemlichkeit. Wer fünfzehn Zeilen markiert und „Wohnen“ wählt, meint nicht die
Umbuchung aufs Tagesgeld, die zufällig dazwischenliegt — sie mitzunehmen verfälschte jede
Auswertung. Nur die ausdrückliche Wahl „Umbuchung“ fasst sie an.

**Filter, Summen, Leerzustand.** Suche plus Chips für Konto, Art und Kategorie; auf dem Telefon
eine scrollende Reihe, ab Tablet umbrechend. Die Summen rechnen gegen den **sichtbaren**
Ausschnitt, Umbuchungen zählen weder als Einnahme noch als Ausgabe und werden nur gezählt. Das
Triage-Banner bezieht sich ebenfalls auf den Ausschnitt — dafür gibt es jetzt
`FilteredUncategorizedCount` neben dem Bestandszähler; ein Banner über fünf Buchungen, von denen
der Filter keine zeigt, wäre eine Aufforderung ins Leere. Statt einer leeren Fläche steht
„Keine Buchung im gewählten Ausschnitt“ mit einem Satz zur Ursache — bei einer Suche nennt er
den Begriff — und zwei Auswegen.

Eine Auswahl, die der Filter nicht mehr zeigt, wird verworfen: sonst beträfe die Stapelvergabe
Zeilen, die niemand vor sich hat.

Abschnitt 10 verlangt „Bleibt übrig“ zuoberst und das Nettovermögen darunter — das steht
bereits so, seit der Erweiterung. Kein Handgriff nötig.

79 Tests, davon 9 neue zur Stapelvergabe, den Summen und den Filtern.

## Was Schritt 6 gebracht hat

**Dokumente als Master und Detail, ohne zwei Navigationen.** Aus der Detailseite wurde die
Komponente `DocumentPane`; die Route `/dokumente/{id}` liegt jetzt auf derselben Seite wie die
Liste. Auf dem Telefon zeigt die CSS je nach Route nur eine Spalte, ab Tablet beide nebeneinander
— 620 px Liste plus 300 px Vorschau, ab Desktop 380 px. Die offene Zeile ist markiert, und die
leere Vorschau sagt, was zu tun ist, statt eine unerklärte Fläche zu sein.

Nachgemessen bei 1280 px: Liste 620, Vorschau 380, beide sichtbar; bei 390 px nur eine von beiden.

**Scan und PKV zweispaltig.** Links der Beleg mit seinen Zahlen und Dokumenten, rechts der
Vorgang — Verlauf, nächster Schritt, Zahlungszuordnung. Der Blick springt damit nicht mehr
zwischen zwei Bildschirmen hin und her.

### Ein Fehler, der beinahe durchgegangen wäre

Die Vorschauspalte blieb zunächst leer, obwohl das Raster stimmte: der Grundzustand
(`display: none` für die jeweils andere Spalte) stand in `app.css` **hinter** der Media Query und
gewann deshalb bei gleicher Spezifität. Behoben durch Umsortieren, nicht durch
`!important` — die Reihenfolge war das Problem, nicht die Regel.

## Was Schritt 7 gebracht hat

**Eine Schnittstelle, kein Anbieter im Fachcode.** `IPolicyDocumentAnalyzer` sagt nur, *was*
herauskommt, nie *wodurch*. Eingebaut ist `NoPolicyDocumentAnalyzer` — er erkennt nichts und sagt
das auch. Bewusst kein Platzhalter, der etwas erfindet: erfundene Werte in einem Formular, das
Vermögenszahlen speist, wären schlimmer als ein leeres Formular.

**Das Panel steht über den Feldern**, als `.blueprint` mit vier Registermarken — beides kommt
unverändert aus Industry. Drei Zustände wie im Handoff: leer mit Dateiwahl und dem Hinweis, dass
nur der Pfad gespeichert wird; lesend als sichtbare Kette (Text erkannt → Absender bestimmt →
Vertragsart erkannt → Werte gelesen), abbrechbar, kein Spinner; geprüft mit den Werten samt
Herkunftsseite, Unsicheres im Akzent. Nur bei Vorsorge und Absicherung — ein Konto hat kein
Dokument, aus dem sich etwas lesen ließe.

**Die Reihenfolge ist Absicht**: erst ablegen, dann lesen. Die Ablage ist das Verlässliche, die
Analyse darf fehlen. Deshalb liegt die Datei auch dann im Dokumentordner, wenn nichts erkannt wird
— als Test festgehalten.

**Herkunft und Bestätigung werden gespeichert.** `DocumentExtraction` hält je gelesenem Wert
Schlüssel, Wert, Seite, Konfidenz und den Vermerk *unbestätigt*. Erst „Alle N Werte
übernehmen“ füllt das Formular und vermerkt die Übernahme; bis dahin verändert kein gelesener
Wert irgendetwas. Die Tabelle bleibt leer, solange keine Analyse angebunden ist — das ist die
ehrliche Aussage: nichts wurde gelesen. Ohne sie wäre später nicht mehr feststellbar, ob eine
Zahl gelesen oder getippt wurde, und genau das ist die Frage, wenn eine Bilanz nicht stimmt.

Nebenbei behoben: in `ExtensionEndpoints` standen seit Schritt 3 zwei XML-Kommentare übereinander
— der von `MapPolicies` war beim Einfügen über `MapCreate` gerutscht.

82 Tests, davon 3 neue zum Einlesen.

## Was Schritt 8 gebracht hat

**Fahrzeuge, strukturgleich zur Immobilie** — und das ist keine Bequemlichkeit: beides sind
Objekte, an denen Verträge, Rechnungen, Fristen und Dokumente hängen. Die Kfz-Versicherung wird
**verknüpft, nicht kopiert**: sie bleibt eine `Policy` unter Absicherung, genau wie der
Stromvertrag unter Wohnen bleibt. Ein Test hält fest, dass nach dem Anlegen genau ein Vertrag
existiert.

Die Kosten werden aus echten Buchungen gerechnet, nicht gepflegt — eine gepflegte Zahl wäre nach
zwei Monaten falsch. Gefunden wird über Kennzeichen und Fahrzeugnamen; das ist eine Heuristik und
wird als solche behandelt: was sie nicht findet, fehlt in der Summe. Besser als eine Zahl, die
mehr behauptet, als sie weiß.

Gegen die laufende API geprüft: **VW Passat 4.120 €, Skoda Fabia 1.980 €, Firmenwagen 0 €** —
genau die Werte des Prototyps. Möglich, weil Steuer, Werkstatt und Kraftstoff in der
Vorgeschichte März bis Juli liegen; der August und seine kalibrierten Monatssummen bleiben
unberührt.

**Scaneingang als Posteingang.** Gescannt wird stapelweise, eingeordnet wird später. Ein Beleg
bleibt darin, bis **Typ und Objekt** bestätigt sind — die Prüfung liegt im Dienst, nicht in der
Oberfläche: ein Beleg ohne Verknüpfung ist nicht eingeordnet, egal über welchen Weg jemand ihn
wegräumen will. Vier Belege warten in der Demo, zwei erkannt, zwei zu prüfen; die Dateien liegen
unter `Scaneingang/`, wie in der Dateiablage des Nutzers.

Damit stehen **alle fünfzehn Bereiche** in der Seitennavigation, in der Reihenfolge aus
Abschnitt 3.

### Eine Abweichung, bewusst

Der Prototyp markiert den Passat mit „1 Frist“. Seine Wechselfrist fällt auf den 30.11.2026 —
das sind 99 Tage nach dem Stichtag, also knapp außerhalb des 90-Tage-Fensters, das überall sonst
gilt. Statt das Fenster für diesen einen Fall zu dehnen, bleibt es bei 90: die Frist erscheint ab
dem 01.09.2026, sechs Tage nach dem Demo-Stichtag. Der Zustand „Frist läuft“ ist ohnehin am
Hausrat vorgeführt.

83 Tests, davon 2 neue zu Fahrzeugen.

## Was der Handoff bewusst offen lässt

Unverändert nicht gebaut, weil nicht gestaltet: Ladezustände, Offline, Fehlerdialoge, 2FA,
Rechtematrix, Auswertungen, Split-Buchung, Sondertilgung, CSV-Spalten-Mapping, Arbeit & Beruf,
Administration. Dazu die beiden neu angedeuteten Bereiche **Steuer nach Jahr** und
**Unterhalt / Scheidung**. Der Handoff sagt dazu: vorher anfragen.

# Nachtrag vom 26.08.2026

Der Handoff kam ein zweites Mal, unter demselben Ordnernamen — eine Fortschreibung, kein v5.
Rein additiv, 72 Zeilen; Design-System und Laufzeit sind byte-identisch. Abgelegt unter
[`design-handoff-v4-nachtrag/`](design-handoff-v4-nachtrag/), der erste Stand bleibt daneben
liegen, damit sich beide vergleichen lassen.

## Abschnitt 6b — Bearbeiten und Löschen

**Dasselbe Formular, vorbefüllt.** `GetFormAsync(type, id)` liefert die Feldliste des Anlegens
mit den vorhandenen Werten, anderem Kicker, Titel und Primärschalter — dazu einen Einleitungstext,
der die *Wirkung der Änderung* beschreibt statt der Anlage, und den Löschabschnitt. Geprüft wird
gegen dieselbe Liste wie beim Anlegen.

**Die Regel, die eine frühere Entscheidung korrigiert.** Der Handoff warnt ausdrücklich davor,
einen gepflegten Objektnamen beim Bearbeiten neu aus Art und Anbieter zusammenzusetzen — sonst
wird aus „Risikoleben“ beim bloßen Öffnen und Speichern „Risikoleben Hannoversche“. Genau das
hätte mein `CreatePolicyAsync` getan. Der Name wird jetzt beim **Anlegen** abgeleitet und beim
**Bearbeiten** nie wieder: dafür gibt es dort das Feld `displayName`, das es im Anlegeformular
nicht gibt. Ein Test hält den Fall fest.

Ebenso: die Werte kommen aus den **Rohfeldern**, nie aus einer Anzeigezeile. Ein Vertragsname wie
„Risikoleben“ trägt keinen Versicherer im Namen — wer ihn dort herausparsen wollte, ließe das
Pflichtfeld leer und das Formular unbenutzbar. Auch das steht als Test.

**Einstiege, zwei Muster.** Zeilen ohne eigenen Detailscreen (Konto, Budget, Depot) führen ganz
ins Bearbeiten. Zeilen, die schon navigieren (Vorsorge, Absicherung, Fahrzeug, Immobilie), tragen
rechts unter dem Betrag einen „Bearbeiten“-Link, 11 px im Akzent — dasselbe Muster wie „Rechte“
in der Benutzerliste, mit gestoppter Ereignisweitergabe.

**Löschen zweistufig, ohne Systemdialog.** Unten im Formular, durch eine Volllinie abgesetzt: der
zweite Tipp ist die Bestätigung, in Akzent-700, daneben „Behalten“.

Die Folgenbeschreibung **zählt echte Bezüge**, statt Prüfungen zu behaupten, die nicht
stattfinden — ein Satz wie „Sind noch Buchungen verknüpft?“ ohne nachzusehen klingt nach
Sorgfalt und ist keine. Geprüft in der laufenden App: „3 Buchungen hängen an diesem Konto …“
gegenüber „An diesem Konto hängt keine Buchung.“

Und die Folge stimmt auch: ein gelöschtes Konto nimmt seine Buchungen **nicht** mit, sie werden
auf „Ohne Konto“ umgeschrieben. Buchungen sind Tatsachen; das Konto war nur ihre Schublade.

**Buchungen und Dokumente.** Einzeln im Kategorie-Sheet („Löschen“ → „Wirklich löschen?“,
Splitten wandert auf Ghost), im Stapel über die Auswahlleiste mit genannter Anzahl. Beim
Dokument verschwindet nur der Eintrag — die Datei bleibt liegen, und das steht auch so da. Danach
rückt die Vorschauspalte auf den nächsten Eintrag oder in den Leerzustand.

91 Tests, davon 8 neue.
