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

1. **Klassenfilter** als Chip-Reihe mit Zählern: `Alle 25 · Konten 3 · Depot 1 · Vorsorge 4 · Absicherung 8 · Wohnen 5 · Fahrzeuge 3 · Darlehen 1`. Aktiver Chip in Akzentfüllung.
2. **Kopfkennzahl, die der Filter setzt** — das ist der Kern des Entwurfs, ohne ihn wäre die Zusammenlegung ein Verlust:

| Filter | Label | Wert | Unterzeile |
| --- | --- | --- | --- |
| Alle | Finanzvermögen · Sachwerte | `248.180 € · 395.000 €` | Verbindlichkeiten −148.300 € · Gesamt netto 494.880 € |
| Konten | Kontostände | Summe | N Positionen |
| Depot | Depotwert | Summe | N Positionen |
| Vorsorge | Erreichter Wert | Summe | N Verträge · Stichtage aus den Statusreporten |
| Absicherung | Jahresbeitrag | Summe | N Verträge · 1 Frist läuft / keine Frist offen |
| Wohnen | Objektwert | Summe | N Objekte · N Verträge |
| Fahrzeuge | Kosten pro Jahr | Summe | N Fahrzeuge · Versicherung, Steuer, Werkstatt |
| Darlehen | Restschuld | −148.300 € | Rate · nächste Zahlung |

3. **Zeilen**: Name, darunter Klassen-Tag und Metazeile; rechts Wert und Stichtag/Notiz. Objekte mit laufender Frist im Akzentmuster (`--color-accent-100`, `tag-accent`).
4. **„+"-Zeile** am Ende: bei aktivem Klassenfilter legt sie direkt in dieser Klasse an („Absicherung anlegen"), bei „Alle" öffnet sie das Erfassen-Sheet.
5. **Fußnote** erklärt die Gliederung, statt sie zu verschweigen.

### Drei Regeln, gegen die der erste Bauversuch verstoßen hat

**(a) Wertarten nicht in eine Summe zwingen.** Verträge (Absicherung, Wohnverträge, Fahrzeuge) haben **keinen** Wert, sondern Jahreskosten. Ihre Zeile zeigt `618 €/J`, nicht einen erfundenen Vermögenswert — und sie zählen in keine Vermögenssumme. Zwei Spaltenbedeutungen in einer Liste sind zulässig, solange die Einheit an der Zahl steht.

**(b) Eine Größe, ein Wert** (Regel aus v4-Handoff 10b, hier erneut verletzt). Die Kopfzeile zeigte „Nettovermögen 99.880 €", während in derselben Liste eine Immobilie mit 395.000 € stand — wer die Zeilen summiert, landet bei 495 T€. Solange Objekte auf getrennten Screens lagen, fiel das nicht auf; in **einer** Liste unter **einer** Kennzahl ist es ein direkter Widerspruch. Lösung: Finanzvermögen und Sachwerte getrennt ausweisen, Gesamt-netto in der Unterzeile. Für die Umsetzung heißt das: das Vermögensmodell braucht die Unterscheidung **Finanzvermögen / Sachwerte / Verbindlichkeiten** als drei Größen, nicht ein „Brutto". Dashboard und Nav-Kennzahl müssen derselben Definition folgen.

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

## 10. Was danach noch offen ist

Ladezustände, Offline, Fehlerdialoge · 2FA · Rechtematrix im Detail · Split-Buchung · Sondertilgung · CSV-Spalten-Mapping · ablaufende Dokumente als Fristquelle (Schritt 7 des Erweiterungsplans) · die fünf offenen Berichte aus v4-Handoff 10b (Vermögensentwicklung nach Klasse, Objektkosten, PKV-Bilanz, Steuerjahr-Paket, Liquiditätsprognose).

Aus der Dateiablage weiterhin ersichtlich und nicht abgebildet: **Steuer nach Jahr** als eigener Bereich, **Unterhalt / Scheidung** als Vorgangstyp mit Zahlungsverfolgung.
