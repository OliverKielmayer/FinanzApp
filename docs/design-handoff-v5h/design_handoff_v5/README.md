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
- **Der Mindermengenzuschlag ist eine Gebühr**, keine Kursdifferenz — er gehört in die Anschaffungskosten (Stück × Kurs **plus** Zuschlag), bleibt aber als eigener Bestandteil sichtbar. Die CSV führt ihn in einer eigenen Spalte, nicht im Wert; jede Beispielrechnung im Handoff muss ihn mitführen.
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
| Transaktionen bis 31.03.2024 | 321 Stück · 28.414 € Einstand (inkl. 1,00 € Zuschlag) |
| Differenz | keine |
| Buchgewinn zum Stichtag | +975 € |

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

## 14. PDF-Dokumente einlesen und Werte speichern (neu)

Der Beleg-Flow aus v4-Handoff 3.3b war auf **einen** Einzelfall gebaut (ein Statusreport, feste Werte). Er trägt jetzt ein **Dokumenttyp-Modell** mit zwei erkennbaren Arten. Grundlage sind zwei reale PDFs des Nutzers.

### 14.1 Der entscheidende Befund: Textebene oder Bild

Die beiden Beispiele unterscheiden sich technisch fundamental:

| Dokument | Beschaffenheit | Folge |
| --- | --- | --- |
| Statusreport (Original der Versicherung) | **PDF mit Textebene** — Felder und Beträge sind als Text vorhanden | direkte Feldextraktion, keine OCR nötig, verlässlich |
| Statusreport (abfotografiert/gescannt) | reines Bild, vier Seiten | OCR zwingend, Teilerkennung der Normalfall |
| Quartalsaufstellung (Bankoriginal) | **PDF mit Textebene** | direkte Feldextraktion |

Daraus die erste Regel: **erst die Textebene prüfen, dann OCR.** Ein Original-PDF der Versicherung oder Bank ist maschinenlesbar; nur Scans brauchen Erkennung. Der Vorschlagsschritt weist das aus — „Erkannt · sicher · Textebene vorhanden" gegen „nur Bild, OCR nötig". Das ist keine Kosmetik: es bestimmt, wie sehr man den Werten trauen kann, und ob Teilerkennung erwartbar ist.

Das Repo führt heute `NoBillTextExtractor`, füllt also die Maske von Hand. Die Struktur bleibt dieselbe — sobald ein Extraktor angebunden ist, füllt er dieselben Felder vor.

### 14.2 Dokumenttyp-Modell

Jede unterstützte Art ist ein Datensatz mit: Bezeichnung, Zielobjekt und Zielbeschreibung, Ablagepfad-Vorlage, Dokumentdatum, **fachlicher Stichtag**, Seitenzahl, Analyseschritte und **Feldliste**. Vorschlagsschritt, Werteprüfung, Ablagepfad, Bestätigungsseite und Speicherlogik entstehen daraus — nichts davon ist je Typ hartkodiert. Eine dritte Art (Beitragsrechnung, Steuerbescheid) ist damit ein Datensatz, kein Screen.

Jedes Feld trägt: Bezeichnung, **Herkunftsseite**, Wert, optional Zahlwert, und zwei Kennzeichen — `lead` (dieser Wert wird ins Objekt übernommen, im Akzent) und `soft` (Angabe ohne Garantie).

### 14.3 Statusreport Lebensversicherung — zehn Felder

Rückkaufswert · Ansammlungsguthaben · **erreichter Wert gesamt** · garantierte Erlebensfallleistung · Gesamtleistung bei Ablauf · Ablaufdatum · Todesfallleistung · monatliche BU-Rente · Bewertungsreserven · Schlussüberschüsse.

Fachliche Regeln:

- **Der erreichte Wert gesamt ist der Vermögenswert** (Rückkaufswert + Ansammlungsguthaben) — nicht der Rückkaufswert allein und nicht die Ablaufleistung. Nur er zählt mit Stichtag ins Vermögen.
- **Bewertungsreserven und Schlussüberschüsse sind ausdrücklich nicht garantiert** — als `soft` markiert, im Akzent beschriftet und **nie** in eine Vermögenssumme aufgenommen. Das Dokument sagt es in drei Fußnoten; die App darf es nicht unterschlagen.
- Das Dokument führt drei Leistungsszenarien (Ablauf, Beitragsfreistellung, Todesfall) mit teils gleichen Beträgen. Sie dürfen nicht vermischt werden; die App übernimmt aus dem Abschnitt **„Wert der Versicherung"**, nicht aus „Leistung im Erlebensfall".
- Übernahme schreibt Wert **und** Stichtag und meldet die Differenz zum Vorjahresbericht.

