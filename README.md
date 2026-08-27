# FinanzApp

Mobiler Begleit-Client für die persönliche Vermögens-, Haushalts- und Dokumentenverwaltung,
umgesetzt nach den Design-Handoffs *Finanz-App Prototyp Deutsch* — zuletzt **Handoff v4**
(Design-System **Industry**, responsiver Client, neue Bereiche), davor Nachtrag 2
*Login & Mehrbenutzerbetrieb* und die *Erweiterung zum Finanz- und Dokumentenmanagement*.

Der Client trägt die täglichen Aufgaben — Vermögen und Liquidität prüfen, Ausgaben erfassen,
importierte Buchungen kategorisieren, Budgets und Depot verfolgen — dazu Vorgänge und Fristen,
eine Dokumentablage, den PKV-Flow von der Arztrechnung bis zur verbuchten Erstattung,
Vorsorge und Absicherung, Wohnen mit Verträgen und Rechnungen, Darlehen, Import sowie Anmeldung und
Benutzerverwaltung eines Haushalts.

Wie die Anwendung bedient wird, steht in der [Bedieneranleitung](docs/bedienung.md).

## Stack

| Schicht | Technologie |
| --- | --- |
| Frontend | Blazor WebAssembly (.NET 10), PWA-fähig |
| Backend | ASP.NET Core Minimal API (.NET 10) |
| Persistenz | EF Core 10 mit SQLite, Schema über Migrationen |
| Dokumente | Dateien im Dateisystem unter `DocumentRoot`, nur der relative Pfad in der Datenbank |
| Anmeldung | Cookie-Authentifizierung, PBKDF2-Hashing, serverseitig widerrufbare Sitzungen |
| Mailversand | MailKit über SMTP |
| Tests | xUnit gegen SQLite im Arbeitsspeicher |
| Verträge | gemeinsames Projekt `FinanzApp.Shared`, von Client und API referenziert |

Die API hostet den WebAssembly-Client mit — es läuft ein Prozess, es gibt keinen zweiten Ursprung
und damit weder CORS noch ein Token im Browserspeicher.

## Starten

```bash
dotnet run --project src/FinanzApp.Api
```

Danach <http://localhost:5111> öffnen. Beim ersten Start legt die Anwendung
`src/FinanzApp.Api/finanzapp.db` an, wendet die Migrationen an und füllt sie mit den Beispieldaten.
Dabei entstehen unter `src/FinanzApp.Api/App_Data/Dokumente` echte Platzhalterdateien — nur so
lässt sich die Pfadauflösung an etwas prüfen. Weder Datenbank noch Dokumentordner sind versioniert;
beide zu löschen stellt den Ausgangszustand wieder her.

> **Datenbank aus einer älteren Fassung?** Frühere Stände haben das Schema mit `EnsureCreated`
> angelegt; solche Dateien haben alle Tabellen, aber keine Migrationshistorie. Die Anwendung
> erkennt das beim Start und nennt die Datei, die zu löschen ist. Übernehmen lässt sich so eine
> Datenbank nicht — je nach Alter fehlen ihr ganze Tabellen.

```bash
dotnet test
```

### Demo-Zugänge

Alle drei Benutzer des Haushalts *Haushalt Kielmayer* teilen das Passwort
`Demo-Haushalt-2026!`:

| E-Mail | Rolle | Was er darf |
| --- | --- | --- |
| `oliver@haushalt-kielmayer.de` | Inhaber | alles, inklusive Benutzerverwaltung und Einladungen |
| `sabine@haushalt-kielmayer.de` | Mitglied | alle Daten ändern, keine Benutzerverwaltung |
| `kanzlei@haas-stb.de` | Lesezugriff | nur ansehen — kein Erfassen, keine Kategorieauswahl, kein Import |

> Diese Konten und ihr Passwort stehen im Quelltext, damit die Vorführung ohne Einrichtung läuft.
> **Vor jedem echten Betrieb gehören sie gelöscht.** Der Einladungscode des Demo-Haushalts lautet
> `HH-4K2P-9XQ1` und lässt sich einmal einlösen.

> Der Start über das Profil setzt `ASPNETCORE_ENVIRONMENT=Development`. Das ist nötig, weil die
> statischen Dateien des Clients im Entwicklungsbetrieb aus dem referenzierten Projekt kommen und
> erst beim `dotnet publish` in das `wwwroot` der API wandern. Im Entwicklungsbetrieb darf das
> Anmelde-Cookie außerdem über HTTP laufen; in jeder anderen Umgebung verlangt es HTTPS.

### Dokumentordner und Mailversand

`Documents:Root` in `appsettings.json` bestimmt, wo die Dateien liegen (Vorgabe
`App_Data/Dokumente`, relativ zum Anwendungsverzeichnis oder absolut). Dazu kommen
`MaxFileSizeMegabytes` und `AllowedExtensions`.

Der Passwort-Reset verschickt eine echte Mail, sobald `Mail:Host` **und** `Mail:Password`
gesetzt sind. Fehlt eines davon, schreibt die Anwendung die Nachricht samt Link ins
Protokoll und arbeitet sonst unverändert; beim Start steht in der Konsole, welcher der
beiden Fälle gerade gilt.

Dass beides nötig ist, ist Absicht: `appsettings.json` bringt den Host für mail.de schon
mit. Würde der allein genügen, wäre der echte Versand ab dem ersten Start aktiv, jede
Nachricht scheiterte an der Anmeldung — und der Link stünde nicht mehr im Protokoll, wo man
ihn ohne Postausgang braucht. Ein Relay ganz ohne Anmeldung ist damit nicht vorgesehen.

Zum Scharfschalten fehlen nur die beiden persönlichen Angaben:

```bash
dotnet user-secrets --project src/FinanzApp.Api set "Mail:User" "vorname.name@mail.de"
```

```bash
dotnet user-secrets --project src/FinanzApp.Api set "Mail:Password" "…"
```

