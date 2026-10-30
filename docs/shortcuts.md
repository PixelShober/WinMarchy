# Tastenkürzel: Omarchy auf Windows

Omarchy bindet fast alles an Super. Unter Windows übernimmt die Win-Taste diese Rolle. AutoHotkey fängt die Kürzel ab, bevor Windows sie sieht, und reicht sie an WinMarchy weiter: Fensterbefehle gehen an GlazeWM, alles andere erledigt WinMarchy selbst (Programme, Web-Apps, Menüs, Scratchpad, Layouts).

Tasten mit Satzzeichen sind nach ihrer **Position** benannt, wie Omarchy sie über den Keycode bindet. `Win+Minus` ist die Taste rechts neben der 0, auf einer deutschen Tastatur also `ß`. Die Finger machen unter Linux und Windows dasselbe.

Zwei Profile:

- **Business** ist immer aktiv. Statt HEY, Signal und Obsidian öffnet es Outlook, OneNote, Word und Stackfield. Fehlt ein Programm, nimmt WinMarchy die Web-Version im eigenen Browserfenster.
- **Persönlich** schaltest du unter **Tastenkürzel** dazu. Es bringt Omarchys private Web-Apps und deine Kürzel aus der `bindings.lua`. Wo es eine Business-Taste belegt, gewinnt das persönliche Kürzel; die Liste zeigt das als „ersetzt …“ an.

Eigene Kürzel legst du in WinMarchy an. Sie liegen in `C:\WinMarchy\Config\hotkeys.json`, schlagen beide Profile und gelten, sobald du sie speicherst. Updates lassen die Datei in Ruhe. **Exportieren** nimmt sie auf einen anderen Rechner mit.

**Importieren** liest einen WinMarchy-Export, Omarchys `bindings.lua` oder Hyprlands `bindings.conf`. Danach zeigt WinMarchy jedes Kürzel aus der Datei mit dem, was daraus würde, und du hakst an, was du übernimmst. Vorausgewählt ist, was neu ist und keine belegte Taste nimmt. Linux-Befehle übersetzt WinMarchy, wo Windows dasselbe kann: Web-Apps, Spotify-Links und bekannte Programme. Was kein Gegenstück hat, steht ausgegraut dabei. Vor dem Übernehmen legt WinMarchy einen Sicherungspunkt an.

**Favoriten:** Am Anfang gibt es keine. Mit dem Stern auf der Seite **Tastenkürzel** markierst du welche; `Win+K` zeigt sie dann ganz oben, darunter alle anderen und zuletzt den Abschnitt „Optional“. Was in den Tabellen unten *optional* heißt, steht dort. Die Sterne liegen mit den eigenen Kürzeln in `hotkeys.json` und überstehen Updates.

## Was sich nicht 1:1 übertragen lässt

| Omarchy | Unter Windows | Warum |
|-|-|-|
| `Super+L` (Layout wechseln) | `Win+Strg+J` | `Win+L` sperrt den Rechner. Das lässt sich ohne Gruppenrichtlinie nicht abschalten, und die will in Firmen niemand anfassen. `Win+Alt+L` kam im Test nie bei WinMarchy an, Windows behält die Taste offenbar auch mit Alt. `Win+Strg+J` liegt neben `Win+J`, das die Teilung dreht. |
| `Super+Alt+Space` (Omarchy-Menü) und `Super+Space` (Programme) | `Win+Space` | Ein Menü für beides: Tippen sucht in WinMarchys Einträgen und in den Programmen des Startmenüs. |
| Super allein | `Win` allein | Öffnet Flow Launcher. Flow hört auf `Alt+F24`, eine Taste, die keine Tastatur hat; läuft Flow nicht, startet WinMarchy ihn. |
| Scrolling-Layout | `Win+Strg+J` bis „Scrolling“ | GlazeWM kann das nicht selbst, WinMarchy setzt die Spalten. Siehe [tiling.md](tiling.md). |
| `Super+Shift+L` (Sperrbildschirm-Explorer) | nicht belegt | Windows hat kein Gegenstück. |
| Spotify-Playlists mit Autoplay | Playlist öffnet sich | Unter Windows gibt es kein `playerctl`. Spotify zeigt die Playlist, starten musst du selbst. |
| `Super+J` (Teilung drehen) | `Win+J` | Gleiche Taste. GlazeWMs eigener Befehl gilt erst fürs nächste Fenster, WinMarchy baut die Teilung deshalb selbst um, damit man sie sofort sieht. Das alte Verhalten liegt auf `Win+Alt+J`. |
| Minimieren | gibt es in Omarchy nicht | WinMarchy holt minimierte Fenster sofort zurück (abschaltbar unter **Tiling**). `Win+M` holt alles zurück, was trotzdem minimiert ist. |
| `Print` (Bildschirmfoto) | `Druck` | Snipping Tool oder Greenshot, umschaltbar unter **Einstellungen**. |
| Idle-Sperre (`Super+Ctrl+I`) | Wach bleiben | Windows regelt die Sperre über Energieoptionen und oft per Richtlinie. WinMarchy verhindert nur den Ruhezustand, solange der Schalter an ist. |

