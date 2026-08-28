# Handoff v5 — Navigationsumbau: 19 Bereiche auf 6

Stand 27.08.2026 · Repo `OliverKielmayer/FinanzApp` · **ergänzt** `design_handoff_v4/` (dessen Abschnitte 1–10b gelten weiter unverändert, soweit hier nichts anderes steht). Prototyp: `FinanzApp v5.dc.html`.

Dieses Dokument beschreibt **nur** den Navigations- und Strukturumbau. Aussehen (Industry), Responsive-Modi, Anlegen/Bearbeiten/Löschen, Import, Kategorien, Freigaben, Auswertungen: siehe v4-Handoff.

---

## 1. Was der Umbau löst

Die Seitennavigation trug 19 gleichrangige Einträge, „Mehr" war ein Sammelbecken. Ursache war nicht die Zahl, sondern dass drei verschiedene Dinge auf einer Ebene standen:

| Gruppe | Vorher | Diagnose |
| --- | --- | --- |
| Objekte, die man besitzt | Konten, Depot, Vorsorge, Absicherung, Wohnen, Fahrzeuge, Darlehen (7) | **Alle formgleich**: Liste → Objektseite mit Kennzahl, Verträgen, Dokumenten, Kosten. Sieben Navigationspunkte für ein Muster. |
| Wege, wie Daten hereinkommen | Erfassen-Sheet, Scaneingang, Import (3) | Ein Ziel, drei Türen. Der Nutzer sucht die Tür, nicht das Dateiformat. |
| Stammdaten | Kategorien, Regeln, Benutzer (3) | Dreimal im Jahr gebraucht, dauerhaft im Weg. |

Verworfene Alternative: eine reine Verb-Achse (Konfigurieren / Erfassen / Auswerten / Aufgaben). Sie ist im Modell sauber, aber „Auswerten" hätte 12 der 19 Bereiche geschluckt — „Mehr" mit neuem Namen. Die Verben stecken jetzt in „Erfassen" und „Einstellungen", nur nicht symmetrisch.

Wireframes zur Entscheidung: `Wireframes Navigation v5.dc.html` (3a Befund, 3b gewählte Fassung, 3c Verb-Achse, 3d Lebensbereiche, 3e Desktop).

---

## 2. Neue Struktur

### Tab-Leiste (Telefon): vier Tabs

| Tab | Screen | Inhalt |
| --- | --- | --- |
| **Heute** | `dash` | Liquidität, offene Vorgänge, Nettovermögen, Kostentrend-Hinweis. Kicker/Titel: „Heute / Übersicht". |
| **Vorgänge** | `cases` | unverändert: Fristen, Rechnungen, PKV, Aufgaben. |
| **Bestand** | `holdings` | **neu** — eine Liste aller Objekte mit Klassenfilter (Abschnitt 3). |
| **Erfassen** | Sheet | alle Wege, Daten hereinzubringen (Abschnitt 4). |

Die Tab-Leiste ist damit `grid-template-columns: repeat(4, 1fr)` statt 5.

### Kopfzeile

Rechts im Kopf jedes Screens, in dieser Reihenfolge: **Suche** (Akzent, 10 px uppercase) · **Beträge verbergen** · **„•••"** (nur Telefon, führt auf Einstellungen). Suche öffnet die Dokumenten-/Objektsuche mit geleertem Query — sie ist der Ersatz für den früheren Dokumente-Tab.

### Seitennavigation (ab 768 px): vier Gruppen

Gruppentitel als 9 px uppercase, `letter-spacing .14em`, `--color-neutral-500`, mit Trennlinie darunter:

```
ALLTAG     Heute (Nettovermögen) · Vorgänge (N offen)
BESTAND    Alle Objekte (N) · Konten & Buchungen · Depot ·
           Vorsorge & Kapital · Absicherung · Wohnen · Fahrzeuge · Darlehen
ANALYSE    Auswertungen (N steigend) · Budgets · Gesundheit / PKV
SYSTEM     Einstellungen
```

Auf breiten Schirmen wird der Klassenfilter der Bestand-Liste also zur **zweiten Navigationsebene** — dieselbe Struktur, nur aufgeklappt. Unten bleibt der feste „Erfassen"-Knopf.

### Aufgelöst

| Vorher eigener Bereich | Jetzt |
| --- | --- |
| Konten, Depot, Vorsorge, Absicherung, Wohnen, Fahrzeuge, Darlehen | **Bestand** mit Klassenfilter (Einzelscreens bleiben als Detailziele erhalten) |
| Dokumente (Tab) | **Suche** in der Kopfzeile |
| Scaneingang, Import | Zeilen im **Erfassen**-Sheet |
| Kategorien, Regeln, Benutzer, Importprofile, Dokumenttypen, Sicherung | **Einstellungen** |
| „Mehr" | entfällt |

---

