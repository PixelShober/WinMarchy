# Tiling: Omarchys Layouts mit GlazeWM

Omarchy kennt pro Workspace drei Layouts, `Super+L` schaltet durch: **Dwindle**, **Scrolling** und **Floating**. GlazeWM hat von Haus aus nur ein Verhalten: Ein neues Fenster teilt den Container in die Richtung, die gerade eingestellt ist. Die Frage war, ob das reicht. Kurz: Für Dwindle und Floating ja, mit Hilfe. Für Scrolling nicht, Spalten ist der nächstbeste Ersatz.

WinMarchy hält dafür eine Verbindung zu GlazeWM offen (WebSocket auf `127.0.0.1:6123`), hört auf `focus_changed` und `window_managed` und gibt GlazeWM die passenden Befehle. Welcher Workspace welches Layout hat, steht in `C:\WinMarchy\Config\layouts.json`. Workspaces ohne Eintrag folgen dem Standard-Layout aus **Tiling**.

`Win+Alt+L` wechselt Dwindle → Spalten → Schwebend, `Win+Alt+Umschalt+L` geht zurück. Auf `Win+L` liegt unter Windows immer das Sperren.

## Dwindle

Hyprlands Dwindle teilt jedes neue Fenster entlang der längeren Seite des fokussierten. GlazeWM macht das nicht selbst, aber `set-tiling-direction` auf ein einzelnes Fenster packt es in einen neuen Container mit dieser Richtung. WinMarchy setzt bei jedem Fokuswechsel die Richtung passend zur Form des Fensters: breit → horizontal, hoch → vertikal. Das nächste Fenster nimmt dann die Hälfte des fokussierten, genau wie in Omarchy.

Unterschied zu Hyprland: Die Teilung folgt dem Fokus, nicht der Mausposition. Wer eine Teilung von Hand drehen will, nimmt `Win+J`.

## Scrolling → Spalten

Bei Omarchys Scrolling (Hyprlands `scrolling`-Layout, Niri-Stil) sind Spalten breiter als der Bildschirm erlaubt, und der Workspace rollt seitlich. GlazeWM zeichnet jedes gekachelte Fenster immer innerhalb des Monitors; dafür gibt es keine Einstellung und keinen Befehl.

Spalten kommt dem am nächsten: Der Workspace bleibt horizontal, jedes neue Fenster wird eine weitere Spalte. `Win+Minus` und `Win+Gleich` ändern die Breite der fokussierten Spalte, wie in Omarchy. Was fehlt, ist das Rollen über den Rand.

Wer echtes Scrolling braucht, muss zu Komorebi oder einem anderen Window-Manager. Komorebi ist für den Einsatz in Firmen kostenpflichtig, deshalb setzt WinMarchy darauf nicht.

## Floating

Alle Fenster des Workspace schweben zentriert, neue ebenso. Wechselst du zurück auf ein gekacheltes Layout, holt WinMarchy genau die Fenster zurück ins Raster, die es selbst losgelöst hat. Fenster, die du vorher schon von Hand schweben ließt (`Win+T`), bleiben schwebend.

## Manuell

GlazeWM ohne Eingriff. Gut für Leute, die GlazeWM schon kennen.

## Was sonst noch fehlt und wie es gelöst ist

| Omarchy | GlazeWM | WinMarchy |
|-|-|-|
| Scratchpad (`Super+S`) | gibt es nicht | ein versteckter Workspace „scratchpad“; `Win+Alt+S` legt das Fenster hinein, `Win+S` holt es schwebend auf den aktuellen Workspace und wieder zurück |
| Nächster/vorheriger Workspace | springt auch auf den Scratchpad | `Win+Tab` überspringt ihn |
| Fenster auf den Nachbarmonitor | nur Workspaces | `Win+Umschalt+Strg+Pfeil` sucht den Monitor in der Richtung und schiebt das Fenster auf dessen Workspace |
| Minimierte zurückholen (`Super+Home`) | nein | `Win+Home` stellt alle minimierten Fenster des Workspace wieder her |
| Fenster durchsichtig (`Super+Backspace`) | Transparenz nur global | WinMarchy merkt sich pro Fenster, ob es durchsichtig ist |
| Abstände aus (`Super+Umschalt+Backspace`) | nur per Konfiguration | WinMarchy schreibt die Konfiguration ohne Abstände und lädt sie neu; ein zweiter Druck stellt sie wieder her |
| Rahmen in der Akzentfarbe, eckig | ja | kommt aus dem Theme, 2 px wie in Omarchy |
| Titelleisten weg | ja | Schalter unter **Tiling**; manche Programme zeichnen ihre eigene und machen nicht mit |