Das Passwort gehört **nicht** in `appsettings.json` — die Datei liegt im Repository. Die
Mailadresse aus demselben Grund nicht: sie ist persönlich und hätte in der Versionierung
nichts verloren. `Mail:FromAddress` bleibt leer, dann wird als Absender genau die Adresse
benutzt, mit der wir uns anmelden — mail.de weist eine fremde Absenderadresse zurück.

Ein anderer Anbieter braucht zusätzlich `Mail:Host`, `Mail:Port` und ggf.
`Mail:UseStartTls=false` für Port 465.

## Aufbau

```
src/
  FinanzApp.Shared/        Verträge (DTOs), Passwortregeln, deutsche Formatierung
  FinanzApp.Api/
    Data/                  Entitäten, DbContext mit Mandantenfilter, Migrationen, Beispieldaten
    Application/           Fachlogik je Bereich
    Endpoints/             HTTP-Oberfläche, ohne Fachlogik
    Infrastructure/        Uhr, aktueller Benutzer, Rollen, Mail, Dokumentablage, Belegerkennung
  FinanzApp.Client/
    Layout/                Kopfzeile, Tab-Bar, Seitennavigation, Anmelderahmen
    Pages/                 die Screens
    Components/            Sheets, Toast, Diagramm, Balken, Lade-/Fehlerhülle
    Navigation/            Screen-Katalog, Anmeldepfade, Zurück-Weg
    Services/              API-Zugriff, Anmeldezustand, Geräteprofile, Beträge-Maske, Toasts
tests/FinanzApp.Tests/     Rechenwege, Pfadauflösung, Mandantentrennung, PKV-Regeln
docs/
  design-handoff/          Erst-Handoff samt Nachtrag 2, wie geliefert
  design-handoff-erweiterung/  Erweiterungs-Handoff mit den Wireframes
  erweiterungsplan.md      der Plan, den der Erweiterungs-Handoff vor dem Bauen verlangt
  design-handoff-v4/       Handoff v4 — Industry, responsiv, neue Bereiche (maßgeblich)
  handoff-v4-umsetzung.md  Arbeitsliste zu v4, Stand je Schritt
  bedienung.md             Bedieneranleitung für den täglichen Gebrauch
```

Die Geschäftslogik liegt in den Application-Services, nicht in den Komponenten. Die Endpunkte nehmen
Parameter entgegen, rufen einen Service und geben dessen Ergebnis zurück.

## Umsetzung des Designs

**`wwwroot/css/industry.css` ist unverändert die Datei aus Handoff v4.** Sie bleibt die Quelle für
Farben, Ramps, Typografie, Abstände und die Basiskomponenten; eine neue Fassung des Design-Systems
lässt sich darüberkopieren. `app.css` enthält nur Layout und Screens und nimmt jeden Wert aus den
Variablen — **keine einzige hartkodierte Farbe**, keine eigenen Radien, keine zentrierten
Button-Labels.

Dass diese Disziplin sich auszahlt, hat der Wechsel von *Modernist* auf *Industry* gezeigt: der
gesamte Farbumstieg fiel allein durch den Dateitausch an. Nachzuziehen waren nur die Dinge, die
nicht in Tokens stehen — Linienstärken, das Überschriftengewicht und die Schriftgrade, weil
Barlow Condensed schmaler läuft als Archivo.

Die Tab-Bar trägt seit der Erweiterung **Vermögen · Vorgänge · Erfassen · Dokumente · Mehr**
(freigegeben: Navigation 1a mit der zentralen Scan-Aktion aus 1c). Die Mitte ist eine **Aktion, kein
Ziel**: sie öffnet ein Sheet über allem. Konten, Budgets und Depot sind unverändert erhalten und
über die Bereichsliste auf „Mehr“ erreichbar.

Seit Handoff v4 schaltet der Client in **drei Modi**: Telefon unter 768 px, Tablet bis 1200 px,
Desktop darüber. Ab Tablet ersetzt eine Seitennavigation die Tab-Bar — mit dem angemeldeten
Benutzer oben, den Bereichen samt Kennzahl in der Mitte und dem Erfassen-Knopf fest am Fuß.
Die Kacheln laufen über zwei, drei und vier Spalten, das Erfassen-Sheet wird ab Tablet zu einem
Panel an der rechten Kante, und Chart und Bilanz stehen dort nebeneinander statt untereinander.
Die Logik der Screens ändert sich dabei nicht, nur ihre Anordnung.

Vier Zustände tragen dasselbe Akzentmuster: **überfällig**, **fällig**, **Datei nicht gefunden**,
**Frist läuft**. Ein leerer Bereich zeigt nie eine leere Liste, sondern eine Erklärzeile mit der
Primäraktion.

### Abweichungen vom Prototyp, und warum

| Punkt | Prototyp | Hier |
| --- | --- | --- |
| Tappbare Zeilen und Kacheln | `div` mit `onClick` | `button` — sonst weder mit der Tastatur erreichbar noch für Screenreader bedienbar. Optisch identisch. |
| Statusleiste, Gerätrahmen | vorhanden | entfallen — im Handoff als Attrappe gekennzeichnet, die reale App ist responsiv. |
| Zähler „7 von 7“ | 7 Beispielbuchungen | mehr, siehe Beispieldaten. |
| „Profile auf diesem Gerät“ | drei feste Profile | nur Benutzer, die sich auf diesem Gerät schon angemeldet haben. |
| Neues Passwort setzen | nicht entworfen | minimal ergänzt, sonst führte der Link aus der Mail ins Leere. |
| Sparpotential | eine Summe für alles | beziffert wird nur, was sich beziffern lässt. Was ein Anbieterwechsel bringt, weiß die Anwendung nicht — dort steht die Gelegenheit ohne Zahl. |
| Wireframe 1a | Tab-Bar mit „Konten“ | maßgeblich ist die Tabelle in Abschnitt 1 des Handoffs; sie führt 1a mit der Scan-Mitte aus 1c zusammen. |

## Erweiterung: Dokumente, Vorgänge, Gesundheit, Wohnen

### Dokumente

Ein Dokumentmodell für alle Bereiche: `Document`, `DocumentType` (eine **Tabelle**, kein Enum) und
`DocumentLink` als polymorphe Verknüpfung.