## 3. Bestand — der zentrale neue Screen

Eine Liste über **alle** Objektklassen. Aufbau von oben:

1. **Klassenfilter** als Chip-Reihe mit Zählern: `Alle 27 · Konten 3 · Depot 1 · Vorsorge 4 · Absicherung 8 · Wohnen 5 · Fahrzeuge 3 · Arbeit 2 · Darlehen 1`. Aktiver Chip in Akzentfüllung.
2. **Kopfkennzahl, die der Filter setzt** — das ist der Kern des Entwurfs, ohne ihn wäre die Zusammenlegung ein Verlust:

| Filter | Label | Wert | Unterzeile |
| --- | --- | --- | --- |
| Alle | **Gesamtvermögen netto** | `494.880 €` | Finanzvermögen 248.180 € · Sachwerte 395.000 € · Verbindlichkeiten −148.300 € |
| Konten | Kontostände | Summe | N Positionen |
| Depot | Depotwert | Summe | N Positionen |
| Vorsorge | Erreichter Wert | Summe | N Verträge · Stichtage aus den Statusreporten |
| Absicherung | Jahresbeitrag | Summe | N Verträge · 1 Frist läuft / keine Frist offen |
| Wohnen | Objektwert | Summe | N Objekte · N Verträge |
| Fahrzeuge | Kosten pro Jahr | Summe | N Fahrzeuge · Versicherung, Steuer, Werkstatt |
| Arbeit | Bruttogehalt pro Jahr | Summe **nur laufender** Verhältnisse | N laufend · N beendet · N Abrechnungen erfasst |
| Darlehen | Restschuld | −148.300 € | Rate · nächste Zahlung |

3. **Zeilen**: Name, darunter Klassen-Tag und Metazeile; rechts Wert und Stichtag/Notiz. Objekte mit laufender Frist im Akzentmuster (`--color-accent-100`, `tag-accent`). Beendete Objekte (Arbeitsverhältnis, später gekündigte Verträge) zeigen `—` statt einer laufenden Jahreslast.
4. **„+"-Zeile** am Ende: bei aktivem Klassenfilter legt sie direkt in dieser Klasse an („Absicherung anlegen"), bei „Alle" öffnet sie das Erfassen-Sheet.
5. **Fußnote** erklärt die Gliederung, statt sie zu verschweigen.

### Drei Regeln, gegen die der erste Bauversuch verstoßen hat

**(a) Wertarten nicht in eine Summe zwingen.** Verträge (Absicherung, Wohnverträge, Fahrzeuge) haben **keinen** Wert, sondern Jahreskosten. Ihre Zeile zeigt `618 €/J`, nicht einen erfundenen Vermögenswert — und sie zählen in keine Vermögenssumme. Zwei Spaltenbedeutungen in einer Liste sind zulässig, solange die Einheit an der Zahl steht.

**(b) Eine Größe, ein Wert** (Regel aus v4-Handoff 10b, hier zweimal verletzt). Erst zeigte der Bestand-Kopf „Nettovermögen 99.880 €", während in derselben Liste eine Immobilie mit 395.000 € stand. Danach trug der Bestand-Kopf die Dreiteilung, das Dashboard aber weiter 99.880 € — zwei Antworten auf dieselbe Frage, 395.000 € auseinander.

**Umgesetzte Lösung: eine Zahl, drei Flächen.** `Gesamtvermögen netto = Finanzvermögen + Sachwerte − Verbindlichkeiten`, an **einer** Stelle gerechnet. Diese Zahl steht identisch im Dashboard-Hero, in der Nav-Kennzahl „Heute" und im Bestand-Kopf; die Dreiteilung erscheint jeweils als Unterzeile, nie als konkurrierender Hauptwert.

Daraus folgt für die Umsetzung:

- Das Vermögensmodell führt **drei** Größen — Finanzvermögen (Konten, Depot, kapitalbildende Vorsorge), Sachwerte (Immobilien, später Fahrzeuge mit Zeitwert) und Verbindlichkeiten — statt eines „Brutto". Ein Endpoint liefert alle drei plus die Nettosumme; kein Screen rechnet sie selbst zusammen.
- Der Dashboard-Hero heißt **„Gesamtvermögen netto"**. Die frühere Zeile „Bruttovermögen" heißt jetzt **„Finanzvermögen"** — sie enthält keine Sachwerte und darf nicht so klingen. Darunter eine neue Bilanzzeile **„Sachwerte"** mit „N Immobilien · Marktwert".
- Reihenfolge im Bilanzblock: Finanzvermögen → Verbindlichkeiten → Sachwerte → Gesamtvermögen netto. Wer die drei Zeilen liest, kommt rechnerisch auf die vierte.
- Risikoverträge, laufende Kosten und beendete Objekte zählen in keine dieser Größen (siehe (a) und §8(b)).

