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
Absicherung, Scaneingang, Gesundheit, Wohnen, Fahrzeuge, Darlehen, Import, Benutzer.

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
**Dokument verknüpfen** · **Aufgabe / Frist** · **Konto / Vertrag / Objekt anlegen**

Vollständig führen *Buchung erfassen*, *Beleg scannen* und *Konto / Vertrag / Objekt anlegen*.
*Arztrechnung / PKV* und *Rechnung* landen auf derselben Belegerfassung, ohne die Art
vorzubelegen; *Dokument verknüpfen* und *Aufgabe / Frist* öffnen nur den jeweiligen Bereich.

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

**Am großen Bildschirm entfällt die Schrittführung**: ab Tabletbreite stehen Betrag, Kategorie
und Konto untereinander auf einer Seite, und gespeichert wird in einem Zug.

### Etwas anlegen

Konto, Depot, Vorsorgevertrag, Versicherung, Immobilie, Vertrag und Budget haben denselben
Formularaufbau: Auswahlwerte als Kacheln, alles übrige als Feld, Pflichtfelder ohne den Zusatz
*optional*. Fehlt eines, sagt die App **welches** — „Versicherer fehlt“ — und hebt die Zeile
hervor. Der Einstieg steht jeweils **am Ende der zugehörigen Liste** als „+“-Zeile; über das
Erfassen-Fenster geht es auch.

Zwei Dinge, die auffallen können:

- **Depot** und **Darlehen** im Konto-Formular legen kein Konto an, sondern führen dorthin, wo
  sie hingehören. Ein Depot ist kein Konto, ein Darlehen erst recht nicht.
- Was es schon gibt, wird nicht doppelt angelegt: „Budget für Lebensmittel besteht bereits“.

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

### Vermögensübersicht einlesen

Wer von seinem Makler monatlich eine **Vermögensübersicht** bekommt — eine Tabelle mit einer
Zeile je Vertrag —, muss die Werte nicht abtippen. Unter *Vorsorge & Kapital* steht dafür unten
**Vermögensübersicht einlesen**; liegt der Ordnerdienst auf einem überwachten Ordner, genügt es,
die Datei dort abzulegen. Dann kommt sie von selbst herein und wartet im Scaneingang.

Der Prüfschritt zeigt **je Vertrag eine Zeile**: die Vertragsnummer, den Wert aus der Datei und
daneben, was bisher am Vertrag steht. Ein Tipp auf die Zeile schaltet den Vertrag zu oder ab.
Vorgeschlagen ist, was etwas ändert — dieselbe Datei ein zweites Mal eingelesen schlägt nichts
mehr vor, und der Kopf sagt auch, warum.

Drei Dinge lohnen einen Blick:

- **Verträge, die es in der App noch nicht gibt**, tragen „neu anlegen“. Sie entstehen aus den
  Angaben der Datei — Art, Gesellschaft, Nummer, Beitrag — und heißen erst einmal nach
  Gesellschaft und Nummer. Umbenennen können Sie sie danach.
- **Nicht übernommen** steht unter der Liste, mit Zeilennummer und Grund. Die Datei führt auch
  Girokonto, Tagesgeld und Depot; die gehören nicht in die Vorsorge und verschwinden trotzdem
  nicht stillschweigend. Steht die Nummer einer solchen Zeile später an einem kapitalbildenden
  Vertrag — einem Bausparvertrag etwa —, kommt sie beim nächsten Mal mit.
- **Der Beitrag** wird nur nachgetragen, wo keiner steht. Nennt die Datei einen anderen als der
  Vertrag, sagt die Zeile das und lässt ihn stehen: eine Dynamik tragen Sie am Vertrag ein, nicht
  eine Datei, die monatlich unbeaufsichtigt hereinkommt.

Übernommen wird je Vertrag, nicht je Stichtag — bei einer Historie mit Hunderten Ständen wäre
alles andere eine Zumutung. Ein einzelner Stand, der nicht stimmt, lässt sich am Vertrag unter
*Verlauf* wieder entfernen; danach zählt der neueste verbliebene.

Ein nachgetragener **älterer** Stand senkt den erreichten Wert nicht: maßgeblich ist der jüngste
Stichtag, nicht der zuletzt eingelesene. Eine über Jahre geführte Historie lässt sich damit auch
noch nach der aktuellen Monatsdatei einlesen.

## 8b. Fahrzeuge · Scaneingang

