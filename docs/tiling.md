# Tiling: Omarchys Layouts mit GlazeWM

Omarchy kennt mehrere Layouts, `Super+L` schaltet durch, darunter **Dwindle** und **Scrolling**. GlazeWM hat von Haus aus nur ein Verhalten: Ein neues Fenster teilt den Container in die Richtung, die gerade eingestellt ist. WinMarchy baut die drei Layouts Dwindle, Spalten und Scrolling darauf auf; Scrolling übernimmt es ganz selbst. Schweben ist kein Layout, sondern etwas für einzelne Fenster (`Win+T`, siehe unten).

Das Layout gehört zum Bildschirm, nicht zum Workspace. Es gibt mehr Workspaces als Bildschirme, und jeder Workspace folgt dem Layout des Bildschirms, auf dem er gerade steht. Eingestellt wird es unter **Tiling → Bildschirme, Layouts und Workspaces**, gleich neben den Workspaces des Bildschirms. Mit einem Bildschirm gibt es genau eine Zeile.

WinMarchy hält dafür eine Verbindung zu GlazeWM offen (WebSocket auf `127.0.0.1:6123`), hört auf `focus_changed`, `window_managed` und `window_unmanaged` und gibt GlazeWM die passenden Befehle. In `C:\WinMarchy\Config\layouts.json` steht, wie die Fenster jedes Workspace gerade angeordnet sind. Passt das nicht mehr zum Layout seines Bildschirms (du hast es geändert, oder der Workspace ist auf einen anderen Bildschirm umgezogen), ordnet WinMarchy ihn neu, sobald er zu sehen ist. Versteckte Workspaces bleiben bis dahin, wie sie sind: Umräumen heißt ihre Fenster zu fokussieren, und das würde zu ihnen springen.

`Win+Strg+J` schaltet das Layout des Bildschirms weiter, auf dem du gerade bist: Dwindle → Spalten → Scrolling → Dwindle. Ein Zurück braucht es bei drei Layouts nicht. Auf `Win+L` liegt unter Windows immer das Sperren. Oben rechts erscheint sofort, welches Layout der Bildschirm jetzt hat; das Umräumen läuft danach. Die Wahl landet auch in den Einstellungen, die Tiling-Seite zeigt sie beim nächsten Öffnen.

GlazeWM ordnet nur neue Fenster nach einem Layout an. Damit ein Wechsel auf Dwindle oder Spalten sofort sichtbar ist, nimmt WinMarchy die Fenster des Workspace kurz heraus und setzt sie in der neuen Anordnung wieder ein: bei Spalten nebeneinander in der alten Reihenfolge, bei Dwindle jedes in die Hälfte des vorigen. Die Fenster warten dabei in einem eigenen versteckten Workspace (`winmarchy-stage`), nie im Scratchpad. Bleibt nach einem Absturz doch eines dort hängen, holt WinMarchy es beim nächsten Verbinden zurück.

## Dwindle

Hyprlands Dwindle teilt jedes neue Fenster entlang der längeren Seite des fokussierten. GlazeWM macht das nicht selbst, aber `set-tiling-direction` auf ein einzelnes Fenster packt es in einen neuen Container mit dieser Richtung. WinMarchy setzt bei jedem Fokuswechsel die Richtung passend zur Form des Fensters: breit → horizontal, hoch → vertikal. Das nächste Fenster nimmt dann die Hälfte des fokussierten, genau wie in Omarchy.

Unterschied zu Hyprland: Die Teilung folgt dem Fokus, nicht der Mausposition.

`Win+J` dreht die Teilung wie in Omarchy sofort: Zwei Fenster nebeneinander stehen danach übereinander und umgekehrt. GlazeWMs eigener Befehl `toggle-tiling-direction` legt nur fest, wohin das nächste Fenster kommt, und man sieht nichts. WinMarchy packt deshalb das Nachbarfenster in einen eigenen Container mit der anderen Richtung, schiebt das fokussierte dazu und lässt GlazeWM danach neu zeichnen. Das ist nötig, weil GlazeWM nach einem Richtungswechsel von sich aus nichts neu anordnet. Bei tief verschachtelten Teilungen geht das nicht; dann gilt die Drehung fürs nächste Fenster, und eine Meldung sagt das. GlazeWMs Verhalten liegt weiter auf `Win+Alt+J`.

## Spalten

Der Workspace bleibt horizontal, jedes neue Fenster wird eine weitere Spalte, alle gleich breit.

## Scrolling

