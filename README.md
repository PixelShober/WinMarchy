<p align="center"><img src="src/WinMarchy.App/Assets/winmarchy.svg" width="96" alt="WinMarchy"></p>

<h1 align="center">WinMarchy</h1>

<p align="center">Omarchys Look und Tastenkürzel auf Windows, mit GlazeWM.</p>

<p align="center">
  <a href="https://github.com/PixelShober/WinMarchy/releases/latest">Download</a> ·
  <a href="docs/shortcuts.md">Tastenkürzel</a> ·
  <a href="docs/tiling.md">Tiling</a>
</p>

---

Omarchys Look und Tastenkürzel auf Windows, ohne Admin-Rechte und ohne Programme, die im Büro nicht erlaubt sind. WinMarchy richtet [GlazeWM](https://github.com/glzr-io/glazewm) als Tiling-Window-Manager ein, legt eine Leiste nach Art von Waybar oben an den Bildschirm, überträgt die Kürzel aus Omarchy und färbt Terminal, VS Code, Flow Launcher und Windows mit einem der 22 Omarchy-Themes ein.

Jede Änderung landet vorher in einem Sicherungspunkt. Unter **Rückgängig** spielst du einzelne Schritte zurück oder setzt alles auf den Stand vor WinMarchy.

Hervorgegangen ist WinMarchy aus dem Winarchy Lite WPF Manager 2.1. Die Logik stammt von dort, die Oberfläche folgt dem Aufbau von [Ember](https://github.com/PixelShober/Ember): Tabs links, gleitende Markierung, Seitenwechsel mit kurzer Animation, Hell und Dunkel mit dem Kreis-Effekt beim Umschalten.

## Installation

1. `WinMarchy.exe` aus dem [neuesten Release](https://github.com/PixelShober/WinMarchy/releases/latest) laden.
2. Starten. WinMarchy kopiert sich nach `C:\WinMarchy\App` und legt einen Startmenü-Eintrag an.
3. Auf **Einrichtung** klicken und durchlaufen lassen.

Die Exe ist eigenständig: .NET muss nicht installiert sein. Läuft auf Windows 10 (ab 1809) und Windows 11, x64.

Wer nicht kopieren will, startet mit `WinMarchy.exe --portable`. Läuft schon eine andere Version, beendet WinMarchy sie und startet sich danach aus `C:\WinMarchy\App`. Eine ältere `WinMarchy.exe` aus dem Download-Ordner überschreibt keine neuere, die sich inzwischen selbst aktualisiert hat; dann startet die neuere.

### Testversionen aus Pull Requests

Jeder Pull Request baut eine `WinMarchy.exe`, zu finden als Artefakt „WinMarchy-PR-…“ auf der Seite des CI-Laufs (Actions → Lauf öffnen → unten bei Artifacts). Daneben liegt „Screenshots-PR-…“ mit jeder Seite in Hell und Dunkel. Eine Testversion installiert sich wie ein Release nach `C:\WinMarchy\App` und ersetzt, was dort liegt. Läuft gerade eine andere Version, beendet WinMarchy sie vorher. Neuere Versionen fahren dabei sauber herunter wie über das Tray-Menü, ältere kennen das Signal noch nicht und werden einfach beendet. Danach startet die neue Version aus `C:\WinMarchy\App`, damit Autostart, AutoHotkey, GlazeWM und die Leiste genau diese Version aufrufen. Testversionen erkennt WinMarchy an der Versionsnummer (`0.0.0-ci.…`); sie laden keine Updates, bis du ein Release startest.

### Updates

Installierte Versionen schauen alle sechs Stunden in die GitHub-Releases. Eine neue Version wird geladen, gegen die SHA-256 aus `latest.json` geprüft und installiert, sobald das Fenster zu ist. GlazeWM, Leiste und Tastenkürzel laufen dabei weiter. Abschalten lässt sich das unter **Einstellungen → Updates**.

Die Programme, die WinMarchy installiert hat, holt **Einstellungen → Updates → Programme aktualisieren** in der neuesten Version vom Hersteller, geprüft wie bei der Einrichtung. Laufende Programme werden dafür kurz beendet und danach wieder gestartet. Ein offenes Terminal behält seine PowerShell, bis es geschlossen ist. Flow Launcher aktualisiert sich selbst.

## Was WinMarchy installiert

Alles kommt direkt aus einem GitHub-Release, wird vor dem Entpacken geprüft (SHA-256) und landet unter `C:\WinMarchy\Tools`. Kein winget, kein Store, keine Administratorrechte.

| Programm | Wofür | Lizenz |
|-|-|-|
| GlazeWM | Fenster kacheln, Workspaces. Der WinMarchy-Build aus [PixelShober/glazewm](https://github.com/PixelShober/glazewm) | GPL-3.0 |
| YASB | Leiste oben, mit Menüs, die von oben aufklappen | MIT |
| AutoHotkey v2 | Tastenkürzel, auch die Win-Taste allein | GPL-2.0 |
| Flow Launcher | Programme suchen (Win allein) | MIT |

**YASB** ersetzt Zebar als Leiste. Die Menüs lassen sich auch per Tastenkürzel aufklappen: Aktion `do:bar-menu` mit `logo`, `calendar`, `audio`, `network`, `media`, `cpu` oder `memory`, etwa auf **Tastenkürzel → Neu**. Kalender und Lautstärke liegen schon auf `Win+Alt+D` und `Win+Alt+A`. Lässt Windows Desktop-Apps nicht an den Standort (Einstellungen → Datenschutz → Standort, in Firmen oft per Richtlinie), gibt Windows 11 keine WLAN-Liste heraus; ein Klick aufs Netz öffnet dann die WLAN-Liste von Windows. Die Lautstärke-Anzeige folgt den Fn-Tasten erst mit YASB 2.0.8, das bisher nur als Vorabversion erschienen ist; sobald es regulär da ist, holt es **Programme aktualisieren**. Workspaces links, die Uhr in der Mitte, Infobereich, Netz, Ton, CPU und Akku rechts, wie die Waybar in Omarchy. Ein Klick auf die Uhr klappt den Kalender mit Kalenderwochen und Feiertagen auf, auf den Ton die Lautstärke mit den Ausgabegeräten, auf das Netz die WLAN-Liste, auf die CPU den Verlauf. Das Logo öffnet ein Menü mit WinMarchy-Menü, Tastenkürzeln, Theme, Hintergrund, deinen eigenen Einträgen und den Punkten zum Sperren, Abmelden und Herunterfahren. Eigene Einträge legst du unter **Tiling → Eigene Einträge in der Leiste** an (Name und Aktion wie bei Tastenkürzeln), auf Wunsch auch als Knopf direkt in der Leiste. Farben und Schrift kommen aus dem Theme. Die Symbole stammen aus Segoe Fluent Icons, die Windows 11 mitbringt; unter Windows 10 muss die Schrift einmal von Microsoft installiert werden. YASBs MSI ist für alle Benutzer gebaut; WinMarchy packt es nur aus, Administratorrechte braucht es dafür nicht.

Wer noch Zebar aus einer früheren Einrichtung hat, behält es, bis er unter **Einstellungen → Leiste** auf YASB umstellt. Dort entfernt **Zebar entfernen** Zebar wieder: WinMarchy beendet es, nimmt seine Leiste aus Zebars Autostart (beides in einem Wiederherstellungspunkt) und löscht es aus `C:\WinMarchy\Tools`. Ein Zebar, das jemand anders installiert hat, bleibt; WinMarchy sagt dann, wo es liegt. GlazeWM bringt Zebar nicht mehr mit: Der WinMarchy-Build startet in seiner Beispielkonfiguration keine Leiste, und die Konfiguration von WinMarchy hat ohnehin keinen Startbefehl.

**GlazeWM** kommt als WinMarchy-Build: GlazeWM von glzr-io plus die Korrekturen und Funktionen, die WinMarchy braucht. Jede Abweichung steht in [WINMARCHY.md](https://github.com/PixelShober/glazewm/blob/winmarchy/WINMARCHY.md) des Forks, Korrekturen gehen zusätzlich als Pull Request an glzr-io. Anders als das offizielle Release ist der Build nicht signiert und läuft deshalb ohne UIAccess: Programme, die als Administrator laufen, kachelt er nicht. Unter **Einstellungen → GlazeWM** lässt sich jederzeit auf das offizielle Release zurückwechseln. Gibt es den WinMarchy-Build gerade nicht, nimmt die Einrichtung das offizielle.

Optionale Extras, in der Einrichtung einzeln anzuhaken:

| Programm | Wofür | Lizenz |
|-|-|-|
| PowerShell 7 | Shell im Terminal | MIT |
| Starship | Omarchys Eingabezeile in PowerShell, in den Farben des Themes | ISC |
| Fastfetch | Systeminfo mit IP- und MAC-Adresse in jedem neuen Terminal-Tab, wie „Über“ in Omarchy (auch im Logo-Menü der Leiste und unter WinMarchy-Menü → Info) | MIT |
| Herdr | Terminal-Arbeitsplatz für Coding-Agents (Win+Strg+Eingabe), unter Windows noch Beta | Apache-2.0 |
| Pi | Coding-Agent im Terminal (Win+Alt+Eingabe), braucht Git Bash | MIT |
| Greenshot | Bildschirmfotos mit Editor, statt Snipping Tool (umschaltbar unter **Einstellungen**) | GPL-3.0 |
| ExplorerBlurMica (experimentell) | Unschärfe hinter Explorer, eingefärbt mit dem Theme (ein- und ausschalten unter **Einstellungen**) | LGPL-3.0 |
| Lunatask | Aufgaben, Notizen, Gewohnheiten. Nur verlinkt, siehe unten | proprietär |

**Starship und Fastfetch** bekommen ihre Einstellungen in `C:\WinMarchy\Runtime`, eine eigene `starship.toml` oder Fastfetch-Konfiguration bleibt also unberührt. Jeder neue PowerShell-Tab zeigt zuerst Fastfetch (mit IP- und MAC-Adresse, praktisch für IT-Tickets) und dann Starships Eingabezeile; `fastfetch` lässt sich auch selbst eintippen. Ins Profil kommt nur der markierte WinMarchy-Block, der das Startskript lädt, wenn es da ist: in PowerShell 7 und in Windows PowerShell 5.1, dort aber nur, wenn die Ausführungsrichtlinie Profile zulässt (RemoteSigned oder lockerer). Windows 10 und 11 stehen ohne Vorgabe auf Restricted; dann bleibt Windows PowerShell unberührt, und Starship und Fastfetch kommen mit dem Extra PowerShell 7. WinMarchy ändert die Richtlinie nicht. Unter AppLocker läuft PowerShell im eingeschränkten Sprachmodus: Fastfetch erscheint trotzdem, Starship nicht, denn sein Skript läuft dort nicht.

**ExplorerBlurMica** ist eine DLL, die Explorer lädt. Mit `regsvr32` als Administrator eingetragen, gilt sie für alle Benutzer. WinMarchy schreibt dieselben zwei Einträge nur für dein Konto unter `HKEY_CURRENT_USER`, ohne Administratorrechte und mit Wiederherstellungspunkt. Neu geöffnete Explorer-Fenster zeigen den Effekt. Ausschalten trägt die DLL aus, entladen wird sie mit der nächsten Anmeldung. Experimentell, weil ein Windows-Update sie stören kann und manche Firmen Explorer-Erweiterungen nur systemweit zulassen. Stürzt Explorer ab: beim Öffnen Esc gedrückt halten, dann bleibt der Effekt aus.

**Lunatask** lädt WinMarchy nicht selbst herunter, weil der Hersteller keine Prüfsumme veröffentlicht. Die Einrichtung verlinkt [lunatask.app](https://lunatask.app/download). Ist es installiert (oder aus dem Microsoft Store), startet die Aktion `app:lunatask` es, etwa als Tastenkürzel oder als eigener Eintrag in der Leiste. Für Lunatask gelten die Bedingungen des Herstellers, auch für den Einsatz in der Firma.

Windows Terminal, Files und VS Code erkennt WinMarchy und färbt sie ein, installiert sie aber nicht selbst. Files lässt sich unter **Einstellungen** als Standard-Dateimanager festlegen: Ordner, Laufwerke und Win+E öffnen dann Files, Dieser PC und Papierkorb bleiben beim Explorer.

Alle Programme, die WinMarchy selbst installiert, dürfen kostenlos in Firmen eingesetzt werden. Komorebi war deshalb keine Option: Für den geschäftlichen Einsatz braucht es eine kostenpflichtige Lizenz.

## Ordner

| Ordner | Inhalt |
|-|-|
| `C:\WinMarchy\App` | WinMarchy.exe |
| `C:\WinMarchy\Config` | Einstellungen, eigene Tastenkürzel, Layouts pro Workspace, Scratchpad |
| `C:\WinMarchy\Backups` | Sicherungspunkte |
| `C:\WinMarchy\Tools` | GlazeWM, YASB, AutoHotkey, PowerShell und die Extras |
| `C:\WinMarchy\Themes` | Vorschaubilder und Hintergründe, unter `<theme>\own` deine eigenen |
| `C:\WinMarchy\Logs` | Protokolle, 30 Tage |

Darf ein normaler Benutzer unter `C:\` keine Ordner anlegen, nimmt WinMarchy `%LOCALAPPDATA%\WinMarchy` mit demselben Aufbau.

## Tastenkürzel

Die Win-Taste übernimmt die Rolle von Super. Die wichtigsten:

| Kürzel | Was passiert |
|-|-|
| `Win` allein | Programme suchen (Flow Launcher) |
| `Win+Eingabe` | Terminal |
| `Win+Space` | WinMarchy-Menü; Tippen sucht auch in den Programmen des Startmenüs |
| `Win+K` | Alle Kürzel anzeigen, Favoriten zuerst (Eingabe führt das gewählte aus) |
| `Win+W` | Fenster schließen |
| `Win+Pfeiltasten` | Fokus wechseln, mit Umschalt Fenster tauschen |
| `Win+J` | Teilung drehen, sofort sichtbar |
| `Win+1` … `Win+0` | Workspace 1 bis 10 |
| `Win+Strg+D`, `Win+Strg+1` bis `3` | Arbeitsmodus: frischer Workspace auf jedem Bildschirm mit festen Programmen, auch der Browser mit festen Seiten; nochmal drücken holt zurück, was vorher da war. `Win+Strg+Pfeil` schaltet alle Bildschirme weiter |
| `Win+Strg+J` | Layout des Workspace wechseln: Dwindle, Spalten, Scrolling (Omarchy: Super+L) |
| `Win+Strg+M` | Musik abspielen oder anhalten |
| `Win+Alt+D`, `Win+Alt+A` | Kalender und Lautstärke der Leiste aufklappen (YASB, optional) |
| `Win+S` | Scratchpad ein- und ausblenden, `Win+Alt+S` legt ein Fenster hinein oder holt es heraus |
| `Win+M` | Minimierte Fenster zurückholen |
| `Win+N` | Editor |
| `Win+Umschalt+G` | Stackfield |
| `Win+Strg+T` | Theme wählen, mit Fortschrittsbalken |
| `Win+Strg+Alt+Space` | Hintergrund wählen, auch eigene Bilder |
| `Druck` | Bildschirmfoto mit Snipping Tool oder Greenshot |

Die vollständige Liste und welche Kürzel aus Omarchy und deiner `bindings.lua` wie übertragen wurden, steht in [docs/shortcuts.md](docs/shortcuts.md). Eine `bindings.lua`, Hyprland-`bindings.conf` oder einen WinMarchy-Export liest **Tastenkürzel → Importieren**; du wählst dann aus, was du übernimmst. Wie die Layouts Dwindle, Scrolling und Floating auf GlazeWM abgebildet sind, erklärt [docs/tiling.md](docs/tiling.md).

## Rückgängig machen

Vor jedem Vorgang (Einrichtung, Theme, Tastenkürzel, Tiling, Files als Standard) legt WinMarchy einen Sicherungspunkt an. Er enthält jede Datei, jeden Registry-Wert und -Schlüssel unter `HKEY_CURRENT_USER`, das Hintergrundbild und, wenn du ihn mitändern lässt, den Sperrbildschirm, so wie sie vorher waren. Außerdem merkt sich WinMarchy beim ersten Anfassen jedes Ziels den Originalzustand.

Greift eine Gruppenrichtlinie oder Intune ein (Hintergrund, Sperrbildschirm, Farben, AppLocker, Skripte), zeigt das **Protokoll → Richtlinien prüfen**. Die Liste landet auch als Bericht in `C:\WinMarchy\Logs`.

**Alles zurücksetzen** stellt diesen Originalzustand her, beendet Leiste und Tastenkürzel-Skript, holt die Taskleiste zurück und entfernt auf Wunsch die Programme unter `C:\WinMarchy\Tools`.

## Fehler suchen

Tut ein Kürzel nichts oder startet etwas nicht, hilft die Seite **Protokoll**:

1. **Ausführlich protokollieren** einschalten. Ab dann steht unter „Alles“ jedes Tastenkürzel, das ankommt, jeder Befehl an GlazeWM mit Dauer und Antwort, und was Flow, Taskleiste, Scratchpad, Scrolling, Bildschirm-Zuordnung und die Arbeitsmodi gerade tun.
2. Den Fehler nachstellen.
3. **Diagnosebericht** drücken. Er landet als Textdatei in `C:\WinMarchy\Logs` und zeigt, was WinMarchy sieht: laufende Programme, Bildschirme, GlazeWMs Monitore, Workspaces und Fenster (ohne Titel), die erzeugten Tastenkürzel, Flows Einstellungen und letzte Protokollzeilen, Taskleiste und Leiste.
4. Danach den Schalter wieder ausschalten, das Protokoll wird sonst schnell lang.

Fenstertitel, Passwörter und Inhalte schreibt WinMarchy in beides nicht.

Beantwortet GlazeWM zwei Befehle hintereinander nicht, prüft WinMarchy über eine frische Verbindung, ob GlazeWM noch lebt. Antwortet es dort, hing nur die Verbindung, und WinMarchy verbindet sich neu. Antwortet es auch dort nicht, beendet WinMarchy GlazeWM und startet es neu, höchstens einmal in fünf Minuten. Der Wächter-Prozess von GlazeWM bleibt dabei stehen und holt Fenster von versteckten Workspaces zurück, so wie nach einem Absturz. „GlazeWM neu laden“ im Tray-Menü startet ein hängendes GlazeWM ebenfalls neu.

## Für Entwickler

```bash
dotnet build WinMarchy.sln -c Release
```

```bash
dotnet test tests/WinMarchy.Core.Tests
```

```bash
dotnet publish src/WinMarchy.App/WinMarchy.App.csproj -c Release -o artifacts/exe
```

`WinMarchy.Core` hat keine NuGet-Pakete und baut und testet auch unter Linux. Die App ist WPF und braucht zum Starten Windows.

`WinMarchy.exe --selftest <ordner>` rendert jede Seite in Hell und Dunkel als PNG, schreibt das erzeugte AutoHotkey-Skript und die GlazeWM-Konfiguration und meldet Texte, für die eine Übersetzung fehlt. Die CI macht das bei jedem Pull Request und hängt Exe und Screenshots als Artefakte an.

### Releases

Der Titel des Pull Requests bestimmt die Version (Conventional Commits): `feat:` erhöht die Minor-, `fix:` die Patch-Version, `feat!:` die Major-Version. Jeder Merge nach `main`, der die App ändert, erzeugt automatisch ein Release mit `WinMarchy.exe`, `latest.json` und `SHA256SUMS.txt`.

## Danke

- [Omarchy](https://github.com/basecamp/omarchy) von David Heinemeier Hansson, MIT. Die Themes stammen von dort.
- [GlazeWM und Zebar](https://github.com/glzr-io) von glzr-io, GPL-3.0.
- [YASB](https://github.com/amnweb/yasb) von AmN, MIT.
- [Starship](https://starship.rs), ISC, und [Fastfetch](https://github.com/fastfetch-cli/fastfetch), MIT.
- [ExplorerBlurMica](https://github.com/Maplespe/ExplorerBlurMica) von Maplespe, LGPL-3.0.
- [AutoHotkey](https://www.autohotkey.com), GPL-2.0.
- [Flow Launcher](https://www.flowlauncher.com), MIT.

## Lizenz

MIT, siehe [LICENSE](LICENSE).
