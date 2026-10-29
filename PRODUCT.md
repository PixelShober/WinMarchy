# Product

<!-- impeccable:product-schema 1 -->

## Platform

windows (native WPF desktop app, .NET 10)

## Stack

WPF/XAML with C# (user's decision: "mehr Sinn als WPF"). Logic in `WinMarchy.Core` without WPF, testable on Linux. No WebView2.

## Users

People who use Omarchy (Hyprland) at home and have to work on a Windows PC at the office, and colleagues they pass the tool to. They want the same keyboard-driven tiling workflow, shortcuts and theme on Windows, on a managed company machine without admin rights.

## Product Purpose

WinMarchy sets up and manages an Omarchy-like desktop on Windows: GlazeWM for tiling, a Zebar bar, AutoHotkey (or GlazeWM) for Omarchy's shortcuts, Flow Launcher as the app launcher, and the 22 Omarchy themes applied to Windows Terminal, PowerShell, VS Code, Flow, Files, Windows itself and the wallpaper. Every change can be undone, down to the state before WinMarchy. Success: a colleague downloads one exe, clicks "Einrichten", and has Omarchy's look and keys a few minutes later; and can get back to plain Windows with one click.

## Positioning

The predecessor (Winarchy Lite) was a set of PowerShell scripts; WinMarchy is one self-updating app that owns the whole setup, knows what it changed (restore points), and only uses programs that are free to use in companies (GlazeWM, Zebar, AutoHotkey, Flow Launcher, PowerShell, Windows Terminal, Files – not Komorebi).

## Operating Context

- Company Windows 10/11 PCs, normal user rights, company proxy, sometimes blocked tools (AutoHotkey, Store, PowerShell policy).
- Used next to Omarchy at home: same physical keys (bindings by key position), same theme names.
- Releases come from GitHub (PR builds as CI artifacts, releases after merge); the app updates itself.

## Capabilities and Constraints

- Installs missing programs directly from the vendors' GitHub releases with SHA-256 check (user's choice: like Ember, no winget).
- Hotkey profiles: "Business" (Omarchy defaults mapped to Windows and company apps) and "Persönlich" (the user's bindings.lua and Omarchy's consumer apps), plus own shortcuts.
- Win+L cannot be rebound (Windows lock), and Win+Alt+L never reached WinMarchy on a test PC; Omarchy's SUPER+L moves to Win+Ctrl+J, next to Win+J.
- GlazeWM has no scrolling layout; WinMarchy emulates Dwindle, offers Columns and Floating per workspace.
- Texts in German (real umlauts, "du") and English, following the Windows language.

## Brand Commitments

- Structure, tab navigation, page-change animation, light/dark switch and the circle reveal effect follow the user's own app Ember (github.com/PixelShober/Ember). Not taken over: Ember's colours (Calcifer palette) and its two designs (Efeu/Glut) – light and dark are enough.
- As close to Omarchy's look and feel as possible: square corners, thin accent borders, the active Omarchy theme's accent, monospace where Omarchy uses it (menus, notifications, keys).
- Business-usable: no consumer branding in the default profile.

## Evidence on Hand

- Winarchy Lite Manager v2.1 (user's ZIP): PowerShell scripts whose logic was ported.
- bindings.lua of the user (Omarchy personal overrides).
- Omarchy repository (themes at tag v4.0.6, default bindings).
- No screenshots of a running WinMarchy yet; CI renders them on Windows.

## Product Principles

1. Nothing changes without a restore point; undo is a first-class page, not a hidden feature.
2. The user's machine stays the user's: user rights only, no policy bypass, no silent installs.
3. Omarchy muscle memory wins over Windows habits, except where Windows reserves a key.
4. Business first, personal on top.

## Accessibility & Inclusion

Keyboard operable throughout, visible focus, contrast ≥ 4.5:1 in both light and dark, reduced motion respected (Windows animation setting).

<!-- Inferred from the user's brief and answers (no separate interview round, as the user asked to proceed autonomously after one question round). -->
