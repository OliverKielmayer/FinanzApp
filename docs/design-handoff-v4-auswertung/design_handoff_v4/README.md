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

## 6b. Bearbeiten und Löschen (neu)

Jeder Datensatz, den die App anlegt, ist auch änderbar und löschbar — Konten, Depots, Vorsorgeverträge, Absicherungen, Fahrzeuge, Immobilien, Wohnverträge, Budgets, **Buchungen** und **Dokumenteinträge**.

### Einstieg

- Zeilen **ohne** eigene Detailnavigation (Depot, Budget, Nebenimmobilie, Konto) sind ganz tappbar und öffnen direkt das Bearbeiten-Formular.
- Zeilen, die schon auf einen Detailscreen führen (Vorsorge, Absicherung, Fahrzeug, Vertrag), tragen rechts unter dem Betrag einen Link **„Bearbeiten"** (11 px/600, Akzent) — dasselbe Muster wie „Rechte" in der Benutzerliste. Der Handler stoppt die Ereignisweitergabe, damit nicht zugleich navigiert wird.
- Die **Hauptimmobilie** wird über „Immobilie bearbeiten" im Kopf des Wohnen-Screens erreicht.

### Formular im Bearbeiten-Modus

Derselbe Screen wie beim Anlegen, vorbefüllt. Kicker „Bearbeiten", Titel je Typ („Vorsorgevertrag bearbeiten"), Primäraktion **„Änderungen speichern"**. Der Einleitungstext beschreibt die Wirkung der Änderung, nicht die Anlage — z. B. „Ein neuer Wert mit Stichtag ersetzt den bisherigen im Vermögen", „Die bisher verbrauchte Summe bleibt erhalten", „Das verknüpfte Darlehen bleibt unverändert".

### Löschen

Unten im Formular, durch eine Volllinie abgesetzt: Überschrift, ein Satz zu den Folgen, Button **„… löschen"** → nach dem ersten Tippen **„Löschen bestätigen"** (Akzent-700) mit zusätzlichem „Behalten". Kein Systemdialog.

Die Folgenbeschreibung ist typgenau und **zählt echte Bezüge**, statt Prüfungen zu behaupten, die nicht stattfinden:

| Typ | Text |
| --- | --- |
| Konto | „N Buchungen hängen an diesem Konto — sie bleiben erhalten und werden auf ‚Ohne Konto' gesetzt." (mit korrektem Singular; bei null Bezügen entsprechend) |
| Depot | verschwindet aus dem Vermögen, Buchungen auf dem Verrechnungskonto unberührt |
| Vorsorge | Wert zählt nicht mehr ins Vermögen, Statusreporte bleiben in den Dokumenten |
| Absicherung | Vertrag und Frist entfallen, gebuchte Beiträge bleiben |
| Fahrzeug | Kostenübersicht entfällt, Versicherung und Dokumente bleiben |
| Immobilie | Objekt entfällt, verknüpftes Darlehen bleibt und wandert unter Darlehen |
| Vertrag | entfällt, erfasste Rechnungen und Buchungen bleiben |
| Budget | nur die Planung entfällt, Buchungen der Kategorie bleiben |
| Dokument | „Nur der Eintrag verschwindet — die Datei bleibt im Dokumentordner liegen." |

Beim Löschen eines Kontos werden die zugehörigen Buchungen tatsächlich auf „Ohne Konto" umgeschrieben und ein aktiver Kontofilter fällt auf „Alle" zurück. Beim Umbenennen wird der neue Name in den Buchungen mitgeführt.

### Buchungen löschen

- **Einzeln** im Kategorie-Sheet: „Löschen" → „Wirklich löschen?" neben „Fertig" (Splitten wandert auf Ghost).
- **Im Stapel** über die Auswahlleiste der Tabelle: „Löschen" nennt beim ersten Tippen die Anzahl („6 Buchungen werden gelöscht — erneut tippen") und führt beim zweiten aus.

### Dokumente löschen

Im Dokument-Detail unten, gleiches zweistufiges Muster. Nach dem Löschen springt die Vorschauspalte auf den nächsten vorhandenen Eintrag (oder in den Leerzustand), und der Listenzähler rechnet aus der Liste statt aus einer festen Zahl.

### Datenmodell-Konsequenz (wichtig für die Umsetzung)

