# Handoff: Gemeinsame Immobilie zu zwei — Eigentum, Einlagen, Unterhalt

Stand 01.09.2026 · Repo `OliverKielmayer/FinanzApp` · **ergänzt** `design_handoff_v5/` · Wireframes: `Wireframes Gemeinschaftsimmobilie.dc.html` (Optionen 4a–4e)

Zwei Personen kaufen eine Immobilie zur Eigennutzung, Eigentum 50/50, ungleiches Eigenkapital, der Rest finanziert. Ein gemeinsames Haushaltskonto, in das beide einzahlen und von dem alle gemeinsamen Ausgaben abgehen. Gefragt ist: welchen Anteil trägt jeder an Finanzierung und Unterhalt, was kostet das Objekt, wie steht die Finanzierung.

**Kein neuer Navigationspunkt.** Fünf Eingriffe in Bestehendes.

---

## 1. Das Modell: drei Größen, die auseinanderfallen

| Größe | Beispiel | Eigenschaft |
| --- | --- | --- |
| **Eigentumsanteil** | 50 / 50 | steht im Grundbuch, ändert sich nicht |
| **Eingebrachtes Eigenkapital** | 90.000 / 50.000 | einmalig beim Kauf, ungleich |
| **Laufende Einlagen** | 1.500 / 1.500 €/Monat | schwankt, verschiebt den Stand weiter |

Nur die erste ist fix. Wer sie mit den anderen verwechselt, beantwortet „wer hat mehr getragen" mit dem Grundbuch — und das ist falsch.

### Die Rechnung beim Kauf

Kaufpreis 420.000 €, Eigenkapital 140.000 €, Darlehen 280.000 €, Eigentum 50/50.

| | Oliver | Sabine |
| --- | --- | --- |
| Anteil am Kaufpreis | 210.000 | 210.000 |
| Eigenkapital eingebracht | 90.000 | 50.000 |
| Haftung Darlehen (gesamtschuldnerisch) | 140.000 | 140.000 |
| **Zusammen aufgebracht** | **230.000** | **190.000** |
| **Ausgleich** | **+20.000** | **−20.000** |

Der Ausgleich ist die **Hälfte** der Eigenkapitaldifferenz (40.000 / 2), weil das Eigentum halbe-halbe ist.

---

## 2. Entschieden

- **Weg 1: der Ausgleich bleibt stehen.** Das Darlehen läuft 50/50 wie im Vertrag; der Stand wird als Forderung geführt und wächst oder schrumpft monatlich mit den Einlagen. Verworfen: die Darlehensquote zu verschieben (57/43) — bei gesamtschuldnerischer Haftung führte die App damit eine Zahl, die die Bank nicht kennt.
- **Die Forderung ist eine Vermögensposition.** Bei Oliver +20.150 €, bei Sabine −20.150 €. Ohne sie zählt Olivers Vermögen 20.150 € zu wenig und Sabines 20.150 € zu viel.
- **Instandhaltung sind Kosten, kein Sparen.** Ein Dach, das in zwölf Jahren neu muss, kostet jedes Jahr ein Zwölftel. Die Rücklage geht in Objektkosten und €/m² ein, nicht in die Sparquote — auch wenn das Geld noch auf dem Konto liegt.
- **Keine Verzinsung** des Ausgleichs.
- **Beim Verkauf: erst Erlös nach Eigentumsanteil teilen, dann verrechnen.** Die Reihenfolge ist keine Formsache — umgekehrt käme ein anderes Ergebnis heraus.

---

## 3. Fünf Eingriffe

### 3.1 Immobilie → Abschnitt „Beteiligung" (Wohnen)