Bei Omarchys Scrolling (Hyprlands `scrolling`-Layout, Niri-Stil) stehen die Fenster als Spalten in einer Reihe, die breiter sein darf als der Bildschirm. GlazeWM zeichnet gekachelte Fenster immer innerhalb des Monitors, deshalb übernimmt WinMarchy hier das Platzieren:

- Die Fenster des Workspace schweben (in GlazeWM „floating“), WinMarchy setzt Größe und Lage. Zwei Spalten stehen nebeneinander auf dem Bildschirm.
- `Win+Links` und `Win+Rechts` gehen eine Spalte weiter. Liegt sie außerhalb, rollt die Reihe um genau so viel, dass sie ganz zu sehen ist. Am Ende der Reihe geht der Fokus auf den Bildschirm daneben, wie bei Kacheln.
- `Win+Umschalt+Links` und `Win+Umschalt+Rechts` tauschen die Spalte mit ihrer Nachbarin. Am Ende der Reihe wandert die Spalte auf den Bildschirm daneben und nimmt dort dessen Layout an, egal welche Nummer der Workspace dort hat. Für GlazeWM schweben die Spalten, es würde sie nur ein Stück verschieben.
- Ein neues Fenster kommt rechts neben das fokussierte. Ein geschlossenes hinterlässt keine Lücke.
- Spalten außerhalb des Bildschirms warten unsichtbar im versteckten Workspace `winmarchy-stage`, bis die Reihe zu ihnen rollt. Nichts liegt hinter einer sichtbaren Spalte und schimmert durch, nichts ragt auf den Nachbarmonitor. Welche Fenster zur Reihe gehören, merkt sich WinMarchy nach Fenster-Handle, damit wartende Spalten beim Verbinden nicht als verirrt gelten.

Wechselst du auf ein anderes Layout, kommen die Fenster zurück in GlazeWMs Kacheln.

Die Layouts „Schwebend“ und „Manuell“ früherer Versionen gibt es nicht mehr. Ein Workspace, der noch schwebend war, bekommt seine Fenster beim nächsten Verbinden zurück in die Kacheln; im Protokoll steht, welcher es war.

## Einzelne Fenster schweben lassen

- `Win+T`: Das Fenster verlässt das Layout und schwebt über den Kacheln, mittig und etwa so groß wie seine Kachel (mindestens 40 %, höchstens 70 % der Breite). Klickst du ein anderes Fenster an, bleibt es trotzdem sichtbar. Die übrigen Fenster schließen die Lücke.
- Noch einmal `Win+T` auf dem schwebenden Fenster, und es kommt zurück ins Layout seines Bildschirms: bei Dwindle teilt es die Kachel, die zuletzt den Fokus hatte, entlang ihrer längeren Seite (ist sein alter Platz noch da, setzt GlazeWM es dorthin zurück), bei Spalten wird es eine weitere Spalte, bei Scrolling eine Spalte rechts neben der fokussierten.
- `Win+O`: Wie Omarchys „Pop out“, nur in die Ecke: klein unten rechts, ein Drittel des Bildschirms, über allen anderen Fenstern. Gut für einen Anruf oder ein Video nebenher. Ein zweiter Druck setzt es zurück ins Layout.

Früher schickte `Win+T` nur GlazeWMs `toggle-floating`: Das Fenster verschwand hinter den Kacheln, sobald ein anderes den Fokus bekam, und zurück in den Kacheln sorgte niemand für das Layout. Im Scrolling-Layout lag es dann über dem ganzen Bildschirm. Der eigene Befehl `do:float-toggle` macht beides richtig.

## Was sonst noch fehlt und wie es gelöst ist

| Omarchy | GlazeWM | WinMarchy |
|-|-|-|
| Scratchpad (`Super+S`) | gibt es nicht | siehe unten |
| Fokus von schwebenden Fenstern aus | nur links und rechts zwischen schwebenden | `Win+Pfeil` sucht das nächste Fenster in der Richtung auf dem Bildschirm, ob gekachelt oder schwebend |
| Nächster/vorheriger Workspace | springt auch auf versteckte Workspaces | `Win+Tab` überspringt sie |
| Workspaces fest pro Bildschirm (Plugin Screens) | `bind_to_monitor` in der Konfiguration | Karte **Bildschirme, Layouts und Workspaces** unter Tiling, siehe unten |
| Virtuelle Desktops über alle Bildschirme | Workspaces gehören je einem Monitor | Arbeitsmodi (`Win+Strg+D`, `Win+Strg+1` bis `3`) geben jedem Bildschirm einen frischen Workspace mit festen Programmen, `Win+Strg+Pfeil` schaltet alle zusammen weiter |
| Minimieren | gibt es in Omarchy nicht | WinMarchy holt minimierte Fenster sofort in ihre Kachel zurück; `Win+M` holt alles zurück, was trotzdem minimiert ist |
| Abstände aus (`Super+Umschalt+Backspace`) | nur per Konfiguration | WinMarchy schreibt die Konfiguration ohne Abstände und lädt sie neu; ein zweiter Druck stellt sie wieder her |
| Rahmen in der Akzentfarbe, eckig | ja | kommt aus dem Theme, 2 px wie in Omarchy |
| Titelleisten weg | ja | Schalter unter **Tiling**; manche Programme zeichnen ihre eigene und machen nicht mit |