- In der Datenbank steht **nur der relative Pfad**. Ein absoluter würde die Daten an einen Rechner
  binden. Zusammengesetzt wird erst beim Öffnen, gegen den konfigurierten Wurzelordner.
- Jede Auflösung prüft, dass das Ergebnis **innerhalb** der Wurzel bleibt. Ein gespeicherter Pfad
  ist eine Eingabe wie jede andere; ohne diese Prüfung ließe sich mit `../` jede Datei des Servers
  ausliefern. Der Test dazu steht in `DocumentPathTests`.
- **Fehlt die Datei, ist das ein Zustand, kein Fehler**: der Eintrag bleibt vollständig sichtbar und
  verknüpft, zeigt den gesuchten Pfad und bietet drei Auswege.
- Die Suche trifft **auch Objekte**, nicht nur Dateinamen — wer „hausrat“ tippt, meint meist den
  Vertrag.
- Verknüpft wird am Objekt; das Dokument zeigt seine Bezüge nur an. `DocumentLink` verzichtet auf
  einen Fremdschlüssel je Zieltyp — dafür prüft `ObjectLabelService` vor dem Anlegen, ob das Ziel im
  eigenen Haushalt existiert.

### Vorgänge

Ein Tab für alles Unerledigte. Die meisten Einträge entstehen von selbst — aus Vertragsende minus
Kündigungsfrist, aus einer Rechnungsfälligkeit, aus einer Erstattung ohne Zahlungseingang. Erzeugt
wird beim Lesen der Liste; ein eindeutiger Index auf Quelle und Quell-Id verhindert Dubletten. **Der
Erzeugungsgrund steht mit in der Aufgabe** und wird in der Zeile erklärt — sonst stünde dort eine
Zeile, die niemand angelegt hat und deren Herkunft niemand nachvollziehen kann.

### Gesundheit / PKV

Der Kernflow: scannen → prüfen → Vorgang → überfällig → Zahlung zuordnen.

- **Der Eigenanteil ist keine offene Forderung.** Er ist eine gebuchte Ausgabe. Offen ist
  ausschließlich die erwartete Erstattung. Diese Regel steht als Test, nicht nur als Kommentar.
- Die Belegerkennung liegt hinter `IBillTextExtractor`. Eingebaut ist eine Umsetzung, die nichts
  erkennt — die Maske ist dann leer, der Flow läuft trotzdem vollständig.
- Die Zahlungszuordnung **schlägt vor, sie entscheidet nicht**: bewertet wird über Betrag, Datum und
  Verwendungszweck, bestätigt wird von Hand. Eine automatische Verknüpfung würde bei jedem
  Fehltreffer stillschweigend die Buchhaltung verfälschen.

### Liquidität

Rechnet ausschließlich auf dem Bestand — Buchungen, Budgets, Rechnungen, PKV-Vorgänge,
Vertragsfristen. Keine neue Eingabe, keine neue Tabelle.

- **Fix gegen variabel wird erkannt, nicht gepflegt**: eine Kategorie gilt als fix, wenn sie in fast
  jedem Monat vorkommt und ihre Monatssummen wenig schwanken.
- Das Sparpotential findet Budgetüberschreitungen, laufende Kündigungsfristen und wiederkehrende
  Buchungen ohne Vertrag. Für „Abo“ gelten zwei Schranken — höchstens 100 € im Monat und höchstens
  5 % Schwankung. Ohne sie landet die Miete in der Liste, und die lässt sich nicht kündigen.

### Vorsorge & Kapital, Absicherung

Seit Handoff v4 sind die früheren „Versicherungen“ **zwei Bereiche mit einem Modell**. Das
Entscheidungsmerkmal ist, ob ein Vertrag einen Wert hat, der ins Vermögen zählt: Kapital-LV,
Riester und Bausparen haben einen (**Vorsorge**), Risikoleben, BU, Hausrat und Kfz haben keinen
(**Absicherung**). Technisch ist es **eine** Tabelle mit dem Flag `IsCapitalForming`, und die
Regel steht an einer einzigen Stelle:

```csharp
public decimal? AssetValue => IsCapitalForming ? CurrentValue : null;
```

So kann ein Risikoleben-Vertrag nicht ins Nettovermögen geraten, selbst wenn versehentlich ein
Wert eingetragen wäre — er zahlt im Todesfall, er ist kein Guthaben. Genau daran ist die alte
Sammelkategorie gescheitert. Jeder Vorsorgewert trägt seinen **Stichtag**; ein Jahresstand ist
kein Tageskurs und wird auch nicht wie einer gezeigt.

### Fristen, Wohnen

Fristen werden **abgeleitet** (Vertragsende minus Kündigungsfrist), nicht gepflegt — ein von Hand
gesetztes Datum liefe der Verlängerung hinterher. Davon getrennt ist die **Erinnerung**: ein
Vertrag kann ein Jahr vor dem Termin auf den Tisch gehören, weil ein Vergleich Vorlauf braucht. Die Immobilie **verweist** über `LoanId` auf das
bestehende Darlehen; es gibt weiterhin genau einen Darlehensbereich mit einem Tilgungsplan.
Zahlungen sind überall Verweise auf echte Buchungen — Geldbewegungen bleiben Buchungen.

### Fahrzeuge und Scaneingang

Ein **Fahrzeug** ist strukturgleich zur Immobilie: ein Objekt, an dem Verträge, Rechnungen,
Fristen und Dokumente hängen. Seine Kosten der letzten zwölf Monate werden aus echten Buchungen
gerechnet, nicht gepflegt. Die Kfz-Versicherung wird **verwiesen**, nicht kopiert — sie bleibt
unter Absicherung.

Der **Scaneingang** ist ein Posteingang: gescannt wird stapelweise, eingeordnet wird später. Ein
Beleg bleibt darin, bis Typ **und** Objekt bestimmt sind. Ohne diese Schwelle verschwände er in
der Ablage, ohne dass jemand entschieden hätte, wozu er gehört — und genau solche Dokumente
findet später niemand wieder.

### Anlegen

