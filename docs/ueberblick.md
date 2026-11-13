# Überblick: Elemente, Funktionen, Abläufe

Was die Anwendung ist, woraus sie besteht und wie die Arbeit durch sie hindurchläuft.
Eine Schritt-für-Schritt-Anleitung steht in [bedienung.md](bedienung.md); hier geht es um
das Bild dahinter.

---

## 1. Was die Anwendung tut

Sie führt das Vermögen und den Papierkram eines Haushalts an einer Stelle: Konten und
Buchungen, Depot, Vorsorge- und Versicherungsverträge, Immobilien, Fahrzeuge, Darlehen,
Anschaffungen, Arbeitseinkommen, Arztrechnungen — und die Belege, die zu all dem gehören.

Zwei Leitsätze ziehen sich durch den ganzen Quelltext:

**Nichts Unbestätigtes verändert eine Vermögenszahl.** Die Anwendung liest, erkennt,
schlägt vor — übernommen wird erst, wenn ein Mensch es gesehen und bestätigt hat. Zwischen
jeder Analyse und jeder gespeicherten Zahl liegt ein Klick.

**Alles bleibt auf diesem Rechner.** Texterkennung und Sprachmodell laufen lokal. Es geht
nichts an einen Dienst im Netz, außer dem, was ausdrücklich dorthin gehört — der Bankdialog
zur eigenen Bank und die Kursabfrage.

### Die vier Teile

| Teil | Aufgabe |
|---|---|
| **FinanzApp.Api** | Der Server: Fachlogik, Datenbank (SQLite), Schnittstelle |
| **FinanzApp.Client** | Die Oberfläche — Blazor WebAssembly, läuft im Browser |
| **FinanzApp.Ordnerdienst** | Überwacht einen Scanordner und liefert ein, was dort landet |
| **FinanzApp.Texterkennung** | Liest Text aus Bildern, über die Windows-eigene Erkennung |

---

## 2. Wie die Oberfläche aufgebaut ist

Unten vier Zellen, und mehr nicht:

**Heute** · **Vorgänge** · **Bestand** · **Erfassen**

Dahinter liegen vier Gruppen, die sich auf breiten Bildschirmen als Seitennavigation
aufklappen und auf dem Telefon als Filter derselben Liste erscheinen:

- **Täglich** — Heute, Vorgänge
- **Bestand** — Konten & Buchungen, Depot, Vorsorge & Kapital, Versicherungen, Wohnen &
  Immobilien, Fahrzeuge, Anschaffungen, Darlehen, Arbeit & Beruf
- **Analyse** — Auswertungen, Budgets, Gesundheit & PKV
- **System** — Einstellungen, Kategorien, Dokumenttypen, Kategorieregeln, Benutzer

Die Ablage hat bewusst **kein eigenes Tab**: Zu den Dokumenten führt das **Suchfeld in der
Kopfzeile**. Wer einen Beleg will, sucht ihn, statt sich durch eine Ordnerstruktur zu
klicken.

---

## 3. Die Elemente — was die Anwendung kennt

Jedes dieser Objekte kann Belege tragen, taucht in der Suche auf und zählt, wo es
hingehört, ins Vermögen:

| Objekt | Was daran hängt |
|---|---|
| **Konto** | Buchungen, Bankzugang, Kontrollpunkte, Auszüge |
| **Depot** | Positionen, Abschlüsse, Depotauszüge, Kurse |
| **Vorsorge- und Versicherungsvertrag** | Beitrag, Werte, Standmitteilungen, Kündigungsfrist |
| **Immobilie** | Kaufpreis, Marktwert, Anteile, Verträge, Rechnungen |
| **Fahrzeug** | Kennzeichen, Versicherung, Werkstattbelege |
| **Darlehen** | Restschuld, Zins, Rate, nächste Fälligkeit |
| **Anschaffung** | Preis, Händler, Garantie |
| **Arbeitsverhältnis** | Brutto, Netto, Lohnabrechnungen |
| **Arztrechnung (PKV)** | Statuskette, Eigenanteil, Erstattung |
| **Vertrag / Rechnung** | Anbieter, Nummer, Fälligkeit |
| **Budget · Kategorie · Kategorieregel** | Die Ordnung über den Buchungen |
| **Dokument** | Die Datei und alles, was daraus gelesen wurde |
| **Vorgang** | Was noch zu tun ist — abgeleitet, nicht getippt |

