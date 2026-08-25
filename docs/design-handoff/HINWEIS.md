# Hinweis zu diesem Archiv

Der Ordner enthaelt den Design-Handoff **wie geliefert** — `FinanzApp.dc.html`, `handoff.md`
(im Archiv `README.md`), `support.js` und `_ds/modernist/styles.css` sind unveraendert.

Zwei Dinge sind spaeter dazugekommen, weil der Export in sich nicht schluessig war und der
Prototyp sich sonst nicht mehr oeffnen liesse:

| Ergaenzt | Warum |
| --- | --- |
| `_ds/modernist-f2a2de5d-ef07-4674-8061-e8aed46977f4/styles.css` | Kopie der gelieferten Datei. Der Canvas verweist auf diesen Pfad, das Archiv liefert sie unter `_ds/modernist/`. Ohne die Kopie laedt keine einzige Stilregel. |
| `_ds/modernist-f2a2de5d-ef07-4674-8061-e8aed46977f4/_ds_bundle.js` | Erklaerter Platzhalter. Der Canvas verweist darauf, kein Archiv enthaelt die Datei — und gebraucht wird sie nicht. |

Die UUID im Pfad ist die des Design-System-Projekts *Modernist* auf claude.ai/design.

## Oeffnen

Nicht per Doppelklick: `support.js` laedt der Browser aus einer `file://`-Seite nicht zuverlaessig.
Stattdessen aus diesem Ordner heraus

```
python -m http.server 8765
```

und <http://127.0.0.1:8765/FinanzApp.dc.html> aufrufen.

## Geprueft

Am 25.08.2026 so geoeffnet: der Prototyp rendert vollstaendig, die Zustandslogik des Canvas
(`sc-if`, `sc-for`) arbeitet, die Konsole bleibt sauber.
