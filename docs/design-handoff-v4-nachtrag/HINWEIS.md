# Hinweis zu diesem Archiv

**Dies ist die maßgebliche Fassung von Handoff v4.** Sie kam am 26.08.2026 und trägt denselben
Ordnernamen wie die erste Lieferung vom 25.08. — es ist eine Fortschreibung, kein v5. Der ältere
Stand liegt weiter unter [`design-handoff-v4/`](../design-handoff-v4/), damit sich beide
vergleichen lassen.

## Was dazugekommen ist

Rein additiv, 72 Zeilen; gestrichen wurde nichts.

- **Abschnitt 6b — Bearbeiten und Löschen.** Jeder Datensatz, den die App anlegt, ist auch
  änderbar und löschbar. Dazu die Datenmodell-Konsequenz: Objekte führen ihre Rohfelder, die
  Anzeigezeile wird daraus gerendert und nie zurückgeparst.
- **Abschnitt 8b — Kontoauszug einlesen.** Der Import wird ein Flow mit vier Zuständen,
  Duplikatprüfung gegen den Bestand statt nur innerhalb der Datei.

Unverändert: `_ds/industry/styles.css` und `support.js` sind byte-identisch zur ersten Lieferung.
Nur der Prototyp ist gewachsen (220 → 250 KB).

## Was ergänzt werden musste

Derselbe Export-Defekt wie in jeder Lieferung zuvor: der Canvas verweist auf
`_ds/industry-c7818bc7-…/`, die ZIP liefert `_ds/industry/`, und das dort referenzierte
`_ds_bundle.js` ist nicht enthalten. Ergänzt sind daher nur der gesuchte Pfad und der erklärte
Platzhalter; die gelieferten Dateien selbst sind unberührt. Näheres steht in
[`../design-handoff-v4/HINWEIS.md`](../design-handoff-v4/HINWEIS.md).

## Öffnen

Nicht per Doppelklick — aus diesem Ordner heraus

```
python -m http.server 8765
```

und <http://127.0.0.1:8765/FinanzApp%20v4%20Responsive.dc.html> aufrufen.