Alle Objekttypen teilen sich **einen** Formularscreen, gesteuert über eine Feldliste, die der
Server liefert — und gegen die er auch prüft. Deshalb kann eine Meldung das fehlende Feld bei
dem Namen nennen, den der Benutzer gesehen hat („Versicherer fehlt“), und ein neuer Objekttyp
kostet einen Listeneintrag statt einer neuen Seite. Der Einstieg steht jeweils am Ende der
zugehörigen Liste, nicht unter „Mehr“.

Jeder Flow schreibt wirklich und rechnet durch: ein neues Konto ist sofort in der Vertragsanlage
wählbar, ein neues Budget verändert Plan und Verbleibend. Doppelte Anlage wird abgelehnt.
„Depot“ und „Darlehen“ im Konto-Formular legen kein Konto an, sondern führen dorthin, wo sie
hingehören.

### Bearbeiten und Löschen

Jeder Datensatz, den die App anlegt, ist auch änderbar und löschbar. Bearbeitet wird im
**selben** Formular, nur vorbefüllt — dieselbe Feldliste, gegen die auch geprüft wird.

Zwei Regeln stecken im Datenmodell:

1. **Keine Metadaten aus Anzeigetexten parsen.** Die Werte kommen aus den Rohfeldern. Ein
   Vertragsname wie „Risikoleben“ trägt keinen Versicherer im Namen; wer ihn dort herausparsen
   wollte, ließe das Pflichtfeld leer.
2. **Ein gepflegter Name wird nie neu zusammengesetzt.** Beim Anlegen wird er abgeleitet, beim
   Bearbeiten nie wieder — sonst würde aus „Risikoleben“ beim bloßen Öffnen und Speichern
   „Risikoleben Hannoversche“.

Gelöscht wird zweistufig, ohne Systemdialog, und die Folgen werden **gezählt**, nicht behauptet:
„3 Buchungen hängen an diesem Konto — sie bleiben erhalten und werden auf ‚Ohne Konto‘ gesetzt.“
Und so geschieht es dann auch. Buchungen sind Tatsachen; das Konto war nur ihre Schublade.

### Kontoauszug einlesen

Ein Flow mit drei Zuständen — leer, liest, prüfen —, ab Tablet zweispaltig. Die
**Duplikatprüfung läuft gegen den Bestand**, nicht nur innerhalb der Datei: derselbe Auszug
zweimal eingelesen ergibt beim zweiten Mal null Vorschläge. Zähler und Aktionsschalter lesen
dieselbe Auswahl, damit der Kopf dem Knopf nicht widerspricht. Fehlerhafte Sätze werden gezählt
und benannt — nie stillschweigend übersprungen.

Gelesen werden **camt.052 und camt.053**. Beide Formate sind unterhalb von `Ntry` gleich
aufgebaut und unterscheiden sich nur im Namen des Wurzelberichts — derselbe Leser nimmt
deshalb beide. Gesucht wird ausschließlich über lokale Elementnamen: der Namensraum trägt die
Version, und die liefert jede Bank anders.

Vier Stellen, an denen ein Auszug leise kippt, und was dagegen steht:

1. **Der Betrag steht ohne Vorzeichen in der Datei**; die Richtung trägt `CdtDbtInd`. Wer den
   Indikator übersieht, bucht jede Abbuchung als Eingang und die Bilanz sieht großartig aus.
2. **Die Gegenseite wechselt mit der Richtung** — bei einer Abbuchung der Gläubiger, bei einer
   Gutschrift der Zahler. Immer den Gläubiger zu nehmen ergäbe beim eigenen Gehalt den eigenen
   Namen als Empfänger.
3. **Die Referenz trägt die Wiedererkennung.** Bevorzugt wird `AcctSvcrRef` von der Bank;
   `EndToEndId` steht oft auf `NOTPROVIDED` und taugt dann nicht. Fehlt jede Referenz, wird aus
   Tag, Empfänger und Betrag ein Fingerabdruck gebildet — stabil über Wiederholungen, sonst
   erkännte die Duplikatprüfung denselben Satz beim zweiten Einlesen nicht wieder.
4. **Die IBAN steht in den Stammdaten mit Leerzeichen und in der Datei ohne.** Ohne
   Normalisierung träfe die Kontozuordnung nie zu und die App schlüge stumm das falsche Konto
   vor.

Ein nur vorgemerkter Umsatz ist keine Buchung — er steht trotzdem in der Liste, mit Grund und
unwählbar. Ein Sammler mit eigenen Einzelbeträgen wird zerlegt, damit Empfänger und Kategorie
nicht geraten werden müssen; der Sammelbetrag entfällt dann, sonst stünde er doppelt da.

Die Datei wird ohne DTD gelesen (`DtdProcessing.Prohibit`, kein `XmlResolver`). Ein Auszug
kommt von außen; mit erlaubter DTD ließe sich über eine externe Entität jede Serverdatei in
die Antwort ziehen.

Zwischen Vorschau und Übernahme liegt die gelesene Datei im Zwischenspeicher des Servers, nicht
beim Client. Käme sie zurückgereicht, entschiede der Aufrufer über Beträge und Referenzen. Nach
der Übernahme ist die Vorschau verbraucht — ein zweiter Klick bucht nicht noch einmal.

Zum Ausprobieren liegt `docs/beispiele/camt052-sparkasse.xml` bereit; dieselbe Datei prüfen
auch die Tests. CSV ist weiterhin nicht angebunden.

### Wenn der Empfänger nicht der Empfänger ist

An einer echten Bankdatei mit 459 Umsätzen geprüft. Bei Kartenzahlungen und über
Zahlungsdienstleister steht im Gläubigerfeld nicht der Laden, sondern der Acquirer
(„PAYONE GmbH“, „DZ BANK AG“, „EDEKABANK AG“) oder ein Platzhalter
(„Lastschrift aus Kartenzahlung“). Der Laden steht im Verwendungszweck — in mehreren
Schreibweisen, alle in derselben Datei:

```
Setzer 24/7 Vell./Wolpertshausen/DE 31.12.2025 um 19:08:01 Uhr
DIAK Klinikum Landkrei/Am Mutterhaus 1/Schwaebisch H/D02.01.2026 / 18:58 Ortszeit
NYX.DeinAutomat/Diakoniestrasse/SchwaebischHa/DE/0 16.01.2026 / 16:01 Ortszeit
KJNUUX Ihr Einkauf bei EDEKA Möller vom 30.12.2025 EREF: T005115664
```

Geschnitten wird am Datum und sonst nirgends — Ladennamen enthalten selbst Schrägstriche
(„Setzer 24/7 Vell.“). Ein **angehängtes Buchungsdatum fällt weg**: bliebe es stehen, wäre jeder
Einkauf ein eigener Empfänger, und aus einer Gruppe mit 42 Sätzen würden 42 Fragen. Nennt das
Gläubigerfeld dagegen schon den Laden, bleibt es dabei — „REWE Martin Sitter“ liest sich besser
als „REWE SAGT DANKE. 45655449/Heidenheim“, und beide ergeben dasselbe Regelmuster.

Wirkung auf der Testdatei: 105 Sätze lagen unter vier nichtssagenden Namen, jetzt nur noch 24.
Ist die Stelle im Zweck leer — bei PayPal in 21 von 36 Fällen — bleibt es beim Dienstleister;
ein geratener Name wäre schlechter.

### Der Buchungstext ist keine Kategorie

Die Bank benennt jede Buchung: „Lastschrift“, „Dauerauftrag“, „SB-Auszahlung“,
„Lohn/Gehalt/Rente“. Daraus eine Kategorie abzuleiten wäre nahegelegen — gemessen bringt es
nichts. Von 96 Empfängern trugen neun mehr als einen Buchungstext, und bei acht davon
unterschied er nur Ein- von Ausgang, was Vorzeichen und Chipliste ohnehin trennen. Der Text
steht deshalb als **Angabe an der Empfängergruppe**, wo die Zuordnung getroffen wird, und nur
dann, wenn er für alle Sätze der Gruppe gilt.

### Kategorien beim Import — lernende Regeln

Gefragt wird je **Empfänger**, nicht je Buchung. Niemand kategorisiert dreihundert Zeilen
einzeln, und dieselbe Antwort dreißigmal zu geben ist keine Sorgfalt. Eine Chip-Wahl gilt für
alle Sätze des Empfängers und kann als Regel hängenbleiben.

Vier Entscheidungen stecken darin:

1. **Die Herkunft gehört in die Vorschau, nicht in den Client.** Die Vorschau liefert je Satz
   Status, Vorschlagskategorie und die greifende Regel-Id; der Client zeigt und bestätigt nur.
   Ohne die Herkunft ließe sich „automatisch zugeordnet“ nicht von „von Hand gewählt“
   unterscheiden.
2. **Gelernt wird erst bei der Übernahme.** Wer den Import verwirft, soll keine Regel
   hinterlassen haben — statt sie anzulegen und wieder wegzuräumen, entstehen sie gar nicht
   erst. Dieselbe Regel zweimal gelernt wird überschrieben, nicht verdoppelt.
3. **Verglichen wird normalisiert** (Groß/Klein, Mehrfachleerzeichen, Satzzeichen). Bankdaten
   schreiben denselben Empfänger je nach Zahlungsweg anders; ein Vergleich, der daran
   scheitert, fragt beim nächsten Import wieder dasselbe. Bei mehreren Treffern gewinnt die
   **längste** Regel — sonst entschiede die Reihenfolge in der Tabelle.
4. **Die Chipliste folgt dem Vorzeichen der Gruppe.** Ein Gehaltseingang darf nicht nur als
   „Sonstiges“ ablegbar sein, sonst wird eine falsche Regel gelernt — und die greift dann bei
   jedem weiteren Import.

Vorrang: die Wahl im Import schlägt jede Regel. Eine Regel ändert **nie**, was schon gebucht
ist. Was ohne Kategorie bleibt, nennt die Meldung nach dem Import — diese Zahl ist die Brücke
zum Triage-Banner der Buchungsliste.

Gelernte Regeln sind unter `/kategorieregeln` einsehbar und löschbar, mit Herkunft. Was die
App sich selbst beigebracht hat, muss sichtbar und widerrufbar sein; eine Regel, die still im
Hintergrund zuordnet und die niemand einsehen kann, ist keine Hilfe.

### Kategorien sind Daten

`/kategorien` verwaltet sie: zwei getrennte Listen für Ausgaben und Einnahmen — eine
Ausgabenkategorie darf bei einer Gutschrift nicht erscheinen, sonst wird die falsche Zuordnung
erst möglich gemacht.

Vor jedem Löschen steht ein **Verwendungsnachweis**, gezählt statt behauptet: „19 Buchungen ·
2 Regeln · Budget“. Beim Löschen fallen die Buchungen auf „nicht zugeordnet“ und erscheinen im
Triage-Banner — sie verschwinden nicht und werden auch nicht stillschweigend umgehängt; Regeln
auf die Kategorie werden entfernt, weil sie sonst beim nächsten Import ins Leere griffen. Läuft
ein Budget darauf, wird abgewiesen.

Umbenennen wirkt überall zugleich, weil Buchungen, Regeln und Budgets **per Id** auf die
Kategorie zeigen und nicht per Text. Genau das hält die Historie zusammen.

Der Prototyp kennt weder eine Sortier-Bedienung noch einen archivierten Zustand — deshalb
Reihenfolge nach Anlage und echtes Löschen, kein Sortierfeld ohne Weg es zu ändern.

### Die Felder des Auszugs

Jeder Satz der Vorschau ist aufklappbar; das Panel zeigt zehn Felder, jedes mit seinem
CAMT-Elementnamen als Herkunftsmarke — `BookgDt / ValDt`, `Amt`, `RltdPties`,
`CdtrAcct / DbtrAcct`, `Agt`, `RmtInf`, `BkTxCd`, `Domn/Fmly`, `AcctSvcrRef`, `Stmt`. Nach dem
Import bleibt dasselbe Panel über das Kategorie-Fenster erreichbar.

Drei Regeln stecken darin:

