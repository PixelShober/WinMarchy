# WinMarchy

Omarchys Look und Tastenkürzel auf Windows, ohne Admin-Rechte und ohne Programme, die im Büro nicht erlaubt sind. WinMarchy richtet [GlazeWM](https://github.com/glzr-io/glazewm) als Tiling-Window-Manager ein, legt eine Leiste nach Art von Waybar oben an den Bildschirm, überträgt die Kürzel aus Omarchy und färbt Terminal, VS Code, Flow Launcher und Windows mit einem der 22 Omarchy-Themes ein.

Jede Änderung landet vorher in einem Sicherungspunkt. Unter **Rückgängig** spielst du einzelne Schritte zurück oder setzt alles auf den Stand vor WinMarchy.

Hervorgegangen ist WinMarchy aus dem Winarchy Lite WPF Manager 2.1. Die Logik stammt von dort, die Oberfläche folgt dem Aufbau von [Ember](https://github.com/PixelShober/Ember): Tabs links, gleitende Markierung, Seitenwechsel mit kurzer Animation, Hell und Dunkel mit dem Kreis-Effekt beim Umschalten.

## Installation

1. `WinMarchy.exe` aus dem [neuesten Release](https://github.com/PixelShober/WinMarchy/releases/latest) laden.
2. Starten. WinMarchy kopiert sich nach `C:\WinMarchy\App` und legt einen Startmenü-Eintrag an.
3. Auf **Einrichtung** klicken und durchlaufen lassen.

Die Exe ist eigenständig: .NET muss nicht installiert sein. Läuft auf Windows 10 (ab 1809) und Windows 11, x64.

Wer nicht kopieren will, startet mit `WinMarchy.exe --portable`.

### Updates

Installierte Versionen schauen alle sechs Stunden in die GitHub-Releases. Eine neue Version wird geladen, gegen die SHA-256 aus `latest.json` geprüft und installiert, sobald das Fenster zu ist. GlazeWM, Leiste und Tastenkürzel laufen dabei weiter. Abschalten lässt sich das unter **Einstellungen → Updates**.

## Was WinMarchy installiert

Alles kommt direkt aus dem GitHub-Release des Herstellers, wird per SHA-256 geprüft und landet unter `C:\WinMarchy\Tools`. Kein winget, kein Store, keine Administratorrechte.

| Programm | Wofür | Lizenz |
|-|-|-|
| GlazeWM | Fenster kacheln, Workspaces | GPL-3.0 |
| Zebar | Leiste oben | GPL-3.0 |
| AutoHotkey v2 | Tastenkürzel, auch die Win-Taste allein | GPL-2.0 |
| Flow Launcher | Programme suchen (Win+Space) | MIT |
| PowerShell 7 | Shell im Terminal (optional) | MIT |

Windows Terminal, Files und VS Code erkennt WinMarchy und färbt sie ein, installiert sie aber nicht selbst. Herdr lässt sich unter **Einstellungen** aus einer ZIP übernehmen.

Alle Programme dürfen kostenlos in Firmen eingesetzt werden. Komorebi war deshalb keine Option: Für den geschäftlichen Einsatz braucht es eine kostenpflichtige Lizenz.

## Ordner

| Ordner | Inhalt |
|-|-|
| `C:\WinMarchy\App` | WinMarchy.exe |
| `C:\WinMarchy\Config` | Einstellungen, eigene Tastenkürzel, Layouts pro Workspace |
| `C:\WinMarchy\Backups` | Sicherungspunkte |
| `C:\WinMarchy\Tools` | GlazeWM, Zebar, AutoHotkey, PowerShell |
| `C:\WinMarchy\Themes` | Vorschaubilder und Hintergründe |
| `C:\WinMarchy\Logs` | Protokolle, 30 Tage |

Darf ein normaler Benutzer unter `C:\` keine Ordner anlegen, nimmt WinMarchy `%LOCALAPPDATA%\WinMarchy` mit demselben Aufbau.

## Tastenkürzel

Die Win-Taste übernimmt die Rolle von Super. Die wichtigsten:

| Kürzel | Was passiert |
|-|-|
| `Win` allein | Programme suchen |
| `Win+Eingabe` | Terminal |
| `Win+Alt+Space` | WinMarchy-Menü, wie Super+Space in Omarchy |
| `Win+K` | Alle Kürzel anzeigen (Eingabe führt das gewählte aus) |
| `Win+W` | Fenster schließen |
| `Win+Pfeiltasten` | Fokus wechseln, mit Umschalt Fenster tauschen |
| `Win+1` … `Win+0` | Workspace 1 bis 10 |
| `Win+Alt+L` | Layout des Workspace wechseln (Omarchy: Super+L) |
| `Win+S` | Scratchpad |
| `Win+Ctrl+Umschalt+Space` | Theme wählen |

Die vollständige Liste und welche Kürzel aus Omarchy und deiner `bindings.lua` wie übertragen wurden, steht in [docs/shortcuts.md](docs/shortcuts.md). Wie die Layouts Dwindle, Scrolling und Floating auf GlazeWM abgebildet sind, erklärt [docs/tiling.md](docs/tiling.md).

## Rückgängig machen

Vor jedem Vorgang (Einrichtung, Theme, Tastenkürzel, Tiling) legt WinMarchy einen Sicherungspunkt an. Er enthält jede Datei, jeden Registry-Wert unter `HKEY_CURRENT_USER` und das Hintergrundbild, so wie sie vorher waren. Außerdem merkt sich WinMarchy beim ersten Anfassen jedes Ziels den Originalzustand.

**Alles zurücksetzen** stellt diesen Originalzustand her, beendet Leiste und Tastenkürzel-Skript, holt die Taskleiste zurück und entfernt auf Wunsch die Programme unter `C:\WinMarchy\Tools`.

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
- [AutoHotkey](https://www.autohotkey.com), GPL-2.0.
- [Flow Launcher](https://www.flowlauncher.com), MIT.

## Lizenz

MIT, siehe [LICENSE](LICENSE).
