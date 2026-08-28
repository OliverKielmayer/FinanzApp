# Erweiterungsplan — Finanz- und Dokumentenmanagement

Zum Handoff [`design-handoff-erweiterung/handoff.md`](design-handoff-erweiterung/handoff.md)
(Stand 24.08.2026). Geschrieben nach Durchsicht des Bestands, wie der Handoff es verlangt.

> **Stand der Umsetzung (28.08.2026).** Die Schritte 1 bis 6 sind gebaut und geprüft. Von
> Schritt 7 laufen `TaskItem` samt der Ableitung von Fristen, Rechnungsfälligkeiten und
> überfälligen Erstattungen; **Arbeit & Beruf** und die **Administration** (Dokumenttypen) sind
> mit dem v5-Handoff nachgezogen. Offen bleiben allein die ablaufenden Dokumente als Fristquelle.
> Der Umstieg auf EF-Core-Migrationen ist erfolgt, die Testsuite aus Abschnitt 6 liegt unter
> `tests/FinanzApp.Tests`. Was insgesamt noch aussteht, steht in
> [`offene-punkte.md`](offene-punkte.md).

## Was der Bestand hergibt

| Vorhanden | Wird wiederverwendet für |
| --- | --- |
| `IHouseholdOwned` + globaler Abfragefilter im `DbContext` | jede neue Entität — ohne Ausnahme |
| `Transaction` mit Kategorien, Regeln, Import | Zahlungsbezug aller Fachobjekte. Keine zweite Geldtabelle. |
| `Loan` + `/darlehen` mit Tilgungsplan | die Immobilie verweist darauf, kopiert nichts |
| `InsurancePolicy` (Kapitallebensversicherung, Rückkaufswert) | bleibt der Vermögenswert auf dem Dashboard |
| `ScreenCatalog` mit `TabLabel` / `IsDetail` / `RequiresWrite` | die neue Tab-Belegung ist eine Datenänderung, keine Umbaumaßnahme |
| `AuthPolicies.Write` / `.ManageUsers`, `AuthorizeView` | Rollenschutz der neuen Endpunkte und Schaltflächen |
| `AsyncView`, `CategorySheet`, `ProgressBar`, `Toast`, `.tap`-Zeilen | die neuen Screens bauen aus denselben Bausteinen |
| `GermanFormat`, Cent-Konverter | Beträge und Datumsangaben bleiben einheitlich |

**Zwei Versicherungsbegriffe, die nicht dasselbe sind.** Der Bestand kennt `InsurancePolicy` als
*Vermögenswert* (Heidelberger Leben, Rückkaufswert 84.900 €, speist die Dashboard-Kachel). Der
Handoff meint mit „Versicherungen“ *Verträge* (Hausrat, Haftpflicht, Kfz, Risikoleben) mit Beitrag,
Frist und Dokumenten. Das sind verschiedene Dinge — die Ziel­typenliste des Handoffs führt
`Insurance` und `LifeInsurance` deshalb getrennt. Neue Entität `Insurance`, bestehende bleibt.

## Schritt 1 — Rahmen

Reine Navigationsänderung, kein neues Datenmodell.

- `ScreenCatalog.All`: `TabLabel` umhängen auf **Vermögen · Vorgänge · Erfassen · Dokumente · Mehr**.
  Konten, Budgets und Depot verlieren ihr `TabLabel` und werden Detailscreens — die Screens selbst
  bleiben Zeile für Zeile unverändert.
- **Erfassen wird ein Sheet** statt eines Tab-Ziels. Die Tab-Zelle öffnet es, statt zu navigieren;
  dafür bekommt `Screen` eine Kennzeichnung „ist eine Aktion, kein Ziel“. Einträge: Beleg scannen
  (primär), Buchung erfassen, Arztrechnung/PKV, Rechnung, Dokument verknüpfen, Aufgabe/Frist.
  Der bestehende 3-Schritt-Screen bleibt vollständig und wird von dort geöffnet.
- **„Mehr“ wird die Bereichsliste** mit elf Zeilen samt Kennzahl rechts. Die Kennzahlen kommen aus
  `MoreOverviewDto`, das entsprechend wächst.
- Ab 768 px listet die Seitennavigation weiterhin alles flach auf.
- **Vorgänge** als leere Hülle mit dem Leerzustand aus Abschnitt 4 des Handoffs.

