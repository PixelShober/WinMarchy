# Tastenkürzel: Omarchy auf Windows

Omarchy bindet fast alles an Super. Unter Windows übernimmt die Win-Taste diese Rolle. AutoHotkey fängt die Kürzel ab, bevor Windows sie sieht, und reicht sie an WinMarchy weiter: Fensterbefehle gehen an GlazeWM, alles andere erledigt WinMarchy selbst (Programme, Web-Apps, Menüs, Scratchpad, Layouts).

Tasten mit Satzzeichen sind nach ihrer **Position** benannt, wie Omarchy sie über den Keycode bindet. `Win+Minus` ist die Taste rechts neben der 0, auf einer deutschen Tastatur also `ß`. Die Finger machen unter Linux und Windows dasselbe.

Zwei Profile:

- **Business** ist immer aktiv. Statt HEY, Signal, Obsidian und ChatGPT öffnet es Outlook, Teams, OneNote, Word und Copilot. Fehlt ein Programm, nimmt WinMarchy die Web-Version im eigenen Edge-Fenster.
- **Persönlich** schaltest du unter **Tastenkürzel** dazu. Es bringt Omarchys private Web-Apps und deine Kürzel aus der `bindings.lua`. Wo es eine Business-Taste belegt, gewinnt das persönliche Kürzel; die Liste zeigt das als „ersetzt …“ an.

Eigene Kürzel legst du in WinMarchy an. Sie liegen in `C:\WinMarchy\Config\hotkeys.json` und schlagen beide Profile.

## Was sich nicht 1:1 übertragen lässt

| Omarchy | Unter Windows | Warum |
|-|-|-|
| `Super+L` (Layout wechseln) | `Win+Alt+L` | `Win+L` sperrt den Rechner. Das lässt sich ohne Gruppenrichtlinie nicht abschalten, und die will in Firmen niemand anfassen. |
| `Super+Space` (Omarchy-Menü) | `Win+Alt+Space` | `Win` allein und `Win+Space` öffnen die Suche, wie Super allein in Omarchy. |
| `Super+Alt+Space` (Programme) | `Win+Space` oder `Win` allein | Flow Launcher hört auf `Alt+F24`, eine Taste, die keine Tastatur hat. So kommt sich nichts in die Quere. |
| Scrolling-Layout | Spalten | GlazeWM kann Fenster nicht über den Bildschirmrand hinaus laufen lassen. Siehe [tiling.md](tiling.md). |
| `Super+Shift+L` (Sperrbildschirm-Explorer) | nicht belegt | Windows hat kein Gegenstück. |
| Spotify-Playlists mit Autoplay | Playlist öffnet sich | Unter Windows gibt es kein `playerctl`. Spotify zeigt die Playlist, starten musst du selbst. |
| `Super+J` (Teilung drehen) | `Win+J` | Gleiche Taste. GlazeWMs eigener Befehl gilt erst fürs nächste Fenster, WinMarchy baut die Teilung deshalb selbst um, damit man sie sofort sieht. Das alte Verhalten liegt auf `Win+Alt+J`. |
| Minimieren | gibt es in Omarchy nicht | Unter **Tiling** lässt sich einschalten, dass minimierte Fenster ins Scratchpad wandern. |
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
| `Win+Strg+T` | Connect-Login mit Defender-Token (Führt das Skript aus, das du in den Einstellungen unter „Persönliches Skript“ einträgst.) | bindings.lua: Super + Strg + T |
| `Win+Alt+N` | Benachrichtigungen | bindings.lua: Super + Alt + N |
| `Win+Alt+B` | Bluetooth | bindings.lua: Super + Alt + B |
| `Win+Alt+V` | VPN | bindings.lua: Super + Alt + V |
| `Win+Alt+C` | Smartphone-Link | bindings.lua: Super + Alt + C (omaconnect) |
| `Win+Alt+Y` | Bildschirme | bindings.lua: Super + Alt + Y (screens) |

`Win+Shift+S` bleibt bewusst frei: Du hattest Super+Shift+S auf das Bildschirmfoto gelegt, und unter Windows macht genau diese Taste das schon (Snipping Tool). `Win+Alt+P` (bei dir Omaplug) öffnet WinMarchy selbst. Signal liegt auf `Win+Umschalt+Alt+S`, weil `Win+Umschalt+G` jetzt Stackfield öffnet, in beiden Profilen.

Das Skript hinter `Win+Ctrl+T` trägst du unter **Einstellungen → Persönliches Skript** ein. PowerShell-Skripte laufen unter der Ausführungsrichtlinie des Rechners; WinMarchy umgeht sie nicht.

## Business-Profil

