# Bedieneranleitung

Der Reihe nach: anmelden, sich zurechtfinden, die täglichen Handgriffe. Alles Technische —
Einrichtung, Aufbau, Konfiguration — steht in der [README](../README.md).

Die App läuft unter <http://localhost:5111>.

## 1. Anmelden

Drei Zugänge des Demo-Haushalts, alle mit dem Passwort `Demo-Haushalt-2026!`:

| E-Mail | Rolle | Darf |
| --- | --- | --- |
| `oliver@haushalt-kielmayer.de` | Inhaber | alles, dazu Benutzer verwalten und einladen |
| `sabine@haushalt-kielmayer.de` | Mitglied | alle Daten ändern, keine Benutzerverwaltung |
| `kanzlei@haas-stb.de` | Lesezugriff | nur ansehen |

- **Angemeldet bleiben** hält die Sitzung über das Schließen des Browsers hinaus. Ohne den Haken
  endet sie mit dem Fenster.
- Wer sich einmal angemeldet hat, steht danach oben unter **Profile auf diesem Gerät** — ein Tipp
  darauf, Passwort eingeben, fertig.
- **Passwort vergessen** schickt einen Link, der 30 Minuten gilt, einmal funktioniert und dabei alle
  offenen Sitzungen beendet. Ist kein Postausgang eingerichtet, steht die Nachricht samt Link im
  Protokoll der Anwendung.
- Nach fünf Fehlversuchen ist das Konto 15 Minuten gesperrt.
- Ohne Anmeldung zeigt die App nichts außer dem Anmeldescreen — keine Kopfzeile, keine Tab-Bar.

## 2. Wie die App aufgebaut ist

Unten fünf Zellen: **Vermögen · Vorgänge · Erfassen · Dokumente · Mehr**.

Die Mitte ist eine **Aktion, kein Ziel**: *Erfassen* legt ein Fenster über die laufende Seite und
fragt, was erfasst werden soll (siehe Punkt 5). `Esc` oder ein Tipp daneben schließt es wieder.

**Mehr** führt die übrigen Bereiche auf — Konten, Budgets, Depot, Vorsorge & Kapital,
Absicherung, Darlehen, Import, Gesundheit, Wohnen, Benutzer.

Am großen Bildschirm sieht die App anders aus. Sie schaltet nach Fensterbreite:

| | Telefon (< 768 px) | Tablet (bis 1200 px) | Desktop (darüber) |
| --- | --- | --- | --- |
| Navigation | Tab-Leiste unten | Seitenspalte links | Seitenspalte, breiter |
| Kacheln | 2 Spalten | 3 | 4 |
| Erfassen | Fenster von unten | Panel rechts | Panel rechts, breiter |
| Vermögen | Chart über Bilanz | nebeneinander | nebeneinander |

In der **Seitenspalte** steht oben, wer angemeldet ist und seit wann, darunter die Bereiche mit
ihrer Kennzahl — offene Vorgänge, Anzahl Dokumente und so fort. Ganz unten bleibt **Erfassen**
stehen, egal wie weit die Liste gescrollt ist.

Detailseiten tragen oben links einen Zurück-Schalter. Rechts in der Kopfzeile steht
**Beträge verbergen** — praktisch, wenn jemand mitschaut; derselbe Schalter holt sie zurück.

Vier Zustände tragen überall dieselbe Kennzeichnung: **überfällig**, **fällig**, **Frist läuft**,
**Datei nicht gefunden**.

## 3. Vermögen — die Startseite

Von oben nach unten: das Nettovermögen, darunter die Vermögensentwicklung und die Bilanz
(Brutto, Verbindlichkeiten, Netto) — am großen Bildschirm nebeneinander, auf dem Telefon
untereinander. Es folgen die Kacheln der einzelnen Posten — Girokonto, Tagesgeld, Depot,
Vorsorge —, jede führt in ihren Bereich, dann der laufende Monat mit Einnahmen,
Ausgaben und Sparquote und die Budgets. Zwei Schalter führen direkt zu **Buchung erfassen**
und **Import**.

**Liquidität** öffnet zwei Auswertungen:

- **Wohin fließt es** trennt feste von schwankenden Ausgaben. Fix heißt: die Kategorie kommt in fast
  jedem Monat vor und schwankt kaum — das erkennt die App selbst, gepflegt wird daran nichts.
- **Sparpotential** sammelt Budgetüberschreitungen, laufende Kündigungsfristen und wiederkehrende
  Buchungen ohne hinterlegten Vertrag. Beziffert wird nur, was sich beziffern lässt; was ein
  Anbieterwechsel bringt, steht ohne Zahl da.

## 4. Vorgänge — alles Unerledigte

