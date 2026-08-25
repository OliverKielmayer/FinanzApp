# Handoff v4 — Responsive Client, Industry-Look, neue Bereiche

Stand 25.08.2026 · Repo `OliverKielmayer/FinanzApp` · **ersetzt** die Gestaltungsvorgaben aus `design_handoff_finanzapp_mobile/` und `design_handoff_erweiterung/`, deren fachliche Beschreibungen (Dokumentmodell, PKV-Regeln, Verknüpfungen, Tests) weiter gelten.

## Was in diesem Ordner liegt

- `FinanzApp v4 Responsive.dc.html` — der vollständige Prototyp, im Browser zu öffnen. Enthält alle Screens, Zustände und Anlege-Flows als klickbaren Ablauf mit Beispieldaten.
- `_ds/industry/styles.css` — **die verbindliche Token- und Komponentenquelle** (Farben, Ramps, Typo, Spacing, Buttons, Tags, Inputs, Tabelle, `.blueprint`, `.duotone`).
- `support.js` — Laufzeit des Prototyp-Formats. Nicht portieren, nicht als Referenz lesen.

Der Prototyp ist eine **Design-Referenz in HTML**, kein Produktionscode zum Kopieren. Nachbauen in der bestehenden Blazor-WASM-Struktur, Werte (Hex, Abstände, Größen) aus der CSS übernehmen.

---

## 1. Wechsel des Design-Systems: Modernist → Industry

Das ist die größte optische Änderung und betrifft **jeden** Screen. Ersetze `wwwroot/css/modernist.css` durch die beiliegende `styles.css` (als `wwwroot/css/industry.css`) und passe `app.css` an.

| | vorher (Modernist) | jetzt (Industry) |
| --- | --- | --- |
| Akzent | `#ec3013` Rot | `#5980a6` Stahlblau (einziger Akzent) |
| Ground / Text | `#f3f2f2` / `#201e1d` | `#f2f2f3` / `#1d1f20` |
| Schrift | Archivo | **Barlow Condensed** (Überschriften, Zahlen) über **Barlow** (Text) |
| Trennlinien | 2px zwischen Bereichen | **Haarlinien**: `1px solid var(--color-text)` zwischen Bereichen, `1px solid var(--color-divider)` zwischen Zeilen |
| Radius | 0 | 4px aus `--radius-*` (Tokens nutzen, nicht hart 0) |
| Gerahmte Objekte | keine | `.blueprint` + vier `<i class="corner tl/tr/bl/br">` Registermarken |
| Bilder | keine | `.duotone`-Wrapper (Belegvorschauen, Fotos) |

Weiter gültig: linksbündige Button-Labels, tabellarische Ziffern (`font-variant-numeric: tabular-nums`), `de-DE`-Formatierung, Minus als `−`, Akzent sparsam (Primäraktion, aktiver Zustand, Überschreitung, Frist, Kicker).

**Wichtig:** Weil Barlow Condensed schmal läuft, wurden die Displaygrößen angehoben. Skala im Prototyp: Hero 52 px (Telefon) / 64 px (Tablet) / 76 px (Desktop), Screen-Titel 21–28 px, Kachelwert 19–24 px, Zeilentitel 14–15 px, Body 12–14 px, Meta 11 px, Kicker/Label 10 px uppercase mit `letter-spacing .12em`. Nicht kleiner setzen — sonst verliert die Condensed-Schrift Präsenz.

Für Paragraphentext im Akzent `--color-accent-700` verwenden, nicht `--color-accent` (Kontrast).

## 2. Responsive: drei Modi statt Handy-Mockup

Der Prototyp misst die Fensterbreite und schaltet: **Telefon** < 768, **Tablet** 768–1200, **Desktop** > 1200. In der App entsprechen das CSS-Breakpoints; die Logik der Screens ändert sich nicht, nur ihre Anordnung.

| | Telefon | Tablet | Desktop |
| --- | --- | --- | --- |
| Navigation | Tab-Leiste unten (5 Tabs) | linke Seitennavigation, alle 15 Bereiche mit Kennzahl | dito, breiter |
| Hülle | 390 × 844 im Rahmen | 860 px breit | **100 % Breite/Höhe**, kein Mockup-Rahmen |
| Kacheln | 2 Spalten | 3 | 4 |
| Buchungen | kompakte Zeile | **Tabelle** | Tabelle |
| Dokumente | Liste → Detailscreen | **Liste + Vorschauspalte** (300 px) | dito (380 px) |
| Kategorie-Sheet | Bottom-Sheet (Y-Animation) | **rechtes Panel** 420 px, volle Höhe, X-Animation | dito |
| Scan / PKV | Schritt für Schritt | **zweispaltig**: Beleg links, Inhalt rechts | dito |
| Dashboard | Chart über Bilanz | **Chart und Bilanz nebeneinander** | dito |