Betroffen: `ScreenCatalog`, `TabBar`, `SideNav`, `MainLayout`, `More.razor`, neu `CaptureSheet`,
`Pages/Tasks.razor`, `MoreContracts`, `OverviewService`.

## Schritt 2 — Dokumentmodell

Das Fundament, an dem alle folgenden Bereiche hängen.

```
DocumentType   Id, HouseholdId, Name, Bereich, SortOrder          (Tabelle, kein Enum)
Document       Id, HouseholdId, Title, DocumentTypeId, Category, Description,
               RelativePath, FileName, Extension, DocumentDate,
               ValidFrom, ValidUntil, Status, Tags, CreatedAt, UpdatedAt
DocumentLink   Id, HouseholdId, DocumentId, TargetType, TargetId
```

- **Nur der relative Pfad** wird gespeichert. `DocumentRoot` kommt aus der Konfiguration und wird
  erst beim Öffnen davorgesetzt. Ein `DocumentPathService` löst auf, prüft die Existenz und
  verhindert Ausbrüche aus dem Wurzelverzeichnis (`..`), bevor irgendetwas gelesen wird.
- **Fehlende Datei ist ein Zustand, kein Fehler**: der Eintrag bleibt vollständig sichtbar, wird
  markiert, zeigt den gesuchten Pfad und bietet drei Auswege. Wird protokolliert.
- Upload: der Client schickt die Datei, die API legt sie unter `DocumentRoot/<Bereich>/…` ab und
  speichert den relativen Pfad. Dateityp und Größe werden begrenzt, der Dateiname bereinigt.
- Screens: Liste mit Bereichs-Chips, Suche (trifft **auch Objekte**, nicht nur Dateinamen), Detail
  mit Vorschau und „Verknüpft mit“, sowie der Zustand „Datei nicht gefunden“.
- **Ab hier EF-Core-Migrationen statt `EnsureCreated`.** Die erste Migration bildet den heutigen
  Stand ab, die zweite bringt die Dokumenttabellen.

## Schritt 3 — Gesundheit / PKV (Kernflow)

```
MedicalBill    Id, HouseholdId, Provider, BillDate, BillNumber, GrossAmount,
               OwnShare, ExpectedReimbursement, Status, SubmittedAt,
               SettledAt, ReimbursementTransactionId?, OwnShareTransactionId?
```

- Statuskette `Erfasst → Eingereicht → Abrechnung erhalten → Zahlung eingegangen → Abgeschlossen`,
  dazu `Teilweise erstattet` und `Abgelehnt`. Die Primäraktion ist immer der nächste Schritt.
- **Eigenanteil ist keine offene Forderung** — er ist eine gebuchte Ausgabe. Offen ist ausschließlich
  `ExpectedReimbursement`. Diese Trennung gehört in einen Test, nicht nur in einen Kommentar.
- Fünf Screens: Scannen → Erkannt/prüfen → Vorgang → Überfällig → Zahlung zuordnen.
- **OCR hinter einer Schnittstelle** (`IBillTextExtractor`) mit einer Umsetzung, die nichts erkennt
  und die Maske leer lässt. Kein Anbieter im Fachcode; die Maske ist ohne OCR vollständig bedienbar.
- **Zahlungszuordnung schlägt vor, sie entscheidet nicht.** Bewertung über Betrag, Datum und
  Verwendungszweck gegen echte `Transaction`-Sätze; bestätigt wird von Hand.

## Schritt 4 — Liquidität

Rechnet ausschließlich auf vorhandenen Daten. Keine neue Tabelle.

- **Liquiditätsblock** über dem bisherigen Dashboard-Inhalt: „Bleibt übrig“ als Hero, darunter
  Einnahmen/Ausgaben und Sparquote. Das Nettovermögen rückt eine Sektion nach unten, sonst bleibt
  alles in Reihenfolge und Optik.
- **Banner „offene Vorgänge“** im Muster des bestehenden Triage-Banners.
- Detailscreens **Liquidität**, **Wohin fließt es** (fix vs. variabel, Kategorien über 6 Monate) und
  **Sparpotential** (Budgetüberschreitungen, kündbare Verträge, wiederkehrende Buchungen ohne
  Vertrag).
- Rechenregel: Eigenanteile zählen als Ausgabe, erstattete Beträge nicht; Umbuchungen weiterhin
  weder noch.

## Schritt 5 — Versicherungen

```
Insurance      Id, HouseholdId, Name, Insurer, PolicyNumber, Premium, PremiumInterval,
               StartsOn, EndsOn, NoticePeriodMonths, AccountId?, Notes
```

