#Requires AutoHotkey v2.0
#SingleInstance Force

; -------- config --------
; Resolve swmctl.exe and swmsearch.exe relative to this script. Searches the
; common dotnet output paths so the script works after either `dotnet build`
; or `dotnet publish` without manual editing. Override either path by setting
; the env var SWMCTL_EXE or SWMSEARCH_EXE before launching this script.

ResolveExe(envName, candidates) {
    fromEnv := EnvGet(envName)
    if (fromEnv != "" && FileExist(fromEnv))
        return fromEnv
    for path in candidates {
        full := A_ScriptDir . "\" . path
        if FileExist(full)
            return full
    }
    MsgBox("swm.ahk: cannot locate " . envName . ". Tried:`n  " . fromEnv . "`n  " . candidates[1], "swm", 16)
    ExitApp
}

SwmCtl    := ResolveExe("SWMCTL_EXE", [
    "swmctl\bin\Release\net10.0\swmctl.exe",
    "swmctl\bin\Debug\net10.0\swmctl.exe",
    "swmctl\publish\swmctl.exe",
    "bin\swmctl.exe",
    "swmctl.exe"])

SwmSearch := ResolveExe("SWMSEARCH_EXE", [
    "swmsearch\bin\Release\net10.0-windows\swmsearch.exe",
    "swmsearch\bin\Debug\net10.0-windows\swmsearch.exe",
    "swmsearch\publish\swmsearch.exe",
    "bin\swmsearch.exe",
    "swmsearch.exe"])

; Modifier prefix for all bindings: Ctrl + Alt (`^!`).
;   `^`=Ctrl  `!`=Alt  `+`=Shift  `#`=Win
;   prefix `<` = left key only, `>` = right key only
ModKey := "^!"

; -------- helpers --------
SwmSend(line) {
    Run('"' . SwmCtl . '" ' . line, , "Hide")
}

OpenSearch(*) {
    Run('"' . SwmSearch . '"')
}

; -------- bindings --------
; Use HotIfWinActive("") so these are global, then register dynamically.
HotKey ModKey . "h",        (*) => SwmSend("focus left")
HotKey ModKey . "l",        (*) => SwmSend("focus right")
HotKey ModKey . ",",        (*) => SwmSend("focus home")
HotKey ModKey . ".",        (*) => SwmSend("focus end")

HotKey ModKey . "+h",       (*) => SwmSend("swap left")
HotKey ModKey . "+l",       (*) => SwmSend("swap right")
HotKey ModKey . "+,",       (*) => SwmSend("move home")
HotKey ModKey . "+.",       (*) => SwmSend("move end")

HotKey ModKey . "t",        (*) => SwmSend("float toggle")
HotKey ModKey . "f",        (*) => SwmSend("fullscreen toggle")
HotKey ModKey . "=",        (*) => SwmSend("resize half")
HotKey ModKey . "Enter",    (*) => SwmSend("swap master")
HotKey ModKey . "+Enter",   (*) => SwmSend("swap secondary")
HotKey ModKey . "Space",    OpenSearch
