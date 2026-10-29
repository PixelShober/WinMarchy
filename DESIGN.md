# Design

WinMarchy hat zwei Gesichter. Das **Fenster** ist ein Werkzeug zum Einrichten und folgt Ember: Seitenleiste mit Tabs, ruhige neutrale Flächen, eine Akzentfarbe. Die **Overlays** auf dem Desktop (Menü, Tastenkürzel, Benachrichtigungen, Leiste) sehen aus wie Omarchy: Farben des aktiven Themes, Monospace, eckig, 2 px Rahmen im Akzent.

Quelle der Werte: `src/WinMarchy.App/Ui/ThemeManager.cs`, `Ui/Controls.xaml`, `Ui/Motion.cs`, `Overlays/OverlayLook.cs`.

## Farben im Fenster

Neutrale Flächen, Hell und Dunkel, kein Farbstich. Die einzige Farbe ist der Akzent des aktiven Omarchy-Themes, angehoben auf lesbaren Kontrast: mindestens 3:1 auf `Surface` für Flächen und Rahmen, 4.5:1 auf `Bg` für Text. Wechselt das Theme, wechselt der Akzent.

| Token | Hell | Dunkel | Wofür |
|-|-|-|-|
| `Bg` | `#f6f6f8` | `#101116` | Fenstergrund |
| `Sidebar` | `#ebebef` | `#0b0c10` | Seitenleiste, Titelleiste |
| `Surface` | `#ffffff` | `#16171d` | Karten, Eingabefelder |
| `Surface2` | `#f0f0f4` | `#1d1f26` | Tasten, Badges, Hinweise |
| `Border` | `#dfe0e6` | `#262832` | Kartenrand, Trennlinien |
| `BorderStrong` | `#c6c8d1` | `#3a3d4b` | Knöpfe, Felder |
| `Text` | `#17181d` | `#e6e7ee` | Text |
| `TextMuted` | `#585b6b` | `#a1a4b6` | Untertitel, Hinweise |
| `Ok` / `Warn` / `Error` | `#1d7343` / `#8a5600` / `#b02430` | `#79d49b` / `#e8bd62` / `#f3848b` | Status |

Abgeleitet: `AccentSoft` (Grund mit 12 % hell bzw. 16 % dunkel Akzent) für die aktive Markierung, `AccentFg` für Akzent als Text, `AccentText` (Weiß oder Fast-Schwarz, je nach Kontrast) für Text auf Akzent.

## Schrift

- Text: Segoe UI Variable Text, 14 px. Überschriften Segoe UI Variable Display, H1 24 px, H2 16 px, H3 14 px, alle SemiBold.
- Tasten, Pfade, Protokoll: Cascadia Mono, 12–12.5 px.
- Keine Großbuchstaben-Labels über Überschriften.

## Form

- Ecken eckig, überall. Omarchy rundet nicht.
- Karten: 1 px Rand, Innenabstand 20 × 18 px, 20 px Abstand untereinander.
- Die aktive Seite markiert ein 2 px Balken im Akzent am linken Rand, wie GlazeWMs Rahmen um das fokussierte Fenster.
- Icons: ein Satz auf 16-px-Raster, 1,5 px Strich, runde Enden.

## Bewegung

| Was | Dauer | Kurve |
|-|-|-|
| Seitenwechsel: alte Seite blendet aus | 160 ms | ease-out |
| Seitenwechsel: neue Seite steigt 6 px auf | 260 ms | cubic-bezier(0.22, 0.8, 0.36, 1) |
| Markierung gleitet zum neuen Tab | 260 ms | wie oben |
| Hell/Dunkel: neuer Look wächst als Kreis aus dem gedrückten Knopf | 500 ms | ease-in-out |

Ist in Windows „Animationseffekte“ aus, schaltet alles ohne Bewegung um.

## Overlays

Farben aus dem aktiven Omarchy-Theme: `background`, `foreground`, `accent`, `muted`. Rahmen 2 px im Akzent, Schrift Cascadia Mono.

- Menü (`Win+Space`): Karte 360 px, mittig auf dem Monitor unter der Maus, abgedunkelter Hintergrund. Zeilen 40 px, die gewählte mit 3 px Akzentbalken links und leicht getöntem Grund. Tippen filtert, Pfeile bewegen, Eingabe führt aus, Esc oder Rücktaste geht zurück. Im Hauptmenü sucht Tippen auch Programme; Abschnittstitel („Programme“, „WinMarchy“) stehen klein in `muted` und lassen sich nicht wählen. Nur eine Maus, die sich wirklich bewegt, wählt eine Zeile; die Pfeiltasten behalten sonst die Hand.
- Tastenkürzel (`Win+K`): dieselbe Karte, 700 px breit, Tasten rechtsbündig. Erst die Favoriten, dann der Abschnitt „Optional“. Die zehn Workspace-Kürzel stehen als eine Zeile („Win + 1 … 0“).
- Hintergrund (`Win+Strg+Alt+Space`, Menü → Stil → Hintergrund): Karte 520 px, Zeilen 48 px mit Vorschau 64 × 36 px und 1 px Rahmen, der beim gewählten Bild im Akzent steht.
- Benachrichtigungen: oben rechts unter der Leiste, 360 px, höchstens vier, nach 3 bis 8 Sekunden weg, klicken geht durch. Ein Theme-Wechsel zeigt eine Karte mit 4 px Fortschrittsbalken im Akzent auf `divider`, darunter der aktuelle Schritt; am Ende das Ergebnis, nach 3 Sekunden (Fehler: 8) weg.
- Leiste (Zebar): 26 px, Workspaces links, Uhr in der Mitte, Netz, Ton, CPU und Akku rechts, wie Waybar. Welche Einträge erscheinen, steht unter Tiling. Jeder lässt sich anklicken (Hover im Akzent): links das Naheliegende, rechts das Zweite, das Mausrad über dem Ton ändert die Lautstärke. Der Infobereich zeigt nur angeheftete Symbole; der Rest wartet hinter einem schmalen Pfeil (‹ ›) und klappt sich auf Klick aus. Mittelklick auf ein Symbol heftet es an oder löst es. Symbole für Netz, Ton und Akku blendet die Leiste aus, wenn sie die Einträge schon selbst zeigt.
