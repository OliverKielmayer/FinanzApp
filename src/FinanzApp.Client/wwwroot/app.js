// Der Verbindungszustand des Browsers, an die Anwendung gemeldet.
//
// navigator.onLine allein genuegt nicht: es sagt nur, ob eine Netzwerkschnittstelle da ist,
// und aendert sich ohne die beiden Ereignisse nie. Der erste Aufruf ist noetig, weil die
// Anwendung sonst bis zum ersten Wechsel annimmt, sie sei online — auch wenn sie es nie war.
window.finanzapp = window.finanzapp || {};

window.finanzapp.watchConnection = function (ref) {
    const melden = () => ref.invokeMethodAsync('SetOnline', navigator.onLine);

    window.addEventListener('online', melden);
    window.addEventListener('offline', melden);

    melden();
};

// Die Kamera — am Telefon die rueckwaertige, am Rechner die Webcam.
//
// Auf dem Telefon genuegte bisher capture="environment" am Dateifeld: das oeffnet die
// Kamera-App des Geraets, und die kann alles besser, als eine Seite es koennte. Am Rechner
// tut dasselbe Attribut nichts — dort oeffnet sich der Dateiwaehler, und eine Webcam bleibt
// unerreichbar. Dafuer gibt es nur diesen Weg: Vorschau im Bild, Einzelbild auf eine
// Leinwand, Bild als Datei.
//
// getUserMedia verlangt einen gesicherten Kontext — https oder localhost. Ueber http von
// einem anderen Geraet aus ist die Funktion schlicht nicht da, und das ist kein Fehler,
// sondern eine Regel des Browsers. Darum wird sie geprueft und nicht abgefangen.
window.finanzapp.kamera = (function () {
    let strom = null;

    const verfuegbar = () =>
        typeof navigator !== 'undefined' &&
        navigator.mediaDevices &&
        typeof navigator.mediaDevices.getUserMedia === 'function';

    return {
        // Gibt die Kennungen der gefundenen Kameras zurueck, oder eine Begruendung.
        //
        // Die Geraeteliste ist vor der ersten Freigabe namenlos: der Browser gibt die
        // Bezeichnungen erst heraus, wenn der Nutzer eine Kamera erlaubt hat. Deshalb erst
        // starten, dann auflisten — andersherum stuenden dort zwei leere Eintraege.
        start: async function (video, deviceId) {
            if (!verfuegbar()) {
                return {
                    ok: false,
                    grund: window.isSecureContext
                        ? 'Dieser Browser bietet keinen Kamerazugriff an.'
                        : 'Die Kamera braucht eine gesicherte Verbindung. Über http erlaubt der '
                          + 'Browser sie nur auf diesem Rechner selbst (localhost).'
                };
            }

            this.stop();

            // Hoch aufloesend, weil daraus Text gelesen werden soll: was die Kamera nicht
            // aufloest, holt keine Erkennung zurueck. „ideal“ und nicht „exact“ — eine
            // Webcam, die 1080p nicht kann, soll liefern was sie hat, statt abzulehnen.
            const wunsch = {
                video: {
                    width: { ideal: 2560 },
                    height: { ideal: 1440 },
                    facingMode: { ideal: 'environment' }
                },
                audio: false
            };

            if (deviceId) {
                wunsch.video.deviceId = { exact: deviceId };
                delete wunsch.video.facingMode;
            }

            try {
                strom = await navigator.mediaDevices.getUserMedia(wunsch);
            } catch (fehler) {
                return { ok: false, grund: beschreiben(fehler) };
            }

            video.srcObject = strom;
            await video.play();

            let kameras = [];
            try {
                const geraete = await navigator.mediaDevices.enumerateDevices();
                kameras = geraete
                    .filter(g => g.kind === 'videoinput')
                    .map((g, i) => ({ id: g.deviceId, name: g.label || ('Kamera ' + (i + 1)) }));
            } catch {
                // Ohne Liste bleibt es bei der einen laufenden Kamera. Kein Grund abzubrechen.
            }

            const spur = strom.getVideoTracks()[0];
            const eingestellt = spur && spur.getSettings ? spur.getSettings() : {};

            return {
                ok: true,
                kameras: kameras,
                aktiv: eingestellt.deviceId || '',
                breite: video.videoWidth,
                hoehe: video.videoHeight
            };
        },

        // Ein Einzelbild in voller Aufloesung der Spur, als JPEG in Base64.
        //
        // Nicht in der Groesse des Vorschaufensters: das ist ein paar hundert Pixel breit, und
        // aus dessen Bild liest keine Erkennung eine Vertragsnummer. Gezeichnet wird, was die
        // Kamera liefert.
        aufnehmen: function (video) {
            const breite = video.videoWidth;
            const hoehe = video.videoHeight;

            if (!breite || !hoehe) {
                return null;
            }

            const leinwand = document.createElement('canvas');
            leinwand.width = breite;
            leinwand.height = hoehe;
            leinwand.getContext('2d').drawImage(video, 0, 0, breite, hoehe);

            // 0.92: sichtbar besser als die Vorgabe 0.8, und noch weit unter der Grenze von
            // 25 MB. Ein Beleg wird gelesen, nicht angesehen — Artefakte kosten hier Zeichen.
            const datenUrl = leinwand.toDataURL('image/jpeg', 0.92);
            return datenUrl.substring(datenUrl.indexOf(',') + 1);
        },

        // Immer aufraeumen: eine Spur, die weiterlaeuft, laesst die Leuchte am Geraet an.
        stop: function () {
            if (strom) {
                strom.getTracks().forEach(spur => spur.stop());
                strom = null;
            }
        }
    };

    function beschreiben(fehler) {
        switch (fehler && fehler.name) {
            case 'NotAllowedError':
                return 'Der Zugriff auf die Kamera wurde abgelehnt. Im Browser neben der '
                     + 'Adresszeile lässt er sich wieder erlauben.';
            case 'NotFoundError':
            case 'OverconstrainedError':
                return 'An diesem Gerät ist keine Kamera gefunden worden.';
            case 'NotReadableError':
                return 'Die Kamera wird gerade von einem anderen Programm benutzt.';
            default:
                return 'Die Kamera ließ sich nicht öffnen'
                     + (fehler && fehler.message ? ': ' + fehler.message : '.');
        }
    }
})();