### Fenster

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+W` | Fenster schließen | gleich |
| `Win+Q` | Fenster schließen | gleich |
| `Win+J` | Teilung drehen (Nebeneinander wird übereinander und zurück, sofort.) | gleich |
| `Win+Alt+J` | Teilung für das nächste Fenster | gleich |
| `Win+T` | Schwebend oder gekachelt | gleich |
| `Win+F` | Vollbild | gleich |
| `Win+Alt+F` | Volle Breite | gleich |
| `Win+O` | Herauslösen und oben halten | gleich |
| `Win+Alt+L` | Layout des Workspace wechseln (Win+L sperrt unter Windows immer den Rechner.) | Super + L |
| `Win+Alt+Umschalt+L` | Layout zurück | Super + Alt + L |
| `Win+Left` | Fokus nach links | gleich |
| `Win+Right` | Fokus nach rechts | gleich |
| `Win+Up` | Fokus nach oben | gleich |
| `Win+Down` | Fokus nach unten | gleich |
| `Win+Umschalt+Left` | Fenster nach links tauschen | gleich |
| `Win+Umschalt+Right` | Fenster nach rechts tauschen | gleich |
| `Win+Umschalt+Up` | Fenster nach oben tauschen | gleich |
| `Win+Umschalt+Down` | Fenster nach unten tauschen | gleich |
| `Win+Minus` | Schmaler | gleich |
| `Win+Equal` | Breiter | gleich |
| `Win+Umschalt+Minus` | Niedriger | gleich |
| `Win+Umschalt+Equal` | Höher | gleich |
| `Win+Alt+Minus` | Etwas schmaler | gleich |
| `Win+Alt+Equal` | Etwas breiter | gleich |
| `Win+Alt+Umschalt+Minus` | Etwas niedriger | gleich |
| `Win+Alt+Umschalt+Equal` | Etwas höher | gleich |
| `Win+Strg+Minus` | Viel schmaler | gleich |
| `Win+Strg+Equal` | Viel breiter | gleich |
| `Win+Strg+Umschalt+Minus` | Viel niedriger | gleich |
| `Win+Strg+Umschalt+Equal` | Viel höher | gleich |
| `Win+M` | Minimieren | gleich |
| `Win+Home` | Minimierte Fenster zurückholen (Omarchy stellt hier die gespeicherte Fensterbreite wieder her; das kann GlazeWM nicht.) | Super + HOME |
| `Win+S` | Scratchpad zeigen oder verstecken | gleich |
| `Win+Alt+S` | Fenster ins Scratchpad | gleich |
| `Win+Backspace` | Fenster durchsichtig | gleich |
| `Win+Umschalt+Backspace` | Abstände ein oder aus | gleich |
| `Win+Umschalt+Alt+R` | GlazeWM neu laden | gleich |
| `Win+Umschalt+Alt+P` | Tiling pausieren | gleich |

### Workspaces

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+1…0` | Workspace 1–10 | gleich |
| `Win+Umschalt+1…0` | Fenster auf Workspace 1–10 | gleich |
| `Win+Umschalt+Alt+1…0` | Fenster still auf Workspace 1–10 | gleich |
| `Win+Tab` | Nächster Workspace | gleich |
| `Win+Umschalt+Tab` | Vorheriger Workspace | gleich |
| `Win+Strg+Tab` | Zuletzt benutzter Workspace | gleich |
| `Win+Strg+D` | Neuer Workspace auf jedem Bildschirm (Wie ein neuer virtueller Desktop in Windows.) | gleich |
| `Win+Strg+Right` | Alle Bildschirme: nächster Workspace (Lässt sich aufs Drei-Finger-Wischen legen.) | gleich |
| `Win+Strg+Left` | Alle Bildschirme: vorheriger Workspace (Lässt sich aufs Drei-Finger-Wischen legen.) | gleich |
| `Win+WheelDown` | Nächster Workspace (Mausrad) | gleich |
| `Win+WheelUp` | Vorheriger Workspace (Mausrad) | gleich |
| `Win+Umschalt+Alt+Left` | Workspace auf linken Monitor | gleich |
| `Win+Umschalt+Alt+Right` | Workspace auf rechten Monitor | gleich |
| `Win+Umschalt+Alt+Up` | Workspace auf oberen Monitor | gleich |
| `Win+Umschalt+Alt+Down` | Workspace auf unteren Monitor | gleich |
| `Win+Umschalt+Strg+Left` | Fenster auf linken Monitor | gleich |
| `Win+Umschalt+Strg+Right` | Fenster auf rechten Monitor | gleich |
| `Win+Umschalt+Strg+Up` | Fenster auf oberen Monitor | gleich |
| `Win+Umschalt+Strg+Down` | Fenster auf unteren Monitor | gleich |