---

## 4. Die Abläufe

### 4.1 Buchungen hereinbekommen

Zwei Türen, ein Ziel: **dieselbe Importvorschau**.

1. **Datei einlesen** — camt (`.camt052`, `.camt053`, `.xml`) oder CSV. Für CSV gibt es
   Importprofile je Bank.
2. **Bei der Bank abrufen** — FinTS. PIN eingeben, Zeitraum wählen; die Bank liefert camt,
   und das geht durch denselben Leser wie eine hochgeladene Datei.

In der Vorschau steht Satz für Satz, was passieren würde:

- **Neue Sätze** sind angehakt.
- **Bereits gebucht** ist abgewählt — erkannt an der Importreferenz, die die Bank vergibt.
- **Mögliches Duplikat** ist abgewählt, mit Grund: Tag, Empfänger und Betrag passen auf
  eine vorhandene Buchung, nur die Referenz ist neu.

Derselbe Auszug zweimal eingelesen ergibt beim zweiten Mal null Vorschläge. Übernommen wird
genau das, was angehakt dasteht — auch ein zugeschaltetes Duplikat, sonst widerspräche der
Knopf dem Kopf.

**Kategorien** vergibt die Anwendung anhand gelernter Regeln (Empfängermuster → Kategorie).
Wer eine Kategorie von Hand ändert, lehrt damit die Regel; die gelernten Regeln lassen sich
ansehen, ändern und löschen.

### 4.2 Der Kontostand

Der gerechnete Saldo ist Anfangsbestand plus Buchungen — und läuft dem tatsächlichen davon,
sobald einmal ein Auszug fehlt. Dagegen gibt es den **Kontrollpunkt**: *Zu diesem Tag war
es diese Zahl.* Ab da rechnet die Anwendung darauf weiter.

Zwei gleichberechtigte Wege hinein:

- **Aus einem Auszug**, wenn die camt-Datei einen Schlusssaldo mit Stichtag nennt — beim
  Abruf bei der Bank steht das Kästchen dafür von vornherein an.
- **Von Hand**, an jedem Konto — auch an einem ohne Bankzugang. Das ist kein Notnagel,
  sondern der einzige Weg für ein Konto, das keinen Zugang hat.

Die Differenz verschwindet dabei nicht, sie wird festgehalten: Am Punkt steht, was die
Anwendung zum selben Stichtag gerechnet hatte. Zurücknehmen geht jederzeit.

**Einen dritten Weg gab es einmal**, und er ist aus einem Grund weg, der für die ganze
Rechnung zählt: Die reine Saldenabfrage bei der Bank (FinTS-Geschäftsvorfall HKSAL) liefert
über die verwendete Bibliothek **kein Datum**. Die Anwendung stempelte den Wert mit der
eigenen Uhr — und ein vormittags gelesener Zwischenstand galt der Rechnung damit als Stand
des *ganzen* Tages. Was die Bank nachmittags noch buchte, fiel still heraus und blieb
draußen. Ein Schlusssaldo aus dem Auszug hat dieses Problem nicht: Er nennt den Tag, auf den
er sich bezieht.

Wer einen Stand von Hand einträgt, kann denselben Fehler erzeugen — deshalb sagt die Maske
es beim heutigen Datum dazu.

### 4.3 Vorgänge — was noch offen ist

Vorgänge werden **abgeleitet, nicht getippt**. Sie entstehen beim Öffnen der Liste aus dem
Bestand:

| Quelle | Entsteht aus |
|---|---|
| Kündigungsfrist | Vertragsende minus Frist |
| Rechnungsfälligkeit | einer erfassten Rechnung |
| Erstattung überfällig | einer eingereichten Arztrechnung |
| Arztrechnung offen | einer erfassten, nicht eingereichten Rechnung |
| Werte übernehmen | einem Beleg, dessen gelesene Angaben noch niemand übernommen hat |
| Forderung aus einem Schreiben | einem Satz auf dem Papier |

Jeder Vorgang **nennt seinen Grund**. Bei einem Schreiben steht die Zeile dabei, aus der die
Forderung gelesen wurde — wer sie liest, prüft in zwei Sekunden, ob richtig gelesen wurde,
ohne den Beleg zu öffnen.