Die meisten Einträge entstehen von selbst: aus einem Vertragsende minus Kündigungsfrist, aus einer
fälligen Rechnung, aus einer eingereichten Erstattung ohne Zahlungseingang. **Warum ein Eintrag da
ist, steht in der Zeile** — es gibt keine Aufgabe unbekannter Herkunft.

Oben schalten drei Filter mit Zähler um: **Offen · Wartet · Erledigt**. Ein Tipp auf die Zeile führt
an die Stelle, an der sich die Sache erledigen lässt — abgehakt wird dort, nicht in der Liste. Ist
die Ursache erledigt, verschwindet der Eintrag beim nächsten Aufruf aus *Offen*.

## 5. Erfassen

Der Schalter in der Mitte fragt zuerst, worum es geht — der Weg von „Papier in der Hand“ zu
„erfasst“ ist dadurch immer gleich lang:

**Beleg scannen** · **Buchung erfassen** · **Arztrechnung / PKV** · **Rechnung** ·
**Dokument verknüpfen** · **Aufgabe / Frist**

Vollständig führen davon bislang die ersten beiden: *Buchung erfassen* öffnet die Maske unten,
*Beleg scannen* die Belegerfassung. *Arztrechnung / PKV* und *Rechnung* landen auf derselben
Belegerfassung, ohne die Art vorzubelegen; *Dokument verknüpfen* und *Aufgabe / Frist* öffnen nur
den jeweiligen Bereich.

### Buchung erfassen

Drei Schritte, oben mitgezählt:

1. **Betrag** — erst *Ausgabe*, *Einnahme* oder *Umbuchung* wählen, dann über den Ziffernblock
   eintippen. Ein Komma gibt es nicht: die letzten beiden Ziffern sind die Cent. Mehr als acht
   Ziffern nimmt die Maske nicht, das entspricht 999.999,99 €. *Leeren* setzt zurück.
2. **Kategorie** — eine der angebotenen Kacheln. Bei einer Umbuchung entfällt dieser Schritt, dort
   gibt es nichts zu kategorisieren.
3. **Konto** und, wenn nötig, eine **Notiz**.

*Weiter* führt durch die Schritte, *Zurück* wieder heraus; im dritten Schritt heißt der Schalter
**Buchung speichern**. Danach steht man auf *Konten & Buchungen* und sieht die neue Buchung in der
Liste.

Mit Lesezugriff fehlt die Zelle in der Tab-Bar; erfassen kann nur, wer Schreibrecht hat.

## 6. Dokumente — die Ablage

- Die **Suche trifft auch Objekte**, nicht nur Dateinamen: Wer „hausrat“ tippt, meint meist den
  Vertrag und bekommt ihn angeboten.
- **Verknüpft wird am Objekt**, nicht am Dokument — die Versicherung, die Rechnung, der PKV-Vorgang
  nimmt das Dokument auf. Die Dokumentseite zeigt ihre Bezüge nur an.
- **Datei nicht gefunden** ist ein Zustand, kein Fehler. Der Eintrag bleibt vollständig sichtbar und
  verknüpft und nennt den gesuchten Pfad. Zwei Auswege stehen bereit: **Pfad korrigieren** trägt den
  richtigen Ort im Dokumentordner nach, **Eintrag behalten** lässt ihn stehen, bis die Datei wieder
  auftaucht.

## 7. Gesundheit & PKV

Der Weg einer Arztrechnung, Schritt für Schritt. Jeder Schritt ist ein Schalter auf der
Vorgangsseite:

1. **Beleg scannen** — die Datei wird abgelegt, danach stehen Rechnungssteller, Datum,
   Rechnungsnummer, Betrag und Eigenanteil zur Eingabe. Eine Texterkennung ist nicht angebunden;
   die Maske meldet „Nichts erkannt“ und wird von Hand ausgefüllt. Die erwartete Erstattung rechnet
   sie mit.
2. **Als eingereicht markieren**, sobald die Rechnung bei der Kasse ist.
3. **Abrechnung erhalten**, wenn die Antwort da ist.
4. **Zahlung zuordnen** — die App schlägt passende Buchungen vor, bewertet nach Betrag, Datum und
   Verwendungszweck. Der beste Treffer steht oben. **Bestätigt wird von Hand**; automatisch
   verknüpft die App nichts, ein Fehltreffer verfälschte sonst stillschweigend die Buchhaltung.
5. **Vorgang abschließen**. Kommt nichts, gibt es daneben **Abgelehnt**.

Zwei Dinge, die regelmäßig für Verwirrung sorgen:

- **Der Eigenanteil ist keine offene Forderung.** Er ist eine gebuchte Ausgabe. Offen ist immer nur
  die erwartete Erstattung — deshalb steht in der Liste nicht der Rechnungsbetrag.