## Aus deiner bindings.lua

Diese Kürzel stehen im Profil **Persönlich**:

| Windows | Was passiert | Herkunft |
|-|-|-|
| `Win+Umschalt+A` | ChatGPT | Omarchy-Standard |
| `Win+Umschalt+Alt+A` | Grok | Omarchy-Standard |
| `Win+Umschalt+Y` | YouTube | Omarchy-Standard |
| `Win+Umschalt+P` | Google Fotos | Omarchy-Standard |
| `Win+Umschalt+X` | X | Omarchy-Standard |
| `Win+Umschalt+Alt+X` | X-Beitrag | Omarchy-Standard |
| `Win+Umschalt+Alt+S` | Signal (Win+Umschalt+G ist Stackfield.) | bindings.lua: Super + Umschalt + G |
| `Win+Umschalt+O` | Obsidian | Omarchy-Standard |
| `Win+Umschalt+Alt+G` | WhatsApp Web | Omarchy-Standard |
| `Win+Umschalt+Strg+G` | Google Messages | Omarchy-Standard |
| `Win+Umschalt+Slash` | Passwörter (Bitwarden) | Super + Umschalt + SLASH (1Password) |
| `Win+Umschalt+T` | Telegram | bindings.lua: Super + Umschalt + T |
| `Win+Umschalt+M` | Spotify | bindings.lua: Super + Umschalt + M |
| `Win+Alt+W` | Spotify: Discover Weekly (Öffnet die Playlist in Spotify; abspielen startest du dort.) | bindings.lua: Super + Alt + W |
| `Win+Alt+R` | Spotify: Release Radar | bindings.lua: Super + Alt + R |
| `Win+Umschalt+Q` | WhatsApp | bindings.lua: Super + Umschalt + Q |
| `Win+Alt+M` | Google Maps | bindings.lua: Super + Alt + M |
| `Win+Alt+N` | Benachrichtigungen | bindings.lua: Super + Alt + N |
| `Win+Alt+B` | Bluetooth | bindings.lua: Super + Alt + B |
| `Win+Alt+V` | VPN | bindings.lua: Super + Alt + V |
| `Win+Alt+C` | Smartphone-Link | bindings.lua: Super + Alt + C (omaconnect) |
| `Win+Alt+Y` | Bildschirme | bindings.lua: Super + Alt + Y (screens) |

`Win+Shift+S` bleibt im Profil frei: Du hattest Super+Shift+S auf das Bildschirmfoto gelegt, und unter Windows macht genau diese Taste das schon. Ist unter **Einstellungen** Greenshot gewählt, fängt WinMarchy die Taste ab und öffnet Greenshots Bereichsauswahl statt des Snipping Tools, solange du nichts Eigenes darauf legst. `Win+Alt+P` (bei dir Omaplug) öffnet WinMarchy selbst. Signal liegt auf `Win+Umschalt+Alt+S`, weil `Win+Umschalt+G` jetzt Stackfield öffnet, in beiden Profilen.

Eigene Skripte legst du als Kürzel vom Typ „Skript“ an. PowerShell-Skripte laufen unter der Ausführungsrichtlinie des Rechners; WinMarchy umgeht sie nicht.

## Business-Profil

