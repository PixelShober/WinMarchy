; ---------------------------------------------------------------------------------------------
; Fixed part of every script WinMarchy generates. Each shortcut calls Act(), which hands the
; action to the running WinMarchy (WM_COPYDATA, about a millisecond). Only if WinMarchy is not
; running, GlazeWM's command line takes window commands and WinMarchy.exe is started for the rest.
; ---------------------------------------------------------------------------------------------

AgentWindow() {
    previous := A_DetectHiddenWindows
    DetectHiddenWindows true
    hwnd := WinExist(AgentTitle)
    DetectHiddenWindows previous
    return hwnd
}

AgentSend(message) {
    hwnd := AgentWindow()
    if !hwnd
        return false
    cds := Buffer(A_PtrSize * 3, 0)
    NumPut("Ptr", 0x4D4E4957, cds, 0)                       ; dwData = "WINM"
    NumPut("UInt", (StrLen(message) + 1) * 2, cds, A_PtrSize) ; cbData in bytes, UTF-16
    NumPut("Ptr", StrPtr(message), cds, A_PtrSize * 2)
    previous := A_DetectHiddenWindows
    DetectHiddenWindows true
    ; This script got the key press, so it may let WinMarchy put a menu in front.
    try DllCall("AllowSetForegroundWindow", "UInt", WinGetPID("ahk_id " hwnd))
    try result := SendMessage(0x004A, A_ScriptHwnd, cds.Ptr, , "ahk_id " hwnd, , , , 2000)
    catch
        result := 0
    DetectHiddenWindows previous
    return result = 1
}

Act(action) {
    if AgentSend(action)
        return
    if SubStr(action, 1, 3) = "wm:" {
        if !FileExist(GlazeCli)
            return
        for part in StrSplit(SubStr(action, 4), ";") {
            part := Trim(part)
            if part != ""
                try Run('"' GlazeCli '" command ' part, , "Hide")
        }
        return
    }
    if FileExist(WinMarchyExe)
        try Run('"' WinMarchyExe '" --action ' action)
}

IsTerminal() {
    try {
        exe := WinGetProcessName("A")
        return exe = "WindowsTerminal.exe" || exe = "wezterm-gui.exe" || exe = "alacritty.exe" || exe = "mintty.exe"
    }
    return false
}

; Omarchy's universal clipboard: terminals take Ctrl+Shift+C/V, everything else Ctrl+C/V.
UniversalCopy() => Send(IsTerminal() ? "^+c" : "^c")
UniversalPaste() => Send(IsTerminal() ? "^+v" : "^v")

; Left Win tapped on its own opens the launcher. vkE8 is not assigned to any key: pressing it while
; Win is down keeps the Start menu closed when Win goes up. If Flow Launcher is not running, WinMarchy
; starts it first.
WinDown(*) => Send("{Blind}{vkE8}")
WinUp(*) {
    if (A_PriorKey != "LWin")
        return
    if ProcessExist("Flow.Launcher.exe")
        Send(LauncherKeys)
    else
        Act("app:launcher")
}