- **Überfällig** heißt: eingereicht und länger als 14 Tage ohne Antwort.

## 8. Vorsorge · Absicherung · Wohnen

Verträge stehen in **zwei** Bereichen, und die Grenze ist einfach: hat der Vertrag einen Wert,
der zum Vermögen zählt?

- **Vorsorge & Kapital** — Kapital-Lebensversicherung, Riester, Bausparen. Oben steht der
  erreichte Wert, immer mit dem **Stichtag** dazu. Ein Jahresstand ist kein Tageskurs, und die
  App tut auch nicht so.
- **Absicherung** — Risikoleben, Berufsunfähigkeit, Hausrat, Haftpflicht, Kfz, Rechtsschutz,
  Krankenversicherung. Oben steht der **Jahresbeitrag**, kein Wert. Diese Verträge leisten im
  Schadensfall; sie tauchen nie im Vermögen auf.

Ein Tipp auf eine Zeile führt in beiden Fällen auf dieselbe Vertragsseite.

Kündigungsfristen werden **gerechnet**, nicht gepflegt: Vertragsende minus Frist. Ein von Hand
gesetztes Datum liefe der stillen Verlängerung hinterher, deshalb gibt es dieses Feld nicht.
Davon getrennt ist die **Erinnerung**: sie darf lange vor dem Termin liegen, weil ein Vergleich
Vorlauf braucht. Die Zeile sagt dann, worauf sich die Tage beziehen — auf den Termin oder auf
die Erinnerung.

Unter *Wohnen* hängen an der Immobilie ihre Verträge und Rechnungen; das Darlehen ist ein Verweis
auf den Darlehensbereich, wo auch der Tilgungsplan steht. Zahlungen sind überall Verweise auf echte
Buchungen — eine Geldbewegung bleibt eine Buchung.

## 9. Import

Die Vorschau sortiert die eingelesenen Sätze in vier Gruppen:

| Gruppe | Was passiert |
| --- | --- |
| Neue Buchungen | werden übernommen |
| Bereits vorhanden | an der Importreferenz erkannt, werden übersprungen |
| Mögliche Duplikate | zur Prüfung, werden **nicht** übernommen |
| Fehlerhafte Sätze | Betrag nicht lesbar, bleiben liegen |

Danach lassen sich die neuen Buchungen kategorisieren. Wer eine Kategorie vergibt, bekommt eine
Regel für künftige Importe angeboten — sie greift auf das erste Wort des Empfängers.

> **Der Import ist echt.** Er schreibt die Buchungen tatsächlich, danach stimmen die Demo-Zahlen
> nicht mehr. Wie sich der Ausgangszustand herstellen lässt, steht unter Punkt 12.

## 10. Benutzer & Anmeldung

Ein **Haushalt** besitzt die Daten, jeder Benutzer gehört genau einem Haushalt und sieht
ausschließlich dessen Bestand.

- Oben stehen die Mitglieder mit Rolle und letzter Aktivität.
- **Einladen** zeigt den gültigen Code samt Ablaufdatum; er ist **einmal** einlösbar, der
  Eingeladene registriert sich damit und landet im selben Haushalt. *Neuer Code* ersetzt den alten.
  Der Code des Demo-Haushalts lautet `HH-4K2P-9XQ1`. Einladen darf nur der Inhaber.
- **Diese Sitzung** nennt Benutzer und Anmeldezeit. Zwei Schalter beenden sie, mit einem
  Unterschied: **Benutzer wechseln** lässt das Profil in der Geräteliste stehen, **Abmelden**
  vergisst es. Beides wirkt sofort — die Sitzung liegt auf dem Server, nicht nur im Browser.

## 11. Was noch nicht geht

Diese Schalter sind vorhanden, aber noch nicht hinterlegt — sie melden es beim Antippen:

Split-Buchung · Neues Budget anlegen · Sondertilgung planen · Aufgabe anlegen ·
Erinnerung setzen · Bei PKV nachfragen · Im Ordner suchen · Rechteverwaltung

Dazu die vier Einträge des Erfassen-Fensters, die nur in ihren Bereich führen (siehe Punkt 5), und
die Belegerkennung, die nichts erkennt.

Ganz fehlen bislang die Bereiche **Arbeit & Beruf** und **Administration** sowie die
Zwei-Faktor-Anmeldung.

## 12. Demo zurücksetzen

Alle Zahlen der Anwendung sind gerechnet, nicht gespeichert; die Beispieldaten hängen am festen
Stichtag **23.08.2026**. Wer sie durcheinandergebracht hat — etwa durch den Import — löscht

```
src/FinanzApp.Api/finanzapp.db
```

und startet die Anwendung neu. Sie legt die Datenbank samt Beispieldaten neu an.