### Fenster

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+W` | Fenster schließen | gleich |
| `Win+Q` | Fenster schließen · *optional* | gleich |
| `Win+J` | Teilung drehen (Nebeneinander wird übereinander und zurück, sofort.) | gleich |
| `Win+Alt+J` | Teilung für das nächste Fenster · *optional* | gleich |
| `Win+T` | Schwebend oder gekachelt (Das Fenster verlässt die Kacheln; andere Fenster können es überdecken.) | gleich |
| `Win+O` | Herauslösen und oben halten (Schwebt und bleibt über allen anderen Fenstern.) | gleich |
| `Win+F` | Vollbild | gleich |
| `Win+Alt+F` | Volle Breite | gleich |
| `Win+Strg+J` | Layout wechseln: Dwindle, Spalten, Scrolling (Win+L gehört unter Windows dem Sperren, und auch Win+Alt+L kommt nicht immer an.) | Super + L |
| `Win+Left` | Fokus nach links | gleich |
| `Win+Right` | Fokus nach rechts | gleich |
| `Win+Up` | Fokus nach oben | gleich |
| `Win+Down` | Fokus nach unten | gleich |
| `Win+Umschalt+Left` | Fenster nach links tauschen | gleich |
| `Win+Umschalt+Right` | Fenster nach rechts tauschen | gleich |
| `Win+Umschalt+Up` | Fenster nach oben tauschen | gleich |
| `Win+Umschalt+Down` | Fenster nach unten tauschen | gleich |
| `Win+M` | Minimierte Fenster zurückholen (Falls doch einmal etwas minimiert wurde.) | – (Omarchy minimiert nicht) |
| `Win+S` | Scratchpad zeigen oder verstecken | gleich |
| `Win+Alt+S` | Fenster ins Scratchpad oder wieder heraus (Auf einem Scratchpad-Fenster holt es das Fenster zurück in die Kacheln.) | gleich |
| `Win+Umschalt+Backspace` | Abstände ein oder aus · *optional* | gleich |
| `Win+Umschalt+Alt+R` | GlazeWM neu laden · *optional* | gleich |
| `Win+Umschalt+Alt+P` | Tiling pausieren · *optional* | gleich |

### Workspaces

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+1…0` | Workspace 1–10 | gleich |
| `Win+Umschalt+1…0` | Fenster auf Workspace 1–10 | gleich |
| `Win+Tab` | Nächster Workspace | gleich |
| `Win+Umschalt+Tab` | Vorheriger Workspace | gleich |
| `Win+Strg+Tab` | Zuletzt benutzter Workspace · *optional* | gleich |
| `Win+Strg+D` | Neuer Workspace auf jedem Bildschirm (Welche Programme dort starten, legst du unter Tiling fest.) | gleich |
| `Win+Strg+Right` | Alle Bildschirme: nächster Workspace (Lässt sich aufs Drei-Finger-Wischen legen.) | gleich |
| `Win+Strg+Left` | Alle Bildschirme: vorheriger Workspace (Lässt sich aufs Drei-Finger-Wischen legen.) | gleich |
| `Win+WheelDown` | Nächster Workspace (Mausrad) · *optional* | gleich |
| `Win+WheelUp` | Vorheriger Workspace (Mausrad) · *optional* | gleich |

### Programme

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+Enter` | Terminal | gleich |
| `Win+Umschalt+Enter` | Zweiter Browser (Welcher Browser, legst du in den Einstellungen fest.) | gleich |
| `Win+Umschalt+B` | Browser | gleich |
| `Win+Umschalt+Alt+B` | Browser (privat) · *optional* | gleich |
| `Win+Umschalt+F` | Dateimanager | gleich |
| `Win+N` | Editor | gleich |
| `Win+Umschalt+N` | Code-Editor (VS Code) | Super + Umschalt + N (Neovim) |
| `Win+Strg+Enter` | Herdr | gleich |
| `Win+Alt+Enter` | Pi (Coding-Agent) | gleich |
| `Win+Umschalt+M` | Spotify (Web-App) | gleich |
| `Win+Umschalt+Q` | Rechner | gleich |
| `Win+Umschalt+T` | Task-Manager | Super + Umschalt + T (btop) |
| `Win+Umschalt+E` | E-Mail (Outlook) | Super + Umschalt + E (HEY) |
| `Win+Umschalt+Alt+E` | Neue E-Mail · *optional* | gleich |
| `Win+Umschalt+C` | Kalender | Super + Umschalt + C (HEY Calendar) |
| `Win+Umschalt+G` | Stackfield | Super + Umschalt + G (Signal) |
| `Win+Umschalt+O` | Notizen (OneNote) | Super + Umschalt + O (Obsidian) |
| `Win+Umschalt+W` | Schreiben (Word) | Super + Umschalt + W (Omawrite) |
| `Win+Alt+P` | WinMarchy öffnen | gleich |

### Menüs

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+Space` | WinMarchy-Menü und Programme (Tippen sucht auch in den Programmen des Startmenüs. Win allein öffnet Flow Launcher.) | Super + Alt + SPACE und Super + SPACE |
| `Win+Escape` | Systemmenü | gleich |
| `Win+K` | Tastenkürzel anzeigen | gleich |
| `Win+Strg+T` | Theme wählen | Super + Strg + Umschalt + SPACE |
| `Win+Strg+Space` | Nächstes Hintergrundbild | gleich |
| `Win+Strg+Alt+Space` | Hintergrund wählen | gleich |
| `Win+Umschalt+Space` | Leiste ein oder aus · *optional* | gleich |
| `Win+Strg+C` | Aufnahme-Menü · *optional* | gleich |
| `Win+Strg+O` | Schalter-Menü · *optional* | gleich |
| `Win+Strg+H` | Hardware-Menü · *optional* | gleich |
| `Win+Strg+E` | Emojis | gleich |
| `Win+Alt+D` | Kalender der Leiste (nur mit YASB) · *optional* | gleich |
| `Win+Alt+A` | Lautstärke der Leiste (nur mit YASB) · *optional* | gleich |