**Fahrzeuge** funktionieren wie Immobilien: ein Objekt, an dem alles hängt. Oben stehen die
Kosten der letzten zwölf Monate — die rechnet die App aus den Buchungen, sie werden nicht
eingetragen. Die Kfz-Versicherung wird nur **verwiesen**; angelegt und geändert wird sie unter
Absicherung.

**Scaneingang** ist der Posteingang. Was eingescannt wurde, wartet hier, bis Typ und Objekt
feststehen — erst dann verschwindet es daraus. Die Zeile sagt, ob etwas erkannt wurde
(„erkannt“) oder ob jemand hinsehen muss („prüfen“), dazu Absender und Seitenzahl.

Unter jeder Zeile stehen die beiden Schalter, mit denen der Beleg den Eingang verlässt:

- **Zuordnen** öffnet ein Fenster mit zwei Fragen: welcher **Typ** und welches **Objekt**. Den
  Typ wählen Sie aus den Chips, das Objekt suchen Sie — ab zwei Buchstaben erscheinen die
  Treffer mit ihrer Art daneben („Vertrag“, „Immobilie“, „Fahrzeug“). Ein Tipp auf *Zuordnen und
  wegräumen* trägt beides ein und nimmt den Beleg aus dem Eingang.
- **Wegräumen** steht nur bei erkannten Belegen: dort sind Typ und Objekt schon eingetragen, es
  fehlt nur Ihr Ja. **Achtung:** Wegräumen nimmt *keine Werte* — es räumt nur die Zeile weg.
  Trägt der Beleg gelesene Werte, die noch niemand übernommen hat, fragt der Eingang deshalb
  nach und bietet „Werte übernehmen“ daneben an.

Gibt es das Objekt in der App noch nicht, legen Sie es zuerst an — der Beleg bleibt bis dahin
liegen und geht nicht verloren.

**Wenn eine Zeile nicht lesbar ist.** Manche Scans verlieren beim Einlesen einzelne Zahlen. Im
Prüfschritt steht die Zeile dann mit einem Strich statt einem Betrag; ein Tipp darauf öffnet ein
Feld, in das Sie den Wert vom Papier abschreiben. Die Summe darunter wird sofort neu gerechnet,
und die **Rechenprobe** sagt Ihnen, ob es stimmt — sie vergleicht mit dem Gesamtwert, den der
Absender selbst ausweist. Passt es nicht, haben Sie sich vertippt oder eine andere Zeile ist
ebenfalls falsch gelesen.

Dass eine Zahl von Ihnen kam und nicht vom Papier, bleibt sichtbar: die Zeile trägt „von Hand
eingetragen“, und die Rechenprobe zählt es mit. Genauso lassen sich die Einzelwerte darunter
korrigieren — antippen, ändern, fertig.

**Gelesen ist nicht übernommen.** Ein Beleg kann eingelesen, richtig abgelegt und mit seinem
Vertrag verknüpft sein, ohne dass je ein Wert im Vertrag gelandet ist — genau das passiert beim
Wegräumen. Von außen sah so ein Beleg früher aus wie ein übernommener. Jetzt steht es auf der
Dokumentseite: *„6 gelesene Angaben noch nicht übernommen“*, mit einem Schalter, der zum
Prüfschritt führt. Am Vertrag erkennen Sie denselben Fall am erreichten Wert von 0 € und einem
Verlauf mit einem einzigen Punkt.

**Kein Vertrag gefunden?** Dann nennt die Meldung die gesuchte Nummer *und* den Haushalt, in dem
gesucht wurde. Die App sieht nie über den Haushalt hinaus, in dem Sie gerade angemeldet sind —
wer zwei Haushalte führt, prüft als Erstes, ob er im richtigen steht.

**Was schon dalag.** Wird genau dieselbe Datei ein zweites Mal eingelesen, legt die App sie nicht
noch einmal ab; sie sagt, unter welchem Beleg sie bereits liegt. Wer im Zweifel zweimal einliest,
tut also nichts kaputt.

Anders beim zweiten *Scan* desselben Blatts: das ist eine andere Datei, und sie kommt herein. Die
Zeile im Eingang trägt dann den Hinweis **„Derselbe Beleg liegt möglicherweise schon in der
Ablage“** mit einem Verweis auf den vorhandenen. Erkannt wird das daran, dass die App beiden
denselben Ablagenamen geben würde — gleiche Art, gleicher Stichtag, gleiche Zuordnung. Ein Blick
auf beide entscheidet: eine berichtigte Fassung sieht genauso aus, und die wollen Sie behalten.

### Belege aus einem überwachten Ordner