Die Seitennavigation trägt oben den angemeldeten Benutzer samt Haushalt und unten einen festen „Erfassen"-Knopf. Die Schrittleisten von Scan und Erfassung entfallen ab Tablet — dort passt das Formular am Stück.

## 3. Neue Navigationsstruktur

Tabs (Telefon): **Vermögen · Vorgänge · Erfassen · Dokumente · Mehr**. Konten, Budgets und Depot liegen unter „Mehr".

Bereiche insgesamt (in dieser Reihenfolge in der Seitennavigation): Vermögen, Vorgänge, Konten & Buchungen, Budgets, Depot, Vorsorge & Kapital, Absicherung, Dokumente, Scaneingang, Gesundheit / PKV, Wohnen, Fahrzeuge, Darlehen, Import, Benutzer.

„Erfassen" ist ein **Sheet**, kein Screen: Beleg scannen (primär), Buchung erfassen, Arztrechnung / PKV, Rechnung, Dokument verknüpfen, Aufgabe / Frist, Konto/Vertrag/Objekt anlegen.

## 4. Fachliche Trennung: Vorsorge vs. Absicherung

Die vorherige Sammelkategorie „Versicherungen" ist aufgeteilt — Entscheidungsmerkmal ist, **ob ein Vertrag einen Wert hat, der ins Vermögen zählt**:

- **Vorsorge & Kapital** (unter Finanzen): Kapital-LV, Rentenversicherung, Riester, Bausparen, bAV. Haben Rückkaufswert / Ansammlungsguthaben / Ablaufleistung → Statusreport, Wertverlauf, Vermögens-Kachel **mit Stichtag** („Stand 07/2025"). Ein Jahresstand ist kein Tageskurs und darf nicht wie einer aussehen.
- **Absicherung**: Risikoleben, BU, Haftpflicht, Hausrat, Wohngebäude, Kfz, Unfall, Rechtsschutz, Krankenversicherung. Haben Beitrag, Versicherungssumme, Selbstbeteiligung, Kündigungsfrist — **keinen** Vermögenswert. Kopfzahl ist der Jahresbeitrag, nicht ein Wert.

Technisch **ein** Objektmodell mit Flag `kapitalbildend`, zwei Einstiege — keine doppelte Verwaltung. Ein Risikoleben-Vertrag darf nie im Nettovermögen erscheinen. Die Kfz-Versicherung erscheint unter Absicherung **und** auf der Fahrzeugseite als verknüpfter Vertrag (wie der Stromvertrag an der Immobilie). Krankenversicherung bleibt Absicherung, ihre Erstattungen laufen in Gesundheit.

Beispieldaten im Prototyp (aus der echten Ablage des Nutzers abgeleitet): Vorsorge = Heidelberger Leben 20.481,52 € (Stand 31.07.2025), Raiffeisenbank LV, Riester Debeka, Bausparen BSK SHA (zuteilungsreif, als Frist markiert) → 58.940 €. Absicherung = 8 Verträge → 12.330 €/Jahr.

## 5. Zwei neue Objekttypen

**Fahrzeuge** — strukturell identisch zur Immobilie: Objektliste → Objektseite mit „Kosten 12 Monate", Verträgen und Vorgängen (Kfz-Versicherung mit Wechselfrist, Kfz-Steuer, Werkstattrechnungen, Finanzierung) und Dokumenten. Beispiel: VW Passat L-2905, Skoda Fabia L-1113, Firmenwagen.

**Scaneingang** — Posteingang statt Einzelbeleg: Liste wartender Belege mit Zustand „erkannt / prüfen", Absender und Seitenzahl; ein Tap öffnet den Vorschlagsschritt des Scan-Flows. Belege bleiben im Eingang, bis Typ und Objekt bestätigt sind. Entspricht dem Ordner `Scaneingang` in der bestehenden Dateiablage.

## 6. Anlege-Flows (neu, ein gemeinsames Muster)

Einstieg ist immer eine „+"-Zeile **am Ende der jeweiligen Liste** (nicht in „Mehr"), plus ein Sammeleintrag im Erfassen-Sheet. Ein gemeinsamer Formularscreen, gesteuert über eine Feldliste je Objekttyp: Chips für Auswahlwerte, Textfelder für den Rest, Pflichtfelder markiert, Validierung nennt das fehlende Feld beim Namen („Anbieter fehlt", „Stichtag fehlt").