### 14.4 Quartalsaufstellung MiFID II — acht Felder

Nominale · Kurs · **Kurswert** · ISIN und WKN · Wertpapierbezeichnung · Verwahrart und Lagerland · Lagerstelle · Referenz-Nr.

- Übernahme belegt den **Depotbestand zum Stichtag** und speist den Bestandsabgleich aus §11.3 — sie ersetzt keinen Depotwert, sondern bestätigt ihn.
- Dokumentdatum (Schreiben) und Stichtag (Bestand) sind verschieden; maßgeblich ist der Stichtag.

### 14.5 Ablauf und Speichern

Erfassen (Kamera oder Datei) → Analyse als sichtbare Schrittkette, **je Typ eigene Schritte** → Vorschlag (Typ, Zielobjekt, Ablagepfad, beide Daten) → Werteprüfung mit Herkunftsseite → Bestätigung mit dem, was gespeichert wurde.

Die Bestätigungsseite nennt **die Wirkung, nicht den Vorgang**: „20.481,52 € übernommen · +521,38 € gegenüber dem Vorjahresbericht" bzw. „321 Stück zu 91,55 € · Kurswert 29.388,98 € zum 31.03.2024". Dazu die gelernte Ablageregel und der Weg ins Zielobjekt — je nach Typ „Zum Vertrag" oder „Zum Depot".

### 14.6 Umsetzungshinweise