Neben dem Einlesen von Hand kann ein **Ordnerdienst** auf dem Rechner einen Scanordner
überwachen und jede neue Datei selbst hereinreichen. Was dabei ankommt, landet ebenfalls hier im
Scaneingang: abgelegt im passenden Bereich und, wo es ging, schon mit dem Objekt verknüpft.

Der Dienst **übernimmt keine Werte**. Ein erreichter Vertragswert steht erst dann im Vermögen,
wenn Sie ihn im Prüfschritt gesehen und bestätigt haben — daran ändert auch ein Ordner nichts,
der sich selbst leert. Eingerichtet wird der Dienst einmal am Rechner; wie, steht in
`src/FinanzApp.Ordnerdienst/README.md`.

## 8c. Ändern und Löschen

Alles, was sich anlegen lässt, lässt sich auch ändern und löschen. Der Weg dorthin hängt
daran, ob die Zeile schon irgendwohin führt:

- **Konto, Budget, Depot** — die Zeile antippen, das Formular öffnet sich.
- **Vorsorge, Absicherung, Fahrzeug, Immobilie** — dort führt die Zeile auf die Detailseite;
  zum Ändern gibt es rechts unter dem Betrag den kleinen Schalter **Bearbeiten**.

Das Formular ist dasselbe wie beim Anlegen, nur ausgefüllt. Oben steht, was die Änderung
bewirkt — etwa „Ein neuer Wert mit Stichtag ersetzt den bisherigen im Vermögen“.

**Gelöscht** wird ganz unten, abgesetzt durch eine Linie. Dort steht zuerst, was passieren
würde, und zwar mit echten Zahlen: „3 Buchungen hängen an diesem Konto — sie bleiben erhalten
und werden auf ‚Ohne Konto‘ gesetzt.“ Der erste Tipp fragt nach, der zweite löscht. Es gibt
keinen Systemdialog.

Buchungen löschen Sie einzeln im Kategorie-Fenster oder im Stapel über die Auswahlleiste der
Tabelle — auch dort nennt der erste Tipp die Anzahl. Bei einem **Dokument** verschwindet nur der
Eintrag; die Datei bleibt im Dokumentordner liegen.

## 9. Import

Der Import läuft in drei Schritten: **Datei wählen → liest → prüfen**. Am großen Bildschirm
steht links, was in der Datei steht — Name, Format, Zeitraum, Auszugssaldo —, rechts die
Prüfung.

Die Datei wählen Sie auf zwei Wegen: **in die Fläche ziehen** oder **die Fläche anklicken**.
Gelesen werden Kontoauszüge im Format **camt.052** und **camt.053** — die XML-Dateien, die das
Onlinebanking zum Herunterladen anbietet. CSV geht noch nicht. Wer keine eigene Datei zur Hand
hat, nimmt **Beispielauszug einlesen**.

Passt die Datei nicht, bleibt der Grund an der Fläche stehen — etwa „Kein camt.052 oder
camt.053“. Es geht nichts verloren, Sie ziehen einfach die nächste Datei hinein.

Das **Zielkonto** erkennt die App an der IBAN aus der Datei; steht dort keine, rät sie über den
Namen der Bank. Vorgeschlagen ist es nur — umstellen lässt es sich immer.

In der Prüfung wählen Sie zuerst das **Zielkonto** (das erkannte ist vorgeschlagen), darunter
stehen drei Zähler und alle Sätze einzeln:

| Zustand | Was die App vorschlägt |
| --- | --- |
| neu | angehakt — wird übernommen |
| bereits vorhanden | abgewählt, grau — dieselbe Importreferenz ist schon gebucht |
| mögliches Duplikat | abgewählt, grau — gleicher Tag, Empfänger und Betrag |
| nicht lesbar | gesperrt — daraus wird keine Buchung |
| nur vorgemerkt | gesperrt — die Bank hat den Umsatz noch nicht gebucht |

Jede Zeile lässt sich einzeln umschalten. Wer ein Duplikat zuschaltet, sieht den Zähler
„Übernehmen“ **und** den Schalter mitwandern — beide lesen dieselbe Auswahl. Ist nichts
gewählt, ist der Schalter aus.

Geprüft wird **gegen den Bestand**, nicht nur innerhalb der Datei: derselbe Auszug zweimal
eingelesen schlägt beim zweiten Mal nichts mehr vor.