**(c) Metazeilen aus Rohfeldern, nie aus einem Anzeigefeld.** Der erste Bau las `meta: p.meta` — ein Feld, das die Objekte nicht haben; 22 von 25 Zeilen blieben ohne Untertitel, und die Liste war ärmer als jeder Einzelbereich vorher. Es gibt je Klasse **eine** Builder-Funktion, die den Untertitel aus den Formularrohfeldern zusammensetzt (`metaPens`, `metaProt`, `metaVeh`, `metaCon`, `metaProp`); jede Ansicht — Klassenliste, Bestand-Liste, Suchtreffer — verwendet dieselbe. Ein Builder fügt außerdem das freie Zusatzinfo-Feld an („MLP bestpartner classic", „Versicherungssumme 250.000 € · kein Rückkaufswert"), das jede Bearbeitung überlebt.

Und die Regel aus dem alten Fehler bleibt: **nichts behaupten, was die Daten nicht tragen.** Eine Vertragszeile schrieb „Vertrag · ohne Konto", weil das Feld leer war — richtig ist, die Angabe dann weglassen, nicht ihre Abwesenheit als Tatsache formulieren.

---

## 4. Erfassen — eine Tür für alles Hereinkommende

Das Sheet trägt jetzt sämtliche Wege, in dieser Reihenfolge:

1. **Beleg scannen** (primär, Akzentfläche) — Rechnung, Bericht, Vertrag; wird analysiert.
2. **Buchung erfassen** — Ausgabe, Einnahme, Umbuchung.
3. **Arztrechnung / PKV** — Vorgang mit Erstattung.
4. **Rechnung** — Fälligkeit und Vertrag zuordnen.
5. **Kontoauszug einlesen** — CAMT.053 oder CSV, Duplikate werden geprüft.
6. **Scaneingang öffnen** — „4 Belege warten auf Zuordnung".
7. **Dokument verknüpfen** — vorhandene Datei einem Objekt zuordnen.
8. **Aufgabe / Frist** — Erinnerung von Hand.
9. **Objekt oder Vertrag anlegen** — Konto, Depot, Versicherung, Immobilie, Fahrzeug.

Scaneingang und Import sind damit **Zustände dieses Flusses**, keine Navigationsziele. Die wartende Belegzahl gehört als Kennzahl in die Sheet-Zeile — sie ist der Grund, das Sheet zu öffnen.

---

## 5. Einstellungen

Ein Screen, sieben Zeilen mit Untertitel und rechter Kennzahl:

Kategorien (Anzahl) · Kategorieregeln (N Regeln, „greifen beim Import, bevor gefragt wird") · Benutzer & Freigaben (Haushalt · N Benutzer) · Importprofile (CAMT.053 · CSV) · Dokumentablage (`DocumentRoot`, relative Pfade) · Dokumenttypen · Sicherheit & Sicherung („2FA noch nicht aktiv · Backup 03:00").

---

## 6. Umsetzungsreihenfolge für diesen Umbau

1. **Screen-Katalog umbauen**: vier Tabs, Gruppen in der Seitennavigation, `holdings` und `settings` als neue Screens, `more` entfällt. Die bestehenden Klassenscreens bleiben unverändert erreichbar — sie sind jetzt Detailziele der Bestand-Liste, nicht Navigationspunkte.
2. **Vermögensmodell auf drei Größen** stellen (Finanzvermögen, Sachwerte, Verbindlichkeiten) und Dashboard, Nav-Kennzahl und Bestand-Kopf aus derselben Quelle rechnen lassen. **Vor** dem Bau der Liste — sonst entsteht der Widerspruch aus 3(b) erneut.
3. **Ein Bestand-Aggregat serverseitig**: ein Endpoint liefert alle Objekte mit Klasse, Name, Metazeile, Wert **oder** Jahreskosten, Stichtag und Dringlichkeit. Nicht sieben Endpunkte im Client zusammenfügen; die Kontofreigaben (v4-Handoff 6c) filtern davor.
4. **Metazeilen-Builder je Klasse** als gemeinsame Funktion, von Klassenliste, Bestand-Liste und Suchtreffern genutzt.
5. **Erfassen-Sheet** um Kontoauszug und Scaneingang erweitern, Import und Scaneingang aus der Navigation entfernen.
6. **Einstellungen** als Sammelscreen, die Einzelscreens bleiben wie sie sind.
7. **Suche** in die Kopfzeile heben; sie sucht über Objekte, Dokumente und Buchungen (bereits im Prototyp so angelegt).

## 7. Lade-, Leer-, Offline- und Fehlerzustände (neu)

Bis hierher beschrieb der Prototyp nur den Erfolgsfall. Diese Zustände sind kein Beiwerk — sie sind das, woran Umsetzungen reihenweise scheitern.

### Verbindungsband (global, unter dem Kopf)