## Scratchpad

In Omarchy ist das Scratchpad ein eigener Workspace, der über dem aktuellen eingeblendet wird. WinMarchy bildet das so nach:

- `Win+Alt+S` legt das fokussierte Fenster ins Scratchpad. Es verschwindet sofort und bleibt dort, auch wenn du den Workspace wechselst.
- `Win+S` blendet alle Scratchpad-Fenster über dem aktuellen Workspace ein: in der Mitte, zusammen 80 % des Bildschirms, nebeneinander, immer oben. Sie gleiten wie in Omarchy kurz von unten herein und beim Ausblenden wieder hinaus; ist unter Windows „Animationseffekte“ aus, erscheinen sie ohne. Die anderen Fenster bleiben, wo sie sind.
- Noch einmal `Win+S` blendet sie wieder aus. Auf einem anderen Workspace holt `Win+S` sie dorthin.
- `Win+Pfeil` geht von einem Scratchpad-Fenster zum nächsten. Gibt es in der Richtung keines mehr, blendet sich das Scratchpad aus, und du bist wieder im Workspace.

Herauslösen, so dass es wieder ein normales Fenster ist (die übrigen Scratchpad-Fenster rücken danach zusammen und füllen die Fläche wieder):

- `Win+Alt+S` auf einem eingeblendeten Scratchpad-Fenster setzt es zurück in die Kacheln des aktuellen Workspace.
- `Win+Umschalt+1` … `0` schiebt es auf diesen Workspace, gekachelt.
- `Win+T` setzt es ins Layout des aktuellen Workspace; beim nächsten `Win+S` gehört es nicht mehr dazu.

Welche Fenster zum Scratchpad gehören, merkt sich WinMarchy in `C:\WinMarchy\Config\scratchpad.json`, nach Fenster-Handle. Ein Fenster kommt nur mit `Win+Alt+S` hinein. Liegt im versteckten Scratchpad-Workspace eines, das nie so hineinkam, holt WinMarchy es beim Verbinden auf den aktuellen Workspace zurück.

## Bildschirme, Layouts und Workspaces

Jede Zeile der Karte ist ein Bildschirm: links sein Name, wie Windows ihn kennt (der eingebaute heißt „Laptop-Bildschirm“, zwei gleiche Monitore bekommen „(1)“ und „(2)“), seine Auflösung und ob er der Hauptbildschirm ist; in der Mitte sein Layout (Dwindle, Spalten, Scrolling), rechts seine Workspaces. Wie im Omarchy-Plugin Screens bekommt jeder Bildschirm feste Workspaces. Der Hauptbildschirm hat 1 und 2, die anderen teilen sich den Rest gleichmäßig von links nach rechts. Ohne Vorgabe ist der Hauptbildschirm der von Windows. Wählst du einen anderen, bekommt er 1 und 2, und die übrigen werden neu verteilt; „Neu aufteilen“ macht dasselbe. Danach lässt sich jede Zeile von Hand ändern. Solange noch nichts festgelegt ist, zeigt die Karte diese Aufteilung als Vorschlag, und erst **Übernehmen** macht sie fest. WinMarchy schreibt die Zuordnung als `bind_to_monitor` in GlazeWMs Konfiguration:

Es gibt sechs Workspaces, an einem einzelnen Bildschirm fünf. `Win+7` bis `Win+0` gehören nicht mehr zu WinMarchy. Bei einem Bildschirm meldet `Win+6`, dass es den Workspace dort nicht gibt. Hast du früher Workspaces über 6 zugeordnet, vergisst WinMarchy diese Zuordnung; Fenster, die noch auf solchen Workspaces liegen, holt `Win+Tab` hervor.

