# FinanzApp

Mobiler Begleit-Client für die persönliche Vermögens- und Haushaltsverwaltung, umgesetzt nach dem
Design-Handoff *Finanz-App Prototyp Deutsch* (Design-System **Modernist**).

Der Client deckt die fünf täglichen Aufgaben ab — Vermögen prüfen, Ausgaben erfassen, importierte
Buchungen kategorisieren, Budgets im Blick behalten, Depot verfolgen — dazu Darlehen mit
Tilgungsplan, Importvorschau und die Sammelseite „Mehr“.

## Stack

| Schicht | Technologie |
| --- | --- |
| Frontend | Blazor WebAssembly (.NET 10), PWA-fähig |
| Backend | ASP.NET Core Minimal API (.NET 10) |
| Persistenz | EF Core 10 mit SQLite |
| Verträge | gemeinsames Projekt `FinanzApp.Shared`, von Client und API referenziert |

Die API hostet den WebAssembly-Client mit — es läuft ein Prozess, es gibt keinen zweiten Ursprung
und damit kein CORS.

## Starten

```bash
dotnet run --project src/FinanzApp.Api
```

Danach <http://localhost:5011> öffnen. Beim ersten Start legt die Anwendung
`src/FinanzApp.Api/finanzapp.db` an und füllt sie mit den Beispieldaten des Handoffs. Die Datei ist
nicht versioniert; sie zu löschen stellt den Ausgangszustand wieder her.

> Der Start über das Profil setzt `ASPNETCORE_ENVIRONMENT=Development`. Das ist nötig, weil die
> statischen Dateien des Clients im Entwicklungsbetrieb aus dem referenzierten Projekt kommen und
> erst beim `dotnet publish` in das `wwwroot` der API wandern.

## Aufbau

```
src/
  FinanzApp.Shared/        Verträge (DTOs) und deutsche Formatierung
  FinanzApp.Api/
    Data/                  EF-Core-Entitäten, DbContext, Beispieldaten
    Application/           Fachlogik: Konten, Buchungen, Budgets, Depot, Darlehen, Import
    Endpoints/             HTTP-Oberfläche, ohne Fachlogik
  FinanzApp.Client/
    Layout/                Kopfzeile, Tab-Bar, Seitennavigation
    Pages/                 die acht Screens
    Components/            Sheet, Toast, Diagramm, Balken, Lade-/Fehlerhülle
    Services/              API-Zugriff, Beträge-Maske, Toasts
    wwwroot/css/           modernist.css (unverändert) + app.css (Anwendungsschicht)
docs/design-handoff/       der Handoff, wie geliefert
```

Die Geschäftslogik liegt in den Application-Services, nicht in den Komponenten. Die Endpunkte nehmen
Parameter entgegen, rufen einen Service und geben dessen Ergebnis zurück.

## Umsetzung des Designs

**`wwwroot/css/modernist.css` ist unverändert die Datei aus dem Handoff.** Sie bleibt die Quelle für
Farben, Ramps, Typografie, Abstände und die Basiskomponenten; eine neue Fassung des Design-Systems
lässt sich darüberkopieren. `app.css` enthält nur Layout und Screens und nimmt jeden Wert aus den
Variablen — keine eigenen Farben, keine abgerundeten Ecken, keine zentrierten Button-Labels.

Umgesetzt sind die im Handoff festgelegten Varianten: Navigation **Tabs**, Dashboard **Kacheln**,
Erfassung **3 Schritte**. Drawer, Listen-Dashboard und Ziffernblock-Direkterfassung sind laut Handoff
nur Referenz und nicht implementiert.

Ab **768 px** wandert die Tab-Bar in eine Seitennavigation und die Vermögenskacheln gehen auf drei,
ab 1024 px auf vier Spalten — das im Handoff verlangte Web-App-Layout.

### Abweichungen vom Prototyp, und warum