Oben bleibt beim Blättern eine Leiste stehen: links steht, wie viele Sätze gewählt sind und
was noch offen ist, rechts der Importieren-Schalter und **Verwerfen**. Bei einer langen Datei
ist der Schalter damit immer erreichbar.

### Kategorien vergeben

Gefragt wird **je Empfänger**, nicht je Buchung — wer fünfmal beim selben Laden war,
beantwortet das einmal. Unter **Gruppen** (Standard) stehen drei Blöcke:

- **„N Empfänger zuordnen"** — antippen klappt die Zeile auf. Sie wählen eine Kategorie, und
  sie gilt für alle Sätze dieses Empfängers. Das Kästchen **„Regel merken"** ist angehakt:
  dann ordnet die App denselben Empfänger künftig von allein zu.
- Darunter zwei Auswege: **„Alle N als ‚Sonstiges‘"** (ohne Regel) und **„Später zuordnen"**.
  Beim zweiten wandern die Buchungen ohne Kategorie in den Bestand — die Leiste sagt Ihnen
  das auch, sie behauptet nie, alles sei zugeordnet.
- **„Automatisch zugeordnet"** zeigt, was die Regeln schon erledigt haben, je Kategorie eine
  Zeile. Der Block fehlt, wenn nichts automatisch zugeordnet wurde.

**Alle Zeilen** ist die flache Liste — dafür, einen einzelnen Satz ab- oder zuzuschalten.

Ist an der Datei nichts Neues, steht das statt leerer Blöcke da: „Nichts Neues in dieser
Datei — N Sätze erkannt, keiner neu."

### Gelernte Regeln ansehen