1. **Die Anzeige liest ausschließlich, was an der Buchung steht** — nie eine
   Nachschlagetabelle über den Empfängernamen. Sonst trüge eine von Hand erfasste Buchung
   plötzlich Auszugsdaten samt erfundener Referenz.
2. **Fehlende Felder sind `null`, nie Leerstring.** Sonst wäre „steht nicht drin“ von „steht
   drin, ist leer“ nicht mehr zu unterscheiden.
3. **Der Grund steht dabei.** Im Import heißt ein leeres Feld „nicht im Auszug“, an der
   gespeicherten Buchung „nicht gespeichert“. Am Datensatz sind die beiden Fälle nicht mehr
   auseinanderzuhalten — dann gilt die Aussage, die belegbar ist: gespeichert ist es nicht.

„Beim Import behalten“ hat drei Schalter (Vorgabe alles an) und „Für alle Sätze“. Der dritte
verdient Vorsicht: **die Importreferenz ist das Duplikatkriterium**. Tag, Betrag und Empfänger
sind nur der Notnagel für Auszüge ohne Referenz — wer sie abwählt, fällt beim nächsten
Einlesen desselben Auszugs von „bereits vorhanden“ auf „mögliches Duplikat“ zurück.

Der gespeicherte Verwendungszweck wird in der Buchungsliste **mitdurchsucht**. Bei einer
Kartenzahlung steht der Laden dort und sonst nirgends.

### Fehlende Kategorie an Ort und Stelle anlegen

Neben den Chips einer Empfängergruppe steht ein gestrichelter Knopf „+ Neue Kategorie“. Er legt
an, ordnet allen Sätzen des Empfängers zu und merkt die Regel — in einem Schritt, ohne den
Import zu verlassen. Ein Screenwechsel und Rücksprung köstete alle bisherigen Zuordnungen, und
das ist Arbeitsverlust.

Die Richtung folgt dem Vorzeichen der Gruppe; das Feldlabel sagt es. Trifft der Name eine
vorhandene Kategorie, ist das kein Fehler: sie wird benutzt, die Meldung sagt „bestand
bereits“, und nichts wird verdoppelt. Dafür gibt es `EnsureAsync` — anlegen oder finden, statt
einer Zurückweisung, die den Nutzer aus dem Fluss wirft.

Derselbe Gedanke gilt allgemein: jeder Fluss, der eine fehlende Stammdatenzeile braucht, muss
sie an der Stelle anlegen können, an der sie fehlt.

## Kontofreigaben — die zweite Stufe der Mandantentrennung

Konten gehören **einem Benutzer** und sind freigebbar: Haushalt, „Nur ich“ oder namentlich.
Sichtbar ist ein Konto, wenn der Angemeldete Eigentümer ist, die Freigabe auf Haushalt steht
oder er namentlich benannt ist.

Der Filter sitzt im `DbContext` neben dem Haushaltsfilter, nicht in den Diensten — dort kann ihn
keiner vergessen, und ein direkter API-Aufruf umgeht ihn nicht. Buchungen tragen die Bedingung
ihres Kontos noch einmal ausdrücklich: sich darauf zu verlassen, dass EF den Filter über die
Navigation mitzieht, wäre genau die Annahme, an der so ein Leck entsteht.

`CurrentUserId` kommt wie der Haushalt aus dem Anmelde-Cookie und bleibt sonst 0 — dann zeigt der
Filter nur, was auf Haushalt steht. **Weniger, nie mehr.**

Was daraus folgt, gilt für alles: Kontenliste, Buchungen, Suche, Filter, Zähler und Summen. Neun
Tests prüfen das über die Dienste statt über die Tabellen — ein Test, der direkt hineinsieht,
umginge den Filter und prüfte damit nicht, worum es geht.

Das **Schreiben** ist eigens geschützt: die Freigabe ändert nur der Eigentümer. Der Filter
schützt das Lesen, diese Prüfung das Schreiben — sonst könnte sich jedes Mitglied selbst Zugang
verschaffen. Ein Bestandskonto ohne Eigentümer bleibt beim Haushalt und für alle sichtbar; die
Umstellung nimmt niemandem etwas weg.

Der Tag an der Kontozeile ist **perspektivisch**, nie der Rohwert: dasselbe Konto heißt für den
Eigentümer „geteilt mit Sabine K.“ und für Sabine „geteilt von Oliver W.“. Ein fremdes Konto
führt gar nicht erst zum Bearbeiten.

Eine Folge, die beim Bauen auffiel: **das Seeden liest durch den Filter**. Beim Seeden gibt es
keinen angemeldeten Benutzer, also ist ein privates Konto unsichtbar und eine Nachschlagetabelle
findet es nicht. Die Seed-Pfade lesen deshalb mit `IgnoreQueryFilters` — Seeden ist keine
Benutzeranfrage.

Die Freigabe ist **reine Lesefreigabe**; Änderungen bleiben beim Eigentümer. Der Handoff stellt
das ausdrücklich zur Entscheidung und der Prototyp beantwortet es so.

## Anmeldung und Mehrbenutzerbetrieb

Ein **Haushalt** besitzt die Daten. Ein **Benutzer** meldet sich mit eigenen Zugangsdaten an und
gehört genau einem Haushalt. Drei Rollen: Inhaber, Mitglied, Lesezugriff.

**Ohne Sitzung zeigt die Anwendung nur den Anmeldescreen** — keine Kopfzeile, keine Tab-Bar, keine
Seitennavigation. Die Rollen wirken an drei Stellen: im Client (keine Schaltflächen, die nicht
funktionieren), am Endpunkt (Policy, sonst 403) und in der Datenbank (Mandantenfilter).

### Mandantentrennung

Jede Entität mit Haushaltsbezug trägt `IHouseholdOwned`. Der `DbContext` hängt daran **per Schleife**
einen globalen Abfragefilter — nicht von Hand je Tabelle, denn dabei vergisst man irgendwann eine.
Eine neue Tabelle bekommt den Filter automatisch. Neue Datensätze werden beim Speichern auf den
aktuellen Haushalt gestempelt, im synchronen wie im asynchronen Weg.

