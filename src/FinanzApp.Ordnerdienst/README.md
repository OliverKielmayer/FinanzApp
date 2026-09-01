# FinanzApp Ordnerdienst

Ein Windows-Dienst, der einen Ordner überwacht und jede neue Datei an die FinanzApp weiterreicht.
Die Anwendung analysiert sie, legt sie im passenden Bereich ab und ordnet sie dem Objekt zu, zu
dem sie gehört. Was sich nicht zuordnen lässt, wartet im **Scaneingang**, bis der Benutzer es
nachträgt.

Der Dienst analysiert **nichts selbst**. Das kann die Anwendung besser, und zwei Fassungen
derselben Leseregeln liefen zwangsläufig auseinander. Er sorgt nur dafür, dass keine Datei liegen
bleibt und keine zweimal hinausgeht.

## Der Weg einer Datei

```
C:\Scans\Eingang\Statusreport_2026.pdf
        │
        │  fertig geschrieben? (Größe stabil, exklusiv öffenbar)
        │  klein genug? (sonst gleich nach _fehlgeschlagen)
        ▼
POST /api/scan/intake  ──►  analysieren · ablegen · verknüpfen · in den Scaneingang
        │
        ├─ angekommen        ──►  C:\Scans\Eingang\_erledigt\2026-08\Statusreport_2026.pdf
        ├─ abgelehnt (4xx)   ──►  C:\Scans\Eingang\_fehlgeschlagen\Statusreport_2026.pdf
        └─ Server weg / 5xx  ──►  bleibt liegen, nächster Durchgang versucht es erneut
```

**Der Ordner ist die Warteschlange** — es gibt keine zweite daneben. Was noch daliegt, ist noch
nicht übergeben; was übergeben ist, liegt nicht mehr da. Ein Neustart mitten im Betrieb kostet
deshalb nichts.

Überwacht wird nur die **oberste Ebene** des Ordners. Deshalb dürfen `_erledigt` und
`_fehlgeschlagen` darin liegen, ohne dass der Dienst seine eigenen Ergebnisse wieder einsammelt.

## Einstellungen

`appsettings.json` neben der ausführbaren Datei, Abschnitt `Ordnerdienst`:

| Schlüssel | Vorgabe | Bedeutung |
| --- | --- | --- |
| `WatchFolder` | — | Der überwachte Ordner. **Pflicht.** |
| `DoneFolder` | `<WatchFolder>\_erledigt` | Wohin übergebene Dateien wandern, dort nach Monat sortiert |
| `FailedFolder` | `<WatchFolder>\_fehlgeschlagen` | Wohin abgelehnte Dateien wandern |
| `BaseAddress` | `http://localhost:5111/` | Adresse der FinanzApp |
| `Email` | — | Zugang des Dienstes. **Pflicht.** |
| `Password` | — | **Pflicht**, gehört aber nicht in diese Datei — siehe unten |
| `Extensions` | alle | Angebotene Dateiarten; leer heißt: alles anbieten, der Server entscheidet |
| `MaxMegabytes` | `25` | Größere Dateien werden nicht angeboten; `0` schaltet die Prüfung ab |
| `SweepSeconds` | `60` | Takt der Nachlese |
| `SettleSeconds` | `5` | Wie lange eine Datei unverändert sein muss, bevor sie als fertig gilt |
| `MaxAttempts` | `5` | Vorübergehende Fehlversuche je Datei, danach beiseitegelegt |

Fehlt eine Pflichtangabe, startet der Dienst **nicht** und schreibt den Grund ins
Ereignisprotokoll. Ein Dienst, der läuft und stillschweigend nichts tut, ist schlimmer als einer,
der sich weigert und sagt, was fehlt.

### Das Passwort

Nicht in `appsettings.json`. Der Dienst liest die üblichen Konfigurationsquellen — im Betrieb ist
die Umgebungsvariable des Dienstkontos der Weg:

```powershell
# Als Umgebungsvariable des Dienstes (überlebt Neustarts, steht in keiner Datei im Klartext)
sc.exe config FinanzAppOrdnerdienst obj= "NT AUTHORITY\NetworkService"
[Environment]::SetEnvironmentVariable('Ordnerdienst__Password', 'geheim', 'Machine')
```

In der Entwicklung:

```bash
dotnet user-secrets --project src/FinanzApp.Ordnerdienst set "Ordnerdienst:Password" "geheim"
```

### Der Zugang

Ein **eigener Benutzer** des Haushalts mit der Rolle *Mitglied* — nicht der des Inhabers. Der
Dienst meldet sich mit demselben Cookie an wie ein Browser; die Anwendung kennt keinen zweiten Weg
herein, und einen dafür zu erfinden hieße, die Sitzungsverwaltung zu umgehen, die es schon gibt.
Der Vorteil ist handfest: die Sitzung des Dienstes steht in der Benutzerverwaltung und lässt sich
dort widerrufen wie jede andere.

