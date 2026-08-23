# FinanzApp

Mobiler Begleit-Client für die persönliche Vermögens- und Haushaltsverwaltung, umgesetzt nach dem
Design-Handoff *Finanz-App Prototyp Deutsch* (Design-System **Modernist**), einschließlich
Nachtrag 2 *Login & Mehrbenutzerbetrieb*.

Der Client deckt die fünf täglichen Aufgaben ab — Vermögen prüfen, Ausgaben erfassen, importierte
Buchungen kategorisieren, Budgets im Blick behalten, Depot verfolgen — dazu Darlehen mit
Tilgungsplan, Importvorschau, die Sammelseite „Mehr“ sowie Anmeldung, Registrierung und
Benutzerverwaltung eines Haushalts.

## Stack

| Schicht | Technologie |
| --- | --- |
| Frontend | Blazor WebAssembly (.NET 10), PWA-fähig |
| Backend | ASP.NET Core Minimal API (.NET 10) |
| Persistenz | EF Core 10 mit SQLite |
| Anmeldung | Cookie-Authentifizierung, PBKDF2-Hashing, serverseitig widerrufbare Sitzungen |
| Mailversand | MailKit über SMTP |
| Verträge | gemeinsames Projekt `FinanzApp.Shared`, von Client und API referenziert |

Die API hostet den WebAssembly-Client mit — es läuft ein Prozess, es gibt keinen zweiten Ursprung
und damit weder CORS noch ein Token im Browserspeicher.

## Starten

```bash
dotnet run --project src/FinanzApp.Api
```

Danach <http://localhost:5011> öffnen. Beim ersten Start legt die Anwendung
`src/FinanzApp.Api/finanzapp.db` an und füllt sie mit den Beispieldaten des Handoffs. Die Datei ist
nicht versioniert; sie zu löschen stellt den Ausgangszustand wieder her.

### Demo-Zugänge

Alle drei Benutzer des Haushalts *Haushalt Kielmayer* teilen das Passwort
`Demo-Haushalt-2026!`:

| E-Mail | Rolle | Was er darf |
| --- | --- | --- |
| `oliver@haushalt-kielmayer.de` | Inhaber | alles, inklusive Benutzerverwaltung und Einladungen |
| `sabine@haushalt-kielmayer.de` | Mitglied | alle Daten ändern, keine Benutzerverwaltung |
| `kanzlei@haas-stb.de` | Lesezugriff | nur ansehen — kein Erfassen-Tab, keine Kategorieauswahl, kein Import |

> Diese Konten und ihr Passwort stehen im Quelltext, damit die Vorführung ohne Einrichtung läuft.
> **Vor jedem echten Betrieb gehören sie gelöscht.** Der Einladungscode des Demo-Haushalts lautet
> `HH-4K2P-9XQ1` und lässt sich einmal einlösen.

> Der Start über das Profil setzt `ASPNETCORE_ENVIRONMENT=Development`. Das ist nötig, weil die
> statischen Dateien des Clients im Entwicklungsbetrieb aus dem referenzierten Projekt kommen und
> erst beim `dotnet publish` in das `wwwroot` der API wandern. Im Entwicklungsbetrieb darf das
> Anmelde-Cookie außerdem über HTTP laufen; in jeder anderen Umgebung verlangt es HTTPS.

### Mailversand einrichten

Der Passwort-Reset verschickt eine echte Mail, sobald ein Postausgangsserver konfiguriert ist.
Ohne `Mail:Host` schreibt die Anwendung die Nachricht samt Link ins Protokoll und arbeitet sonst
unverändert — der Reset lässt sich damit vollständig durchspielen.

```bash
dotnet user-secrets --project src/FinanzApp.Api set "Mail:Host" "smtp.example.net"
dotnet user-secrets --project src/FinanzApp.Api set "Mail:User" "postfach@example.net"
dotnet user-secrets --project src/FinanzApp.Api set "Mail:Password" "…"
```

Das Passwort gehört **nicht** in `appsettings.json` — die Datei liegt im Repository.
Alternativ per Umgebungsvariable `Mail__Password`.

## Aufbau