| Punkt | Prototyp | Hier |
| --- | --- | --- |
| Tappbare Zeilen und Kacheln | `div` mit `onClick` | `button` — sonst wären sie weder mit der Tastatur erreichbar noch für Screenreader bedienbar. Optisch identisch. |
| Beträge-Maske | maskiert Kontostände und Buchungen, nicht aber Deltas und Budgetzahlen | maskiert **alle** Geldbeträge, Prozentwerte bleiben sichtbar — so, wie der Handoff die Regel beschreibt. |
| Zurück-Weg | keiner | Detailscreens (Mehr, Darlehen, Import) haben links im Kopf einen Textschalter „Zurück“, im Stil der übrigen Kopfschalter. Der Handoff nennt das als offenen Punkt. |
| Statusleiste `9:41 / AUG 2026 / 100 %` | vorhanden | entfällt — im Handoff als Attrappe gekennzeichnet. |
| Gerätrahmen 390 × 844 | vorhanden | entfällt — die reale App ist responsiv. |
| Zähler „7 von 7“ | 7 Beispielbuchungen | „29 von 29“, siehe Beispieldaten. |

## Beispieldaten

Alle Summen der Oberfläche werden **gerechnet**, nicht gespeichert: Kontosalden aus Anfangsbestand
plus Buchungen, Budgetauslastung aus den Buchungen der Kategorie, Depotwert aus Stück mal Kurs,
Bruttovermögen aus Konten, Depot und Rückkaufswerten. Die Beispieldaten sind so gewählt, dass dabei
genau die Zahlen des Handoffs herauskommen: Nettovermögen 125.839,95 €, Bruttovermögen 274.139,95 €,
Depotwert 132.480,00 €, G/V +18.940,20 €, Einnahmen 5.240 €, Ausgaben 3.612 €, Sparquote 31 %,
Budgets 892 € von 1.250 €, Kontosalden 4.812,60 € / 1.947,35 € / 50.000,00 €.

Damit das aufgeht, enthält der August 2026 **29 Buchungen** statt der sieben aus dem Prototyp. Die
sieben des Handoffs stehen unverändert oben in der Liste; die übrigen füllen die Kategoriesummen, aus
denen Budgets und Monats-KPIs entstehen.

Zwei Detailwerte des Handoffs waren mit seinen eigenen Kopfzahlen nicht vereinbar. Die Kopfzahlen
haben Vorrang, weil sie auf mehreren Screens auftauchen:

- Die vier Depotpositionen summieren sich im Handoff auf 132.440,22 €, der Depotwert steht dort mit
  132.480,00 €. Die Position **Allianz SE** ist deshalb 46 St. zu 342,55 € statt 52 St. zu 302,26 €.
- Die Einstandswerte, die die Positionsrenditen des Handoffs ergäben, summieren sich nicht auf den
  ausgewiesenen G/V. Der Einstand des **Xtrackers MSCI EM** trägt die Differenz, seine Rendite liegt
  damit bei +19,2 % statt +4,2 %.

Der Tilgungsplan rechnet auf Cent genau; in zwei Zeilen weicht die gerundete Anzeige deshalb um 1 €
von der Tabelle des Handoffs ab, die durchgehend mit ganzen Euro rechnet.

Der Stichtag der Beispieldaten liegt fest auf dem 23.08.2026 (`Demo:Today` in `appsettings.json`).
Wird der Wert geleert, läuft die Anwendung auf der echten Uhr.

**Der Import ist echt.** „34 Buchungen importieren“ schreibt tatsächlich 34 Buchungen. Danach stimmen
Salden, Monatssummen und Budgets nicht mehr mit dem Handoff überein — `finanzapp.db` löschen stellt
den Ausgangszustand wieder her.

## API

| Methode | Pfad | Zweck |
| --- | --- | --- |
| GET | `/api/dashboard` | Vermögensaggregat, Zeitreihe, Monats-KPIs, Top-Budgets |
| GET | `/api/accounts` | Konten mit gerechnetem Saldo |
| GET | `/api/transactions?search=&skip=&take=` | Buchungsliste mit Suche und Paging |
| POST | `/api/transactions` | Buchung anlegen, idempotent über `requestKey` |
| PATCH | `/api/transactions/{id}/category` | Kategorie zuordnen, optional Regel merken |
| GET | `/api/categories?direction=` | Kategorien je Richtung |
| GET | `/api/rules` | Kategorisierungsregeln |
| GET | `/api/budgets?period=Month\|Quarter\|Year` | Budgetauslastung im Zeitraum |
| GET | `/api/portfolio` | Depotwert, Positionen, Kurszeitstempel |
| GET | `/api/loans/primary`, `/api/loans/{id}?months=` | Darlehen mit Tilgungsplan |
| GET | `/api/import/preview` | Vorschau, schreibt nichts |
| POST | `/api/import/{id}/commit` | Übernahme in einer Transaktion |
| GET | `/api/overview/more` | Kennzahlen der Sammelseite |