Objekte führen ihre **Rohfelder** (die Formularwerte) als eigene Struktur; die Anzeigezeile wird daraus gerendert und **nie** zurückgeparst. Zusätzlich trägt jedes Objekt ein freies Feld für Zusatzinfos, die im Formular nicht vorkommen („MLP bestpartner classic", „Versicherungssumme 250.000 €", „BU-Rente 3.871,36 € monatlich") — es überlebt jede Bearbeitung unverändert.

Zwei Regeln, die sich daraus ergeben:

1. **Keine Metadaten aus Anzeigetexten parsen.** Ein Vertragsname wie „Risikoleben" hat keinen Versicherer im Namen; ein aus dem Text geratener Wert lässt das Pflichtfeld leer und macht das Formular unbenutzbar.
2. **Relative Angaben nie einfrieren.** „Kündigung in 18 Tagen" ist eine Berechnung aus dem Vertragsende, kein gespeicherter Text — gespeichert wird das Datum.

Ein gepflegter Objektname wird beim Bearbeiten **nicht** neu aus Art + Anbieter zusammengesetzt (sonst wird aus „Risikoleben" beim bloßen Öffnen und Speichern „Risikoleben Hannoversche").

## 6c. Kontofreigaben im Haushalt (neu)

Konten gehören **einem Benutzer** und sind für andere Mitglieder des Haushalts freigebbar. Das ist die Grundlage des Mehrbenutzerbetriebs — ohne sie sieht jeder alles.

### Modell

Jedes Konto trägt einen **Eigentümer** und eine **Freigabe** mit drei Stufen:

| Freigabe | Bedeutung |
| --- | --- |
| Haushalt | alle Mitglieder sehen Konto, Buchungen und Salden |
| Nur ich | privat — nur der Eigentümer, zählt nur in dessen Vermögen |
| *Name* | namentlich für ein einzelnes Mitglied freigegeben |

Sichtbar ist ein Konto, wenn der angemeldete Benutzer **Eigentümer** ist, die Freigabe auf „Haushalt" steht oder er **namentlich** benannt ist. „Nur ich" ist eigentümerrelativ und darf nie global ausgewertet werden.

### Was daraus abgeleitet wird

Eine **einzige gefilterte Basis** (sichtbare Konten und deren Buchungen) speist alles: Kontenliste, Buchungsliste und deren Suche, Filterchips für Konto und Kategorie, Kontoauswahl bei Erfassung und Import, Triage-Zähler, Summenblock sowie **Brutto-, Netto- und Kachelwerte des Dashboards**. Ein privates Konto eines anderen Mitglieds erscheint nirgends und zählt in keiner Summe — auch nicht in der Nav-Kennzahl.

Serverseitig gehört dieser Filter in denselben Zugriffspfad wie der Haushalts-Filter (globaler Query-Filter im DbContext), nicht in einzelne Services: die Kontofreigabe ist die zweite Stufe der Mandantentrennung. Über direkte API-Aufrufe darf ein Mitglied ein nicht freigegebenes Konto nicht lesen können.

### Darstellung

- **Tag an der Kontozeile ist perspektivisch**, nie der Rohwert: eigene Konten zeigen „privat" / „Haushalt" / „geteilt mit Sabine", fremde „geteilt von Oliver". Ein fremdes Konto ist nicht bearbeitbar — der Hinweis nennt den Eigentümer.
- **Feld „Sichtbar für"** beim Anlegen und Bearbeiten, Chips aus der Mitgliederliste **ohne den angemeldeten Benutzer** (er selbst ist „Nur ich"). Neu angelegte Konten gehören dem Anmeldenden.
- **Block „Kontofreigaben"** im Benutzer-Screen listet nur **eigene** Konten mit direkt umschaltbaren Chips und der Klartextfolge („alle 3 Benutzer", „nur Oliver W.", „Oliver W. + Sabine K."). Wer keine eigenen Konten hat, sieht stattdessen „N Konten sind für dich freigegeben — die Freigabe verwaltet jeweils der Eigentümer"; der Zähler „N von M eigenen geteilt" entfällt dann.
- **Mitgliederliste** zeigt je Person „sieht N von M Konten" (Plural nach M, nicht nach N), beim Lesezugriff plus „nur lesend".

Offen und vor der Umsetzung zu entscheiden: ob eine Freigabe nur Einsicht oder auch das Erfassen von Buchungen erlaubt — im Prototyp ist sie reine Lesefreigabe, Änderungen bleiben beim Eigentümer.

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

## 8b. Kontoauszug einlesen — CAMT.053 / CSV (neu)