```
src/
  FinanzApp.Shared/        Verträge (DTOs), Passwortregeln, deutsche Formatierung
  FinanzApp.Api/
    Data/                  EF-Core-Entitäten, DbContext mit Mandantenfilter, Beispieldaten
    Application/           Fachlogik: Anmeldung, Haushalt, Konten, Buchungen, Budgets,
                           Depot, Darlehen, Import
    Endpoints/             HTTP-Oberfläche, ohne Fachlogik
    Infrastructure/        Uhr, aktueller Benutzer, Rollen-Policies, Mailversand
  FinanzApp.Client/
    Layout/                Kopfzeile, Tab-Bar, Seitennavigation, Anmelderahmen
    Pages/                 die zwölf Screens
    Components/            Sheet, Toast, Diagramm, Balken, Lade-/Fehlerhülle
    Navigation/            Screen-Katalog, Anmeldepfade, Zurück-Weg
    Services/              API-Zugriff, Anmeldezustand, Geräteprofile, Beträge-Maske, Toasts
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
| Statusleiste `9:41 / AUG 2026 / 100 %` | vorhanden | entfällt — im Handoff als Attrappe gekennzeichnet. |
| Gerätrahmen 390 × 844 | vorhanden | entfällt — die reale App ist responsiv. |
| Zähler „7 von 7“ | 7 Beispielbuchungen | „29 von 29“, siehe Beispieldaten. |
| „Profile auf diesem Gerät“ | drei feste Profile | nur Benutzer, die sich auf diesem Gerät schon angemeldet haben. Beim ersten Aufruf fehlt der Abschnitt deshalb — so beschreibt es der Nachtrag. |
| Passwortstärke | hängt an der Länge | Länge, Zeichenvielfalt und offensichtliche Muster; dieselbe Bewertung prüft der Server. |
| Neues Passwort setzen | nicht entworfen | minimal ergänzt, sonst führte der Link aus der Mail ins Leere. Bausteine von der Registrierung übernommen. |

Drei Entscheidungen aus der ersten Umsetzung hat Nachtrag 2 übernommen und sind damit keine
Abweichungen mehr: die Depotposition *Allianz SE* mit 46 St. zu 342,55 €, die Rendite des
*Xtrackers* mit +19,2 % und die Beträge-Maske, die **alle** Geldbeträge verdeckt. Auch der
Zurück-Schalter auf Detailscreens steht inzwischen im Handoff.

## Anmeldung und Mehrbenutzerbetrieb

Ein **Haushalt** besitzt die Daten. Ein **Benutzer** meldet sich mit eigenen Zugangsdaten an und
gehört genau einem Haushalt. Drei Rollen: Inhaber, Mitglied, Lesezugriff.

**Ohne Sitzung zeigt die Anwendung nur den Anmeldescreen** — keine Kopfzeile, keine Tab-Bar, keine
Seitennavigation. Wer einen geschützten Pfad aufruft, landet auf `/anmelden` und nach dem Anmelden
wieder dort, wo er hinwollte.

Die Rollen wirken an drei Stellen:

1. **Im Client**, damit niemand Schaltflächen sieht, die für ihn nicht funktionieren: Lesezugriff
   bekommt keinen Erfassen-Tab, keine Kategorie-Chips im Sheet, kein Triage-Banner, keine
   Import-Übernahme und keine Budgetanlage.
2. **Am Endpunkt**, weil der Client nur eine Oberfläche ist: schreibende Endpunkte verlangen die
   Rolle Inhaber oder Mitglied, die Benutzerverwaltung verlangt Inhaber. Wer die Rolle im Browser
   manipuliert, bekommt eine 403.
3. **In der Datenbank**, durch den Mandantenfilter: jede Abfrage auf Fachdaten trägt automatisch
   die Bedingung „gehört meinem Haushalt“.

### Mandantentrennung

Jede Entität mit Haushaltsbezug trägt `IHouseholdOwned`. Der `DbContext` hängt daran per Schleife
einen globalen Abfragefilter — nicht von Hand je Tabelle, denn dabei vergisst man irgendwann eine,
und genau das wäre das Datenleck, vor dem der Handoff warnt. Eine neue Tabelle bekommt den Filter
automatisch, sobald sie die Schnittstelle trägt.

Der Haushalt kommt ausschließlich aus dem Anmelde-Cookie. Ist keiner gesetzt, bleibt er 0 und der
Filter findet **nichts** — der Standardfall ist „nichts sichtbar“, nicht „alles sichtbar“.
Nachgeprüft: ein zweiter, frisch registrierter Haushalt sieht 0 Konten, 0 Buchungen, 0 Budgets, kein
Depot und ein Nettovermögen von 0; der Direktaufruf einer fremden Id antwortet mit 404, nicht mit
fremden Daten.

Die Anmeldedaten selbst — Benutzer, Sitzungen, Einladungen, Reset-Token — tragen bewusst keinen
Filter: sie werden gebraucht, *bevor* ein Haushalt feststeht. Ihre Abfragen führen die
Haushaltsbedingung ausdrücklich mit.

### Was beim Anmelden passiert

- Passwörter liegen als PBKDF2-Hash (`PasswordHasher<User>`, aktuelle Parameter, automatisches
  Nachhashen bei Formatwechsel). Nie im Klartext, nie umkehrbar.
- **Eine einzige Meldung** für „Adresse unbekannt“ und „Passwort falsch“. Auch eine unbekannte
  Adresse durchläuft eine Hash-Berechnung, damit sie sich nicht über die Antwortzeit verrät.
- Nach fünf Fehlversuchen ist das Konto 15 Minuten gesperrt. Die Sperre wird nur dem genannt, der
  das richtige Passwort liefert — wer es nicht kennt, erfährt daraus nichts über das Konto.
  Zusätzlich bremst ein Rate-Limit von 10 Anfragen je Minute und IP die Anmeldeendpunkte.
- Die Sitzung steht als Datensatz in der Datenbank; das Cookie trägt nur ihre Id. Jede Anfrage
  prüft, ob sie noch gilt. Damit ist „Angemeldet bleiben“ (30 Tage statt 12 Stunden) überhaupt erst
  vertretbar: Abmelden wirkt sofort, auch für ein Cookie, das kryptografisch noch gültig wäre.
- Der Passwort-Reset antwortet **immer** gleich, ob die Adresse existiert oder nicht. Vom Token
  liegt nur der SHA-256-Hash in der Datenbank, er gilt 30 Minuten und lässt sich einmal einlösen.
  Ein eingelöster Reset widerruft alle offenen Sitzungen des Benutzers.
- „Profile auf diesem Gerät“ liegt in `localStorage` und enthält nur Name, Adresse und Rolle —
  nie ein Passwort und nie ein Token. Ein Tipp darauf füllt bloß das E-Mail-Feld vor.

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

Der Tilgungsplan rechnet auf Cent genau; in zwei Zeilen weicht die gerundete Anzeige deshalb um 1 €
von der Tabelle des Handoffs ab, die durchgehend mit ganzen Euro rechnet.

Der fachliche Stichtag liegt fest auf dem 23.08.2026, 08:24 Uhr (`Demo:Today` in `appsettings.json`).
Wird der Wert geleert, läuft die Anwendung auf der echten Uhr. **Sitzungen, Sperren und Reset-Token
hängen nicht daran** — sie laufen über `TimeProvider` auf der echten Uhr, denn der Browser prüft die
Gültigkeit des Cookies ebenfalls dagegen.

**Der Import ist echt.** „34 Buchungen importieren“ schreibt tatsächlich 34 Buchungen. Danach stimmen
Salden, Monatssummen und Budgets nicht mehr mit dem Handoff überein — `finanzapp.db` löschen stellt
den Ausgangszustand wieder her.

## API

Alle Endpunkte unter `/api` außerhalb von `/api/auth` verlangen eine Anmeldung.

| Methode | Pfad | Zweck |
| --- | --- | --- |
| POST | `/api/auth/login` | Anmelden, legt die Sitzung an |
| POST | `/api/auth/register` | Benutzer anlegen, Haushalt beitreten oder neu anlegen |
| POST | `/api/auth/logout` | Sitzung widerrufen |
| GET | `/api/auth/me` | angemeldeter Benutzer, oder 401 |
| POST | `/api/auth/password-reset` | Reset anfordern, antwortet immer 204 |
| POST | `/api/auth/password-reset/redeem` | Token einlösen, setzt das neue Passwort |
| GET | `/api/household` | Haushalt, Mitglieder, Einladung, laufende Sitzung |
| POST | `/api/household/invitations` | neuen Einladungscode erzeugen (nur Inhaber) |
| GET | `/api/dashboard` | Vermögensaggregat, Zeitreihe, Monats-KPIs, Top-Budgets |
| GET | `/api/accounts` | Konten mit gerechnetem Saldo |
| GET | `/api/transactions?search=&skip=&take=` | Buchungsliste mit Suche und Paging |
| POST | `/api/transactions` | Buchung anlegen, idempotent über `requestKey` (Schreibrecht) |
| PATCH | `/api/transactions/{id}/category` | Kategorie zuordnen, optional Regel merken (Schreibrecht) |
| GET | `/api/categories?direction=` | Kategorien je Richtung |
| GET | `/api/rules` | Kategorisierungsregeln |
| GET | `/api/budgets?period=Month\|Quarter\|Year` | Budgetauslastung im Zeitraum |
| GET | `/api/portfolio` | Depotwert, Positionen, Kurszeitstempel |
| GET | `/api/loans/primary`, `/api/loans/{id}?months=` | Darlehen mit Tilgungsplan |
| GET | `/api/import/preview` | Vorschau, schreibt nichts |
| POST | `/api/import/{id}/commit` | Übernahme in einer Transaktion (Schreibrecht) |
| GET | `/api/overview/more` | Kennzahlen der Sammelseite |

## Entscheidungen, die man kennen sollte

- **Geld ist überall `decimal`.** In SQLite liegen Beträge als ganzzahlige Cent (Wertkonverter im
  `DbContext`) — sonst legt EF Core `decimal` als TEXT ab, und Summen und Sortierungen in SQL wären
  falsch. Gerundet wird erst in der Anzeige.
- **Deutsche Formatierung ohne ICU-Abhängigkeit.** `GermanFormat` definiert Zahlen- und Datumsformate
  explizit, statt sie über `CultureInfo("de-DE")` aufzulösen. Damit liefert der WebAssembly-Client
  dieselbe Ausgabe wie der Server, unabhängig von Browsersprache und geladenen ICU-Daten. Minus ist
  das typografische „−“ (U+2212), zwischen Zahl und Einheit steht ein geschütztes Leerzeichen.
- **Zwei Uhren.** `IClock` trägt die fachliche Zeit und kann für die Vorführung stillstehen;
  `TimeProvider` trägt die echte und entscheidet über Gültigkeiten. Die beiden zu vermischen hat
  während der Umsetzung jede Sitzung sofort ablaufen lassen.
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

- **Zwei-Faktor-Anmeldung.** Ihr Platz ist der Schritt nach „Anmelden“; die Sicherheits-Zeile auf
  „Mehr“ weist bereits darauf hin. `User.TwoFactorEnabled` steht im Modell.
- **Rechteverwaltung.** Die Rollenmatrix ist nicht entworfen, deshalb gibt es noch keinen Endpunkt
  zum Ändern einer Rolle. Der Link „Rechte“ meldet das.
- **Sperrmeldung, Sitzungsübersicht, Widerruf einzelner Geräte.** Das Modell trägt sie
  (`UserSession`), die Oberfläche noch nicht.
- Ladezustände, leere Listen, Offline, Fehlerdialoge — derzeit nur eine Textzeile in der Sprache des
  Systems (`AsyncView`).
- Auswertungen, wiederkehrende Buchungen, Split-Buchung, Sondertilgungsdialog, CSV-Spalten-Mapping,
  Verwaltung von Kategorien und Regeln, Budgetanlage. Die betroffenen Schaltflächen melden das mit
  einem Toast, statt ins Leere zu führen.

Technisch offen:

- **Kein Datei-Upload und kein CAMT-Parser.** `DemoImportBatch` steht für die Datei; Vorschau,
  Referenzabgleich und Übernahme arbeiten darauf mit derselben Logik, die später eine echte Datei
  bekommt.
- **Umbuchungen legen noch keine Gegenbuchung an.** `Transaction.CounterAccountId` ist vorhanden, die
  zweite Buchungshälfte fehlt.
- **Archivo kommt von Google Fonts.** Für den Produktivbetrieb selbst hosten — der Handoff sagt das
  ausdrücklich.
- **Keine Migrationen.** Das Schema entsteht über `EnsureCreated`. Vor dem ersten echten Datenbestand
  auf EF-Core-Migrationen umstellen; die Anmeldetabellen kamen nachträglich dazu, eine bestehende
  `finanzapp.db` muss dafür gelöscht werden.
- **Jede Anfrage prüft die Sitzung in der Datenbank.** Das ist der Preis für sofort wirksames
  Abmelden. Bei Bedarf lässt sich der Sitzungsstatus kurz zwischenspeichern.
- **Keine Tests.** Für die Rechenwege — Tilgungsplan, Budgetzeiträume, Duplikaterkennung,
  Formatierung, Passwortbewertung — und für die Mandantentrennung lohnt sich eine Testsuite als
  Nächstes.

## Design-Handoff

Der Handoff liegt unverändert unter [`docs/design-handoff/`](docs/design-handoff/):
`handoff.md` (die Spezifikation samt Nachtrag 2), `FinanzApp.dc.html` (der Prototyp, im Browser zu
öffnen) und `_ds/modernist/styles.css` (die Token-Quelle).