**Geschlossen** wird ein Vorgang dort, wo sein Gegenstand sich ändert: Die Rechnung wird
bezahlt, der Vertrag läuft aus, die Erstattung kommt an. Abhaken von Hand gibt es nur, wo
kein Objekt den Stand trägt — bei einem Schreiben und bei einer selbst angelegten Aufgabe.

**Der Zahlungsabgleich** schließt Vorgänge aus Schreiben von allein, wenn die Zahlung
zweifelsfrei dasteht:

- Der Betrag stimmt auf den Cent — gelesen aus der Forderungszeile, und wenn die keinen
  nennt („Dieser Betrag ist am … fällig"), aus der beschrifteten Betragszeile des Belegs.
- Die Buchung liegt im Zeitraum: vom früheren von Belegdatum und Frist 90 Tage zurück,
  30 Tage über die Frist hinaus.
- **Eine Nummer aus dem Belegtext steht im Verwendungszweck** — Aktenzeichen, Rechnungs-
  oder Steuernummer.

Nur wo genau ein so belegter Treffer steht, wird zugeordnet und geschlossen, und der Beleg
hängt sich dabei an die Buchung. Alles andere erscheint als **Vorschlag** unter der Zeile,
mit Datum, Betrag, Empfänger und dem, was dafür spricht — zum Annehmen mit einem Klick. Der
Grund steht danach im Vorgang: *„bezahlt: Buchung vom …, erkannt an …"*.

Warum so streng: Betrag, Tag und Empfänger trennen zwei gleichartige Bescheide nicht. Ohne
die Nummer ordnete die Anwendung sie womöglich über Kreuz zu — und das fiele nie auf, weil
die Summe stimmt.

### 4.4 Gesundheit & PKV

Eine Arztrechnung läuft eine Kette entlang: **erfasst → eingereicht → Abrechnung erhalten →
Zahlung eingegangen → abgeschlossen**, mit den Ausgängen *teilerstattet* und *abgelehnt*.

Die Regel, die überall durchschlägt: **Der Eigenanteil ist keine offene Forderung.** Er ist
eine gebuchte Ausgabe. Offen ist allein die erwartete Erstattung, und auch die nur, solange
keine Zahlung zugeordnet ist. Passende Buchungen schlägt die Anwendung vor; zugeordnet wird
erst auf Bestätigung.

Eine falsch erfasste Rechnung lässt sich **löschen**, solange keine Buchung daran hängt —
mitsamt ihrem Vorgang. Hängt eine Buchung daran, wird abgewiesen: Sie bliebe auf dem Konto
stehen, und nichts erklärte sie mehr.

### 4.5 Vermögen und Auswertungen

Die Startseite rechnet das Gesamtvermögen netto: Finanzvermögen plus Sachwerte minus
Verbindlichkeiten. Darunter Einnahmen, Ausgaben, Sparquote, Budgets.

**Der Saldo entsteht an einer Stelle.** Wer ihn anderswo noch einmal zusammenrechnet,
bekommt irgendwann eine zweite Wahrheit — zwei Seiten, zwei Zahlen. Deshalb fragen Bestands-
und Übersichtsseiten denselben Dienst.

Auswertungen gibt es nach Kategorie, Zeitraum und Vergleichszeitraum, dazu Liquidität
(„Wohin fließt es", „Sparpotential") und Steuerjahre.

**Fixkosten lassen sich nach Kostenart oder nach Konto gliedern.** Die erste Frage lautet
*wofür* — was könnte gekündigt werden; die zweite *von wo* — welches Konto muss jeden
Monat gedeckt sein. Das Konto steht am Vertrag, an der Police und am Darlehen; wo keines
eingetragen ist, steht der Posten unter „Ohne Konto" und wird nicht geraten.

**Haushaltskonto** ist die Kontosicht auf ein Gemeinschaftskonto: wer wie viel eingebracht
hat, wofür es wieder abgegangen ist, was davon gebunden ist. Wessen Beitrag ein Zufluss
ist, wird **gepflegt und nicht abgeleitet** — entweder an der Buchung als Einlage einer
Person, oder an einer Einnahmekategorie, an der eine Person eingetragen ist. Der zweite Weg
trägt weiter: Was die Kategorieregeln künftig zuordnen, ist damit zugleich zugeordnet.

Am Zahlernamen wird nichts erkannt. Derselbe Mensch heißt im Kontoauszug in mehreren
Schreibweisen, und wer eine Erstattung empfängt, hat nichts eingebracht. Was weder noch
trifft, steht als „noch nicht zugeordnet" mit Anzahl und Betrag neben den Anteilen — ohne
diese Zeile läse sich eine halb gepflegte Zuordnung wie ein Befund über zwei Menschen.

---

## 5. Dokumente bearbeiten und verwalten

### 5.1 Drei Wege herein

**Hochladen** — Datei wählen, fertig. Für den Einzelfall.

**Der Prüfplatz** (*Beleg einlesen*) — die Anwendung liest das PDF, schlägt Dokumenttyp,
Zielobjekt und Ablage vor und zeigt die gelesenen Werte **neben der Seite, auf der sie
stehen**. Übernommen wird, was angehakt ist. Das ist der Weg für Belege, aus denen Zahlen
ins Vermögen wandern — eine Standmitteilung, eine Vermögensübersicht, eine Arztrechnung.

**Der Ordnerdienst** — ein überwachter Ordner. Was dort landet, wird eingeliefert und
danach in einen `_erledigt`-Unterordner weggeräumt. Der Dienst arbeitet den Ordner **reihum
durch** statt auf Ereignisse zu horchen: Die Ordnerüberwachung von Windows verliert
Ereignisse, wenn ihr Puffer überläuft, und auf Netzlaufwerken bekommt sie manche nie zu
sehen. Die Nachlese ist der eigentliche Antrieb, die Überwachung nur der Wecker.

Was der Dienst darf, wenn niemand zusieht: **ablegen und zuordnen, aber nichts übernehmen.**
Bereich, Ordner, Dokumenttyp und die Verknüpfung zum Objekt sind Einordnung — sie lassen
sich ansehen und mit einem Griff ändern. Ein erreichter Wert in einem Vertrag ist das
nicht. Deshalb endet dieser Weg immer im Scaneingang.

### 5.2 Was beim Einliefern geschieht

1. **Fingerabdruck** über den Inhalt. Ein Beleg, der schon da ist, wird als Duplikat
   erkannt, auch unter anderem Dateinamen.
2. **Text gewinnen** — aus der Textebene des PDF, sonst über die Texterkennung. Am Beleg
   steht, woher der Text stammt: Textebene, Erkennung, oder *Bild ohne Erkennung*.
3. **Lesen** — Absender, Belegdatum, Belegnummer, Beträge; die Werte an ihren
   Beschriftungen, nicht an ihrer Position auf dem Blatt.
4. **Einordnen** — Bereich (Versicherung, Gesundheit, Wohnen, Arbeit, Finanzen, Fahrzeug,
   Persönliches, Sonstiges), Dokumenttyp, Zielobjekt.
5. **Ablegen** — in einen Ordner je Bereich, unter einem gebauten Dateinamen aus Art, Objekt
   und Datum: `Arztrechnung_Musterklinik_2027-03-09.pdf`. In der Datenbank steht nur der
   **relative** Pfad; ein absoluter bände die Daten an einen Rechner.
6. **Forderung suchen** — verlangt das Schreiben etwas? Dazu unten mehr.

### 5.3 Der Scaneingang als Schwelle

Gescannt wird stapelweise, eingeordnet wird später. Ein Beleg bleibt im Eingang stehen, bis
**Typ und Objekt** bestätigt sind. Ohne diese Schwelle verschwände er in der Ablage, ohne
dass jemand entschieden hätte, wozu er gehört — und genau solche Dokumente findet später
niemand wieder.

Der Eingang zeigt je Beleg den Absender, die erkannten Seiten und den Vorschlag. Wird ein
Beleg einem Objekt zugeordnet, **lernt die Anwendung die Ablageregel**: Absendermuster →
Ziel. Der nächste Brief desselben Absenders kommt schon zugeordnet an.

Duplikate werden dort ebenfalls erkannt und als solche gekennzeichnet, statt still ein
zweites Mal abgelegt zu werden.

### 5.4 Was sich am Beleg ändern lässt

Auf der Dokumentseite:

- **Angaben** — Titel, Dokumenttyp, Dokumentdatum, Gültigkeit (von/bis), Schlagworte
- **Sichtbarkeit** — *den Haushalt* oder *nur mich*
- **Verknüpfungen** — Objekte anhängen und wieder lösen; ein Beleg kann an mehreren hängen
  (etwa an der Immobilie *und* an der Buchung, die ihn bezahlt hat)
- **Ablage berichtigen** — Bereich und Pfad; die Datei wird dabei mitbewegt
- **Umbenennen** — ein Scannername wie `Scannen0013` wird durch den gebauten Namen ersetzt
- **Als offenen Posten erfassen** — wenn das Schreiben etwas verlangt
- **Öffnen und Herunterladen**
- **Löschen** — nur der Eintrag verschwindet, die Datei bleibt im Dokumentordner liegen.
  Daneben steht, wie viele Verknüpfungen entfallen.

Am Datenmodell steht außerdem ein **Status** (*aktiv · abgelaufen · archiviert*). Er ist
vorbereitet, aber noch ohne Funktion: Jeder Beleg wird als *aktiv* angelegt, und keine
Oberfläche ändert das bisher.

### 5.5 Gelesene Werte übernehmen

Was die Anwendung aus einem Beleg gelesen hat, steht als Liste daran — mit Beschriftung,
Wert, Seitenzahl und einem Sicherheitsmaß. Übernommen wird angehakt und einzeln.

Solange an einem Beleg Werte stehen, die niemand übernommen hat, führt die Anwendung dafür
einen **Vorgang**. Sonst verschwände der Beleg aus dem Eingang, und danach zeigte nichts
mehr auf seine offenen Zahlen.

### 5.6 Forderungen erkennen

Zwei Schichten:

1. **Leseregeln** erkennen eine Forderung an ihren Wörtern — *fällig*, *zahlbar bis*,
   *innerhalb von 14 Tagen*.
2. **Ein lokales Sprachmodell** findet, was frei formuliert ist: *„Bitte stellen Sie Ihren
   Lastschrifteinzug auf das neue Konto um"* enthält keines dieser Wörter und verlangt
   trotzdem etwas.

**Das Modell findet, es entscheidet nicht.** Es gibt eine Zeile zurück, nie einen Wert. Ob
die Zeile echt ist, wird am gespeicherten Text geprüft; was dort nicht vorkommt, wird
verworfen. Gefundenes ist als *vom Sprachmodell gefunden* gekennzeichnet — die Zeile ist so
geprüft wie jede andere, schwächer ist nur der Grund, sie für eine Forderung zu halten.

Eine Frist aus einer Spanne („innerhalb von 14 Tagen") wird gerechnet und als gerechnet
gekennzeichnet. *„Sofort"* bleibt ohne Termin: Das ist keine Frist, sondern eine Haltung.

### 5.7 Nachlauf

Für Belege, die vor einer Neuerung abgelegt wurden, gibt es Nachläufe, die in kleinen
Schritten arbeiten:

- **Text nachtragen** für Belege ohne durchsuchbaren Text — die Liste sagt, wie viele es
  noch sind
- **Datum und Namen nachziehen** für Belege, die vor der automatischen Benennung abgelegt
  wurden

---

## 6. Dokumente suchen und finden

### 6.1 Ein Feld für alles

Das Suchfeld steht in der Kopfzeile und durchsucht **Dokumente und Objekte zugleich**. Wer
„Hausrat" eintippt, meint meistens den Vertrag und nicht den Dateinamen — eine Suche, die
nur Dateien fände, ginge am Zweck vorbei.

### 6.2 Worin ein Dokument gefunden wird

| | |
|---|---|
| **Titel** | wie er am Beleg steht |
| **Dateiname** | der gebaute wie der ursprüngliche |
| **Dokumenttyp** | „Arztrechnung", „Bescheid", … |
| **Verknüpftes Objekt** | wer nach dem Fahrzeug sucht, findet seine Werkstattrechnung |
| **Schlagworte** | die am Beleg gesetzten |
| **Dokumentdatum** | in allen Schreibweisen, siehe unten |
| **Volltext** | der gesamte gelesene Text des Belegs |

**Das Datum in allen Schreibweisen.** Es gibt keinen Grund anzunehmen, dass jemand die Form
trifft, in der die Datenbank das Datum führt. Gesucht wird deshalb gegen alle:

```
2027-03-09   09.03.2027   03/2027   2027-03   März 2027
```

Das bloße Jahr trifft jeden Beleg dieses Jahres — gewollt: „2024" ist eine Jahresabfrage und
keine zufällige Zeichenfolge.

**Der Volltext** wird in der Datenbank durchsucht, nicht in der geladenen Liste: Der Text
bleibt, wo er hingehört, statt bei jedem Aufruf über die Leitung zu gehen.

### 6.3 Objekte werden mitgefunden

Dieselbe Eingabe trifft elf Zielarten: Police, Vertrag, Immobilie, Konto, Anschaffung,
Fahrzeug, Depot, Darlehen, Rechnung, Arztrechnung, Buchung. Gesucht wird
jeweils in Bezeichnung **und** Untertitel, und der Untertitel wird aus allen Rohfeldern
gebaut — damit findet die Suche auch, was nur dort steht, etwa eine Vertragsnummer oder ein
Kennzeichen.

Gefundene Objekte stehen über den Dokumenten, mit ihrer Art daneben, und führen direkt zum
Objekt.

Was die Anwendung verknüpfen und beschriften kann, muss sie auch finden können — sonst ist
eine Zielart für den Prüfplatz praktisch nicht vorhanden. Das war mehrfach die Ursache dafür,
dass ein Beleg im Eingang liegenblieb, obwohl sein Objekt danebenstand.

### 6.4 Filter statt Ordnerbaum

Neben dem Suchfeld stehen Filter:

- **nach Bereich** — Versicherung, Gesundheit, Wohnen, Arbeit, Finanzen, Fahrzeug,
  Persönliches, Sonstiges
- **ohne Objekt** — was abgelegt ist, aber an nichts hängt. Eine Sicht auf die Ablage, kein
  Fehlerhinweis; ohne Treffer gibt es diesen Filter gar nicht.

Dazu melden Hinweiszeilen, wenn Belege ohne durchsuchbaren Text oder mit fehlender Datei
vorliegen.

### 6.5 Wo Dokumente sonst auftauchen

Ein Beleg muss nicht gesucht werden, wenn man ohnehin beim Objekt steht: An jedem Konto,
Vertrag, Fahrzeug, an jeder Immobilie und an jeder Buchung stehen die verknüpften Belege.
Der Weg läuft in beide Richtungen — die Buchung zeigt das Papier, das sie verlangt hat, und
das Papier zeigt die Zahlung, die es erledigt.

### 6.6 Grenzen

- **Umlaute in der Volltextsuche.** `LIKE` in SQLite ignoriert Groß- und Kleinschreibung nur
  bei ASCII-Zeichen: „rechnung" findet „Rechnung", „über" findet „Über" **nicht**. Eine
  Einschränkung, keine Absicht.
- **Kein Text, kein Volltexttreffer.** Belege ohne gelesenen Text sind nur über Titel,
  Dateiname, Typ, Verknüpfung, Schlagworte und Datum zu finden.
- **Platzhalter sind entschärft.** Wer nach „50 %" sucht, meint das Zeichen und nicht
  „irgendetwas".

---

## 7. Was die Anwendung bewusst nicht tut

- **Keine TAN entgegennehmen.** Verlangt die Bank eine Freigabe, endet der Abruf mit einer
  Meldung.
- **Keine Zahlungen auslösen.** Sie liest Konten, sie bedient sie nicht.
- **Nichts in die Cloud geben.** Texterkennung und Sprachmodell laufen lokal.
- **Keine Zahl übernehmen, die niemand gesehen hat.** Auch nicht aus dem Ordnerdienst, auch
  nicht aus einem Abruf.
- **Keinen Vorgang schließen, ohne sagen zu können warum.** Ein Vorgang, der von selbst
  zuging und niemandem Rechenschaft gibt, ist schlimmer als einer, der offen blieb.

---

## 8. Mehrere Benutzer

Die Daten gehören einem **Haushalt**; Mitglieder kommen über einen Einladungscode hinzu.
Ein Konto ist entweder privat, dem Haushalt zugänglich oder einzeln freigegeben — und die
Sichtbarkeit zieht sich durch alles, was daran hängt: Buchungen, Kontrollpunkte,
Auswertungen. Ein privates Konto des anderen Mitglieds steht nicht einmal in der Abfrage,
also auch nicht in einem Suchvorschlag.

Dokumente gehören dem Haushalt, solange niemand sie auf *nur ich* stellt.

Drei Rollen:

| Rolle | Darf |
|---|---|
| **Eigentümer** | alles, dazu Benutzer und Einladungen verwalten |
| **Mitglied** | alle Daten des Haushalts ändern |
| **Nur lesen** | sehen, nichts ändern — gedacht für das Steuerbüro |

Wer nicht schreiben darf, sieht die Schaltflächen dafür gar nicht erst.