### Werkzeuge

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+C` | Kopieren (auch im Terminal) | gleich |
| `Win+V` | Einfügen (auch im Terminal) | gleich |
| `Win+X` | Ausschneiden | gleich |
| `Win+A` | Alles markieren | gleich |
| `Win+Strg+V` | Zwischenablage-Verlauf | gleich |
| `Win+Strg+M` | Musik abspielen oder anhalten | gleich |
| `PrintScreen` | Bildschirmfoto (Mit Windows oder Greenshot, je nach Einstellung.) | gleich |
| `Win+Strg+Alt+C` | Bildschirmfoto | gleich |
| `Alt+PrintScreen` | Bildschirmaufnahme | gleich |
| `Win+Comma` | Benachrichtigungen | gleich |
| `Win+Slash` | Skalierung größer · *optional* | gleich |
| `Win+Alt+Slash` | Skalierung kleiner · *optional* | gleich |
| `Win+Strg+Z` | Lupe vergrößern · *optional* | gleich |
| `Win+Strg+Alt+Z` | Lupe beenden · *optional* | gleich |
| `Win+Strg+Alt+T` | Uhrzeit zeigen · *optional* | gleich |
| `Win+Strg+Alt+B` | Akku zeigen · *optional* | gleich |
| `Win+Strg+Alt+D` | Kalender aufklappen · *optional* | gleich |
| `Win+Strg+Alt+E` | Weltuhr · *optional* | gleich |

### System

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+Strg+L` | Sperren | gleich |
| `Win+Strg+I` | Bildschirm wach halten · *optional* | Super + Strg + I (Idle-Sperre) |
| `Win+Strg+A` | Audio · *optional* | gleich |
| `Win+Strg+B` | Bluetooth · *optional* | gleich |
| `Win+Strg+Umschalt+D` | Anzeige · *optional* | gleich |
| `Win+Strg+N` | Netzwerk · *optional* | gleich |
| `Win+Strg+P` | Energie · *optional* | gleich |
| `Strg+Alt+Umschalt+T` | Windows-Taskleiste ein oder aus · *optional* | gleich |

## Zwei Wege, die Tasten abzufangen

**AutoHotkey** (Standard) fängt jedes Kürzel ab und schickt es in etwa einer Millisekunde per `WM_COPYDATA` an WinMarchy. Nur so geht die Win-Taste allein und das Mausrad mit Win. Läuft WinMarchy gerade nicht, schickt das Skript Fensterbefehle direkt an GlazeWMs Kommandozeile und startet für den Rest `WinMarchy.exe --action …`.

**GlazeWM** ist für Rechner gedacht, auf denen AutoHotkey gesperrt ist. GlazeWM bindet die Kürzel dann selbst; was kein Fensterbefehl ist, läuft über `shell-exec WinMarchy.exe --action …`. Die Win-Taste allein und das Mausrad gehen so nicht.

Vor jeder Änderung prüft AutoHotkey das neue Skript mit `/Validate`. Hat es einen Fehler, bleibt das alte aktiv.