- `Win+3` öffnet Workspace 3 immer auf seinem Bildschirm, egal wo du gerade bist.
- Ein Arbeitsmodus nimmt für jeden Bildschirm den nächsten freien seiner eigenen Workspaces. Sind die alle belegt, nimmt er einen freien, der keinem Bildschirm zugeordnet ist, oder einen, dessen Bildschirm gerade fehlt.
- Bildschirme zählen von links nach rechts, so wie GlazeWM sie zählt. Steckst du einen ab, ziehen seine Workspaces auf einen anderen um und kommen zurück, wenn er wieder da ist.
- Am Laptop ohne Dock stehen die abgesteckten Bildschirme weiter in der Karte, als „nicht angeschlossen“. Ihre Namen merkt sich WinMarchy vom letzten Mal. Ihre Workspaces bleiben ihnen zugeordnet, bis du die Zeile leerst; „Neu aufteilen“ zählt sie mit.

GlazeWM beachtet `bind_to_monitor` nur, wenn ein Workspace neu angelegt wird. Steht einer schon auf dem falschen Bildschirm, bleibt er dort. WinMarchy schiebt deshalb beim Verbinden und nach jedem **Übernehmen** unter Tiling jeden zugeordneten Workspace selbst auf seinen Bildschirm. Klappt das nicht, steht eine Warnung im Protokoll; mit dem ausführlichen Protokoll steht dort jeder Schritt.

Ohne Eintrag nimmt ein Bildschirm, was frei ist, wie bisher.

## Arbeitsmodi

Windows wechselt virtuelle Desktops auf allen Bildschirmen zugleich, GlazeWM hat pro Monitor eigene Workspaces. Die Arbeitsmodi bringen das zusammen und gehen einen Schritt weiter: Ein Modus ist ein frischer Workspace auf jedem Bildschirm mit festen Programmen, zum Beispiel „Recherche“ mit dem Browser und zwei Seiten links, den Notizen in der Mitte und dem Explorer rechts, oder „Tools“ mit Outlook, Teams und dem Fachverfahren. Unter **Tiling → Arbeitsmodi** legst du bis zu drei an.

- `Win+Strg+D` oder `Win+Strg+1` öffnet Modus 1, `Win+Strg+2` und `Win+Strg+3` die anderen beiden.
- Beim ersten Druck bekommt jeder Bildschirm einen frischen Workspace; zeigt er schon einen leeren, bleibt der. Die offenen Fenster bleiben unangetastet auf ihren Workspaces. Danach starten die Programme des Modus nacheinander, jedes auf dem Workspace seines Bildschirms.
- Läuft der Modus schon, holt die Taste seine Workspaces zurück, statt alles noch einmal zu starten. Nur was du geschlossen hast, startet neu. WinMarchy erkennt die Fenster am Fenster-Handle und merkt sie sich in `C:\WinMarchy\Config\workmodes.json`, auch über einen Neustart von WinMarchy hinweg.
- Ist der Modus schon zu sehen, bringt dieselbe Taste zurück, was vorher auf den Bildschirmen war. So springst du mit einer Taste zwischen deiner Arbeit und dem Modus hin und her.
- Ein Modus ohne Programme ist einfach ein leerer Desktop auf allen Bildschirmen.

Die Programme schreibst du wie die Aktion eines Tastenkürzels. Der Browser nimmt Adressen mit und öffnet sie als Tabs eines neuen Fensters: `app:browser https://seite-eins.de https://seite-zwei.de` (`app:browser2` für den zweiten Browser). Edge, Chrome, Brave, Vivaldi und Opera öffnen dafür ein neues Fenster mit allen Tabs, Firefox ebenso. Daneben gehen `web:` (eine Seite als eigenes App-Fenster), `run:` (ein Programm oder eine Verknüpfung, etwa ein Fachverfahren) und `open:`.

Programme mit nur einem Fenster (Stackfield, Teams, Outlook) öffnen kein neues, wenn sie schon laufen, sondern holen ihr Fenster nach vorn; WinMarchy holt dieses Fenster dann in den Modus. Es fehlt danach auf dem Workspace, auf dem es vorher lag.

### Ein Bildschirm, zwei, drei

Jedes Programm gehört zu einem Bildschirm: das erste zum linken, das zweite zum nächsten und so weiter. Fehlt ein Bildschirm, kommt sein Programm auf den letzten, der angeschlossen ist:

| Angeschlossen | Programm 1 | Programm 2 | Programm 3 |
|-|-|-|-|
| drei Bildschirme | Bildschirm 1 | Bildschirm 2 | Bildschirm 3 |
| zwei Bildschirme | Bildschirm 1 | Bildschirm 2 | Bildschirm 2 |
| ein Bildschirm | Bildschirm 1 | Bildschirm 1 | Bildschirm 1 |