## Entscheidungen, die man kennen sollte

- **Geld ist überall `decimal`.** In SQLite liegen Beträge als ganzzahlige Cent (Wertkonverter im
  `DbContext`) — sonst legt EF Core `decimal` als TEXT ab, und Summen und Sortierungen in SQL wären
  falsch. Gerundet wird erst in der Anzeige.
- **Deutsche Formatierung ohne ICU-Abhängigkeit.** `GermanFormat` definiert Zahlen- und Datumsformate
  explizit, statt sie über `CultureInfo("de-DE")` aufzulösen. Damit liefert der WebAssembly-Client
  dieselbe Ausgabe wie der Server, unabhängig von Browsersprache und geladenen ICU-Daten. Minus ist
  das typografische „−“ (U+2212), zwischen Zahl und Einheit steht ein geschütztes Leerzeichen.
- **Umbuchungen sind eine eigene Buchungsart** und zählen weder als Einnahme noch als Ausgabe.
- **Anlegen ist idempotent.** Der Client vergibt je Formular einen `requestKey`; ein zweiter
  Sendeversuch liefert die bereits angelegte Buchung zurück, statt doppelt zu buchen. Eine eindeutige
  gefilterte Indexspalte trägt die Zusage bis in die Datenbank.
- **Duplikaterkennung beim Import** läuft primär über die Importreferenz der Bank. Sätze, die nur nach
  Tag, Empfänger und Betrag verdächtig sind, werden gezeigt, aber nicht mit übernommen.
- **Der Kurszeitstempel bleibt sichtbar**, weil die Kurse aus einem austauschbaren Anbieter stammen
  und veralten können. `PricesStale` im Vertrag ist der Platz für den Hinweis bei Providerausfall;
  die Anzeige dafür steht bereits.

## Offene Punkte

Aus dem Handoff bewusst offen gelassen und noch zu entwerfen:

- Ladezustände, leere Listen, Offline, Fehlerdialoge — derzeit nur eine Textzeile in der Sprache des
  Systems (`AsyncView`).
- Login und 2FA, Auswertungen, wiederkehrende Buchungen, Split-Buchung, Sondertilgungsdialog,
  CSV-Spalten-Mapping, Verwaltung von Kategorien und Regeln, Budgetanlage. Die betroffenen
  Schaltflächen melden das mit einem Toast, statt ins Leere zu führen.

Technisch offen:

- **Keine Authentifizierung.** Die API ist offen; das ist für einen persönlichen Finanzdienst nicht
  haltbar und der erste Schritt vor jedem Deployment.
- **Kein Datei-Upload und kein CAMT-Parser.** `DemoImportBatch` steht für die Datei; Vorschau,
  Referenzabgleich und Übernahme arbeiten darauf mit derselben Logik, die später eine echte Datei
  bekommt.
- **Umbuchungen legen noch keine Gegenbuchung an.** `Transaction.CounterAccountId` ist vorhanden, die
  zweite Buchungshälfte fehlt.
- **Archivo kommt von Google Fonts.** Für den Produktivbetrieb selbst hosten — der Handoff sagt das
  ausdrücklich.
- **Keine Migrationen.** Das Schema entsteht über `EnsureCreated`. Vor dem ersten echten Datenbestand
  auf EF-Core-Migrationen umstellen.
- **Keine Tests.** Für die Rechenwege — Tilgungsplan, Budgetzeiträume, Duplikaterkennung,
  Formatierung — lohnt sich eine Testsuite als Nächstes.

## Design-Handoff

Der Handoff liegt unverändert unter [`docs/design-handoff/`](docs/design-handoff/):
`handoff.md` (die Spezifikation), `FinanzApp.dc.html` (der Prototyp, im Browser zu öffnen) und
`_ds/modernist/styles.css` (die Token-Quelle).