Neue Felder am Objekt: **Eigentumsanteile** je Person in Prozent (Summe muss 100 % sein, sonst speichert die App nicht), **Eigenkapital beim Kauf** je Person mit Belegbezug, **Haftungsquote Darlehen** (fest auf „wie Eigentum", weil gesamtschuldnerisch).

Der Kopf zeigt zusätzlich zur Objektsicht die **eigene** Sicht: Anteil Wert, Anteil Schuld, und was davon ins Vermögen zählt. Beispiel: Marktwert 420.000 €, Restschuld 275.100 € → netto 144.900 €, davon **72.450 € eigener Anteil** — „nicht 144.900 €, die andere Hälfte gehört Sabine".

### 3.2 Vermögen → fünfte Größe (der Eingriff mit der größten Reichweite)

Das Vermögensmodell aus §3b führt drei Größen: Finanzvermögen, Sachwerte, Verbindlichkeiten. Es kommt eine vierte hinzu — **Forderungen und Schulden zwischen Beteiligten** — und zwei bestehende ändern ihre Berechnung:

| Zeile | vorher | jetzt |
| --- | --- | --- |
| Finanzvermögen | 125.840 € | unverändert |
| Sachwerte | voller Objektwert | **nur eigener Anteil** — 210.000 statt 420.000 |
| Verbindlichkeiten | volle Restschuld | **nur eigener Haftungsanteil** — −137.550 statt −275.100 |
| Forderung an Beteiligte | — | **+20.150 €** (abgeleitet) |
| **Gesamtvermögen netto** | | **218.440 €** |

Regeln:

- Die Quote wirkt **an einer Stelle** und schlägt überall durch — Dashboard-Hero, Bestand-Kopf, Vermögensbericht (§3b: eine Zahl, drei Flächen).
- Die Forderung ist **abgeleitet, nicht erfasst**. Quelle sind Eigenkapital und Einlagen; sie darf nirgends von Hand gesetzt werden.
- Im Vermögensbericht nach Klasse (§17.1) ist sie eine eigene Klasse mit dem Kennzeichen **gemessen** (sie ist errechnet, nicht fortgeschrieben).

### 3.3 Konto → vierte Freigabestufe „Gemeinschaftskonto"

§6c gibt jedem Konto einen Eigentümer und eine Freigabe (Haushalt / Nur ich / namentlich). Es kommt eine vierte Stufe dazu: **mehrere Beteiligte mit Einzahlungssoll** je Person und Termin.

Der Kontoschirm vergleicht Soll und Eingang je Monat („Oliver 1.500 € ✓ · Sabine 1.200 €, 300 € unter Soll") und nennt den Jahresstand. **Er mahnt nicht — er sagt, was steht.**

Alles darüber bleibt unverändert: Buchungsliste, Filter, Summen, Import, Freigabelogik.

### 3.4 Buchungsart „Einlage" und Kennzeichen „objektbezogen"

**Einlage** kommt neben Ausgabe, Einnahme und Umbuchung. Sie ist keine Einnahme (kein Zufluss von außen) und keine Umbuchung (der Eigentümer wechselt). Sie zählt in die Beteiligungsrechnung und **nicht** in Einnahmen, Sparquote oder Liquidität.

Die Import-Regelmechanik aus §8c lernt sie wie jede Kategorie — „SABINE K auf dem Haushaltskonto → Einlage" ist eine Regelzeile, kein neues System.

**Kennzeichen „objektbezogen"** an Kategorie und Vertrag: trennt Hauskosten von Lebenshaltung. Ohne es wäre jede €/m²-Zahl falsch, weil Lebensmittel vom selben Konto abgehen.

### 3.5 Auswertungen → achter Bericht „Objekt & Beteiligung"

Löst den offenen Bericht „Objektkosten" aus §10b mit. Drei getrennte Aussagen:

**1. Was kostet das Objekt** — zwei Kacheln, beide benannt: **angefallen seit Kauf** (14.200 €, 5 Monate) und **hochgerechnet aufs Jahr** (34.080 €, 2.840 €/Monat, 18,20 €/m², inkl. 500 € Rücklage). Darunter die Posten mit der Spalte **Art**: Zins ist Aufwand, **Tilgung ist Vermögensaufbau**. Von 18.000 € Jahresrate (1.500 €/Monat) sind nur 35 % wirkliche Kosten; die übrigen 65 % (980 €/Monat) tilgen.

**2. Was ist objektbezogen** — der Ausschlussblock nennt, was bewusst fehlt: Lebensmittel, Freizeit, Mobilität. Dazu die Trennung, die zwei Zahlen auseinanderhält:

> Kontoabfluss 2.960 €/Monat = 2.340 € objektbezogen + 620 € übrige gemeinsame. Die 500 € Rücklage zählen zu den Objektkosten (2.840 €), verlassen das Konto aber nicht.

**3. Wer hat wie viel getragen** — Eigenkapital plus Einlagen gegen den Grundbuchanteil:

| | Oliver | Sabine |
| --- | --- | --- |
| Eigenkapital beim Kauf | 90.000 | 50.000 |
| Einlagen 2026 (5 Monate) | 7.500 | 7.200 |
| **Zusammen eingebracht** | **97.500** | **57.200** |
| Anteil laut Grundbuch | 50 % | 50 % |
| **Ausgleich** | **+20.150** | **−20.150** |

Der Proportionsbalken darüber zeigt **63 / 37** — das Verhältnis des Eingebrachten, nicht den Eigentumsanteil, und er ist beschriftet. Kachel, Tabellenzeile und Klartextsatz sind **eine** abgeleitete Größe.

Rahmen wie bei allen Berichten: Zeitraum, Vergleich, gespeicherte Ansicht, CSV, Druckblatt.

---

## 4. Was bewusst nicht gebaut wird

Keine WG-Abrechnung, keine Mahnungen, keine Aufteilung jeder Einzelbuchung nach Kopf. Die App **führt** den Ausgleichsstand als Forderung im Vermögen — sie mahnt ihn nicht an, verzinst ihn nicht und treibt ihn nicht ein.

---

## 5. Reihenfolge

1. **Eigentumsanteile und Haftungsquote** am Objekt — zuerst, weil sie das Vermögen korrigieren. Solange sie fehlen, zählt der volle Objektwert bei einer Person.
2. **Vermögensmodell auf vier Größen** (Finanzvermögen, Sachwerte, Verbindlichkeiten, Forderungen) mit einer Quelle für alle Flächen.
3. **Gemeinschaftskonto** mit Einzahlungssoll und **Einlage** als Buchungsart, dazu das Kennzeichen *objektbezogen*.
4. **Bericht** — er braucht 1 bis 3.

---

## 6. Regeln, die dieser Entwurf durchgesetzt hat

Fünf Korrekturrunden am Wireframe, jede dieselbe Klasse — Zahlen, die einander widersprechen. Für die Umsetzung:

1. **Eine Größe, eine Quelle.** Der Ausgleich steht an vier Stellen desselben Screens; er wird einmal gerechnet. Dasselbe gilt für Restschuld, Objektkosten und Kontoabfluss.
2. **Jede Zahl sagt, was sie ist.** „Angefallen" gegen „hochgerechnet", „gemessen" gegen „fortgeschrieben" (§17.1). Eine Zwölfmonatssumme darf nicht als Jahresstand beschriftet werden, wenn fünf Monate erfasst sind.
3. **Jeder Balken nennt seine Bezugsgröße.** Ein unbeschrifteter Balken, der 63/37 zeigt, während der Nutzer 50/50 erwartet, ist ein Widerspruch — auch wenn beide Zahlen stimmen.
4. **Abgeleitete Werte müssen herleitbar sein.** Restschuld = Darlehen − Tilgung × Monate. Wer eine Zahl nicht aus den Nachbarzahlen nachrechnen kann, misstraut ihr zu Recht.
5. **Objektkosten ≠ Kontoabfluss.** Zwei verschiedene Größen dürfen nie dieselbe Zahl tragen, auch wenn sie zufällig ähnlich sind.

---

## 7. Offen

- **Verkauf** ist als Regel entschieden, aber nicht gestaltet — kein Screen für Erlösaufteilung und Schlussverrechnung.
- **Mehr als zwei Beteiligte** ist im Modell möglich (Anteile als Liste), aber nicht durchgezeichnet.
- **Trennung** — was passiert mit Anteilen, Forderung und Gemeinschaftskonto, wenn einer aussteigt. Berührt den offenen Punkt „Unterhalt / Scheidung" aus der Dateiablage.
- **Steuerlich** bleibt es außen vor: bei Eigennutzung sind Darlehenszinsen nicht absetzbar (steht so im Steuerjahr-Bericht, §15).

---

## 8. Im Prototyp gebaut (01.09.2026)

Die drei Kernscreens sind in `FinanzApp v5.dc.html` umgesetzt — damit liegt neben den Wireframes eine hochauflösende Vorlage in Industry vor.

**Immobilie → Beteiligung** (Wohnen-Screen, über dem Darlehen): Proportionsbalken mit Anteilen und Beschriftung, zwei Blueprint-Kacheln „Dein Anteil Wert" / „Dein Anteil Schuld", darunter „Zählt in deinem Vermögen" mit der Herleitung der Restschuld (280.000 − 5 × 980), und der Ausgleichsstand als Akzentzeile mit Sprung in den Bericht.

**Haushaltskonto** (Konten-Screen, unter der Kontoliste): Tag „Gemeinschaftskonto", Soll gegen Abfluss mit der objektbezogenen Teilmenge, je Beteiligter Soll-Erfüllung mit Datum, und die Regel, dass Einlagen nicht in Einnahmen, Sparquote oder Liquidität zählen.

**Buchungsart „Einlage"** (Erfassen): vierter Chip neben Ausgabe, Einnahme, Umbuchung. Die Erklärzeile steht unter dem Betrag; Kategorien werden übersprungen wie bei Umbuchung.

**Bericht „Objekt & Beteiligung"** (Auswertungen, dritter Reiter): drei Kacheln (angefallen / hochgerechnet / Ausgleichsstand), Postentabelle mit Art-Tag (Zins = Aufwand, Tilgung = Vermögen im Akzent), die drei Notizzeilen (Rate, Objektkosten ≠ Kontoabfluss, Rundung), Proportionsbalken mit Bezugsgröße, Beteiligungstabelle mit hervorgehobener Ausgleichszeile, Akzentblock mit dem Stand, Verkaufsregel und Verrechnungsknopf.

Alle Zahlen kommen aus **einer** Struktur (`SHARE`) — Anteile, Eigenkapital, Einlagen, Darlehensstand, Tilgung, Soll, Abfluss, Rücklage. Ausgleich, Restschuld, Anteilswerte und Balkenverhältnisse sind daraus abgeleitet, nirgends doppelt geschrieben.

---

## 9. Wie der Prototyp die Zahlen führt (verbindlich für die Umsetzung)

Sieben Korrekturrunden am Prototyp, jede dieselbe Ursache: eine Größe, die an zwei Stellen unterschiedlich gerechnet wurde. Die Struktur, die am Ende hält:

### 9.1 Zwei Schuldgrößen, zwei Namen

Der schwerste Fehler war, `debt` von „volle Restschuld" auf „mein Haftungsanteil" umzudefinieren und den Namen zu lassen. Der Widerspruch wanderte damit nur — vorher zeigte die Bilanz zu viel, danach der Darlehenschirm zu wenig.

| Größe | Wert | Wer liest sie |
| --- | --- | --- |
| `debt` — volle Restschuld | 148.300 € | Darlehen-Detailschirm (passt zum Tilgungsplan), Objektzeile in Wohnen, Bestand-Klasse Darlehen |
| `myDebt` — eigener Haftungsanteil | 74.150 € | Bilanzzeile „Verbindlichkeiten", Beteiligungsblock am Objekt, Vermögensbericht |

**Regel: eine Konstante trägt eine Bedeutung.** Wenn eine Quote eingeführt wird, entsteht eine *zusätzliche* benannte Größe — die bestehende behält ihren Sinn.

### 9.2 Was `SHARE` führt — und was nicht

`SHARE` trägt **ausschließlich** die Beteiligungsdaten: Personen mit Anteil, Eigenkapital und Einlagen, Kaufdatum, Zeitfenster, Einzahlungssoll, Kontoabfluss, Rücklage, dazu Rate/Zins/Tilgung des verknüpften Darlehens als Referenz.

Nicht in `SHARE`: Restschuld, Objektwert, Wohnfläche, Objektkosten, Vertragsbeträge. Die kommen aus dem Objekt und seinen Verträgen. Beim ersten Bau führte `SHARE` eigene Darlehenswerte (280.000 €, 980 €/Monat) neben denen des Objekts (148.300 €, Rate 1.180 €) — der Beteiligungsblock mischte beide Basen, und „Zählt in deinem Vermögen" war die Differenz zweier unvereinbarer Zahlen.

### 9.3 Eine Postenliste für zwei Anzeigen

`obRows0` (Zins, Tilgung, Verträge, Absicherung, Rücklage) und ihre Summe `obTotal` sind die einzige Quelle für: die Kacheln des Berichts, die Postentabelle, die Rundungssumme, **„Kosten 12 Monate" auf dem Wohnen-Screen** und die objektbezogene Teilmenge im Gemeinschaftskonto-Block. Vorher rechnete jede Stelle selbst.

Abgeleitet, nie als Literal: die Anteilsspalte und ihre Rundungssumme (Hinweis entfällt bei genau 100 %), €/m² aus der Wohnfläche des Objekts (fehlt sie, entfällt die Angabe), das Zeitfenster aus `SHARE.months`.

### 9.4 Der Vermögensbericht braucht dieselbe Quote

Der Bericht nach Klasse (§17.1) rechnet eine eigene Zeitreihe. Wird die Quote nur auf die Gegenwart angewandt, entsteht ein **erfundener Einbruch** — im Prototyp waren es 174.500 €, ausgewiesen als „Wert übernommen, nicht neu bewertet".

Deshalb: **historische Punkte und Live-Punkt laufen durch dieselbe Quotenfunktion**, und die Forderung ist eine eigene Klasse mit Kennzeichen *gemessen* (sie ist errechnet, nicht fortgeschrieben). Beide Summenfunktionen zählen sie mit.

### 9.5 Ein Datum für „heute"

Eine `TODAY`-Konstante speist Vermögensbericht-Kopf, Druckblatt und den Ausgleichsstand. Der Ausgleich ist ausdrücklich eine laufend abgeleitete Größe — sein Stichtag muss der Stichtag der App sein, kein eigener.

### 9.6 Was das für die Umsetzung heißt

Ein Endpoint liefert das Beteiligungsaggregat: Anteile, Eigenkapital, Einlagen, abgeleiteter Ausgleich, Objektkostenposten mit Summe, und die vier Vermögensgrößen. **Kein Screen rechnet selbst.** Die Zahlen, die im Prototyp siebenmal auseinandergelaufen sind, laufen in einer Implementierung mit mehreren Services genauso auseinander — nur schwerer sichtbar.