Teilen sich mehrere Programme einen Bildschirm, ordnet sie das Dwindle-Layout an: das erste links, das zweite rechts oben, das dritte rechts unten, auch wenn der Bildschirm sonst Spalten oder Scrolling hat. Das gilt nur, solange dieser Workspace offen ist. Schließt GlazeWM ihn, weil sein letztes Fenster zu ist, gilt wieder das Layout des Bildschirms. Schaltest du in der Zwischenzeit mit `Win+Strg+J` das Layout des Bildschirms um, gilt das neue sofort auch für diesen Workspace.

Auch sonst ist ein einzelner Bildschirm kein Sonderfall: Workspaces, die einem fehlenden Bildschirm zugeordnet sind, nimmt der vorhandene mit, und `Win+Strg+Pfeil` schaltet eben nur ihn weiter.

### Alle Bildschirme zusammen weiterschalten

`Win+Strg+Rechts` und `Win+Strg+Links` schalten auf jedem Bildschirm zum nächsten oder vorigen Workspace, alle zusammen. Das Drei-Finger-Wischen auf dem Touchpad lässt sich unter **Einstellungen → Bluetooth und Geräte → Touchpad → Erweiterte Gesten** auf diese Kürzel legen. „Desktops wechseln“ lieber nicht wählen: Windows' eigene virtuelle Desktops vertragen sich nicht mit GlazeWM.

## Minimieren

Omarchy minimiert nicht; was aus dem Weg soll, kommt auf einen anderen Workspace oder ins Scratchpad. WinMarchy hält es genauso: Minimiert ein Fenster, das GlazeWM verwaltet, kommt es nach einer Viertelsekunde in seine Kachel zurück. Programme, die sich in den Infobereich verabschieden, bleiben dort. Falls doch etwas minimiert bleibt, holt `Win+M` alles zurück. Das ist immer so; einen Schalter dafür gibt es nicht mehr.

## Vollbild und neue Fenster

Öffnet sich auf einem Workspace ein neues Fenster, während dort eines im Vollbild ist, geht das Vollbild-Fenster zurück an seinen Platz (in seine Kachel oder schwebend), wie bei Hyprlands `new_window_takes_over_fullscreen`. So liegt das neue Fenster weder versteckt dahinter noch darüber. Dialoge, die schwebend aufgehen, beenden das Vollbild nicht.

Schließt sich das neue Fenster wieder (und jedes, das seitdem auf dem Workspace dazukam), bekommt das andere sein Vollbild zurück. Vorher blieb es dann in seiner halben Kachel stehen, etwa nach einer Meldung oder einem kurzen Fenster, das ein Programm von selbst geöffnet hatte. Nicht zurück geht es, wenn du es inzwischen auf einen anderen Workspace geschoben hast oder dort schon ein anderes Fenster im Vollbild ist. Welche Programme das Vollbild beendet haben, steht mit ihrem Prozessnamen im ausführlichen Protokoll. Dasselbe gilt, wenn sich ein Programm, das schon offen ist, nach vorn drängt, etwa Outlook, wenn du in Teams auf eine Mail klickst. Ohne das bliebe es hinter dem Vollbild-Fenster versteckt. Das Vollbild-Fenster bekommt sein Vollbild zurück, sobald du zu ihm zurückkehrst oder das andere Programm zu ist. Abschalten lässt sich das Ganze unter **Tiling → Neues Fenster beendet Vollbild**.

## Dialoge und Erinnerungen

Schwebende Fenster bleiben vor den Kacheln, wie in Hyprland. Bisher holte GlazeWM bei jedem Klick auf eine Kachel alle Kacheln nach vorn, und eine Outlook-Erinnerung oder ein Setup-Dialog verschwand dahinter. Windows' Standarddialoge (Fensterklasse `#32770`, auch die meisten NSIS-Setups), Windows Installer (MSI) und Inno-Setup-Assistenten schweben jetzt immer, mittig und im Vordergrund. Outlook-Erinnerungen und Task-Manager, Rechner und Snipping Tool taten das schon. Die Spalten des Scrolling-Layouts schweben auch, gehören aber zur Reihe und bleiben auf ihrer Ebene.

## Arbeitsmodi und das Scrolling-Layout

Ein Arbeitsmodus startet seine Programme nacheinander. Vor jedem Start bekommt der neue Workspace den Fokus, damit das Fenster gleich dort aufgeht. Solange WinMarchy auf die Fenster wartet, lassen die Layouts neue Fenster in Ruhe, und jedes wird erst angeordnet, wenn es auf seinem Workspace liegt. Vorher konnte das Scrolling-Layout ein Fenster auf dem Workspace festhalten, den es gerade verlassen sollte.

