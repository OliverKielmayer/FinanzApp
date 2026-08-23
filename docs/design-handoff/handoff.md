# Handoff: FinanzApp — Mobile Client (Vermögens- & Haushaltsverwaltung)

## Überblick

Mobiler Begleit-Client für die persönliche Finanzverwaltung (Spec: modulare ASP.NET-Core-Anwendung mit Girokonten, Tagesgeld, Depot, Lebensversicherung, Budgets, Darlehen, Import, Reporting). Der mobile Client ist **kein** Vollumfang der Web-App, sondern deckt die fünf täglichen Aufgaben ab:

1. Vermögen & Kontostände prüfen (Netto-/Bruttovermögen, Verbindlichkeiten)
2. Ausgaben schnell erfassen (3-Schritt-Flow)
3. Importierte Buchungen kategorisieren (Triage + Bottom-Sheet + Regel merken)
4. Budget-Auslastung im Blick behalten
5. Depot-Performance verfolgen

Sekundär, bereits angelegt: Darlehen mit Tilgungsplan, CAMT/CSV-Importvorschau mit Duplikaterkennung, Sammelseite „Mehr" (Lebensversicherungen, Auswertungen, Kategorien/Regeln, Sicherheit/Backup).

Zielrepo: `github.com/OliverKielmayer/FinanzApp` (derzeit leer).

## Zu den Design-Dateien

Die Dateien in diesem Bundle sind **Design-Referenzen in HTML** — Prototypen, die Aussehen und Verhalten zeigen, **kein** Produktionscode zum Kopieren. Aufgabe ist es, diese Designs in der Zielumgebung nachzubauen und dabei deren etablierte Muster und Bibliotheken zu verwenden. Da im Repo noch keine Frontend-Umgebung existiert, ist die Framework-Wahl offen; die Spec verlangt eine klar getrennte API-Schicht (ASP.NET Core + EF Core) und ein modernes Web-Frontend. Empfehlung: **Blazor WebAssembly** (eine Sprache/ein Typmodell über Frontend und Backend, PWA-fähig, damit derselbe Code Desktop und Mobile bedient) — alternativ React + TypeScript, falls im Team mehr Web-Erfahrung als .NET-Frontend-Erfahrung liegt. Die Geschäftslogik gehört in Application-Services, nicht in UI-Komponenten.

Der Prototyp ist ein reines Frontend mit Beispieldaten im Client-State — es gibt keine API-Aufrufe, keine Persistenz, keine Authentifizierung.

## Fidelity

**High-fidelity.** Farben, Typografie, Abstände, Rahmen und Interaktionen sind final und sollen pixelgenau übernommen werden. Alle Werte kommen aus dem gebundenen Design-System **Modernist** (`_ds/modernist/styles.css` in diesem Bundle). Diese Tokens sind verbindlich: keine eigenen Farben, keine abgerundeten Ecken, keine zentrierten Button-Labels.

## Design-Grundsätze (Modernist)

- Flach, architektonisch, alles in **Archivo** (400/600/800), geladen von Google Fonts.
- **Border-Radius überall 0.** Keine Ausnahme (auch nicht bei Buttons, Chips, Sheets, Progress-Bars).
- Struktur entsteht durch Linien, nicht durch Karten/Schatten: **2px** Trennlinien zwischen Hauptbereichen (`--color-divider`), **1px** zwischen Listenzeilen.
- Alles linksbündig — Überschriften, Copy und **Button-Labels** (breite Buttons: `justify-content: flex-start`).
- Akzentrot sparsam: primäre Aktion, aktiver Zustand, Überschreitungen, Kicker-Labels.
- Zahlen immer `font-variant-numeric: tabular-nums`, deutsche Formatierung (`1.234,56 €`, Minus als `−`).

## Design-Tokens

Aus `_ds/modernist/styles.css` (`:root`) — nie hart kodieren, immer über Variablen/Theme:

| Token | Wert | Verwendung |
| --- | --- | --- |
| `--color-bg` | `#f3f2f2` | App-Hintergrund |
| `--color-surface` | `#eae9e9` | Inputs, Sheets-Flächen |
| `--color-text` | `#201e1d` | Text, Gerätrahmen, Toast-Fläche |
| `--color-accent` | `#ec3013` | Primäraktion, aktive Tabs, Überschreitung |
| `--color-divider` | `rgba(32,30,29,.4)` (`color-mix` 40 %) | alle Trennlinien |
| `--color-accent-100/200/700/800` | `#fff2ef` / `#ffe0d9` / `#ae1800` / `#7c1405` | Tint-Flächen, Pressed, Text auf Tint |
| `--color-neutral-300/500/600/700/800` | `#d7d3d3` / `#9b9797` / `#7d7979` / `#605d5d` / `#444141` | Balkenspur, Sekundärtext |
| `--space-1…8` | 4 / 8 / 12 / 16 / 24 / 32 px | Abstände |
| `--radius-sm/md/lg` | `0px` | überall |
| `--shadow-sm/md/lg` | siehe styles.css | nur Sheet/Toast/Gerät |

Typo-Skala im Prototyp (px): Hero-Zahl 42 (Dashboard) / 46 (Betrag) / 38 (Depot, Darlehen), Screen-Titel 25/800, Kachelwert 19/800, Zeilentitel 14–15/600, Body 13–14/400, Meta 11, Kicker & Tab-Label 10 mit `letter-spacing .1em` und `text-transform: uppercase`. Negative Letter-Spacings bei Displaygrößen: −.025em bis −.035em.

## Bildschirm-Rahmen

Prototyp-Canvas: Gerät **390 × 844** (iPhone-Klasse), 2px Rahmen in `--color-text`, `overflow:hidden`, Flex-Spalte. Reale App: responsive, Breakpoint-Verhalten wie unten. Statusleiste im Prototyp ist Attrappe (`9:41 / AUG 2026 / 100 %`).

Globaler Aufbau jedes Screens:

1. **Header** (fix): `padding 6px 16px 12px`, unten 2px Divider. Links optional Drawer-Button (nur Variante „Drawer", 34×34, 1px Rahmen, drei 2px-Balken). Mitte: Kicker (10px, uppercase, accent) über Screen-Titel (25px/800). Rechts: Textschalter „Beträge verbergen / Beträge zeigen" (10px, uppercase, neutral-600) — maskiert alle Geldbeträge zu `••••••`.
2. **Scroll-Bereich** (`flex:1`, Scrollbalken ausgeblendet).
3. **Tab-Bar** (fix, Variante „Tabs"): 5 gleiche Spalten, oben 2px Divider, je Spalte 1px rechte Trennlinie, `padding 11px 4px 16px`. Aktiv: 22×2px Akzentstrich über dem Label, Label in Akzent, Zellenfläche `rgba(236,48,19,.07)`. Labels: VERMÖGEN · KONTEN · ERFASSEN · BUDGETS · DEPOT (10px/600, uppercase, `letter-spacing .06em`).

**Festgelegte Varianten** (der Prototyp kann alle drei umschalten, umzusetzen ist jeweils die erste): Navigation **Tabs**, Dashboard **Kacheln**, Erfassung **3 Schritte**. Die Alternativen (Drawer, Listen-Dashboard, Ziffernblock-Direkterfassung) sind im Prototyp erhalten und nur als Referenz gedacht — nicht implementieren, sofern nicht ausdrücklich gewünscht.

---

## Screens

### 1. Vermögen (Dashboard) — Startscreen

Kicker „Übersicht", Titel „Vermögen".

- **Hero**: `padding 18px 16px 16px`, Label „Nettovermögen" (10px uppercase neutral-600), Wert 42px/800 tabular (`125.839,95 €`), darunter zwei Deltas in einer Reihe (`gap 16px`): „+2.140,80 € zum Vormonat" (13px/600, `--color-accent-700`) und „+11,4 % im Jahr" (13px, neutral-700).
- **Vermögensentwicklung**: Sektion mit 2px Oberlinie. Kopfzeile: Label uppercase links, „12 Monate" rechts. Chart: SVG `viewBox 0 0 358 96`, `preserveAspectRatio="none"`, Höhe 96px — Grundlinie 1px `#bab6b6` bei y=95, Hilfslinie 1px `#d7d3d3` bei y=48, Datenlinie `polyline` 2,5px `#ec3013`, kein Fill, keine Punkte. Achsenbeschriftung: SEP 25 / FEB 26 / AUG 26 (10px, `space-between`).
- **Vermögenswerte als Kacheln**: 2-Spalten-Grid, jede Zelle `padding 14px 14px 16px`, 1px Linie rechts und unten. Zellinhalt von oben: Kategoriename (10px uppercase accent), Wert (19px/800 tabular), Untertitel (11px neutral-600), unten ein 4px-Balken (Spur `--color-neutral-300`, Füllung `--color-accent`, Breite = Anteil am Bruttovermögen). Zellen sind tappbar (Hover `rgba(32,30,29,.04)`) und navigieren: Girokonten → Konten, Tagesgeld → Konten, Depot → Depot, Lebensvers. → Mehr.
- **Bilanzblock** (`padding 0 16px`): drei Zeilen à 12px vertikal, je 1px Unterlinie: „Bruttovermögen" (14px/600 · 15px/800), „Verbindlichkeiten / Darlehen · Sparkasse" (Wert 15px/600 in `--color-accent-700`, tappbar → Darlehen), „Nettovermögen" (15px/800 · 17px/800, ohne Unterlinie).
- **KPI-Reihe**: 3 gleiche Spalten, 2px Oberlinie, 1px Trennlinien: Einnahmen `5.240 €`, Ausgaben `3.612 €`, Sparquote `31 %` (letzterer Wert in Akzent). Labels 10px uppercase, Werte 16px/800.
- **Budgets August**: Kopfzeile mit Link „Alle ansehen" (12px/600 accent) → Budgets. Danach die drei wichtigsten Budgets: Name + „412 € / 500 €" (13px, Farbe rot bei Überschreitung) und 6px-Balken.
- **Aktionen**: `padding 16px`, zwei Buttons in einer Reihe (`gap 10px`): `.btn-primary` „Buchung erfassen" (`flex:1`, min-height 48) und `.btn-secondary` „Import". Labels linksbündig.

### 2. Konten & Buchungen

Kicker „Finanzen", Titel „Konten & Buchungen".

- **Suchzeile**: `.input` „Buchungen durchsuchen" (`flex:1`, min-height 42) + `.btn-secondary` „Reset". Filtert Buchungen live über Empfänger und Kategorie (case-insensitive Substring).
- **Kontoliste**: je Zeile `padding 13px 16px`, 1px Unterlinie, links Kontoname (15px/600) über IBAN bzw. Zinsinfo (11px neutral-600), rechts Saldo (15px/800 tabular) über Aktualität (11px). Beispieldaten: Sparkasse Giro `DE44 6725 0020 0034 8891 02` — 4.812,60 €; Raiffeisenbank Giro `DE12 6706 2366 0009 1140 07` — 1.947,35 €; Tagesgeld Raiffeisen (2,35 % · Zinsen 98,12 € YTD) — 50.000,00 €.
- **Abschnittskopf** „Buchungen" mit Zähler „7 von 7".
- **Triage-Banner** (nur wenn unkategorisierte Buchungen existieren): `margin 0 16px 10px`, `padding 12px 14px`, Fläche `--color-accent-100`, links 2px Akzentbalken, Text „N Buchungen ohne Kategorie" (13px/600, accent-800) + „Jetzt zuordnen", rechts Pfeil „→" (18px/800 accent). Tap öffnet das Kategorie-Sheet für die erste unkategorisierte Buchung.
- **Buchungsliste**: je Zeile `padding 11px 16px`, 1px Unterlinie, drei Spalten: Datum (11px, Breite 36px, tabular), Mitte Empfänger (14px/600, einzeilig mit Ellipsis) über Chip-Reihe, rechts Betrag (14px/600 tabular; Einnahmen `--color-accent-700`, Ausgaben `--color-text`, Vorzeichen `+`/`−`). Chips: `.tag-neutral` bei gesetzter Kategorie, `.tag-accent` bei „Nicht zugeordnet", `.tag-outline` bei „Umbuchung"; daneben Kontokürzel (10px). Tap öffnet das Kategorie-Sheet.

### 3. Buchung erfassen — 3-Schritt-Flow

Kicker „Erfassen", Titel „Neue Buchung".

- **Schrittleiste** (fix oben, 2px Unterlinie): drei gleiche Zellen „01 Betrag", „02 Kategorie", „03 Konto", je 1px rechte Linie; aktive Zelle Fläche `--color-accent`, Text `--color-bg`, inaktive transparent mit neutral-600.
- **Schritt 1 — Betrag**: Art-Umschalter aus drei gleich breiten Zellen („Ausgabe", „Einnahme", „Umbuchung"; 1px Rahmen, `margin-left:-1px` für geteilte Kanten, aktiv = Akzentfläche + `--color-bg`). Darunter Betragsanzeige: Label (10px uppercase) über 46px/800-Zahl (bei Einnahme `--color-accent-700`) und „EUR" (24px/800 neutral-500). Darunter Ziffernblock: 3-Spalten-Grid, Zellen min-height 60, 22px/600, 1px Linien rechts/unten, Reihenfolge 1–9, `,`, `0`, `⌫`; Hover `rgba(32,30,29,.05)`, Active `--color-accent-200`. Eingabelogik: Ziffern werden als Cent-Wert angehängt (max. 8 Stellen, führende Nullen entfallen), `⌫` löscht die letzte Stelle, `,` ist inaktiv (Dezimalstellen ergeben sich automatisch).
- **Schritt 2 — Kategorie**: Chip-Wolke (`flex-wrap`, `gap 8px`), Chip `padding 10px 12px`, 13px, 1px Rahmen, aktiv Akzentfläche. Ausgabe-Kategorien: Wohnen, Lebensmittel, Auto, Freizeit, Reisen, Gesundheit, Versicherung, Sonstiges. Einnahme-Kategorien: Gehalt, Dividenden, Zinsen, Miete, Sonstiges. Bei Art „Umbuchung" wird Schritt 2 übersprungen (direkt von 1 auf 3).
- **Schritt 3 — Konto & Notiz**: Kontoliste als Zeilen (`padding 12px 0`, 1px Unterlinie), gewähltes Konto in Akzent mit Marker „gewählt". Darunter Feld „Notiz" (`.input`, Platzhalter „z. B. Wocheneinkauf").
- **Aktionsleiste** (immer sichtbar, `padding 16px`): `.btn-primary` „Weiter" (Schritt 1–2) bzw. „Buchung speichern" (Schritt 3), daneben `.btn-secondary` „Leeren" (Schritt 1) bzw. „Zurück" (Schritt 2–3). Unter der Leiste eine Zusammenfassungszeile (11px neutral-600): „Kategorie offen · Sparkasse Giro · 23.08.2026".
- **Validierung**: „Weiter" ohne Betrag → Toast „Betrag fehlt", kein Schrittwechsel. Speichern ohne Betrag ebenso.
- **Speichern**: legt die Buchung mit Vorzeichen nach Art oben in der Liste an (Empfänger = Notiz, sonst „Manuelle Buchung"), setzt Betrag/Kategorie/Notiz/Schritt zurück, wechselt auf „Konten" und zeigt Toast „−68,42 € erfasst".

### 4. Budgets

Kicker „Planung", Titel „Budgets".

- **Zeitraum-Umschalter**: Monat / Quartal / Jahr, gleiche Optik wie der Art-Umschalter (im Prototyp ohne Datenwirkung; real: Budgetperioden monatlich, jährlich, benutzerdefiniert).
- **Summenblock**: „Verbleibend" (10px uppercase) über 32px/800-Wert, rechts „892 € von 1.250 € · 1 Überschreitung" (12px neutral-700).
- **Budgetzeilen**: `padding 14px 16px`, 1px Unterlinie; Name (15px/600) links, „412 € / 500 €" rechts (13px tabular, rot bei Überschreitung); 8px-Balken (max. 100 % Breite, Farbe rot bei Überschreitung, sonst `--color-neutral-800`); Fußzeile 11px neutral-600 mit „88 € verbleibend" bzw. „36 € über Budget" links und Auslastung in Prozent rechts.
- **Fuß**: `.btn-secondary.btn-block` „Neues Budget anlegen".

### 5. Depot

Kicker „Investments", Titel „Depot".

- **Hero**: „Depotwert · finanzen.net ZERO" über 38px/800-Wert `132.480,00 €`, darunter „+18.940,20 € G/V" (accent-700) und „+16,7 % · TTWROR 9,8 % p. a." (neutral-700).
- **Depotentwicklung**: Chart wie Dashboard, `viewBox 0 0 358 84`, Kopfzeile rechts mit Kursstand „Kurse: 22.08.2026, 17:35" (wichtig: sichtbarer Zeitstempel, weil Kursdaten aus einem austauschbaren Provider stammen und veralten können).
- **Positionen**: je Zeile Name (14px/600, Ellipsis) + Wert (14px/800 tabular); Metazeile 11px mit „412 St. · 118,40 € · IE00BK5BQT80" links und Performance rechts (600, accent-700 positiv, neutral-700 negativ). Beispiele: Vanguard FTSE All-World, iShares Core MSCI World, Xtrackers MSCI EM, Allianz SE.

### 6. Darlehen (aus Dashboard/Mehr erreichbar)

Restschuld 38px/800 in `--color-accent-700`, Unterzeile „1,84 % Sollzins · Rate 1.180 € · nächste Zahlung 01.09.2026". Darunter `.table` „Tilgungsplan" mit Spalten Monat / Zins / Tilgung / Rest (12px, tabular) und `.btn-secondary.btn-block` „Sondertilgung planen".

### 7. Importvorschau (CSV / CAMT)

Kopf: Dateiname „camt053_2026-08.xml · Sparkasse · 41 Datensätze" plus Tags `.tag-neutral` „CAMT.053" und `.tag-outline` „Profil: Sparkasse Standard". Danach vier Statuszeilen mit Zähler (20px/800): Neue Buchungen 34 (Text), Bereits vorhanden 5 (neutral-700, „per Importreferenz erkannt"), Mögliche Duplikate 2 (accent, „Prüfung empfohlen"), Fehlerhafte Sätze 0. Aktionen: `.btn-primary` „34 Buchungen importieren" (→ Konten + Toast „34 Buchungen importiert") und `.btn-secondary` „Abbrechen".

### 8. Mehr / Sammelseite

Zeilenliste (`padding 15px 16px`, 1px Unterlinie): Label (15px/600) über Untertitel (11px), rechts Wert (14px/600 tabular neutral-700). Einträge: Lebensversicherungen (Heidelberger Leben · Rückkaufswert 01.07.2026 · 84.900 €), Darlehen (Rate 1.180 € · −148.300 €), Datenimport (CSV & CAMT · 2 Profile), Auswertungen, Kategorien & Regeln (24 Kategorien · 11 Regeln), Sicherheit (2FA aktiv · Backup 23.08.2026 03:00).

---

## Overlays

### Kategorie-Sheet (Bottom-Sheet)

Öffnet aus Buchungsliste oder Triage-Banner. Backdrop `rgba(45,43,43,.5)` (Tap schließt). Sheet unten angedockt, volle Breite, `max-height 82 %`, 2px Oberlinie in `--color-text`, Fläche `--color-bg`, Einblendung `translateY(100%) → 0` in **220 ms**, `cubic-bezier(.2,.8,.2,1)`.

- Kopf (2px Unterlinie): Kicker „Kategorie zuordnen" (accent), Empfänger 19px/800, Metazeile „22.08.2026 · Sparkasse Giro"; rechts Betrag 19px/800 tabular.
- Körper (scrollbar): Chip-Wolke der Kategorien passend zum Vorzeichen (Einnahme- vs. Ausgabekategorien) plus „Umbuchung"; Auswahl setzt die Kategorie sofort und zeigt Toast „Lebensmittel zugeordnet" (Sheet bleibt offen). Darunter, durch 1px Linie getrennt: „Regel für ‚REWE' merken" (12px neutral-700) mit `.radio`-Checkbox rechts (Default an) — Regel legt beim Bestätigen eine Kategorisierungsregel auf dem Empfänger-Präfix an.
- Fuß (2px Oberlinie): `.btn-primary` „Fertig" (`flex:1`) und `.btn-secondary` „Splitten" (Split-Buchung noch nicht designt).

### Drawer (Variante, nicht zu implementieren)

290px breit, links, 2px rechte Kante, `translateX(-100%) → 0` in 200 ms; Kopf mit „Angemeldet / Oliver W. / 2FA aktiv · Sitzung 12 Min", danach Navigationszeilen; aktiver Eintrag Akzenttext auf `rgba(236,48,19,.07)`.

### Toast

`position:absolute`, `left/right 16px`, `bottom 96px`, Fläche `--color-text`, Text `--color-bg`, `padding 12px 14px`, links Meldung (13px/600), rechts „GESPEICHERT" (10px uppercase, Opacity .7), Schatten `0 8px 24px rgba(45,43,43,.35)`. Auto-Dismiss nach **2600 ms**, jede neue Meldung setzt den Timer zurück.

## Interaktionen & Verhalten

- Navigation: Tab-Tap wechselt Screen und schließt den Drawer; Deep-Taps aus dem Dashboard (Kachel, Verbindlichkeiten-Zeile, „Alle ansehen") führen auf Detailscreens. Kein Zurück-Stack im Prototyp — real: Systemback/Breadcrumb auf Detailscreens (Darlehen, Import) vorsehen.
- Hover auf tappbaren Zeilen/Kacheln: `rgba(32,30,29,.04)`; Ziffernblock Hover `rgba(32,30,29,.05)`, Active `--color-accent-200`; Buttons nutzen die Zustände aus `styles.css` (`--color-accent-600` Hover, `-700` Pressed). Fokus: `:focus-visible` 2px Akzent, Offset 2px — Standard-Browserring nie stehen lassen.
- Beträge-Maske: ein Schalter maskiert alle Geldwerte aller Screens gleichzeitig (`••••••`); Prozentwerte bleiben sichtbar.
- Fehlende Zustände (bewusst offen, vor Implementierung zu designen): Ladezustände, API-/Kursdaten-Ausfall-Hinweis, leere Listen, Offline, Fehlerdialoge, Login/2FA, wiederkehrende Buchungen, Auswertungen/Reports, Split-Buchung, Sondertilgungsdialog, CSV-Spalten-Mapping.
- Responsive: Layout ist eine Einspalten-Flexspalte und skaliert von 360 px bis Tablet-Breite; ab ≥768 px sollten die Kacheln auf 3–4 Spalten gehen und die Tab-Bar in eine seitliche Navigation wandern (Web-App-Layout). Tap-Ziele nie unter 44 px.

## State (Prototyp → Zielarchitektur)

Client-State im Prototyp: `screen`, `drawerOpen`, `hide` (Maske), `query`, `kind` (Ausgabe/Einnahme/Umbuchung), `digits` (Cent-String), `cat`, `acct`, `note`, `step` (1–3), `sheetId`, `ruleOn`, `period`, `toast`, `tx[]`.

In der Zielarchitektur entsprechen diese Daten API-Ressourcen — der Client hält nur UI-State (aktueller Screen, Filter, Erfassungsformular, Sheet, Maske) und ruft die Application-Schicht:

- Dashboard: Vermögensaggregat (Brutto/Netto/Verbindlichkeiten, Deltas Vormonat/Vorjahr), Zeitreihe Vermögensentwicklung, Monatssummen Einnahmen/Ausgaben/Sparquote, Top-Budgets.
- Konten: Kontenliste mit Saldo, Buchungsliste mit Server-Paging/Filter/Suche.
- Erfassung: POST Buchung (idempotent, mit clientseitigem Request-Key), Kategorien pro Richtung, Konten.
- Kategorisierung: PATCH Kategorie, optional POST Kategorisierungsregel.
- Depot: Positionen mit letzten Kursen inkl. Kurszeitstempel; bei Providerausfall zuletzt bekannte Kurse + Hinweis.
- Import: Vorschau-Endpoint (neu/vorhanden/Duplikat/fehlerhaft), Commit transaktional.

Beträge durchgehend `decimal` (nie `double`/`float`), Rundung erst in der Anzeige, Formatierung `de-DE`. Umbuchungen dürfen in Auswertungen nicht als Einnahme/Ausgabe zählen (im Prototyp durch eigene Art + `.tag-outline` sichtbar gemacht).

## Assets

Keine Bild- oder Icon-Assets. Der Prototyp ist bewusst iconfrei und typografisch — die einzigen Grafiken sind zwei Inline-SVG-Linienchts sowie geometrische Elemente (Drawer-Balken, Fortschrittsbalken). Falls im Produkt Icons gewünscht sind, verwendet Modernist **Lucide** (lucide.dev). Schrift: Archivo via Google Fonts (im Produkt selbst hosten).

## Dateien

- `FinanzApp.dc.html` — der Prototyp (Template + Logik in einer Datei). Er lädt das Design-System aus `_ds/modernist-f2a2de5d-.../styles.css`; in diesem Bundle liegt die Datei unter `_ds/modernist/styles.css` — Pfad anpassen oder die CSS direkt als Token-Quelle lesen.
- `support.js` — Laufzeit des Prototyp-Formats. Nicht portieren, nicht als Referenz lesen.
- `_ds/modernist/styles.css` — **die verbindliche Token- und Komponentenquelle** (Farben, Ramps, Typo, Spacing, Buttons, Tags, Inputs, Tabelle, Dialog).

Öffnen: `FinanzApp.dc.html` direkt im Browser (relative Pfade beibehalten).
