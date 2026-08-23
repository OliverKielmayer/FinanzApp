# Handoff: Erweiterung zum Finanz- und Dokumentenmanagement (Mobile Client)

Stand 24.08.2026 · ergänzt den bestehenden Handoff `design_handoff_finanzapp_mobile/` · Repo `OliverKielmayer/FinanzApp`, Stand `31eb21a`

## Was hier drin ist

- `Wireframes Erweiterung.dc.html` — 24 Wireframe-Screens in sieben Optionen, im Browser zu öffnen. **Lo-fi und bewusst grau/handschriftlich**: sie legen Struktur, Reihenfolge und Zustände fest, nicht das Aussehen.
- Das Aussehen kommt unverändert aus dem bestehenden Handoff (`design_handoff_finanzapp_mobile/README.md`) und dem Design-System Modernist (`_ds/modernist/styles.css` bzw. `wwwroot/css/modernist.css` im Repo): flach, Archivo, kein Radius, 2px-Trennlinien, Akzent `#ec3013` sparsam, Button-Labels linksbündig, Beträge tabular und `de-DE`.
- Freigegebene Richtung: Navigation **1a** (Tabs bleiben, „Vorgänge" wird der fünfte Tab) plus die zentrale Scan-Aktion aus **1c**. Die Wireframes **1b** (Bereichsraster) sind verworfen und dienen nur als Referenz.

## Die wichtigste Regel

Das ist eine **Erweiterung**, kein neues Projekt. Erst den Bestand lesen — `Data/Entities/Entities.cs`, `Data/FinanzAppDbContext.cs`, die Services unter `Application/`, `Endpoints/ApiEndpoints.cs`, `Shared/Contracts/`, die Screens unter `Client/Pages/` und `Client/Navigation/ScreenCatalog.cs` — dann einen Erweiterungsplan schreiben, dann bauen. Vorhandenes (Buchungen, Konten, Budgets, Depot, Darlehen, Import, Login) bleibt unverändert funktionsfähig. Kein zweiter Darlehensbereich, keine zweite Buchungstabelle, keine parallele Kategorienverwaltung.

---

## 1. Navigation — was sich am Rahmen ändert

Die Tab-Bar bleibt fünfteilig. Neue Belegung:

| Position | vorher | nachher |
| --- | --- | --- |
| 1 | Vermögen | **Vermögen** (unverändert, um Liquidität und offene Vorgänge erweitert) |
| 2 | Konten | **Vorgänge** — neu |
| 3 | Erfassen | **Erfassen** — wird zur zentralen Aktion (Sheet statt Screen, siehe unten) |
| 4 | Budgets | **Dokumente** — neu |
| 5 | Depot | **Mehr** |

Konten, Budgets und Depot wandern nach „Mehr" und bleiben als Screens unverändert. „Mehr" wird zur Bereichsliste: Konten & Buchungen, Budgets, Depot, Darlehen, Import, Versicherungen, Gesundheit/PKV, Wohnen & Immobilien, Arbeit & Beruf, Benutzer & Anmeldung, Administration. Jede Zeile trägt wie bisher Untertitel und rechts eine Kennzahl (z. B. „7 Verträge", „4 Vorgänge offen", „312 · 2 ohne Datei").

Umsetzung: `ScreenCatalog.All` erweitern, `TabLabel` umhängen; die neuen Screens sind Detailscreens (`IsDetail: true`) mit dem bestehenden Zurück-Schalter, außer den vier Tabs. Ab 768 px zeigt die Seitennavigation weiterhin alle Einträge — dort ist kein Platzproblem, also alle Bereiche flach auflisten.

**Erfassen wird ein Sheet** (Wireframe `1c`, zweiter Screen), das über jedem Screen aufgeht, statt eines eigenen Screens: „Beleg scannen" (primär), „Buchung erfassen", „Arztrechnung / PKV", „Rechnung", „Dokument verknüpfen", „Aufgabe / Frist". Der bestehende 3-Schritt-Erfassungsscreen bleibt vollständig erhalten — er wird künftig aus diesem Sheet heraus geöffnet.

---

## 2. Datenmodell — neue Bausteine

Alles Neue hängt am Haushalt (siehe Nachtrag 2 zum Login: globaler Mandantenfilter im `DbContext` — **jede** neue Entität bekommt ihn, sonst sieht Benutzer A die Dokumente von Benutzer B).

### Document

Zentrales Dokumentmodell, von allen Bereichen genutzt: Id, HouseholdId, Titel, DocumentTypeId, Kategorie, Beschreibung, **RelativerPfad**, Dateiname, Erweiterung, Dokumentdatum, GültigVon, GültigBis, Status, Tags, CreatedAt, UpdatedAt.

- Gespeichert wird **nur der relative Pfad** unter einem konfigurierten `DocumentRoot` (`Versicherungen/Risikoleben/Police_2026.pdf`), nie der absolute. Kombiniert wird erst beim Öffnen.
- Fehlt die Datei: Eintrag bleibt vollständig sichtbar und verknüpft, wird als „Datei nicht gefunden" markiert, Pfad wird angezeigt, Korrektur wird angeboten, Vorfall wird protokolliert. Kein Absturz, kein Ausblenden.
- `DocumentType` ist eine **Tabelle**, kein Enum — Administratoren legen Typen an (Arbeitsvertrag, Lohnabrechnung, Versicherungsschein, Beitragsrechnung, Arztrechnung, PKV-Abrechnung, Kaufvertrag, Grundbuchauszug, Energieausweis, Stromrechnung, Darlehensvertrag, Bankdokument …).

### DocumentLink

Polymorphe Verknüpfung (`DocumentId`, `TargetType`, `TargetId`), damit ein neuer Zieltyp keine Änderung am Dokumentmodell erzwingt. Zieltypen zum Start: Account, Transaction, Portfolio, Insurance, LifeInsurance, Loan, Property, Contract, Invoice, Employer, EmploymentContract, Payslip, MedicalBill.

### Neue Fachobjekte

`Insurance`, `MedicalBill` (PKV-Vorgang), `Property`, `Contract` (Wohnverträge), `Invoice`, `Employer` / `EmploymentContract` / `Payslip`, `TaskItem` (Aufgaben & Fristen). Felder je Objekt wie in der Spec; Beträge durchgehend `decimal` mit dem bestehenden Cent-Konverter.

### Was ausdrücklich NICHT dupliziert wird

Geldbewegungen bleiben Buchungen. Ein Versicherungsbeitrag, eine Rechnungszahlung, eine PKV-Erstattung sind **Verweise auf eine bestehende `Transaction`**, keine eigenen Finanzsätze. Fachobjekte liefern Kontext (erwarteter Betrag, Fälligkeit, Status), die Buchhaltung liefert die Tatsache.

---

## 3. Screens

Reihenfolge, Inhalte und Zustände stehen in den Wireframes; hier steht, was sie bedeuten.

### 3.1 Vermögen (bestehender Screen, erweitert) — Wireframe `1a`, `1g`

Neu **über** dem bisherigen Inhalt:

1. **Liquiditätsblock**: „Bleibt übrig" als Hero-Zahl (+1.628 €), darunter Einnahmen/Ausgaben und Sparquote. Ersetzt nicht das Nettovermögen — dieses rückt eine Sektion nach unten, alles andere bleibt in Reihenfolge und Optik unverändert.
2. **Offene Vorgänge** als Akzent-Banner (gleiches Muster wie das bestehende Triage-Banner auf „Konten"): „3 offene Vorgänge · Erstattung 680 € · 2 Rechnungen fällig" → führt auf den Vorgänge-Tab.

Der ausführliche Liquiditätsscreen (`1g`, erster Screen) hängt am Liquiditätsblock: „Noch fällig diesen Monat" (bekannte, aber noch nicht gebuchte Beträge), „Erwartet" (PKV-Erstattung), „Verfügbar nach Fixkosten". Dazu zwei Folgescreens: **Wohin fließt es** (fix vs. variabel, Kategorien über 6 Monate) und **Sparpotential** (Budgetüberschreitungen, kündbare Verträge mit Frist, wiederkehrende Buchungen ohne Vertrag, Summe „+94 €/Monat"). Diese Screens rechnen ausschließlich auf vorhandenen Daten — Buchungen, Budgets, Vertragsfristen. Keine neuen Eingaben.

**Wichtig für die Zahlen**: Eigenanteile zählen als Ausgabe, erstattete Beträge nicht. Umbuchungen zählen weiterhin weder als Einnahme noch als Ausgabe.

### 3.2 Vorgänge (neuer Tab) — Wireframe `1a`, zweiter Screen

Eine Liste alles Unerledigten aus allen Bereichen, sortiert nach Dringlichkeit: PKV-Erstattungen, offene Rechnungen, Kündigungsfristen, Aufgaben. Filter-Chips „Offen / Wartet / Erledigt" mit Zähler. Jede Zeile: Titel mit Betrag, darunter Zustand und Fälligkeit; überfällige Einträge im Akzent-Muster.

Einträge entstehen automatisch (Vertragsende minus Kündigungsfrist, Rechnungsfälligkeit, Erstattung ohne Zahlungseingang nach N Tagen, ablaufende Dokumente, fällige Wartung) oder von Hand. Erzeugungsgrund wird mitgespeichert und in der Zeile erklärt („eingereicht 02.08. · überfällig seit 12 T").

### 3.3 PKV-Flow (Kernflow) — Wireframe `1d`, fünf Screens

1. **Scannen**: Kamerabild, Zieltyp-Chips (Arztrechnung / Rechnung / Dokument), Auslöser. Die Datei landet im Dokumentordner, gespeichert wird der relative Pfad. Ohne Kamera/OCR: derselbe Weg mit Dateiauswahl.
2. **Erkannt — prüfen**: Rechnungssteller, Datum, Nummer, Betrag, **Eigenanteil**, erwartete Erstattung — jeder Wert einzeln korrigierbar. Ohne OCR dieselbe Maske, nur leer. Die OCR-Anbindung muss hinter einer Schnittstelle liegen, austauschbar, kein Anbieter im Fachcode.
3. **Vorgang**: Kopf mit der Dreiteilung Rechnung / Erstattung / Eigenanteil, darunter der Statusverlauf als abhakbare Kette (Erfasst → Eingereicht → Abrechnung erhalten → Zahlung eingegangen → Abgeschlossen; dazu Teilweise erstattet, Abgelehnt). Primäraktion ist immer der nächste Schritt.
4. **Überfällig**: Wartezeit prominent („Wartet seit 12 Tagen · übliche Dauer 14 T"), Aktionen „Erinnerung setzen" und „Bei PKV nachfragen", angehängte Dokumente.
5. **Zahlung zuordnen**: Vorschlagsliste aus echten Buchungen, bewertet nach Betrag, Datum und Verwendungszweck; der beste Treffer im Akzent, abweichende Kandidaten darunter mit Begründung („Betrag weicht ab"). Bestätigt wird **von Hand** — die automatische Zuordnung schlägt vor, sie entscheidet nicht.

Fachliche Regel, die im UI sichtbar bleiben muss: **Eigenanteil ist keine offene Forderung.** Er ist eine gebuchte Ausgabe und darf nirgends als „noch zu erstatten" gezählt werden.

### 3.4 Dokumente (neuer Tab) — Wireframe `1e`, vier Screens

- **Liste**: Suchfeld, Bereichs-Chips, Zeilen mit Dateiname (fett), darunter Typ · Objekt · Dokumentdatum; fehlende Dateien im Akzent-Muster. Fuß: Gesamtzahl und Sortierung.
- **Suche**: Volltext plus Filter (Bereich, Typ, Zeitraum, Tag, Status). Trifft **auch Objekte**, nicht nur Dateinamen — ein Treffer kann eine Versicherung oder eine Buchung sein, entsprechend gekennzeichnet.
- **Detail**: Titel, Vorschau/Öffnen, relativer Pfad, Typ/Datum/Gültigkeit, Tags, „Verknüpft mit"-Liste mit Weg ins Objekt.
- **Datei nicht gefunden**: Akzentblock mit dem gesuchten Pfad, drei Auswege — „Pfad korrigieren", „Im Ordner suchen", „Eintrag behalten, Datei später". Metadaten und Verknüpfungen bleiben sichtbar.

Verknüpft wird **am Objekt** (die Versicherung hängt ihr Dokument an), das Dokument zeigt seine Bezüge nur an — mit einer Aktion „+ Verknüpfung hinzufügen" als Nebenweg.

### 3.5 Versicherungen — Wireframe `1b`, Screens 2 und 3

Liste mit Chips (Alle / Frist / Beitrag), Zeilen mit Versicherer, Beitrag und Ablauf; Verträge mit laufender Kündigungsfrist im Akzent. Detailseite: Vertragskopf, **Fristen** (abgeleitet: Vertragsende minus Kündigungsfrist → Aufgabe), Dokumente chronologisch, Zahlungen mit Verweis auf die jeweilige Buchung.

### 3.6 Wohnen & Immobilien — Wireframe `1f`, drei Screens

- **Immobilie**: Marktwert als Hero, darunter das **bestehende** Darlehen als Verweiszeile (Rate, Zinsbindung) — Tap führt auf den vorhandenen Darlehensscreen, keine Kopie. „Kosten 12 Monate" summiert Darlehen, Energie, Versicherung, Wartung. Danach Verträge und Dokumente.
- **Vertrag** (z. B. Strom): Anbieter, Vertragsnummer, Abschlag und Konto, Kündigungsfrist im Akzent, Rechnungsliste (offene im Akzent), Dokumente.
- **Rechnung**: Fälligkeit als Akzentblock, Zugehörigkeit (Vertrag → Immobilie), Dokument, Zahlungsbereich mit „Buchung zuordnen" / „Als bezahlt markieren" — dieselbe Zuordnungsmechanik wie beim PKV-Vorgang.

### 3.7 Arbeit & Beruf, Administration

In dieser Runde nicht gestaltet. Arbeit & Beruf (Arbeitgeber, Arbeitsverträge, Lohnabrechnungen, Vereinbarungen) folgt dem Muster aus 3.5: Liste → Objektseite mit Dokumenten und Zahlungsbezug. Administration (Dokumenttypen, Kategorien, Regeln) folgt dem Muster der bestehenden Verwaltungsseiten. Beide vor dem Bau anfragen.

---

## 4. Zustände, die zum Design gehören

| Zustand | Muster |
| --- | --- |
| Erstattung überfällig | Akzentblock im Vorgang **und** Zeile im Vorgänge-Tab mit Tagen seit Einreichung |
| Rechnung offen / fällig | Akzentblock „Fällig in 5 Tagen" mit Datum; nach Fälligkeit „überfällig seit …" |
| Datei nicht gefunden | Akzentblock mit Pfad plus drei Auswege, Metadaten bleiben |
| Leerer Bereich | eine Zeile Erklärung und die Primäraktion — nie eine leere Liste |

Ladezustände, Offline und Fehlerdialoge bleiben offen (wie im Erst-Handoff vermerkt) und sollten vor dem Ausbau gestaltet werden.

---

## 5. Umsetzungsreihenfolge

Der Spec-Reihenfolge folgend, aber am Nutzen ausgerichtet:

1. **Rahmen**: `ScreenCatalog` umbauen (Tabs, „Mehr" als Bereichsliste), Erfassen-Sheet, Vorgänge-Tab als leere Hülle.
2. **Dokumentmodell**: `Document`, `DocumentType`, `DocumentLink`, `DocumentRoot`-Konfiguration, Pfadprüfung, Liste/Detail/Suche, „Datei nicht gefunden".
3. **Gesundheit / PKV**: `MedicalBill` mit Statuskette, Eigenanteil-Trennung, Erfassungsmaske, Zuordnung zur Buchung. Das ist der Kernflow — hier zuerst echten Nutzen liefern.
4. **Liquidität**: Liquiditätsblock im Dashboard, Detailscreens „Wohin fließt es" und „Sparpotential".
5. **Versicherungen** samt abgeleiteten Fristen.
6. **Wohnen & Immobilien** samt Verknüpfung zum bestehenden Darlehen und Rechnungen.
7. **Aufgaben & Fristen** vollständig automatisieren, danach Arbeit & Beruf, Administration, Reporting.

Ab Schritt 2 gilt: EF-Core-**Migrationen** statt `EnsureCreated` (im Repo als offener Punkt notiert) — vor dem ersten echten Datenbestand umstellen.

## 6. Tests, die neu dazugehören

Dokument anlegen/bearbeiten/löschen/verknüpfen, Pfadauflösung relativ↔absolut, fehlende Datei, Dokumentberechtigungen, Rechnung, PKV-Erstattung, Eigenanteil (darf nicht als offene Forderung zählen), Verknüpfung Erstattung ↔ Buchung, Versicherung, abgeleitete Kündigungsfrist, Immobilie, Vertrag, Rechnung ↔ Buchung, **Benutzerisolierung über alle neuen Endpunkte**. Bestehende Tests bleiben grün.

## 7. Backups

Die Anwendung sichert die Datenbank weiter automatisch. Die referenzierten **Dokumentdateien sichert sie nicht** — das gehört in die Dokumentation und in die Einrichtung eines Dateisystem-Backups für `DocumentRoot`. Eine spätere Prüffunktion („sind alle referenzierten Dateien vorhanden?") ist vorgesehen; der Zustand „Datei nicht gefunden" ist dafür bereits gestaltet.