Der Haushalt kommt ausschließlich aus dem Anmelde-Cookie. Ist keiner gesetzt, bleibt er 0 und der
Filter findet **nichts** — der Standardfall ist „nichts sichtbar“, nicht „alles sichtbar“.
`HouseholdIsolationTests` prüft das über alle neuen Entitäten.

### Was beim Anmelden passiert

- Passwörter liegen als PBKDF2-Hash. **Eine einzige Meldung** für „Adresse unbekannt“ und „Passwort
  falsch“; auch eine unbekannte Adresse durchläuft eine Hash-Berechnung, damit sie sich nicht über
  die Antwortzeit verrät.
- Nach fünf Fehlversuchen 15 Minuten Sperre, dazu ein Rate-Limit von 10 Anfragen je Minute und IP.
- Die Sitzung steht als Datensatz in der Datenbank, das Cookie trägt nur ihre Id. Abmelden wirkt
  sofort, auch für ein Cookie, das kryptografisch noch gültig wäre.
- **Passwort ändern** (angemeldet) verlangt das bisherige und beendet danach alle *anderen*
  Sitzungen — wer sein Passwort ändert, will fremde Zugänge loswerden, nicht sich selbst
  abmelden. Offene Reset-Links werden dabei entwertet.
- Der Passwort-Reset antwortet **immer** gleich. Vom Token liegt nur der SHA-256-Hash in der
  Datenbank; er gilt 30 Minuten, ist einmal einlösbar und widerruft alle offenen Sitzungen.

## Beispieldaten

Alle Summen werden **gerechnet**, nicht gespeichert. Die Beispieldaten sind so gewählt, dass dabei
genau die Zahlen der Handoffs herauskommen: Liquidität **+1.628 €** (Einnahmen 5.240 €, Ausgaben
3.612 €, Sparquote 31 %), Nettovermögen **99.879,95 €**, Bruttovermögen **248.179,95 €**,
Depotwert 132.480,00 €, Vorsorge **58.940,00 €** aus vier Verträgen, Absicherung
**12.330 €/Jahr** aus acht, G/V +18.940,20 €, Budgets 892 € von 1.250 €, Kontosalden
4.812,60 € / 1.947,35 € / 50.000,00 €, Erinnerung Kündigung Hausrat in **18 Tagen**,
PKV-Erstattung **680 €** überfällig,
Arztrechnung **210 €** noch nicht eingereicht, Stromrechnung **142,50 €** offen.

Damit das aufgeht, enthält der August 2026 mehr Buchungen als die sieben des ersten Prototyps, und
die Erweiterung bringt fünf Monate Vorgeschichte (März bis Juli) mit — ohne Historie könnte „Wohin
fließt es“ nicht zwischen fix und variabel unterscheiden und das Sparpotential nichts erkennen. Der
August bleibt davon unberührt; die Anfangsbestände werden danach neu ausgerichtet.

Ein Dokument hat **mit Absicht keine Datei** auf der Platte (`Lohn_07_2026.pdf`), damit der Zustand
„Datei nicht gefunden“ vorführbar ist. Die Erstattung zum überfälligen PKV-Vorgang ist als Buchung
vorhanden, aber nicht zugeordnet — genau der Fall, für den es den Screen „Zahlung zuordnen“ gibt.

Der fachliche Stichtag liegt fest auf dem 23.08.2026, 08:24 Uhr (`Demo:Today`). **Sitzungen, Sperren
und Reset-Token hängen nicht daran** — sie laufen über `TimeProvider` auf der echten Uhr, denn der
Browser prüft die Gültigkeit des Cookies ebenfalls dagegen.

**Der Import ist echt.** „34 Buchungen importieren“ schreibt tatsächlich 34 Buchungen; danach
stimmen die Demo-Zahlen nicht mehr. `finanzapp.db` löschen stellt den Ausgangszustand her.

## API

Alle Endpunkte außerhalb von `/api/auth` verlangen eine Anmeldung; schreibende zusätzlich die Rolle
Inhaber oder Mitglied.

| Bereich | Endpunkte |
| --- | --- |
| Anmeldung | `POST /api/auth/login`, `/register`, `/logout`, `/password-reset`, `/password-reset/redeem` · `GET /api/auth/me` |
| Haushalt | `GET /api/household` · `POST /api/household/invitations` |
| Übersicht | `GET /api/dashboard`, `/api/overview/more` |
| Konten | `GET /api/accounts` · `GET|POST /api/transactions` · `PATCH /api/transactions/{id}/category` |
| Kataloge | `GET /api/categories`, `/api/rules` |
| Budgets | `GET /api/budgets?period=Month\|Quarter\|Year` |
| Depot | `GET /api/portfolio` |
| Darlehen | `GET /api/loans/primary`, `/api/loans/{id}?months=` |
| Import | `GET /api/import/preview` · `POST /api/import/{id}/commit` |
| Dokumente | `GET /api/documents`, `/types`, `/search`, `/{id}`, `/{id}/file`, `/for/{typ}/{id}` · `POST /api/documents` (Upload), `/{id}/links` · `PUT /{id}`, `/{id}/path` · `DELETE /{id}`, `/links/{id}` |
| Vorgänge | `GET /api/tasks`, `/summary` · `POST /api/tasks` · `PATCH /api/tasks/{id}/state` |
| Gesundheit | `GET /api/health/bills`, `/{id}`, `/{id}/payment-candidates` · `POST /api/health/bills`, `/{id}/payment`, `/api/health/extract` · `PATCH /{id}/status` |
| Versicherungen | `GET /api/insurances`, `/{id}` |
| Wohnen | `GET /api/properties`, `/{id}`, `/api/contracts/{id}`, `/api/invoices/{id}`, `/{id}/payment-candidates` · `POST /api/invoices/{id}/pay` |
| Liquidität | `GET /api/liquidity`, `/cashflow?months=`, `/savings` |

## Entscheidungen, die man kennen sollte