`Documents:AllowedExtensions` und `Documents:MaxFileSizeMegabytes` der API entscheiden, was
überhaupt angenommen wird. Was der Dienst anbietet und die API ablehnt, landet in
`_fehlgeschlagen`.

`MaxMegabytes` hält die Größengrenze deshalb auf **beiden** Seiten. Eine Datei über der
Rumpfgrenze des Servers bekommt keine saubere Ablehnung: er bricht die Verbindung ab, *während*
gesendet wird — und ein Abbruch mitten im Rumpf ist von „Server nicht erreichbar“ nicht zu
unterscheiden. Ohne die eigene Prüfung hielte ein einziger 300-MB-Scan den ganzen Eingang auf.

> Außerhalb der Entwicklungsumgebung verlangt das Anmelde-Cookie HTTPS
> (`CookieSecurePolicy.Always`). Der Dienst braucht dann eine `BaseAddress` mit `https://` und ein
> Zertifikat, dem der Rechner traut. Ein Schalter, der die Zertifikatsprüfung abschaltet, ist
> bewusst nicht vorgesehen.

## Ausprobieren, ohne zu installieren

Derselbe Build läuft von Hand in einer Konsole — `AddWindowsService` ist dort wirkungslos:

```bash
dotnet run --project src/FinanzApp.Ordnerdienst
```

## Installieren

```powershell
# 1. Veröffentlichen
dotnet publish src/FinanzApp.Ordnerdienst -c Release -r win-x64 --self-contained false -o C:\Dienste\FinanzAppOrdnerdienst

# 2. Als Dienst anlegen (binPath braucht den vollen Pfad, das Leerzeichen nach "binPath=" ist Pflicht)
New-Service -Name FinanzAppOrdnerdienst `
            -DisplayName "FinanzApp Ordnerdienst" `
            -Description "Überwacht einen Ordner und übergibt neue Belege an die FinanzApp." `
            -BinaryPathName "C:\Dienste\FinanzAppOrdnerdienst\FinanzApp.Ordnerdienst.exe" `
            -StartupType Automatic

# 3. Nach einem Absturz von selbst wieder hochkommen
sc.exe failure FinanzAppOrdnerdienst reset= 86400 actions= restart/60000/restart/60000/restart/60000

# 4. Starten
Start-Service FinanzAppOrdnerdienst
```

Das Protokoll steht danach in der Ereignisanzeige unter *Windows-Protokolle → Anwendung*, Quelle
*FinanzApp Ordnerdienst*:

```powershell
Get-EventLog -LogName Application -Source "FinanzApp Ordnerdienst" -Newest 20 | Format-List
```

Entfernen:

```powershell
Stop-Service FinanzAppOrdnerdienst
sc.exe delete FinanzAppOrdnerdienst
```

### Rechte des Dienstkontos

Das Konto, unter dem der Dienst läuft, braucht **Lesen, Schreiben und Löschen** im überwachten
Ordner — Lesen allein genügt nicht: der Dienst verschiebt jede Datei nach der Übergabe. Liegt der
Ordner auf einem Netzlaufwerk, braucht es ein Konto mit Zugriff darauf; `LocalSystem` hat auf
Freigaben nichts zu suchen.

## Wenn etwas nicht geht

| Im Protokoll steht | Was zu tun ist |
| --- | --- |
| `WatchFolder ist leer` | `appsettings.json` liegt nicht neben der `.exe` oder der Abschnitt fehlt |
| `Anmeldung als … abgelehnt` | Zugang prüfen; nach zehn Fehlversuchen je Minute bremst die API |
| `Die FinanzApp … ist nicht erreichbar` | Adresse, Zertifikat, Firewall. Es bleibt alles liegen — nichts geht verloren |
| `Der überwachte Ordner … ist nicht da` | Pfad falsch geschrieben, oder das Netzlaufwerk ist noch nicht verbunden |
| `DoneFolder zeigt auf den überwachten Ordner selbst` | Ziel- und Eingangsordner sind derselbe. So verschöbe der Dienst jede Datei in den Eingang zurück und lieferte sie erneut ein — er startet deshalb nicht |
| `… abgelehnt: Dateityp nicht zugelassen` | `Documents:AllowedExtensions` der API oder `Extensions` hier anpassen |
| `… abgelehnt: 300 MB — mehr als die erlaubten 25 MB` | Zu groß. `MaxMegabytes` hier und `Documents:MaxFileSizeMegabytes` dort heben, oder die Datei teilen |
| `… liegt aber noch im Eingang` | Der Dienst darf im Ordner nicht schreiben. Bis dahin geht die Datei erneut hinaus |
