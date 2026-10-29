# Tiling: Omarchys Layouts mit GlazeWM

Omarchy kennt pro Workspace drei Layouts, `Super+L` schaltet durch: **Dwindle**, **Scrolling** und **Floating**. GlazeWM hat von Haus aus nur ein Verhalten: Ein neues Fenster teilt den Container in die Richtung, die gerade eingestellt ist. Die Frage war, ob das reicht. Kurz: Für Dwindle und Floating ja, mit Hilfe. Für Scrolling nicht, Spalten ist der nächstbeste Ersatz.

WinMarchy hält dafür eine Verbindung zu GlazeWM offen (WebSocket auf `127.0.0.1:6123`), hört auf `focus_changed` und `window_managed` und gibt GlazeWM die passenden Befehle. Welcher Workspace welches Layout hat, steht in `C:\WinMarchy\Config\layouts.json`. Workspaces ohne Eintrag folgen dem Standard-Layout aus **Tiling**.

`Win+Alt+L` wechselt Dwindle → Spalten → Schwebend, `Win+Alt+Umschalt+L` geht zurück. Auf `Win+L` liegt unter Windows immer das Sperren. Oben rechts erscheint kurz, welches Layout der Workspace jetzt hat.

GlazeWM ordnet nur neue Fenster nach einem Layout an. Damit ein Wechsel sofort sichtbar ist, nimmt WinMarchy die Fenster des Workspace kurz heraus und setzt sie in der neuen Anordnung wieder ein: bei Spalten nebeneinander in der alten Reihenfolge, bei Dwindle jedes in die Hälfte des vorigen. Das dauert einen Augenblick, die Fenster blinken dabei einmal.

## Dwindle

Hyprlands Dwindle teilt jedes neue Fenster entlang der längeren Seite des fokussierten. GlazeWM macht das nicht selbst, aber `set-tiling-direction` auf ein einzelnes Fenster packt es in einen neuen Container mit dieser Richtung. WinMarchy setzt bei jedem Fokuswechsel die Richtung passend zur Form des Fensters: breit → horizontal, hoch → vertikal. Das nächste Fenster nimmt dann die Hälfte des fokussierten, genau wie in Omarchy.

Unterschied zu Hyprland: Die Teilung folgt dem Fokus, nicht der Mausposition.

`Win+J` dreht die Teilung wie in Omarchy sofort: Zwei Fenster nebeneinander stehen danach übereinander und umgekehrt. GlazeWMs eigener Befehl `toggle-tiling-direction` legt nur fest, wohin das nächste Fenster kommt, und man sieht nichts. WinMarchy packt deshalb das Nachbarfenster in einen eigenen Container mit der anderen Richtung und schiebt das fokussierte dazu. Bei tief verschachtelten Teilungen geht das nicht; dann gilt die Drehung fürs nächste Fenster, und eine Meldung sagt das. GlazeWMs Verhalten liegt weiter auf `Win+Alt+J`.

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
| Scratchpad (`Super+S`) | gibt es nicht | siehe unten |
| Nächster/vorheriger Workspace | springt auch auf den Scratchpad | `Win+Tab` überspringt ihn |
| Fenster auf den Nachbarmonitor | nur Workspaces | `Win+Umschalt+Strg+Pfeil` sucht den Monitor in der Richtung und schiebt das Fenster auf dessen Workspace |
| Virtuelle Desktops über alle Bildschirme | Workspaces gehören je einem Monitor | `Win+Strg+D` gibt jedem Bildschirm einen leeren Workspace, `Win+Strg+Pfeil` schaltet alle Bildschirme zusammen weiter, siehe unten |
| Minimieren | gibt es in Omarchy nicht | Schalter unter **Tiling**: Ein minimiertes Fenster landet im Scratchpad statt in der Taskleiste |
| Minimierte zurückholen (`Super+Home`) | nein | `Win+Home` stellt alle minimierten Fenster des Workspace wieder her |
| Fenster durchsichtig (`Super+Backspace`) | Transparenz nur global | WinMarchy merkt sich pro Fenster, ob es durchsichtig ist |
| Abstände aus (`Super+Umschalt+Backspace`) | nur per Konfiguration | WinMarchy schreibt die Konfiguration ohne Abstände und lädt sie neu; ein zweiter Druck stellt sie wieder her |
| Rahmen in der Akzentfarbe, eckig | ja | kommt aus dem Theme, 2 px wie in Omarchy |
| Titelleisten weg | ja | Schalter unter **Tiling**; manche Programme zeichnen ihre eigene und machen nicht mit |

## Scratchpad

In Omarchy ist das Scratchpad ein eigener Workspace, der über dem aktuellen eingeblendet wird. WinMarchy bildet das so nach:

- `Win+Alt+S` legt das fokussierte Fenster ins Scratchpad. Es verschwindet sofort und bleibt dort, auch wenn du den Workspace wechselst.
- `Win+S` blendet alle Scratchpad-Fenster über dem aktuellen Workspace ein, mittig, schwebend, 80 % groß und immer oben. Sie werden dabei nicht Teil des Layouts; die anderen Fenster bleiben, wo sie sind.
- Noch einmal `Win+S` blendet sie wieder aus. Auf einem anderen Workspace holt `Win+S` sie dorthin.
- Ein Fenster verlässt das Scratchpad nur, wenn du es mit `Win+Umschalt+1` … `0` auf einen Workspace schiebst. Danach ist es ein ganz normales Fenster.

Technisch ist das Scratchpad ein versteckter GlazeWM-Workspace namens `scratchpad`. Die Leiste zeigt ihn nicht an, und `Win+Tab` überspringt ihn.

## Ein Workspace auf jedem Bildschirm

Windows wechselt virtuelle Desktops auf allen Bildschirmen zugleich, GlazeWM hat pro Monitor eigene Workspaces. Zwei Kürzel bringen das zusammen:

- `Win+Strg+D` gibt jedem Bildschirm einen leeren Workspace, wie ein neuer Desktop. Zeigt ein Bildschirm schon einen leeren, bleibt er.
- `Win+Strg+Rechts` und `Win+Strg+Links` schalten auf jedem Bildschirm zum nächsten oder vorigen Workspace, alle zusammen.

Das Drei-Finger-Wischen auf dem Touchpad legst du unter **Einstellungen → Bluetooth und Geräte → Touchpad → Erweiterte Gesten** auf diese Kürzel (Tiling hat dafür einen Knopf). „Desktops wechseln“ lieber nicht wählen: Windows' eigene virtuelle Desktops vertragen sich nicht mit GlazeWM.

## Minimieren

Omarchy minimiert nicht; was aus dem Weg soll, kommt auf einen anderen Workspace oder ins Scratchpad. Unter Windows bleibt Minimieren erst einmal, wie es ist, denn viele Programme rechnen damit. Wer es wie in Omarchy will, schaltet unter **Tiling** „Minimieren legt ins Scratchpad“ ein: Ein minimiertes Fenster verschwindet dann im Scratchpad, `Win+S` holt es zurück.