### Programme

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+Enter` | Terminal | gleich |
| `Win+Umschalt+Enter` | Browser | gleich |
| `Win+Umschalt+B` | Browser | gleich |
| `Win+Umschalt+Alt+B` | Browser (privat) | gleich |
| `Win+Umschalt+F` | Dateimanager | gleich |
| `Win+N` | Editor | gleich |
| `Win+Umschalt+N` | Code-Editor (VS Code) | Super + Umschalt + N (Neovim) |
| `Win+Strg+Enter` | Herdr | gleich |
| `Win+Alt+Enter` | Pi (Coding-Agent) | gleich |
| `Win+Umschalt+Q` | Rechner | gleich |
| `Win+Umschalt+T` | Task-Manager | Super + Umschalt + T (btop) |
| `Win+Umschalt+E` | E-Mail (Outlook) | Super + Umschalt + E (HEY) |
| `Win+Umschalt+Alt+E` | Neue E-Mail | gleich |
| `Win+Umschalt+C` | Kalender | Super + Umschalt + C (HEY Calendar) |
| `Win+Umschalt+G` | Stackfield | Super + Umschalt + G (Signal) |
| `Win+Umschalt+Alt+T` | Teams | gleich |
| `Win+Umschalt+O` | Notizen (OneNote) | Super + Umschalt + O (Obsidian) |
| `Win+Umschalt+W` | Schreiben (Word) | Super + Umschalt + W (Omawrite) |
| `Win+Umschalt+A` | Copilot | Super + Umschalt + A (ChatGPT) |
| `Win+Alt+P` | WinMarchy öffnen | gleich |

### Menüs

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+Space` | Programme suchen | Super + Alt + SPACE |
| `Win+Alt+Space` | WinMarchy-Menü | Super + SPACE |
| `Win+Escape` | Systemmenü | gleich |
| `Win+K` | Tastenkürzel anzeigen | gleich |
| `Win+Umschalt+Space` | Leiste ein oder aus | gleich |
| `Win+Strg+Space` | Nächstes Hintergrundbild | gleich |
| `Win+Strg+Alt+Space` | Hintergrund wählen | gleich |
| `Win+Strg+W` | Nächstes Hintergrundbild | gleich |
| `Win+Strg+Umschalt+Space` | Theme wählen | gleich |
| `Win+Strg+T` | Theme wählen | gleich |
| `Win+Strg+C` | Aufnahme-Menü | gleich |
| `Win+Strg+O` | Schalter-Menü | gleich |
| `Win+Strg+H` | Hardware-Menü | gleich |
| `Win+Strg+E` | Emojis | gleich |

### Werkzeuge

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+C` | Kopieren (auch im Terminal) | gleich |
| `Win+V` | Einfügen (auch im Terminal) | gleich |
| `Win+X` | Ausschneiden | gleich |
| `Win+A` | Alles markieren | gleich |
| `Win+Strg+V` | Zwischenablage-Verlauf | gleich |
| `Win+Strg+Alt+C` | Bildschirmfoto | gleich |
| `Alt+PrintScreen` | Bildschirmaufnahme | gleich |
| `Win+Comma` | Benachrichtigungen | gleich |
| `Win+Slash` | Skalierung größer | gleich |
| `Win+Alt+Slash` | Skalierung kleiner | gleich |
| `Win+Strg+Z` | Lupe vergrößern | gleich |
| `Win+Strg+Alt+Z` | Lupe beenden | gleich |
| `Win+Strg+Alt+T` | Uhrzeit zeigen | gleich |
| `Win+Strg+Alt+B` | Akku zeigen | gleich |
| `Win+Strg+Alt+D` | Kalender aufklappen | gleich |
| `Win+Strg+Alt+E` | Weltuhr | gleich |

### System

| Windows | Was passiert | Omarchy |
|-|-|-|
| `Win+Strg+L` | Sperren | gleich |
| `Win+Strg+I` | Bildschirm wach halten | Super + Strg + I (Idle-Sperre) |
| `Win+Strg+A` | Audio | gleich |
| `Win+Strg+B` | Bluetooth | gleich |
| `Win+Strg+Umschalt+D` | Anzeige | gleich |
| `Win+Strg+N` | Netzwerk | gleich |
| `Win+Strg+P` | Energie | gleich |
| `Strg+Alt+Umschalt+T` | Windows-Taskleiste ein oder aus | gleich |

## Zwei Wege, die Tasten abzufangen

**AutoHotkey** (Standard) fängt jedes Kürzel ab und schickt es in etwa einer Millisekunde per `WM_COPYDATA` an WinMarchy. Nur so geht die Win-Taste allein und das Mausrad mit Win. Läuft WinMarchy gerade nicht, schickt das Skript Fensterbefehle direkt an GlazeWMs Kommandozeile und startet für den Rest `WinMarchy.exe --action …`.

**GlazeWM** ist für Rechner gedacht, auf denen AutoHotkey gesperrt ist. GlazeWM bindet die Kürzel dann selbst; was kein Fensterbefehl ist, läuft über `shell-exec WinMarchy.exe --action …`. Die Win-Taste allein und das Mausrad gehen so nicht.

Vor jeder Änderung prüft AutoHotkey das neue Skript mit `/Validate`. Hat es einen Fehler, bleibt das alte aktiv.