- **Ein Extraktions-Interface**, dahinter je Dokumenttyp eine Feldzuordnung. Anbieterabhängige OCR bleibt austauschbar und ist nie Voraussetzung: fehlt sie, erscheint dieselbe Maske leer.
- **Jeder extrahierte Wert wird mit Herkunft (Seite, Konfidenz) und Bestätigungsstatus gespeichert.** Nichts Unbestätigtes verändert Vermögenszahlen.
- **Metadaten kommen aus dem Inhalt, nie aus dem Dateinamen** (v4-Handoff 3.3b) — im echten Beispiel heißt die Datei „statusreport 2024", der Inhalt sagt Stichtag 31.07.2025.
- Der Ablagepfad wird aus Bereich/Objekt/Jahr **vorgeschlagen**; gespeichert wird der relative Pfad unter `DocumentRoot`.
- Die Typwahl im Prototyp („Beispieldokument") ist ein Demoschalter, um beide Wege zu zeigen — **nicht mit ausliefern**; im Betrieb erkennt die Analyse den Typ aus dem Text.

## 15. Steuerjahr-Paket, Druckblatt und vier entschiedene Rückfragen (neu)

Ergänzt v4-Handoff §10b (Auswertungen) und v5 §8 (Arbeit & Beruf). Der Bericht „Steuerjahr" ist der sechste unter Auswertungen; das Druckblatt gilt für alle Berichte, ist aber am Steuerjahr gestaltet, weil dort gedruckt wird.

### 15.1 Steuerjahr-Bericht

Vier Abschnitte, je aufklappbar, mit Jahreswechsel (2024–2026): **Vorsorgeaufwendungen** · **Krankheitskosten** · **Handwerkerleistungen** · **Werbungskosten**. Kopf: erfasste Summe mit Positionszahl, daneben die Belegquote.

Jede Position trägt Betrag und **Belegbezug**. Der Abschnitt trägt eine Einschränkung im Klartext — nicht als Kleingedrucktes, sondern als Teil der Aussage:

| Abschnitt | Einschränkung, die dort steht |
| --- | --- |
| Vorsorgeaufwendungen | Für Riester braucht das Finanzamt die Anbieterbescheinigung, nicht die Buchung. |
| Krankheitskosten | Nur Eigenanteile; wirksam erst über der zumutbaren Belastung — die App kennt das zu versteuernde Einkommen nicht und rechnet sie nicht aus. |
| Handwerkerleistungen | Nur Arbeitslohn, nicht Material — die App trennt das nicht selbst. Barzahlung wird nicht anerkannt; alle Positionen stammen aus Kontobuchungen. |
| Werbungskosten | Kandidaten, keine Feststellung. Die Entfernungspauschale ist gerechnet und muss geprüft werden. |

Dazu zwei Blöcke, die genauso wichtig sind wie die Summen:

- **„Nicht enthalten"** — was bewusst fehlt, mit Grund: Darlehenszinsen (selbst genutzt), Kfz-Versicherung (privat), Kapital-LV-Beiträge (Altvertrag), erstattete Arztrechnungen (kein Eigenanteil). Ohne diesen Block hält der Nutzer die Liste für vollständig.
- **„Erwartet, noch ohne Betrag"** — Posten, die im Jahr anfallen, aber keinen Betrag haben. Sie zählen in **keine** Summe und in **keinen** Zähler.

Der Bericht ist ausdrücklich als Kandidatensammlung ausgewiesen, nicht als Steuerberechnung: Höchstbeträge, zumutbare Belastung und die Trennung Arbeitslohn/Material bleiben außen vor.

### 15.2 Zwei Kennzeichen, nie eines: Beleg und Schätzung

Der teuerste Fehler dieser Runde. `ok: false` hieß zunächst zweierlei — „Beleg fehlt" **und** „Wert ist geschätzt". Damit stand die Entfernungspauschale (aus Arbeitsvertrag gerechnet, aber sehr wohl belegt) im Topf „ohne Beleg" und machte 61 % dieses Betrags aus. Für den Empfänger des Blattes ist das der entscheidende Unterschied: **einen fehlenden Beleg reicht man nach, eine Schätzung muss man nachrechnen.**

Umgesetzt sind zwei unabhängige Kennzeichen und **eine gemeinsame Formel**, die Marke, Text und Ton erzeugt — Bericht und Druckblatt lesen dieselbe:

| Fall | Marke | Ton |
| --- | --- | --- |
| Beleg fehlt | `⚠ fehlt: <Belegart>` | `--color-accent-800`, Gewicht 600 |
| Wert geschätzt | `≈ geschätzt · <Herkunft>` | `--color-neutral-600`, Gewicht 400 |
| in Ordnung | Belegbezug ohne Marke | `--color-neutral-600` |

Regeln, die daraus folgen:

- **Der Belegtext benennt die Sache, nicht die Aussage** — „Anbieterbescheinigung", nicht „Bescheinigung fehlt". Sonst entsteht beim Zusammensetzen „⚠ Beleg fehlt · Beleg fehlt", und genau das stand eine Runde auf dem Blatt.
- **Zwei Kennzahlen, zwei Zeilen**: „1.540 € ohne Beleg · 2 Positionen" und darunter „2.440 € davon geschätzt · 1 Position nachrechnen". Nicht in eine Quote zusammenziehen.
- **Beide Kennzahlen in derselben Einheit** (Euro zuerst, Positionen dahinter). Eine Euro-Quote neben einem Positionszähler erzeugte „belegt 100 %" neben „1 Position ohne Beleg" — dieselbe Regelverletzung wie in §3b.
- **Ein Wort, eine Bedeutung.** „gerechnet" heißt in der Marke *geschätzt*; der fehlerfreie Abschnittsfall heißt deshalb „belegt, kein Schätzwert", nicht „belegt und gerechnet".
- **Eine Position über 0 € ist keine Steuerposition** und fällt aus jeder Summe und jedem Zähler.

### 15.3 Druckblatt

Erstmals gestaltet — das Repo hatte Druck-Stylesheet plus `window.print()` gebaut, ohne dass jemand das Blatt gesehen hat. Als Vorschau-Overlay über allem, damit man es vor dem Drucken beurteilen kann: graue Fläche, darauf ein weißes Blatt (max. 620 px, auf dem Telefon volle Breite).

Aufbau: Kopf mit Haushalt und Benutzer, Titel, Untertitel („Absetzbare Kandidaten mit Belegbezug · keine Steuerberechnung") und Erstellungszeitpunkt rechts · KPI-Reihe (Erfasst · Ohne Beleg · Geschätzt) · je Abschnitt eine Überschrift mit Summe über Volllinie, darunter die Positionen · Gesamtsumme über Doppellinie · Fußnote mit der Erklärung beider Marken und den nicht berücksichtigten Größen · Quellenzeile und Seitenangabe.

**Der Belegbezug steht als zweite Zeile unter der Position**, nicht in einer eigenen Spalte. Eine feste Spaltenbreite (132 px) hatte „aus der PKV-Bilanz" mitten im Wort getrennt und die Zeilenhöhe verdoppelt; als zweite Zeile hat der Text die volle Blattbreite und jede Zeile bleibt gleich hoch.

Das Blatt nennt **exakt dieselben Zahlen** wie der Bildschirm — es hat keine eigene Rechnung, sondern liest die des Berichts. Umsetzung bleibt Druck-Stylesheet plus `window.print()`; keine PDF-Abhängigkeit.

### 15.4 Die Brücke von der PKV-Bilanz

Die PKV-Bilanz endete mit „potenziell absetzbar" und einem Toast. Jetzt springt der Link in den Steuerjahr-Bericht, setzt das Jahr aus der PKV-Ansicht und klappt die Krankheitskosten auf.

Wichtiger als die Navigation: **beide nennen dieselbe Zahl aus derselben Quelle.** Die Krankheitskosten werden aus den PKV-Vorgängen gerechnet, nicht als Literal geführt — vorher standen 9.620 € (Bilanz) gegen 12.798 € (Bericht) für dieselbe Aussage, und die Apotheke war doppelt gezählt. Für die Umsetzung: die Steuerpositionen sind **abgeleitete Werte**, keine eigenen Datensätze, wo eine Quelle existiert.

### 15.5 Vier Rückfragen aus dem Repo, entschieden

| Frage | Entscheidung |
| --- | --- |
| Netto-Schätzfaktor 0,62 | **Nettogehalt ist ein Eingabefeld.** Die Schätzung (59 %) greift nur, wenn es leer bleibt, und wird überall gekennzeichnet: Abrechnungszeile „Netto 5.240 € (geschätzt)", Abgabenquote mit Sternchen und Fußnote „trag es aus der Abrechnung ein, dann rechnet die Quote echt". Ein Faktor, der niemandes Steuerklasse kennt, darf nicht unsichtbar in Auswertungen wirken. |
| Vertragsende beim Arbeitsverhältnis | **Feld ergänzt** (leer = laufend). Es beendet das Verhältnis und bedient damit die Regel „Beendetes zählt nicht als laufende Last" aus §3(a). |
| Lohnabrechnung erfassen | **Maske im Arbeitsbereich**, aufklappbar an der Stelle der „+"-Zeile — nicht im generischen Erfassen-Sheet, weil der Kontext (Arbeitgeber, letzte Abrechnung) hier schon steht. Vier Felder (Monat, Brutto, Netto, Auszahlung), Nebenweg „stattdessen scannen", Live-Hinweis, der die Abgabenquote der Eingabe rechnet oder sagt, was ohne Netto passiert. Doppelter Monat wird abgewiesen. |
| Druckansicht | siehe 15.3. |

### 15.6 Rechenprobe beim PDF-Einlesen

Aus zwei Repo-Befunden zu den echten Dokumenten: das Original des Statusreports ist **Seitenbilder mit unsichtbarer Textebene darunter** (nicht „Original oder Scan" — beides zugleich), und in dieser Textebene steht die Wertspalte stellenweise um **eine Zeile** versetzt. Wer Zeilen zählt, liest dort die Abschnittsüberschrift als Rückkaufswert.

Deshalb zeigt der Prüfschritt vor der Übernahme eine **Rechenprobe** in einem Blueprint-Rahmen:

- Statusreport: `Rückkaufswert + Ansammlungsguthaben = ausgewiesene Gesamtleistung`
- Quartalsaufstellung: `Nominale × Kurs = ausgewiesener Kurswert`

Dazu die Begründung im Klartext. Die Analysekette endet mit „Rechenprobe bestanden", und der Vorschlagsschritt beschreibt die **Beschaffenheit** der Textebene („Seitenbilder mit Textebene darunter" / „durchsuchbarer Text") statt eines Ja/Nein. Die Zuordnung hängt an Abschnitt und Beschriftung, nicht an Zeilenposition — die Probe fängt ab, was dabei trotzdem verrutscht.

### 15.7 Was danach offen bleibt

Aus §10b noch nicht gebaut: **Vermögensentwicklung nach Klasse** (mit dem Stichtagsproblem: Depotkurse tagesaktuell, LV-Werte bis zu ein Jahr alt), **Objektkosten** (€/m², €/km), **Liquiditätsprognose 3–6 Monate**. Weiterhin offen: 2FA, Rechtematrix im Detail, Split-Buchung, Sondertilgung, CSV-Spalten-Mapping, ablaufende Dokumente als Fristquelle, **Steuer nach Jahr als eigener Bereich** (der Bericht ist die Auswertung, nicht die Ablage) und **Unterhalt/Scheidung** als Vorgangstyp.

Beim PDF-Scan offen (vom Repo gemeldet): eine Position je Quartalsaufstellung — für mehrere Fonds bräuchte der Typ eine Wiederholgruppe · keine Texterkennung für reine Bilder · die gelernte Ablageregel wird angezeigt, aber nicht gespeichert · kein Weg, die vorgeschlagene Ablage zu ändern.

## 16. Kursverlauf je Position und Kursabruf über eine Web-API (neu)

Ergänzt §11 (Depot). Bisher hatte jede Position genau einen Kurs — den aus der letzten Ausführung. Jetzt gibt es eine **gespeicherte Kurszeitreihe** je Wertpapier und einen Abruf, der sie fortschreibt.

### 16.1 Die tragende Entscheidung: der Verlauf ist die Datenhaltung, nicht die API

Beide vom Nutzer genannten Quellen — finanzen.net und `api.boerse-frankfurt.de` — sind **inoffiziell**: keine dokumentierte öffentliche Schnittstelle, keine Zusage über Bestand, Format oder Nutzungsrecht. Eine Anwendung, die ihre Vermögenszahlen an so etwas hängt, verliert sie beim ersten Umbau der Gegenseite.

Deshalb:

- **Die Kurszeitreihe gehört der Anwendung** (ISIN, Datum, Kurs, Währung, Quelle, Abrufzeitpunkt). Sie bleibt vollständig erhalten, wenn eine Quelle ausfällt oder gewechselt wird.
- **Bewertet wird immer mit dem jüngsten gespeicherten Kurs** — nie mit einem Live-Wert, der beim nächsten Aufruf fehlt. Sein Datum steht sichtbar dabei.
- Der Abruf liegt hinter **einer Quellenschnittstelle** (`IQuoteSource`) mit ISIN als Schlüssel. Zweitquelle, manuelle Pflege und „gar keine Quelle" sind Implementierungen derselben Schnittstelle, kein Sonderfall.
- **Pull, nicht Push**: ein Zeitplan (täglich nach Börsenschluss) plus ein Knopf. Kein Abruf bei jedem Seitenaufruf — das ist bei einer inoffiziellen Quelle der schnellste Weg zur Sperre.
- Gespeichert werden **Tages- bzw. Monatsschlusskurse**, keine Intraday-Ticks. Für eine Vermögensübersicht ist alles darunter Rauschen.

### 16.2 Positionszeile

Die Zeile trägt zusätzlich eine **Sparkline** (80 × 22, Akzent bei Aufwärtstrend, Neutralton sonst) und ist aufklappbar.

Aufgeklappt:

1. **Zeitraumwahl** 1M / 6M / 12M / Alle.
2. **Kursverlauf** im Blueprint-Rahmen mit gestrichelter **Einstandslinie** — die eigentliche Aussage des Charts ist nicht der Kurs, sondern das Verhältnis zum eigenen Einstand.
3. **Drei Kennzahlen**: Veränderung im Zeitraum, Tief–Hoch, „über Einstand seit MM/JJJJ".
4. **Herkunft im Klartext**: Kursquelle mit ISIN, letzter Abruf, Zahl der gespeicherten Kurse — und der Satz, dass der Verlauf einen Quellenwechsel überlebt.

**Regel für die Einstandslinie** (hier zuerst gebrochen): Sie wird **nur gezeichnet, wenn der Einstand im dargestellten Kursbereich liegt.** Ein an den Chartrand geklemmter Wert behauptet eine Größenrelation, die es nicht gibt — in der 12-Monats-Ansicht sah die Kurve knapp über der Linie aus, tatsächlich lagen 33 % dazwischen. Liegt der Einstand außerhalb, entfällt die Linie und die Legende sagt es: „97,10 € liegt unter dem Bereich". Die Y-Skala **nicht** aufweiten, um ihn hineinzuzwingen — dann wird die Kurve in kurzen Zeiträumen flach und nutzlos.

**Kurse durchgehend mit zwei Dezimalen.** Ein auf 130 € gerundeter Höchstkurs, den es nie gab, steht sonst zwei Zeilen über dem echten „aktuell 129,50 €".

### 16.3 Abrufzustände

Ein Band über der Positionsliste, vier Zustände:

| Zustand | Aussage | Aktion |
| --- | --- | --- |
| aktuell | „Kurse aktuell · Börse Frankfurt · heute 17:35 · Verlauf seit 03/2023 gespeichert" | Jetzt abrufen |
| lädt | „Kurse werden abgerufen …" | Abbrechen |
| veraltet | „Kurse vom 27.08.2026 · Bewertung rechnet mit dem gespeicherten Kurs." | Jetzt abrufen |
| fehlgeschlagen | „Die Kursquelle antwortet nicht. Angezeigt wird der zuletzt gespeicherte Kurs vom 27.08.2026." (Akzentfläche) | Jetzt abrufen |

Kein Zustand blendet Zahlen aus — er sagt, wie alt sie sind. Das ist dieselbe Regel wie beim Ladefehler in §7.

**Ein Stand, überall derselbe.** Kopfzeile der Depotentwicklung, Depotzeile, Kursband und die Bestand-Liste lesen dasselbe Abrufdatum; bei veralteten Kursen springen alle vier gemeinsam auf den älteren Stand. Vorher stand „Stand 14.08." (aus der Transaktionsableitung) neben „heute 17:35" auf demselben Screen — dieselbe Verletzung wie in §3b.

### 16.4 Einstellungen

Zeile **„Kursquelle"** mit Untertitel „Börse Frankfurt · täglich 18:00 · Verlauf wird gespeichert" und dem aktuellen Zustand als Kennzahl. In der Umsetzung gehören dorthin: Quelle wählen, Abrufzeit, Zweitquelle, und die Möglichkeit, einen Kurs von Hand zu setzen (für Papiere, die keine Quelle kennt — Anteile an geschlossenen Fonds, Belegschaftsaktien).

### 16.5 Datenmodell

```
Quote:  Isin · Date · Close · Currency · Source · FetchedAt
```

- Eindeutig über (Isin, Date) — ein erneuter Abruf desselben Tages **aktualisiert**, statt zu duplizieren.
- Der Depotwert einer abgeleiteten Position ist `Stück × jüngster Quote.Close`; `AsOf` ist dessen `Date`, nicht der Abrufzeitpunkt.
- Währung mitführen: ein in USD notiertes Papier braucht einen Umrechnungskurs, der ebenfalls eine Zeitreihe ist. Im Prototyp nicht gestaltet — vor dem Bau anfragen.
- Fremdwährung, Splits und Ausschüttungen verändern die Vergleichbarkeit der Reihe. Mindestens Splits müssen erkannt werden, sonst bricht der Verlauf optisch ein, ohne dass Vermögen verloren ging.

### 16.6 Was offen bleibt

Kein Intraday. Keine Zweitquelle im Prototyp gestaltet. Keine Fremdwährungsreihe. Kein manueller Kurs. Realisierte Gewinne (Verkäufe) sind weiterhin nicht abgebildet — der Verlauf zeigt unrealisierte Bewertung.

## 17. Vermögensentwicklung nach Klasse und die Reste des PDF-Scans (neu)

Zwei Runden in einem Abschnitt: der letzte große Bericht aus §10b und die vier Punkte, die das Repo beim PDF-Scan offen gemeldet hat.

---

## 17.1 Vermögensentwicklung nach Klasse

Der Bericht, den der v4-Handoff mit „vor dem Bau anfragen" markiert hatte — wegen einer Frage, die durch den Kursabruf (§16) erst richtig scharf geworden ist.

### Das Stichtagsproblem

Die Klassen werden **unterschiedlich oft bewertet**:

| Klasse | Bewertung | Frequenz |
| --- | --- | --- |
| Konten | Saldo zum Stichtag | täglich, exakt |
| Depot | Kurs zum Stichtag | täglich, exakt (seit §16) |
| Vorsorge | Statusreport | **jährlich** — dazwischen wird der Wert fortgeschrieben |
| Immobilie | Marktwert | **Schätzung**, zuletzt 06/2024 |
| Verbindlichkeiten | Tilgungsplan | monatlich, exakt |

Eine durchgehende Linie über alle Klassen behauptet, dass zu jedem Punkt der Kurve alle Werte gemessen wurden. Beim Depot stimmt das seit dieser Woche auf den Tag genau, bei der Lebensversicherung ist der jüngste echte Wert vom 31.07.2025 — über ein Jahr alt. Eine Linie, die beides führt, ist eine Behauptung, keine Messung.

### Die Antwort: Balken je Stichtag, nicht Linie

- **Gestapelte Balken** an sechs Stichtagen im Halbjahresraster (12 Monate / 2 Jahre / 3 Jahre wählbar). Ein Balken ist ein Zeitpunkt, kein Verlauf — genau das ist die ehrliche Aussage.
- **Gestrichelte Oberkante** an jedem Segment, dessen Wert an diesem Stichtag nicht neu bewertet, sondern übernommen wurde. Die Legende erklärt das Zeichen.
- **Balken antippen wechselt den Stichtag.** Darunter je Klasse: Wert, Veränderung seit Periodenbeginn und **wie der Wert zustande kam** — „Kurs zum Stichtag" (neutral) gegen „Wert übernommen — zuletzt bewertet vor diesem Stichtag" (Akzent).
- Der ausgewählte Balken ist voll gesättigt, die übrigen auf 55 % gemischt — Auswahl ohne zweite Farbe.
- **Block „Wie belastbar ist diese Kurve?"** unter der Tabelle: je Klasse die Bewertungsart, Nicht-Exaktes im Akzent, und der Satz, warum es Balken sind. Dieser Block ist nicht Beiwerk — er ist der Grund, warum der Bericht überhaupt so aussieht.

### Umsetzungsregeln

- **Vermögensstände als Zeitreihe speichern**, nicht rückwirkend rechnen: je Stichtag und Klasse ein Wert plus die Angabe, ob er **gemessen** oder **fortgeschrieben** ist. Ohne dieses Flag lässt sich die gestrichelte Kante nicht zeichnen, und der Bericht verliert seine Aussage.
- Ein neuer Statusreport (§14) setzt rückwirkend den Wert **seines** Stichtags, nicht den heutigen — dazwischenliegende Stichtage bleiben fortgeschrieben.
- Die Netto-Definition ist dieselbe wie überall (§3b): Finanzvermögen + Sachwerte − Verbindlichkeiten, an einer Stelle gerechnet.
- Verbindlichkeiten sind im Balken nicht dargestellt (Balken zeigen Bruttovermögen), stehen aber in der Tabelle darunter und in der Nettozahl im Kopf. Das ist bewusst: ein gestapelter Balken mit negativem Segment ist nicht lesbar.

---

## 17.2 Mehrere Positionen je Quartalsaufstellung

Der Extraktor nahm bisher je Feld den ersten Treffer — ein Depot mit drei Fonds hätte nur den ersten gelesen. Der Dokumenttyp trägt jetzt eine **Wiederholgruppe**.

- Das Typ-Modell (§14.2) bekommt ein optionales `repeat` mit Titel, Zeilenschema und Summenfeld.
- Der Prüfschritt zeigt die Positionen **vor** den Einzelfeldern in einem Blueprint-Rahmen: je Zeile Name, Nominale × Kurs und Wert, darunter die Summe mit dem Abgleich gegen den ausgewiesenen Depotwert.
- **Die Rechenprobe (§15.6) prüft zweifach**: je Zeile Nominale × Kurs = Zeilenwert, und die Summe aller Zeilen gegen den Depotwert der Aufstellung. Bei einer verrutschten Wertspalte fällt genau eine Zeile aus der Probe — deshalb je Zeile, nicht nur in Summe.
- Im Prototyp als dritte wählbare Dokumentart hinterlegt („Aufstellung · 3 Positionen", Consorsbank-Beispiel mit drei Fonds).

Was das für die Umsetzung heißt: eine Aufstellung erzeugt **eine Bestandsmeldung mit N Positionen**, nicht N Dokumente. Die Zuordnung zum Depot geschieht über die Depotnummer, die Zuordnung der Zeilen über ISIN.

---

## 17.3 Ablage ändern

Der Knopf „Ablage ändern" lief bisher ins Leere. Er klappt jetzt eine Pfadwahl direkt im Vorschlagsschritt auf:

- **Drei Vorschläge als Chips**: der aus Bereich/Objekt/Jahr abgeleitete Pfad, ein Steuerjahr-Ordner (`Steuer/2025/Belege`), der Scaneingang.
- **Freies Feld** für einen abweichenden Ordner unter `DocumentRoot`.
- **Nur der Ordner ist änderbar, nicht der Dateiname** — den schlägt die Analyse aus Typ und Stichtag vor, und er trägt die Information. Der Hinweis sagt das.
- Gespeichert wird weiterhin der **relative** Pfad (v4-Handoff §3.3b).
- Die Änderung wirkt sofort auf die Pfadanzeige und wird Teil der gelernten Regel.

---

## 17.4 Gelernte Ablageregel wird gespeichert

Die Bestätigungsseite zeigte „Absender X + Typ Y → künftig automatisch hierher", ohne dass etwas gespeichert wurde.

- Die Übernahme legt jetzt eine **Ablageregel** an: Absendermuster, Dokumenttyp, Zielordner.
- Sie erscheint im Screen **Kategorieregeln** in einem eigenen Block **„Ablageregeln aus Belegen"** — mit dem gemerkten Ordner und einzeln löschbar. Darunter unverändert die „Kategorieregeln aus Importen".
- Die Bestätigungsseite nennt den **tatsächlich gespeicherten** Ordner, nicht den vorgeschlagenen — wer die Ablage geändert hat, sieht seine Wahl.

Damit gilt weiterhin: **ein Regelsystem, mehrere Einstiege** (v4-Handoff §8c). Import lernt Kategorieregeln, Belegablage lernt Ablageregeln, beide liegen im selben Screen und sind dort sichtbar und löschbar.

---

## 17.5 Was aus dem PDF-Scan offen bleibt

**Keine Texterkennung für reine Bilder.** Beide Originale tragen eine Textebene; ein abfotografierter Beleg wird erkannt, abgelegt und mit leerer Maske gemeldet, aber nicht gelesen. `IPdfTextReader` ist der Andockpunkt — die Maske ändert sich dadurch nicht, sie füllt sich nur von selbst.

## 17.6 Was insgesamt offen bleibt

Aus §10b: **Objektkosten** (€/m², €/km) und **Liquiditätsprognose 3–6 Monate** — beide rechnen auf vorhandenen Daten. Weiterhin: 2FA, Rechtematrix im Detail, Split-Buchung, Sondertilgung, CSV-Spalten-Mapping, ablaufende Dokumente als Fristquelle, **Steuer nach Jahr** als eigener Bereich, **Unterhalt/Scheidung** als Vorgangstyp. Aus §16: Zweitquelle, Fremdwährungsreihe, manueller Kurs, realisierte Gewinne.

## 10. Was danach noch offen ist

Ladezustände, Offline, Fehlerdialoge · 2FA · Rechtematrix im Detail · Split-Buchung · Sondertilgung · CSV-Spalten-Mapping · ablaufende Dokumente als Fristquelle (Schritt 7 des Erweiterungsplans) · die vier verbliebenen Berichte aus v4-Handoff 10b (Vermögensentwicklung nach Klasse, Objektkosten, Steuerjahr-Paket, Liquiditätsprognose — die PKV-Bilanz ist gebaut, siehe §12).

Aus der Dateiablage weiterhin ersichtlich und nicht abgebildet: **Steuer nach Jahr** als eigener Bereich, **Unterhalt / Scheidung** als Vorgangstyp mit Zahlungsverfolgung.