Liste mit Chips (Alle / Frist / Beitrag), Detail mit Vertragskopf, **abgeleiteten Fristen**
(Vertragsende minus Kündigungsfrist → `TaskItem`), Dokumenten und Zahlungen als Verweis auf
Buchungen.

## Schritt 6 — Wohnen & Immobilien

```
Property       Id, HouseholdId, Name, Address, PurchaseDate, PurchasePrice, MarketValue, LoanId?
Contract       Id, HouseholdId, PropertyId?, Name, Provider, ContractNumber, MonthlyAmount,
               AccountId?, StartsOn, EndsOn, NoticePeriod…
Invoice        Id, HouseholdId, ContractId?, Number, IssuedOn, DueOn, Amount, Status,
               TransactionId?
```

Die Immobilie **verweist** über `LoanId` auf das bestehende Darlehen — Tap führt auf `/darlehen`,
keine Kopie. „Kosten 12 Monate“ summiert Darlehen, Energie, Versicherung, Wartung aus vorhandenen
Buchungen. Rechnungen nutzen dieselbe Zuordnungsmechanik wie der PKV-Vorgang.

## Schritt 7 — Aufgaben, Arbeit, Administration

```
TaskItem       Id, HouseholdId, Title, DueOn, Status, Source, SourceType, SourceId, Notes
```

`TaskItem` entsteht schon in Schritt 3 mit, wird hier aber vollständig automatisiert: Vertragsende
minus Frist, Rechnungsfälligkeit, Erstattung ohne Zahlungseingang nach N Tagen, ablaufende
Dokumente. Der Erzeugungsgrund wird mitgespeichert und in der Zeile erklärt.

**Arbeit & Beruf** und **Administration** sind laut Handoff in dieser Runde nicht gestaltet und
ausdrücklich vor dem Bau anzufragen. Sie bleiben deshalb außen vor.

## Querschnitt

- **Mandantenfilter**: jede neue Entität trägt `IHouseholdOwned`. Der Filter hängt sich per Schleife
  selbst an — nichts weiter zu tun, aber in den Tests nachzuweisen.
- **Rollen**: alle schreibenden Endpunkte an `AuthPolicies.Write`; Dokumenttypen und Administration
  an `ManageUsers`. Lesezugriff sieht Vorgänge und Dokumente, ändert nichts.
- **Beträge** durchgehend `decimal` mit dem bestehenden Cent-Konverter.
- **Zustände** aus Abschnitt 4 des Handoffs: überfällig, fällig, Datei fehlt, leerer Bereich — jeweils
  im Akzentmuster beziehungsweise als Erklärzeile mit Primäraktion. Nie eine leere Liste.
- **Backups**: die Datenbank wird gesichert, die Dateien unter `DocumentRoot` nicht. Gehört in die
  README und in die Einrichtung.

## Tests, die mit dazugehören

Dokument anlegen/bearbeiten/löschen/verknüpfen · Pfadauflösung relativ ↔ absolut · fehlende Datei ·
Ausbruchsversuch aus `DocumentRoot` · Rechnung · PKV-Erstattung · **Eigenanteil zählt nicht als
offene Forderung** · Verknüpfung Erstattung ↔ Buchung · abgeleitete Kündigungsfrist ·
Rechnung ↔ Buchung · **Benutzerisolierung über alle neuen Endpunkte**.

Der Bestand hat heute keine Tests. Mit Schritt 2 kommt ein Testprojekt dazu; die Rechenwege des
Bestands (Tilgungsplan, Budgetzeiträume, Duplikaterkennung) werden dabei mit abgedeckt.

## Was der Handoff offen lässt

- **Arbeit & Beruf** und **Administration** — nicht gestaltet, vor dem Bau anzufragen.
- **Ladezustände, Offline, Fehlerdialoge** — schon im Erst-Handoff offen.
- **Wireframe 1a zeigt eine ältere Tab-Belegung** (mit „Konten“ als Tab 2) als die Tabelle in
  Abschnitt 1. Maßgeblich ist die Tabelle, weil sie 1a mit der freigegebenen Scan-Mitte aus 1c
  zusammenführt — 1c zeigt genau diese fünf Zellen.
- **„Vorgänge wird der fünfte Tab“** im Kopftext, in der Tabelle steht es an Position 2. Umgesetzt
  wird die Tabelle.