- **Geld ist überall `decimal`.** In SQLite liegen Beträge als ganzzahlige Cent (Wertkonverter im
  `DbContext`) — sonst legt EF Core `decimal` als TEXT ab, und Summen und Sortierungen in SQL wären
  falsch. Gerundet wird erst in der Anzeige.
- **Deutsche Formatierung ohne ICU-Abhängigkeit.** `GermanFormat` definiert Zahlen- und Datumsformate
  explizit. Minus ist das typografische „−“ (U+2212), zwischen Zahl und Einheit steht ein
  geschütztes Leerzeichen.
- **Zwei Uhren.** `IClock` trägt die fachliche Zeit und darf für die Vorführung stillstehen;
  `TimeProvider` trägt die echte und entscheidet über Gültigkeiten.
- **Geldbewegungen bleiben Buchungen.** Ein Versicherungsbeitrag, eine Rechnungszahlung, eine
  PKV-Erstattung sind Verweise auf eine `Transaction`, keine eigenen Finanzsätze.
- **Umbuchungen** zählen weder als Einnahme noch als Ausgabe.
- **Anlegen ist idempotent**: der Client vergibt je Formular einen `requestKey`, eine eindeutige
  gefilterte Indexspalte trägt die Zusage bis in die Datenbank.
- **Der Kurszeitstempel bleibt sichtbar**, weil die Kurse aus einem austauschbaren Anbieter stammen.
- **Der App-Rahmen hat feste Höhe**, nicht Mindesthöhe: sonst wächst er mit dem Inhalt, das Fenster
  wird zum Scroller und die Tab-Bar wandert aus dem Bild.

## Tests

`dotnet test` — 48 Tests. Abgedeckt sind die Stellen, an denen ein Fehler teuer wäre:

- **Eigenanteil zählt nicht als offene Forderung**, Teilerstattung, abgelehnter Vorgang,
  Zahlungsvorschlag mit bestem Treffer.
- **Pfadauflösung** relativ ↔ absolut, fehlende Datei, Ausbruchsversuch aus `DocumentRoot`,
  Dateinamen entschärfen, kein Überschreiben bei Namensgleichheit.
- **Benutzerisolierung** über alle neuen Entitäten, auch der Fall „kein Haushalt gesetzt“.
- **Rechenwege**: Tilgungsplan, Budgetzeiträume, abgeleitete Kündigungsfristen, Beitragsumrechnung,
  Regelpräfix, deutsche Formatierung, Passwortbewertung.
- **Start gegen eine Datenbank ohne Migrationshistorie** — der Fall, der sonst in einer
  unverständlichen SQLite-Meldung endet.

Der erste Testlauf hat dabei eine echte Lücke gefunden: der Haushalts-Stempel griff nur im
asynchronen `SaveChanges`.

## Offene Punkte

Aus den Handoffs bewusst offen und noch zu entwerfen:

- **Arbeit & Beruf** und **Administration** — laut Erweiterungs-Handoff nicht gestaltet und vor dem
  Bau anzufragen. Auf „Mehr“ stehen sie als Zeile mit Hinweis, statt ins Leere zu führen.
- **Zwei-Faktor-Anmeldung**, **Rechteverwaltung**, Sperrmeldung, Sitzungsübersicht.
- Ladezustände, Offline, Fehlerdialoge — derzeit eine Textzeile in der Sprache des Systems.
- Auswertungen, wiederkehrende Buchungen, Split-Buchung, Sondertilgung, CSV-Spalten-Mapping,
  Verwaltung von Kategorien und Regeln, Budgetanlage. Die Schaltflächen melden das mit einem Toast.

Technisch offen:

- **Kein CAMT-Parser und keine Texterkennung.** `DemoImportBatch` steht für die Importdatei,
  `NoBillTextExtractor` für die Belegerkennung; beide hängen hinter der Schnittstelle, die später
  eine echte Umsetzung bekommt.
- **Umbuchungen legen noch keine Gegenbuchung an.**
- **Barlow und Barlow Condensed kommen von Google Fonts** — für den Produktivbetrieb selbst hosten.
- **Backups**: die Datenbank wird gesichert, die Dateien unter `DocumentRoot` nicht. Das gehört in
  ein Dateisystem-Backup. Eine Prüffunktion „sind alle referenzierten Dateien vorhanden?“ ist
  vorgesehen; der Zustand „Datei nicht gefunden“ ist dafür bereits gestaltet.
- **Jede Anfrage prüft die Sitzung in der Datenbank** — der Preis für sofort wirksames Abmelden.
- **Der Start prüft das Schema, bevor er migriert.** Eine Datenbank ohne Migrationshistorie wird
  nicht stillschweigend übernommen und auch nicht gelöscht — die Anwendung sagt, was zu tun ist,
  und überlässt die Entscheidung dem Menschen.
- **Die Buchungssuche filtert im Speicher.** Bei Jahren an Buchungen gehört sie in SQL.

## Handoffs

Unverändert abgelegt:

- [`docs/design-handoff/`](docs/design-handoff/) — Erst-Handoff samt Nachtrag 2 (`handoff.md`),
  Prototyp (`FinanzApp.dc.html`), Token-Quelle (`_ds/modernist/styles.css`). Gestalterisch überholt
  durch v4, fachlich weiter gültig.
- [`docs/design-handoff-erweiterung/`](docs/design-handoff-erweiterung/) — Erweiterungs-Handoff
  (`handoff.md`) und die 24 Wireframes (`Wireframes Erweiterung.dc.html`).
- [`docs/erweiterungsplan.md`](docs/erweiterungsplan.md) — der Erweiterungsplan, den der Handoff vor
  dem Bauen verlangt.
- [`docs/design-handoff-v4/`](docs/design-handoff-v4/) — **der maßgebliche Handoff**: Prototyp
  (`FinanzApp v4 Responsive.dc.html`), Token-Quelle (`_ds/industry/styles.css`). `HINWEIS.md`
  erklärt, was am Export ergänzt werden musste und wie sich der Prototyp öffnen lässt.
- [`docs/handoff-v4-umsetzung.md`](docs/handoff-v4-umsetzung.md) — die Arbeitsliste dazu, mit
  Stand je Schritt.
