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