Unter **Mehr → Kategorien & Regeln** (oder über „Regeln ansehen" im Import) stehen alle
Regeln: Muster, Kategorie und Herkunft. Was die App beim Import gelernt hat, steht im Akzent
mit Datum; der Rest war von Anfang an dabei. Jede Zeile lässt sich löschen — der erste Tipp
fragt nach, der zweite löscht.

Eine gelöschte Regel lässt bereits importierte Buchungen unverändert; sie greift nur beim
nächsten Import.

### Kategorien anlegen, umbenennen, löschen

Unter **Mehr → Kategorien**. Oben schalten Sie zwischen **Ausgaben** und **Einnahmen** um —
das sind zwei getrennte Listen, damit bei einer Gutschrift keine Ausgabenkategorie auftaucht.

Hinter jedem Namen steht, was daran hängt: „19 Buchungen · 2 Regeln · Budget“ oder „noch nicht
verwendet“. Das ist die Auskunft, die Sie vor dem Löschen brauchen.

- **Umbenennen** ändert den Namen im Feld selbst. Er wirkt sofort überall — in Buchungen,
  Regeln, Budgets und Filtern. Die Meldung sagt, wie viele Buchungen betroffen waren.
- **Löschen** fragt einmal nach. Danach stehen die betroffenen Buchungen ohne Kategorie da
  und erscheinen im Hinweisband der Buchungsliste; Regeln auf diese Kategorie sind weg.
  Läuft ein Budget auf der Kategorie, löscht die App nicht — dann ist erst das Budget dran.
- Zwei gleiche Namen gehen nicht. Derselbe Name bei Ausgaben *und* Einnahmen schon.

### Was im Auszug stand

In der Prüfansicht des Imports öffnet **Details** an einem Satz die Felder des Auszugs —
Buchungstag und Valuta, Betrag, Gegenseite mit IBAN und BIC, Verwendungszweck, Buchungsart,
Geschäftsvorfallcode, Importreferenz. Neben jedem Feld steht klein, wie es in der Datei heißt.
Liefert die Datei ein Feld nicht, steht dort **„nicht im Auszug“** — nie ein leeres Feld.

Darunter **„Beim Import behalten“**: Verwendungszweck, IBAN und BIC der Gegenseite,
Importreferenz. Alle drei sind an. **„Für alle Sätze“** überträgt Ihre Wahl auf den ganzen
Auszug.

Die Importreferenz sollten Sie anlassen: an ihr erkennt die App beim nächsten Mal, dass ein
Satz schon gebucht ist. Ohne sie bleibt nur der Vergleich von Tag, Empfänger und Betrag.

Nach dem Import bleiben die Felder erreichbar: die Buchung antippen, dann **Auszugsdaten →
Ansehen**. Was Sie nicht behalten haben, steht dort als **„nicht gespeichert“**.

### Wenn die passende Kategorie fehlt

Neben den Kategorie-Chips steht **„+ Neue Kategorie“** (gestrichelt). Ein Klick, ein Name,
**„Anlegen & zuordnen“** — fertig: die Kategorie ist angelegt, alle Sätze des Empfängers sind
zugeordnet, und die Regel ist gemerkt. Sie müssen den Import dafür nicht verlassen; alles bisher
Zugeordnete bleibt stehen.

Ob eine Ausgaben- oder Einnahmekategorie entsteht, richtet sich nach der Gruppe — das Feld sagt
es Ihnen. Gibt es den Namen schon, wird er benutzt statt ein zweites Mal angelegt; die Meldung
lautet dann „bestand bereits“.

> **Der Import ist echt.** Er schreibt die Buchungen tatsächlich, danach stimmen die Demo-Zahlen
> nicht mehr. Wie sich der Ausgangszustand herstellen lässt, steht unter Punkt 12.

## 10. Benutzer & Anmeldung

Ein **Haushalt** besitzt die Daten, jeder Benutzer gehört genau einem Haushalt und sieht
ausschließlich dessen Bestand.

- Oben stehen die Mitglieder mit Rolle und letzter Aktivität.
- **Einladen** zeigt den gültigen Code samt Ablaufdatum; er ist **einmal** einlösbar, der
  Eingeladene registriert sich damit und landet im selben Haushalt. *Neuer Code* ersetzt den alten.
  Der Code des Demo-Haushalts lautet `HH-4K2P-9XQ1`. Einladen darf nur der Inhaber.
- **Passwort ändern** verlangt das bisherige — wer einen unbeaufsichtigten Bildschirm findet,
  soll das Konto nicht übernehmen können. Danach sind alle **anderen** offenen Sitzungen beendet,
  die eigene bleibt. Offene „Passwort vergessen“-Links werden dabei wertlos.
- **Diese Sitzung** nennt Benutzer und Anmeldezeit. Zwei Schalter beenden sie, mit einem
  Unterschied: **Benutzer wechseln** lässt das Profil in der Geräteliste stehen, **Abmelden**
  vergisst es. Beides wirkt sofort — die Sitzung liegt auf dem Server, nicht nur im Browser.

## 10b. Wer welches Konto sieht

Jedes Konto gehört einem Benutzer. Unter **Benutzer & Anmeldung → Kontofreigaben** stellen Sie
für Ihre eigenen Konten ein, wer sie sehen darf:

- **Haushalt** — alle Mitglieder.
- **Nur ich** — niemand sonst. Das Konto erscheint bei den anderen nirgends und zählt in keiner
  ihrer Summen.
- **Ein Name** — antippen gibt frei, noch einmal antippen nimmt zurück. Bleibt kein Name übrig,
  ist das Konto wieder privat.

Unter jedem Konto steht in Klartext, wen es erreicht: „alle 3 Benutzer“, „nur Oliver W.“,
„Oliver W. + Sabine K.“.

Fremde Konten stehen nicht in dieser Liste — ihre Freigabe verwaltet der jeweilige Eigentümer.
Haben Sie selbst keine eigenen Konten, sagt der Block genau das.

In der Kontenliste trägt jede Zeile, wie sie zu Ihnen steht: bei eigenen „privat“, „Haushalt“
oder „geteilt mit …“, bei fremden „geteilt von …“. Ein fremdes Konto lässt sich nicht
bearbeiten.

In der Mitgliederliste steht je Person, wie viele Konten sie sieht.

## 11. Was noch nicht geht

Diese Schalter sind vorhanden, aber noch nicht hinterlegt — sie melden es beim Antippen:

Split-Buchung · Sondertilgung planen · Aufgabe anlegen · Erinnerung setzen ·
Bei PKV nachfragen · Im Ordner suchen · Rechteverwaltung

Dazu die Einträge des Erfassen-Fensters, die nur in ihren Bereich führen (siehe Punkt 5), und
die Belegerkennung, die nichts erkennt. Beim Import werden camt.052 und camt.053 gelesen —
CSV-Dateien noch nicht.

Ganz fehlen bislang die Bereiche **Arbeit & Beruf** und **Administration** sowie die
Zwei-Faktor-Anmeldung.

## 12. Demo zurücksetzen

Alle Zahlen der Anwendung sind gerechnet, nicht gespeichert; die Beispieldaten hängen am festen
Stichtag **23.08.2026**. Wer sie durcheinandergebracht hat — etwa durch den Import — löscht

```
src/FinanzApp.Api/finanzapp.db
```

und startet die Anwendung neu. Sie legt die Datenbank samt Beispieldaten neu an.