Ein Band über allen Screens, zwei Ausprägungen:

| Zustand | Fläche | Text | Aktion |
| --- | --- | --- | --- |
| Offline | `--color-text`, Schrift `--color-bg` | „Offline — keine Verbindung zum Haushalt" / „Anzeige aus dem letzten Abgleich. Erfasste Buchungen werden gesendet, sobald die Verbindung steht." | „Erneut versuchen" |
| Abgleich fehlgeschlagen | `--color-accent-100`, Schrift `--color-accent-800` | „Stand von heute, 06:12" / „Der letzte Abgleich ist fehlgeschlagen. Kurse und Salden können veraltet sein." | „Jetzt abgleichen" |

Das Band **nennt den Zeitpunkt** des letzten gültigen Standes. Ein Offline-Hinweis ohne Zeitangabe ist wertlos — die Frage ist nie „bin ich offline", sondern „wie alt ist, was ich sehe".

### Ladezustand

Platzhalterzeilen in der Form der echten Zeilen (zwei Textbalken unterschiedlicher Breite plus rechter Betragsbalken), Breiten variieren je Zeile, dezente Pulsation über `@keyframes` (Opazität 0.45 → 0.9, 1.4 s). **Kein Spinner** — der Platzhalter zeigt, was kommt, und verhindert den Sprung im Layout.

### Leerzustand

Nie eine leere Fläche. Überschrift, ein Satz zur Ursache, Primäraktion. Der Text unterscheidet zwei Fälle, weil sie verschiedene Auswege haben:

- **noch nichts erfasst** → „Noch kein Objekt erfasst" + Erklärung, was hier erscheinen wird, Aktion „Objekt oder Vertrag anlegen".
- **nichts in dieser Auswahl** → „Kein Objekt in ‚Fahrzeuge'" + Hinweis, dass andere Klassen gefüllt sein können, zusätzliche Aktion **„Filter aufheben"**.

### Fehlerzustand