Umgesetzte Typen und ihre Pflichtfelder:

| Typ | Felder (Pflicht fett) |
| --- | --- |
| Konto | **Art** (Giro/Tagesgeld/Depot/Darlehen), **Bank**, IBAN, **Startsaldo**, **Stichtag**, Importprofil |
| Depot | **Broker**, Depotnummer, Depotart, **Depotwert** + **Stichtag**, Verrechnungskonto, Kursdatenquelle |
| Vorsorgevertrag | **Vertragsart**, **Anbieter**, Nummer, Beitrag, **Erreichter Wert**, **Stichtag**, Ablauf, Statusbericht (erzeugt Frist) |
| Versicherung | **Art**, **Versicherer**, Nummer, **Beitrag**, Intervall, Vertragsende, Kündigungsfrist (erzeugt Frist) |
| Immobilie | **Bezeichnung**, Adresse, Typ, **Kauf**, Marktwert, bestehendes Darlehen verknüpfen |
| Fahrzeug | **Bezeichnung**, **Kennzeichen**, Typ, Erstzulassung, Kilometerstand, Versicherung verknüpfen |
| Vertrag (Wohnen) | **Anbieter**, **Art**, Nummer, **Abschlag**, Bankkonto, Immobilie, Kündigungsfrist |
| Budget | **Kategorie**, **Betrag**, Zeitraum, Gilt ab, Warnschwelle (80/90/100 %) |

Jeder Flow schreibt wirklich in die Liste und rechnet auf Summen durch (neues Konto erscheint in der Kontoliste **und** als wählbares Bankkonto in der Vertragsanlage; neues Budget verändert Plan und „Verbleibend"; neues Depot verändert Depotwert und Kopfzeile). Doppelte Anlage wird abgelehnt („Budget für Lebensmittel besteht bereits"). „Depot" im Konto-Formular verweist auf den Depot-Flow statt ein Konto anzulegen.

## 7. Police / Beleg einlesen (neu)

Beim Anlegen von **Vorsorgevertrag** und **Versicherung** sitzt über den Feldern ein Import-Panel (`.blueprint` mit Registermarken) mit drei Zuständen:

1. **Leer** — „PDF wählen" / „Police scannen", plus Hinweis: die Datei bleibt liegen, gespeichert wird nur der relative Pfad.
2. **Liest** — sichtbare Kette: Text erkannt → Absender bestimmt → Vertragsart erkannt → Werte gelesen; abbrechbar, kein Spinner.
3. **Geprüft** — je Feld erkannter Wert **mit Herkunftsseite**; unsichere Felder im Akzent (`--color-accent-100` Fläche, „Seite 2 · unsicher"). „Alle N Werte übernehmen" füllt das Formular, Meldung „N Werte übernommen · bitte prüfen"; „Verwerfen" führt in den Leerzustand.

Erkannte Felder Vorsorge: Vertragsart, Anbieter, Vertragsnummer, Beitrag, Erreichter Wert, Stichtag, Ablauf. Versicherung: Art, Versicherer, Nummer, Beitrag, Intervall, Vertragsende, Kündigungsfrist.

Regeln für die Umsetzung: Übernahme **nie** ohne Bestätigung; jedes Feld bleibt danach editierbar; extrahierte Werte immer mit Herkunft (Seite, Konfidenz) und Bestätigungsstatus speichern; nichts Unbestätigtes verändert Vermögenszahlen; Analyse hinter **einer** austauschbaren Schnittstelle (kein Anbieter im Fachcode), lauffähig auch wenn sie fehlt — dann dieselbe Maske, leer. Metadaten kommen aus dem **Inhalt**, nie aus dem Dateinamen (der Originalname wird dennoch gespeichert).

Derselbe Ablauf gilt für den allgemeinen Scan-Flow (Beleg → Analyse → Ablagevorschlag mit Pfad → Werteprüfung → Ablage mit Vorjahresvergleich und gelernter Regel), Details im Abschnitt 3.3b des vorherigen Handoffs.

## 8. Buchungstabelle mit Stapelvergabe (Desktop-Gewinn)

Ab Tablet wird die Buchungsliste eine Tabelle: `grid-template-columns: 28px 56px minmax(160px,2fr) 118px 110px 104px` — Auswahlspalte, Datum, **Empfänger (breiteste Spalte)**, Kategorie, Konto, Betrag rechtsbündig. Kopfzeile mit „alles wählen".

Mehrfachauswahl: ausgewählte Zeilen im Akzent hinterlegt, darüber eine Leiste „N Buchungen ausgewählt" mit „Kategorie zuweisen" und „Auswahl aufheben". „Kategorie zuweisen" öffnet **das Kategorie-Panel** (kein Direktschreiben) mit Kopf „N Buchungen · Stapelvergabe · Umbuchungen bleiben unverändert". Fachliche Regel: **Umbuchungen werden von der Stapelvergabe ausgenommen**, sofern nicht ausdrücklich „Umbuchung" gewählt wird; Meldung nennt beides („6 × Wohnen · 1 Umbuchung geschützt").

## 9. Filter, Summen, Leerzustände

- **Filterzeile** über der Buchungsliste: Suche plus Chips für Konto, Kategorie und Art; ab Tablet umbrechend (`flex-wrap`), auf dem Telefon eine scrollende Reihe.
- **Summenblock** rechnet immer gegen die **sichtbare** Auswahl: Einnahmen, Ausgaben, Saldo; Umbuchungen zählen weder als Einnahme noch als Ausgabe. Nullwerte als „0,00 €" ohne Vorzeichen.
- **Triage-Banner** („N Buchungen ohne Kategorie") bezieht sich ebenfalls auf die sichtbare Menge und verschwindet, wenn der Filter keine unkategorisierten Buchungen enthält. Singular/Plural korrekt.
- **Leerzustand** statt leerer Liste: Überschrift „Keine Buchung im gewählten Ausschnitt", ein Satz zur Ursache (nennt bei Suche den Begriff) und zwei Aktionen — „Filter zurücksetzen", „Buchung erfassen".

Dasselbe Muster gilt für jede Liste: nie eine leere Fläche, immer ein Satz plus die Primäraktion.

## 10. Liquidität

Das Dashboard beginnt mit „Bleibt übrig" (Einnahmen minus Ausgaben, Sparquote) und dem Vorgangs-Banner; Nettovermögen folgt darunter. Detailscreens: **Diesen Monat** (noch fällige und erwartete Beträge, „Verfügbar nach Fixkosten"), **Wohin fließt es** (fix vs. variabel, Kategorien; kapitalbildende Vorsorge zählt als Sparen, **nicht** als Ausgabe; Eigenanteile zählen, erstattete Beträge nicht), **Sparpotential** (Budgetüberschreitungen, kündbare Verträge mit Frist, wiederkehrende Buchungen ohne Vertrag, Summe).

## 11. Umsetzungsreihenfolge

1. **Stylesheet tauschen** (Industry) und die Typo-Skala anheben — betrifft alle Screens, sonst driftet alles Weitere.
2. **Responsive Rahmen**: Seitennavigation ab 768 px, Hülle auf 100 % ab 1200 px, Kachelspalten, Erfassen-Sheet.
3. **Bereichstrennung** Vorsorge / Absicherung inklusive Flag und Vermögensberechnung (Risikoverträge raus aus dem Netto).
4. **Anlege-Flows** als eine gemeinsame Formularkomponente mit Feldliste je Typ.
5. **Buchungstabelle** mit Auswahl und Stapelvergabe, Filter, Summen, Leerzustände.
6. **Dokumente** als Master/Detail, **Scan / PKV** zweispaltig.
7. **Police-Import** hinter der Analyse-Schnittstelle; ohne OCR dieselbe Maske leer.
8. **Fahrzeuge** und **Scaneingang** als neue Objekttypen.

Ab Schritt 3 gilt weiter: EF-Core-**Migrationen** statt `EnsureCreated`, und der globale Haushalts-Filter im `DbContext` für jede neue Entität.

## 12. Was noch nicht gestaltet ist

Ladezustände, Offline, Fehlerdialoge; 2FA; Rechtematrix im Detail; Auswertungen/Reports; Split-Buchung; Sondertilgungsdialog; CSV-Spalten-Mapping; Arbeit & Beruf; Administration (Dokumenttypen, Kategorien, Regeln). Vor dem Bau dieser Bereiche anfragen.

Ebenfalls offen und aus der Dateiablage des Nutzers ersichtlich: **Steuer nach Jahr** als eigener Bereich (die Belege dort ziehen quer durch alle Bereiche) und **Unterhalt / Scheidung** als eigener Vorgangstyp mit Zahlungsverfolgung.