Der Import-Screen ist ein Flow, keine feste Zusammenfassung. Ab Tablet zweispaltig: links Dateiname, Format, Zeitraum, Auszugssaldo und Trennzeichen — rechts die Prüfung.

1. **Leer**: Ablagefläche im `.blueprint`-Rahmen („Datei hierher ziehen oder auswählen") mit „CAMT.053 wählen" und „CSV wählen"; darunter „Zuletzt importiert" mit Datei, Datum, Konto und Satzzahl.
2. **Liest**: sichtbare Kette Datei gelesen → Format erkannt → Konto zugeordnet → Duplikate geprüft, abbrechbar.
3. **Prüfen**: Zielkonto als Chips (aus IBAN bzw. CSV-Kopfzeile vorgeschlagen, änderbar), drei Zähler (Übernehmen / Duplikat / Vorhanden) und die Zeilenliste mit Häkchen. Neue Sätze angehakt, Treffer abgewählt und grau hinterlegt, jede Zeile einzeln umschaltbar, Knopftext zählt mit. Bei leerer Auswahl ist der Primärbutton deaktiviert (45 % Deckkraft).
4. **Import** schreibt die gewählten Sätze in die Buchungen — mit Kategorie aus den gelernten Regeln und dem gewählten Konto — und springt auf „Konten".

Verbindliche Regeln:

- **Duplikatprüfung gegen den Bestand**, nicht nur innerhalb der Datei: Abgleich über Tag, Betrag und Empfänger (in der Umsetzung über die Importreferenz des Auszugs, sofern vorhanden). Derselbe Auszug zweimal eingelesen ergibt beim zweiten Mal null Vorschläge. Der Hinweistext nennt das Kriterium.
- **Zähler und Aktionsbutton lesen dieselbe Auswahl.** Zugeschaltete Duplikate erhöhen „Übernehmen" mit — sonst widerspricht der Kopf dem Knopf.
- Fehlerhafte Sätze werden gezählt und benannt, nie stillschweigend übersprungen.

## 8c. Kategoriezuordnung beim Import — lernende Regeln (neu)

Der Import-Screen aus 8b ist auf große Dateien ausgelegt worden. Zwei Probleme waren zu lösen: bei hunderten Sätzen liegt der Importieren-Knopf unerreichbar weit unten, und niemand kategorisiert 300 Buchungen einzeln.

### Aktionsleiste bleibt stehen

Die Prüfansicht trägt oben eine **sticky Leiste** (`position: sticky; top: 0`, Grundfläche, Volllinie unten): links „18 von 25 Sätzen gewählt" (10 px uppercase) über einer Statuszeile, rechts der Primärbutton und „Verwerfen" als Ghost. Der Button ist bei leerer Auswahl deaktiviert (45 %). Die Statuszeile hat drei Fassungen — „N Empfänger ohne Kategorie" (Akzent-700), „Alle gewählten Sätze haben eine Kategorie" (neutral), „Nichts zu übernehmen" (Akzent-700, bei null gewählten Sätzen). Sie darf **nie** Vollständigkeit behaupten, wenn nichts importiert wird.

### Zuordnung je Empfänger, nicht je Buchung

Das ist die eigentliche Änderung. Die Prüfansicht hat zwei Modi (Segment-Umschalter „Gruppen" / „Alle Zeilen", Gruppen ist Standard).

**Gruppen** besteht aus drei Blöcken:

1. **„N Empfänger zuordnen"** — eine Zeile je Empfänger **ohne** Kategorie, mit „+"-Marke, Anzahl, Zeitraum und Summe (bei einer Buchung nur ein Datum, keine Spanne „22.08. – 22.08."). Antippen klappt die Zeile auf (Akkordeon, einer zugleich, Fläche `--color-accent-100`): Kategorie-Chips und darunter eine Checkbox **„Regel merken: ‚Amazon' → künftig automatisch"** (Standard an). Eine Chip-Wahl gilt für **alle** Sätze dieses Empfängers, schließt die Gruppe und meldet „4× Freizeit · Regel gemerkt".
   Die Chipliste folgt dem **Vorzeichen** der Gruppe: Einnahmen bekommen Gehalt/Dividenden/Zinsen/Miete/Sonstiges, Ausgaben die Ausgabekategorien — dieselbe Regel wie im Kategorie-Sheet. Ein Gehaltseingang darf nicht nur als „Sonstiges" ablegbar sein, sonst wird eine falsche Regel gelernt.
2. **Massenauswege** unter der Liste: „Alle N als ‚Sonstiges'" (ohne Regel) und „Später zuordnen" (Empfänger fallen aus der Fragenliste, die Buchungen landen unkategorisiert im Triage-Banner der Buchungsliste).
3. **„Automatisch zugeordnet"** — je Kategorie eine Zeile mit Tag, Anzahl, den Empfängern und der Summe, dazu „N von M Sätzen". Der Block **entfällt**, wenn nichts automatisch zugeordnet wurde (keine leere Liste). Darunter „N Regeln greifen · M davon in diesem Import gelernt" mit Link „Regeln ansehen".

**Alle Zeilen** ist die frühere Flachliste — nötig, um einen einzelnen Satz ab- oder zuzuschalten.

### Leerzustand: Datei schon importiert

Der häufigste Wiederholungsfall. Sind alle Sätze bereits verbucht, zeigt die Prüfansicht statt leerer Blöcke einen Akzentblock: „Nichts Neues in dieser Datei — 25 Sätze erkannt, keiner neu", Auswege „Andere Datei wählen" und „Verwerfen", plus den Hinweis, dass sich einzelne Sätze unter „Alle Zeilen" trotzdem zuschalten lassen.

### Lernende Regeln

- **Regelmodell**: Muster plus Kategorie — die Regel greift, wenn der Empfänger mit dem Muster **beginnt** (Präfix des ersten Wortes, z. B. „Amazon" für „Amazon EU Sarl"). In der Umsetzung ist ein normalisierter Vergleich nötig (Groß/Klein, Sonderzeichen, Mehrfachleerzeichen); Präfix ist die Untergrenze, kein Endzustand — Bankdaten variieren im Verwendungszweck.
- **Anwendung vor der Frage**: Beim Einlesen wird zuerst jede Regel angewendet, erst der Rest landet in „Empfänger zuordnen". Beim zweiten Import derselben Bank erscheinen gelernte Empfänger gar nicht mehr.
- **Vorrang**: manuelle Zuordnung im laufenden Import vor Regel. Eine Regel ändert **nie** bereits importierte Buchungen.
- **Gelernte Regeln sind sichtbar und löschbar** — eigener Screen „Kategorieregeln" (aus „Mehr" und aus dem Import): Muster, Kategorie, Herkunft („in diesem Import gelernt" im Akzent vs. „seit dem ersten Import"), Löschen je Zeile, Zähler als Hero. Fußnote: „Eine gelöschte Regel lässt bereits importierte Buchungen unverändert — sie greift nur beim nächsten Import."
- **Verwerfen des Imports** entfernt die in diesem Lauf gelernten Regeln wieder; ein durchgeführter Import macht sie dauerhaft.
- Nach dem Import meldet der Toast, wie viele Sätze **ohne** Kategorie blieben („25 Buchungen importiert · 3 ohne Kategorie") — diese Zahl ist die Brücke zum Triage-Banner.

### Fehlende Kategorie im Import anlegen

Ein Empfänger, für den keine passende Kategorie existiert, darf **nicht** dazu zwingen, den Import zu verlassen — ein Screenwechsel und Rücksprung kostet alle bisherigen Zuordnungen und ist damit Arbeitsverlust.

Deshalb steht in der aufgeklappten Empfängergruppe neben den Kategorie-Chips ein gestrichelter Knopf **„+ Neue Kategorie"** (Akzentrahmen, gestrichelt, um ihn von den Auswahl-Chips zu unterscheiden). Er öffnet ein Feld direkt darunter mit „Anlegen & zuordnen" und „Abbrechen". Ein Klick erledigt drei Dinge in einem Schritt: Kategorie anlegen, allen Sätzen des Empfängers zuordnen, Regel merken.

Regeln dazu:

- **Richtung folgt dem Vorzeichen** der Gruppe — bei einer Gutschrift entsteht eine Einnahmekategorie; das Feldlabel sagt es („Neue Einnahmekategorie").
- **Bestehender Name wird erkannt**, nicht doppelt angelegt: die Meldung lautet dann „Abos bestand bereits · 4× zugeordnet".
- **Alle übrigen Zuordnungen bleiben unangetastet** — kein Neuaufbau der Vorschau, kein Verlust der bereits gesetzten Gruppen.

Derselbe Gedanke gilt allgemein: jeder Erfassungsfluss, der eine fehlende Stammdatenzeile braucht (Kategorie, Konto, Vertrag), muss sie **an der Stelle** anlegen können, an der sie fehlt. Ein Verlassen des Flusses darf nie eingegebene Arbeit verwerfen.

### Konsequenz für die Umsetzung

Die Regeltabelle ist dieselbe, die das Kategorie-Sheet („Regel für ‚REWE' merken") und der Beleg-Scan (gelernte Ablageregeln, Abschnitt 3.3b) füllen — **ein** Regelsystem, drei Einstiege, nicht drei Mechaniken. Serverseitig gehört die Regelanwendung in die Importvorschau, nicht in den Client: die Vorschau liefert je Satz Status, Vorschlagskategorie und deren Herkunft (Regel-Id oder leer), der Client zeigt und bestätigt nur.

## 8d. Kategorienverwaltung und CAMT-Detailfelder (neu)

### Kategorien sind konfigurierbar

Eigener Screen **„Kategorien"** (Seitennavigation und „Mehr", neben „Kategorieregeln"). Kategorien sind ab jetzt Daten, keine Konstante im Code — sie speisen die Chips bei Erfassung, Kategorie-Sheet, Import-Zuordnung und Budgetanlage sowie die Filter und Auswertungen.

Aufbau: Hero mit Gesamtzahl, Segment-Umschalter **Ausgaben / Einnahmen** (zwei getrennte Listen — eine Ausgabenkategorie darf nicht bei einer Gutschrift erscheinen), Eingabezeile „Neue Kategorie" + „Hinzufügen", darunter je Kategorie eine Zeile mit **Verwendungsnachweis** und den Aktionen „Umbenennen" / „Löschen".

- **Verwendungsnachweis** zählt echte Bezüge: „1 Buchung · 2 Regeln · Budget" bzw. „noch nicht verwendet". Das ist die Entscheidungsgrundlage vor dem Löschen — keine Behauptung, sondern eine Zählung.
- **Umbenennen** geschieht inline im Feld (Speichern / Abbrechen) und wirkt **sofort überall**: Buchungen, Regeln, Budgets und ein aktiver Filter werden mitgeführt. Meldung: „Wohnen → Wohnen & Energie · 1 Buchung angepasst".
- **Löschen** zweistufig („Löschen" → „Wirklich löschen"). Betroffene Buchungen fallen auf „Nicht zugeordnet" (und erscheinen damit im Triage-Banner), Regeln auf diese Kategorie werden entfernt, ein aktiver Filter fällt auf „Alle". Fußnote sagt genau das.
- **Doppelte Namen** werden abgewiesen (case-insensitiv), leere Eingabe ebenso.

Umsetzung: Kategorie als eigene Entität mit Haushalt-Bezug, Richtung (Ausgabe/Einnahme), Name und Sortierung; Buchungen referenzieren sie per Id, **nicht** per Text — sonst zerreißt jedes Umbenennen die Historie. Beim Löschen wird die Referenz auf null gesetzt, der Datensatz bleibt (Soft-Delete, wenn Auswertungen über alte Zeiträume laufen sollen). „Umbuchung" ist keine Kategorie, sondern eine Buchungsart, und darf hier nicht auftauchen.

### CAMT-Detailfelder einsehen und speichern

Jeder Satz der Importvorschau ist aufklappbar — „Details" in der Zeilenansicht und in der aufgeklappten Empfängergruppe (dort mit Datum, Verwendungszweck und Betrag als Vorschau). Das öffnet ein Seitenpanel (auf dem Telefon ein Bottom-Sheet) mit den Feldern des Auszugs, jedes mit seinem **CAMT-Elementnamen** als Herkunftsmarke:

| Feld | Element |
| --- | --- |
| Buchungstag · Valuta | BookgDt / ValDt |
| Betrag · Währung | Amt |
| Auftraggeber / Empfänger | RltdPties |
| IBAN der Gegenseite | CdtrAcct / DbtrAcct |
| BIC | Agt |
| Verwendungszweck | RmtInf |
| Buchungsart | BkTxCd |
| Geschäftsvorfallcode | Domn/Fmly (z. B. PMNT-ICDT-ESCT) |
| Importreferenz | AcctSvcrRef |
| Auszug · Zielkonto | Stmt |

Fehlt ein Feld im Auszug, steht „nicht im Auszug" in neutral — nie ein leeres Feld und nie ein erfundener Wert.

Darunter **„Beim Import behalten"** mit drei Schaltern (Standard alle an): Verwendungszweck speichern (danach in der Buchung durchsuchbar), IBAN und BIC der Gegenseite speichern (Grundlage künftiger Zuordnung nach Gegenkonto), Importreferenz speichern (verhindert Doppelimport zuverlässiger als Tag und Betrag). „Für alle Sätze" überträgt die Wahl auf den ganzen Auszug. Der Import schreibt die gewählten Felder in die Buchung.

Nach dem Import bleiben die Felder erreichbar: das Kategorie-Sheet einer importierten Buchung trägt eine Zeile **„Auszugsdaten"** mit „Ansehen" — dasselbe Panel, Kicker „Auszugsdaten der Buchung".

Zwei Regeln, an denen der Prototyp zuerst gescheitert ist:

1. **Die Anzeige liest ausschließlich die an der Buchung gespeicherten Felder** — nie eine Nachschlagetabelle über den Empfängernamen. Sonst tragen auch manuell erfasste Buchungen plötzlich Auszugsdaten samt erfundener Referenz.
2. **Abgeschaltete Felder sind sichtbar abgeschaltet.** Im Buchungsmodus zeigt das Panel für nicht gespeicherte Felder „nicht gespeichert" (neutral), analog zu „nicht im Auszug" für Felder, die der Auszug nicht liefert. Die Vorschauzeile fällt auf das nächste vorhandene Feld zurück (Verwendungszweck → IBAN → „nur Importreferenz"); ohne jedes Feld erscheint die Zeile gar nicht. Die Schalter „Beim Import behalten" erscheinen nur im Importmodus, nicht am gespeicherten Datensatz.

Umsetzung: die Auszugsfelder gehören an die Buchung (eigene Tabelle oder Spaltengruppe), **die Importreferenz ist das Duplikatkriterium** — Tag/Betrag/Empfänger ist nur der Notnagel, wenn der Auszug keine Referenz liefert. Der Verwendungszweck gehört in den Suchindex der Buchungsliste. Fehlende Felder als null speichern, nicht als Leerstring.

## 9. Filter, Summen, Leerzustände

- **Filterzeile** über der Buchungsliste: Suche plus Chips für Konto, Kategorie und Art; ab Tablet umbrechend (`flex-wrap`), auf dem Telefon eine scrollende Reihe.
- **Summenblock** rechnet immer gegen die **sichtbare** Auswahl: Einnahmen, Ausgaben, Saldo; Umbuchungen zählen weder als Einnahme noch als Ausgabe. Nullwerte als „0,00 €" ohne Vorzeichen.
- **Triage-Banner** („N Buchungen ohne Kategorie") bezieht sich ebenfalls auf die sichtbare Menge und verschwindet, wenn der Filter keine unkategorisierten Buchungen enthält. Singular/Plural korrekt.
- **Leerzustand** statt leerer Liste: Überschrift „Keine Buchung im gewählten Ausschnitt", ein Satz zur Ursache (nennt bei Suche den Begriff) und zwei Aktionen — „Filter zurücksetzen", „Buchung erfassen".

Dasselbe Muster gilt für jede Liste: nie eine leere Fläche, immer ein Satz plus die Primäraktion.

## 10. Liquidität

Das Dashboard beginnt mit „Bleibt übrig" (Einnahmen minus Ausgaben, Sparquote) und dem Vorgangs-Banner; Nettovermögen folgt darunter. Detailscreens: **Diesen Monat** (noch fällige und erwartete Beträge, „Verfügbar nach Fixkosten"), **Wohin fließt es** (fix vs. variabel, Kategorien; kapitalbildende Vorsorge zählt als Sparen, **nicht** als Ausgabe; Eigenanteile zählen, erstattete Beträge nicht), **Sparpotential** (Budgetüberschreitungen, kündbare Verträge mit Frist, wiederkehrende Buchungen ohne Vertrag, Summe).

## 10b. Analyse- und Auswertungsbereich (neu)

Eigener Bereich **„Auswertungen"** (Seitennavigation und „Mehr", Kennzahl „N steigend"). Er beantwortet drei Fragen, die der Nutzer benannt hat: wo sind Einsparpotentiale, welche Kosten steigen, wie steht ein Depot.

### Gemeinsamer Berichtsrahmen

Alle Berichte teilen eine Kopfleiste (`position: sticky; top: 0`), damit die Einstellung beim Blättern sichtbar bleibt:

- **Zeitraum**: Monat (Standard) / Quartal / Jahr.
- **Vergleichszeitraum**: Vorperiode / Vorjahr (Standard) / Ø 12 Monate. Beide Achsen rechnen **echt** durch — sie sind keine Anzeigefilter.
- Klartextzeile „August 2026 gegen August 2025 · N sichtbare Konten" — der Kontobezug folgt den Freigaben aus Abschnitt 6c.
- **Ansicht speichern** legt Zeitraum, Vergleich und Bericht als Chip ab; der aktive Chip ist hervorgehoben.
- **CSV** und **PDF** je Bericht.

Darunter die Berichtsauswahl als Chip-Reihe. Umgesetzt sind vier Berichte.

### Kostentrend (der tiefe Bericht)

- **Kopf**: Ausgabensumme des Zeitraums, Delta zum Vergleichszeitraum in Euro und Prozent, darunter die Aussage „N Kategorien steigen um mehr als 5 % — Freizeit, Gesundheit, Wohnen".
- **Sortierung**: stärkster Anstieg (Standard) / höchster Betrag / Name.
- **Je Kategorie eine Zeile**: Name, Status-Tag (`steigt` / `stabil` / `sinkt`, Schwelle ±5 %), Vergleichswert und Ø 12 Monate, eine 24-Monats-Sparkline als Inline-SVG (Akzent bei Anstieg, neutral sonst), Betrag und Änderung in Prozent.
- **Drilldown** durch Aufklappen: Empfängergruppen mit Anzahl und Summe in einem `.blueprint`-Rahmen, darunter die Einzelbuchungen mit Häkchen. **Abwählen schließt eine Buchung aus der Auswertung aus** — Kategoriesumme, Gesamtsumme, Prozentwert, Status-Tag und Riser-Zähler folgen sofort. Ein Banner nennt die Zahl der Ausschlüsse und setzt sie zurück.
- Aus der Kategorie heraus: „In Buchungen zeigen" (setzt den Filter der Buchungsliste) und „Budget anlegen" bzw. „Budget prüfen".

### Fixkosten & vertragliche Bindung

Fix pro Monat gegen frei disponibel, als Balken mit Prozentanteil. Je Posten die **Kündigungsfrist**; das Darlehen ist als „nicht kündbar" markiert, Vorsorgebeiträge als „kapitalbildend · zählt als Sparen". Quelle der Fristen sind die Rohfelder der Verträge (`f.frist`, `f.intervall`) — **nie** die Anzeigezeile (siehe 6b).

### Depot G/V

Depotwahl als Chips. Gewinn/Verlust absolut und bezogen auf den Einstand, „Einstand → aktueller Wert", je Position Stückzahl, Einstandskurs, aktueller Kurs, G/V absolut und prozentual. Ausdrücklich als **unrealisiert, ohne Steuern und Gebühren** ausgewiesen; realisierte Gewinne kommen erst mit den Wertpapiertransaktionen.

### Datenqualität

Unkategorisierte Buchungen, Dokumente ohne Datei, Verträge ohne Beleg, Konten ohne frischen Stand — je Zeile Anzahl, Folge („fehlen in jeder Kategorieauswertung") und Sprung zur Behebung. Kopf: „N Lücken" mit dem Satz, dass die Summen darüber unvollständig bleiben, solange sie offen sind.

### Regeln, die beim Bauen gebrochen wurden — bitte beachten

1. **Eine Größe, ein Wert.** Fixkosten und Kostentrend rechneten zunächst gegen verschiedene Monatssummen (hartkodiert vs. berechnet) und widersprachen sich direkt nebeneinander. Es gibt **eine** gemeinsame Monatsbasis, die alle Tabs verwenden — und sie wird im Balkentext genannt.
2. **Vergleichszeitraum braucht jahresperiodische Daten.** Die synthetischen Beispielreihen hatten einen Saisonterm mit beliebiger Periode; dadurch verglich „August gegen August" Rauschen statt Trend und **alle** Kategorien galten als steigend. Ein Saisonterm muss jahresperiodisch sein und seine Amplitude unter der Trenddifferenz liegen. In der echten Anwendung stellt sich das Problem anders: dort sind es reale Buchungen, aber die Regel bleibt — der Vorjahresvergleich ist nur belastbar, wenn er denselben Saisonpunkt trifft, und einzelne Monate schwanken stark. Deshalb ist Ø 12 Monate als dritter Vergleichsmodus vorhanden.
3. **Zwei Zahlen über dieselbe Menge müssen dieselbe Menge zählen.** Der Drilldown nannte „1 Buchung im Bestand" und darunter „keine erfasste Buchung", weil eine Zahl Ausschlüsse ignorierte. Eine Aussage, beide Anteile: „2 Buchungen · 1 ausgeschlossen".

### Umsetzungshinweise

- Aggregation gehört in die Application-Schicht, nicht in den Client: ein Endpoint je Bericht mit Zeitraum, Vergleichszeitraum und Ausschlussliste als Parameter; der Client zeigt nur.
- Der **Ausschluss einzelner Buchungen** ist eine Eigenschaft der Auswertung, keine der Buchung — er gehört in die gespeicherte Ansicht, nicht als Flag an den Datensatz.
- **Gespeicherte Ansichten** speichern Bericht, Zeitraum, Vergleich, Sortierung, Kontoauswahl und Ausschlüsse.
- Kontosichtbarkeit (6c) filtert **vor** jeder Aggregation; ein Bericht darf nie Beträge aus nicht freigegebenen Konten enthalten, auch nicht summiert.
- Umbuchungen zählen in keiner Ausgabenauswertung; Eigenanteile zählen, erstattete Beträge nicht; kapitalbildende Vorsorgebeiträge zählen als Sparen, nicht als Ausgabe.

### Noch nicht gebaut (aus derselben Auswahl)

Vermögensentwicklung nach Klasse (mit Stichtagsproblem: Depotkurse tagesaktuell, Lebensversicherungswerte ein Jahr alt), Objektkosten (Immobilie €/Monat und €/m², Fahrzeuge Gesamtkosten und €/km), Gesundheit/PKV-Bilanz (Eigenanteil pro Jahr, Erstattungsquote, Bearbeitungsdauer), Steuerjahr-Paket (Beiträge, Handwerkerleistungen, Werbungskosten-Kandidaten mit Dokumentbezug), Liquiditätsprognose 3–6 Monate. Vor dem Bau anfragen.

## 11. Umsetzungsreihenfolge

1. **Stylesheet tauschen** (Industry) und die Typo-Skala anheben — betrifft alle Screens, sonst driftet alles Weitere.
2. **Responsive Rahmen**: Seitennavigation ab 768 px, Hülle auf 100 % ab 1200 px, Kachelspalten, Erfassen-Sheet.
3. **Bereichstrennung** Vorsorge / Absicherung inklusive Flag und Vermögensberechnung (Risikoverträge raus aus dem Netto).
4. **Anlege-Flows** als eine gemeinsame Formularkomponente mit Feldliste je Typ.
5. **Buchungstabelle** mit Auswahl, Stapelvergabe und Stapellöschung, Filter, Summen, Leerzustände.
5a. **Kontofreigaben** (Abschnitt 6c) — gehören in denselben Zugriffspfad wie der Haushalts-Filter, nicht nachträglich in die Screens.
5b. **Kategorien als Entität** (Abschnitt 8d) — vor allem, was Kategorien anzeigt: Chips, Filter, Budgets und Regeln lesen daraus.
5b. **Bearbeiten und Löschen** für jeden Objekttyp (Abschnitt 6b) — zusammen mit den Anlege-Flows umsetzen, nicht danach: das Datenmodell mit Rohfeldern und Zusatzinfo-Feld ist die Voraussetzung dafür.
6. **Dokumente** als Master/Detail, **Scan / PKV** zweispaltig, **Kontoauszug-Import** als Flow (Abschnitt 8b) mit Empfänger-Zuordnung und Regellernen (8c) — die Regeltabelle vor dem Kategorie-Sheet bauen, beide nutzen sie.
7. **Police-Import** hinter der Analyse-Schnittstelle; ohne OCR dieselbe Maske leer.
8. **Fahrzeuge** und **Scaneingang** als neue Objekttypen.

Ab Schritt 3 gilt weiter: EF-Core-**Migrationen** statt `EnsureCreated`, und der globale Haushalts-Filter im `DbContext` für jede neue Entität.

## 12. Was noch nicht gestaltet ist

Ladezustände, Offline, Fehlerdialoge; 2FA; Rechtematrix im Detail; Auswertungen/Reports; Split-Buchung; Sondertilgungsdialog; CSV-Spalten-Mapping; Arbeit & Beruf; Administration (Dokumenttypen, Kategorien, Regeln). Vor dem Bau dieser Bereiche anfragen.

Ebenfalls offen und aus der Dateiablage des Nutzers ersichtlich: **Steuer nach Jahr** als eigener Bereich (die Belege dort ziehen quer durch alle Bereiche) und **Unterhalt / Scheidung** als eigener Vorgangstyp mit Zahlungsverfolgung.