Akzentblock **über** der Liste, nicht darunter — die Warnung muss vor den möglicherweise falschen Zahlen stehen, und die Wiederholaktion darf nicht hinter 27 Zeilen liegen. Inhalt: was fehlgeschlagen ist, **welcher Stand ersatzweise gezeigt wird** („zuletzt bekannter Stand von heute, 06:12 — Beträge können veraltet sein"), und zwei Wege: „Erneut laden" (führt in den Ladezustand) und „Mit altem Stand weiter" (blendet den Block aus, das Verbindungsband bleibt).

Grundregel: **Bei einem Ladefehler werden vorhandene Daten weiter angezeigt, nie durch eine leere Seite ersetzt** — aber immer mit sichtbarem Alter.

### Im Prototyp vorführbar

„Einstellungen → Zustände vorführen" schaltet reihum: normal → lädt → Abgleich fehlgeschlagen → offline → Ladefehler. Die Zeile nennt jeweils den aktuellen und den nächsten Zustand. **Nicht mit ausliefern** — sie ersetzt in der echten Anwendung die realen Zustandsquellen (Netzwerkstatus, Antwortzeit, Fehlerantwort).

## 8. Arbeit & Beruf (neu — war im Erweiterungsplan als „vor dem Bau anfragen" offen)

Der Bereich ist zweifach verankert: als **Bestandsklasse „Arbeit"** in der Objektliste und als eigener Screen mit den Abrechnungen. Er liefert die Einnahmenseite, die den Auswertungen bisher fehlte.

### Screen „Arbeit & Beruf"

1. **Kopf**: Arbeitgeber des laufenden Verhältnisses als Kicker, Bruttogehalt pro Jahr als Hero, Unterzeile „Brutto pro Jahr · 1 laufend · 2 erfasst".
2. **KPI-Reihe** (drei Spalten, Muster der Dashboard-Reihe): Brutto · Netto · **Abgabenquote** (aus beiden gerechnet, Akzent).
3. **Arbeitsverhältnisse**: Name, Tag `laufend` (Akzent) / `beendet` (Outline), Metazeile aus den Rohfeldern (Position · Art · seit · Std./Woche · Kündigungsfrist), rechts Bruttogehalt und „Bearbeiten". Darunter die „+"-Zeile.
4. **Lohnabrechnungen**: je Monat eine Zeile mit Monat, „Brutto … · Netto …", Zustandszeile und Auszahlungsbetrag. Zustände im Akzent: „Beleg fehlt", „Zahlung nicht zugeordnet"; neutral: „Zahlung zugeordnet · 21.08.". Abschnittskopf nennt „4 erfasst · 1 ohne Beleg".
   **Aufgeklappt** (Akkordeon, `.blueprint`-Rahmen): Bruttogehalt, Nettogehalt, Auszahlungsbetrag, Abgaben, Dokument, Bankbuchung — fehlende Werte im Akzent. Aktionen: „Beleg scannen" / „Beleg öffnen" und „Zahlung zuordnen" / „Zuordnung lösen".
5. **Vereinbarungen**: Gehaltsänderung, Bonusvereinbarung, bAV — Name, Datum, Beleg, Typ-Tag.
6. Fußnote: „Lohnabrechnungen liefern die Einnahmenseite der Auswertungen. Die Zahlung bleibt die Buchung — hier wird nur darauf verwiesen."

### Zahlungszuordnung

Gleiche Mechanik wie PKV und Rechnungen: **vorschlagen, nicht entscheiden.** Kandidat ist eine echte Gutschrift auf einem sichtbaren Konto, deren Betrag innerhalb von 15 % des Auszahlungsbetrags liegt; ohne Treffer eine ehrliche Meldung („Keine passende Gutschrift gefunden"). Die Zuordnung ist wieder lösbar. Es entsteht **keine** zweite Geldbuchung — die Abrechnung verweist auf die vorhandene `Transaction`.

### Anlegen und Bearbeiten

Typ `work` im gemeinsamen Formular. Felder (fett = Pflicht): **Arbeitgeber**, Position, Beschäftigungsart (unbefristet / befristet / Teilzeit / Werkvertrag), **Vertragsbeginn**, Arbeitszeit pro Woche, **Bruttogehalt monatlich**, Nettogehalt monatlich, Kündigungsfrist. Fehlt das Netto, wird es geschätzt und bleibt editierbar. Löschen zweistufig: „Das Arbeitsverhältnis entfällt. Erfasste Lohnabrechnungen und ihre Dokumente bleiben erhalten."

### Zwei Regeln, gegen die der erste Bau verstoßen hat

**(a) Beispieldaten müssen zur eigenen Logik passen.** Die Abrechnung 08/2026 war an die Gehaltsbuchung (5.240 €) geknüpft, führte selbst aber 3.812 € Auszahlung — eine Paarung, die der eigene Matcher (±15 %) nie vorgeschlagen hätte, und ein direkter Widerspruch zur Einnahmenzahl des Dashboards. **Der Gehaltseingang ist app-weit eine Größe**: Lohnabrechnung, Bankbuchung und Dashboard-Einnahmen müssen dieselbe Zahl nennen. In der echten Umsetzung heißt das: der Auszahlungsbetrag der Abrechnung ist die Vergleichsgröße für die Zuordnung, und Seed-/Demodaten sind gegen den Matcher zu prüfen, nicht frei zu erfinden.

**(b) Beendetes zählt nicht als laufende Last.** Die Bestandsklasse summierte beide Arbeitsverhältnisse zu „127.200 € Bruttogehalt pro Jahr", während der Bereich selbst 77.760 € nannte — 49.440 € Unterschied für dieselbe Größe. Jahreslasten rechnen **nur über aktive** Verhältnisse; die beendete Zeile zeigt „—" statt einer Jahreszahl, und die Unterzeile trennt „1 laufend · 1 beendet". Dasselbe gilt für gekündigte Verträge und verkaufte Fahrzeuge, sobald es sie gibt: `active` ist ein Feld des Objekts und ein Filter jeder Summe.

**Zähler unter demselben Wort müssen dieselbe Menge zählen oder sich benennen.** Der Navigationseintrag heißt „Arbeit & Beruf · 1 laufend", der Bestand-Chip „Arbeit 2" — er zählt die Zeilen, die die Liste zeigt. Ein nackter Zähler, der etwas anderes zählt als der daneben, ist ein Fehler.

## 9. Dokumenttypen (Administration — war ebenfalls offen)

Screen unter **Einstellungen**, gleiche Bauform wie „Kategorien" (Abschnitt 8d des v4-Handoffs), weil es dieselbe Aufgabe ist: gepflegte Stammdaten mit Verwendungsnachweis.

- **Kopf**: Anzahl als Hero, dazu der Satz, wozu der Typ dient — „bestimmt den Ablagepfad-Vorschlag und was die Beleganalyse zu erkennen versucht". Ein Dokumenttyp ist keine Dekoration; er steuert Ablage und Erkennung.
- **Bereichsfilter** als Chips mit Zählern: Alle · Arbeit · Absicherung · Vorsorge · Gesundheit · Wohnen · Finanzen · Sonstiges. Der aktive Bereich bestimmt die Zuordnung eines neu angelegten Typs — das Feldlabel sagt es („Neuer Typ · Bereich Wohnen"; bei „Alle" → Sonstiges).
- **Anlegen** über Feld + „Hinzufügen"; doppelte Namen werden case-insensitiv abgewiesen.
- **Je Zeile**: Name, Verwendungsnachweis („12 Dokumente" / „noch nicht verwendet"), Bereichs-Tag, „Umbenennen" (inline, Speichern/Abbrechen) und zweistufiges „Löschen".
- **Umbenennen** wirkt auf alle Dokumente dieses Typs; die Meldung nennt die Zahl. **Löschen** lässt abgelegte Dokumente unverändert — sie behalten ihren Typ und bleiben in der Suche. Die Fußnote sagt genau das.

### Umsetzung

`DocumentType` ist im Repo bereits eine Tabelle (`Id, HouseholdId, Name, Bereich, SortOrder`) — es fehlt nur der Pflege-Screen. Dokumente referenzieren den Typ **per Id**, nicht per Text; beim Löschen bleibt die Referenz bestehen (Soft-Delete oder Kennzeichnung „nicht mehr gepflegt"), damit die Historie nicht zerreißt. Der Screen gehört hinter `AuthPolicies.ManageUsers`, wie der Erweiterungsplan für Administration vorsieht.

## 11. Depot: Transaktionen und Quartalsaufstellungen (neu)

Der Depot-Screen hat drei Reiter: **Positionen · Transaktionen · Aufstellungen**. Grundlage sind zwei reale Dokumentarten des Nutzers (finanzen.net ZERO / Baader Bank), die als Beispieldaten im Prototyp liegen.

### 11.1 Transaktionen (CSV der Orderdatei)

Quelle: `ZERO-orders-<Datum>.csv`, semikolongetrennt, deutsche Zahlen (`-26.529,00`), Spalten u. a. Name · ISIN · WKN · Anzahl · Status · Orderart · Limit · Erstellt Datum/Zeit · Gültig bis · Richtung · Wert · Mindermengenzuschlag · Ausführung Datum/Zeit/Kurs · Anzahl ausgeführt/offen.

Screen:

- **Kopf**: Einstand gesamt (Summe der ausgeführten Werte), Unterzeile „773 Stück in 26 Ausführungen · nur Käufe"; daneben Ø Einstandskurs und die Gegenüberstellung zum aktuellen Kurs mit G/V absolut und prozentual.
- **Jahresfilter** (Alle / 2026 / 2025 / 2024) mit Zeile „N von M · N Stück · Summe".
- **Zeilen**: Ausführungsdatum, Wertpapier, Orderart als Metazeile („Limit 90,00", „Markt · 1,00 € Zuschlag"), Stück, Kurs, Wert. Ab Tablet mit Spaltenkopf.
- **Aufgeklappt**: Ausführung mit Uhrzeit, Richtung und Orderart, Stück × Ausführungskurs, Wert, ISIN · WKN, Anteil am Einstand.

Verbindliche Regeln für die Umsetzung:

- **Nur ausgeführte Sätze** zählen (`Status = ausgeführt`, `Anzahl ausgeführt`), nicht die bestellte Menge; stornierte und offene Orders erscheinen getrennt oder gar nicht, nie in der Summe.
- **Der Mindermengenzuschlag ist eine Gebühr**, keine Kursdifferenz — er gehört in die Anschaffungskosten, aber sichtbar als eigener Bestandteil.
- Verkäufe mindern den Einstand anteilig und erzeugen einen **realisierten** Gewinn; die aktuelle Datei enthält nur Käufe, das Modell muss beides tragen.
- Duplikaterkennung über Ausführungsdatum + Uhrzeit + Stück + Kurs (die Datei hat keine Ordernummer).

### 11.2 Quartalsaufstellungen (PDF nach MiFID II Art. 63)

Quelle: Bestandsnachweis der depotführenden Bank zum Quartalsstichtag. Ausgelesen und einsehbar: Stichtag, Wertpapier, ISIN · WKN, Nominale, Kurs, Kurswert, Verwahrart, Lagerland, Depot-Nr., Referenz-Nr., Verwahrstelle mit Lagerstelle, Dokumentname.

Zwei Daten sind zu unterscheiden — **Stichtag** (fachlich maßgeblich) und **Erstellungsdatum** des Schreibens; dieselbe Regel wie beim Statusreport der Lebensversicherung (v4-Handoff 3.3b).

### 11.3 Bestandsabgleich — die eigentliche Analyse

Über der Liste der Aufstellungen steht ein Abgleichblock. Er summiert die importierten Transaktionen **bis zum Stichtag** und stellt sie dem ausgewiesenen Bestand gegenüber:

| Zeile | Beispiel |
| --- | --- |
| Aufstellung per 31.03.2024 | 321 Stück · 29.389 € |
| Transaktionen bis 31.03.2024 | 321 Stück · 28.413 € Einstand |
| Differenz | keine |
| Buchgewinn zum Stichtag | +976 € |

Stimmen die Stückzahlen, ist der Depotwert **belegt** — der Block bleibt neutral (`--color-surface`). Bei Abweichung schlägt er in den Akzentzustand um und nennt den wahrscheinlichen Grund („meist fehlen Käufe aus einer nicht importierten Datei"). Das ist der einzige Weg, eine Depotbewertung zu prüfen, ohne dem Broker blind zu glauben — und der Grund, beide Dokumentarten zu unterstützen.

### 11.4 Die Regel, an der dieser Bau gescheitert ist

**Importierte Echtdaten machen erfundene Nachbardaten zu Fehlern.** Die Positionsliste führte weiterhin vier Beispielwertpapiere (u. a. 386 Stück derselben ISIN, Depotwert 132.480 €), während Transaktionen und Aufstellung 773 bzw. 321 Stück auswiesen — dieselbe ISIN mit drei Wahrheiten auf einem Screen, und der falsche Depotwert floss über das Finanzvermögen bis ins Gesamtvermögen netto.

Daraus, verbindlich:

- **Die Positionsliste wird aus den Transaktionen abgeleitet**, nicht gepflegt: Stück = Summe der Ausführungen, Einstand = Summe der Werte, aktueller Wert = Stück × letzter Kurs. Ein Depot ohne Transaktionen zeigt einen Leerzustand mit „Transaktionen importieren", keine Platzhalterpositionen.
- **Der Depotwert hat eine Quelle.** Depot-Hero, Positionsliste, Bestand-Zeile, Finanzvermögen, Gesamtvermögen netto und der Bericht „Depot G/V" lesen dieselbe Zahl. Ein manuell gepflegter Depotwert ist nur zulässig, solange keine Transaktionen vorliegen — sobald sie da sind, gewinnen sie.
- **Kursstände werden belegt oder als geschätzt gekennzeichnet.** Statt eines erfundenen Zeitstempels steht „Kurs 14.08.2026 · aus der letzten Ausführung". Ein Kurs ohne belegbare Herkunft darf nicht wie ein Live-Kurs aussehen.
- **Rundung erst bei der Ausgabe.** Der Hero der Depot-Auswertung rundete die Summe vor der Differenzbildung und wich dadurch um 1 € von der einzigen Positionszeile darunter ab. Aggregate und ihre Bestandteile müssen aus derselben ungerundeten Rechnung stammen; gerundet wird ausschließlich beim Anzeigen.

### 11.5 Einstiege

„Transaktionen importieren (CSV)" und „Quartalsaufstellung einlesen (PDF)" stehen jeweils am Ende ihrer Liste und zusätzlich im Erfassen-Sheet. Die Aufstellung läuft durch den normalen Beleg-Scan-Flow (v4-Handoff 3.3b) — sie ist ein Dokument mit Werten, keine Sonderform.

## 12. PKV-Bilanz (neu — Bericht 5 unter Auswertungen)

Schließt den Flow, der dem Nutzer am wichtigsten war: Rechnung → Einreichung → Erstattung, jetzt mit Jahresbilanz. Zeitraum ist das **Kalenderjahr** (nicht der Berichtsrahmen aus 10b), weil Eigenanteile und Beiträge steuerlich jahresweise zählen.

### Aufbau

1. **Kopf, zwei Kennzahlen**: „Eigenanteil 2026" (Akzent, mit Anteil an der Rechnungssumme und dem Hinweis „zählt als Ausgabe") und „Davon ausgezahlt" in Prozent, Unterzeile „1.126 € von 1.725 € Anspruch · 4 von 7 abgeschlossen".
2. **Dreisegment-Balken** über die Rechnungssumme: ausgezahlt · erwartet · Eigenanteil, mit Legende in Euro.
3. **KPI-Reihe**: Ø Bearbeitungsdauer (aus Einreich- und Zahldatum der abgeschlossenen Vorgänge gerechnet), offener Betrag, PKV-Jahresbeitrag — letzterer **getrennt ausgewiesen**, weil er Absicherung ist und keine Behandlungskosten.
4. **Jahresfilter** mit „N Vorgänge · Rechnungen X €".
5. **Nach Leistungserbringer**: je Praxis Rechnungssumme, derselbe Dreisegment-Balken, Anzahl, Eigenanteil und „N % ausgezahlt · X € erwartet".
6. **Offene Vorgänge**: „eingereicht 16.08. · wartet seit 12 Tagen (über dem Schnitt)" bzw. „noch nicht eingereicht"; im Akzent, wenn über dem eigenen Durchschnitt.
7. **Steuerbrücke**: „9.287 € potenziell absetzbar 2026 (Eigenanteile + Beiträge)" als Einstieg ins spätere Steuerjahr-Paket.

### Fachliche Regeln

- **Eigenanteile zählen als Gesundheitsausgabe, erstattete Beträge nicht** — sonst ist die Ausgabenseite doppelt so hoch. Diese Regel gilt app-weit (Dashboard, Kostentrend, Liquidität).
- **Anspruch ≠ Auszahlung.** Sie dürfen nie unter demselben Wort stehen: „ausgezahlt" meint Geld, das eingegangen ist, „erwartet" den offenen Anspruch. Ein Balken ohne eigenes Segment für „erwartet" behauptet Zahlungen, die nicht stattgefunden haben.
- **Eine Farbe je Bedeutung**, aus denselben Token in Legende, Kopfbalken und allen Zeilenbalken: ausgezahlt `--color-accent`, erwartet `--color-accent-300`, Eigenanteil `--color-neutral-700`.
- **Bearbeitungsdauer wird gerechnet, nicht gesetzt** — derselbe Wert speist die Bilanz-KPI und die Einordnung „über dem Schnitt" im Detailscreen.

## 13. Die Regel hinter acht Prüfrunden: eine Menge, eine Quelle

Dieser Abschnitt ist kein Feature, sondern die Lehre aus dem Bau. Acht aufeinanderfolgende Prüfrunden fanden **denselben Fehlertyp** an acht verschiedenen Stellen: eine Zahl oder ein Text wurde neben der Menge geführt, die sie beschreibt.

Gefundene Ausprägungen, alle behoben:

| Symptom | Ursache |
| --- | --- |
| Nav „4 offen" gegen 3 Einträge im Screen | Kennzahl als Literal im Screen-Katalog |
| Dashboard „3 offene Vorgänge" gegen Screen „6 offen" | Banner zählte eine Teilmenge, nannte sie aber unqualifiziert |
| Vorgangsliste zeigte bezahlten Fall als überfällig | zweite, unabhängig gepflegte Literal-Liste neben den Daten |
| PKV-Detail öffnete für jeden Eintrag denselben Fall | Screen nahm keine Datensatz-Id entgegen |
| „Einreichung_02-08.pdf" bei jedem Vorgang | Dokumentzeile hartkodiert, auch bei nicht eingereichter Rechnung |
| „4 Kategorien steigen — A, B, C" | Zahl aus `length`, Aufzählung aus `slice(0,3)` |
| Nav-Kennzahl verschwand nach Filterwechsel | globale Übersichtszahl hing am Filter eines Unterberichts |
| Depot: 386 / 773 / 321 Stück derselben ISIN | Positionsliste gepflegt statt aus Transaktionen abgeleitet |

Daraus die verbindlichen Regeln für die Umsetzung:

1. **Jede Menge hat genau eine Definition im Code.** Zähler, Listen, Banner und Nav-Kennzahlen lesen dieselbe Funktion — nie eine zweite, „nur für die Anzeige" gepflegte Liste.
2. **Detailscreens nehmen eine Id entgegen.** Ein Screen ohne Parameter, der einen Einzelfall zeigt, ist ein hartkodierter Fall und wird beim ersten echten Datensatz falsch.
3. **Übersichtskennzahlen sind filterunabhängig.** Was in der Navigation steht, darf nicht von einer Einstellung innerhalb eines Berichts abhängen — sonst verschwindet die Warnung genau dann, wenn niemand hinsieht.
4. **Abgeleitetes wird abgeleitet, nicht gepflegt.** Positionen aus Transaktionen, Vorgangslisten aus Vorgängen, Dokumentnamen aus dem Datensatz. Ein gepflegter Wert ist nur zulässig, solange die Quelle fehlt — sobald sie da ist, gewinnt sie.
5. **Zwei Zahlen unter demselben Wort müssen dieselbe Menge zählen** — oder das Wort muss den Unterschied benennen („Anspruch" vs. „ausgezahlt", „offen" vs. „eilig").
6. **Nichts behaupten, was die Daten nicht tragen.** Fehlt ein Feld, entfällt die Zeile — sie wird nicht mit einem plausiblen Wert gefüllt.
7. **Aggregate und ihre Bestandteile stammen aus derselben ungerundeten Rechnung**; gerundet wird ausschließlich bei der Ausgabe.

In der echten Anwendung ist das mehr als Sorgfalt: Zähler gehören in dieselbe Query wie die Liste, Detailrouten tragen die Id in der URL, und Übersichtskennzahlen kommen aus einem eigenen Endpoint mit festen Parametern — nicht aus dem Zustand eines Berichts.

## 10. Was danach noch offen ist

Ladezustände, Offline, Fehlerdialoge · 2FA · Rechtematrix im Detail · Split-Buchung · Sondertilgung · CSV-Spalten-Mapping · ablaufende Dokumente als Fristquelle (Schritt 7 des Erweiterungsplans) · die vier verbliebenen Berichte aus v4-Handoff 10b (Vermögensentwicklung nach Klasse, Objektkosten, Steuerjahr-Paket, Liquiditätsprognose — die PKV-Bilanz ist gebaut, siehe §12).

Aus der Dateiablage weiterhin ersichtlich und nicht abgebildet: **Steuer nach Jahr** als eigener Bereich, **Unterhalt / Scheidung** als Vorgangstyp mit Zahlungsverfolgung.
