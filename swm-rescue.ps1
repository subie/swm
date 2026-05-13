# Restore windows after a force-killed swm daemon.
#
# Preferred: reads ~/.swm/original-rects.json (written continuously by the daemon)
# and restores ONLY those windows to their original positions.
#
# Fallback: if the state file is missing/empty, shows every hidden top-level
# window (noisier; will reveal background apps that legitimately hide themselves).

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Rescue {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
}
"@

$stateFile = Join-Path $env:USERPROFILE ".swm\original-rects.json"

if (Test-Path $stateFile) {
    $json = Get-Content $stateFile -Raw
    $data = $json | ConvertFrom-Json
    $count = 0
    foreach ($prop in $data.PSObject.Properties) {
        $h = [IntPtr]::new([int64]$prop.Name)
        if (-not [Rescue]::IsWindow($h)) { continue }
        $r = $prop.Value
        $left, $top, $right, $bottom = $r[0], $r[1], $r[2], $r[3]
        [Rescue]::ShowWindow($h, 5) | Out-Null
        # SWP_NOZORDER | SWP_NOACTIVATE = 0x14
        [Rescue]::SetWindowPos($h, [IntPtr]::Zero, $left, $top, ($right - $left), ($bottom - $top), 0x14) | Out-Null
        $sb = New-Object System.Text.StringBuilder 256
        [Rescue]::GetWindowText($h, $sb, 256) | Out-Null
        Write-Host "restored: $($sb.ToString())"
        $count++
    }
    Write-Host ""
    Write-Host "restored $count window(s) from state file"
    Remove-Item $stateFile -ErrorAction SilentlyContinue
} else {
    Write-Host "no state file at $stateFile — falling back to show-all-hidden"
    $restored = 0
    $cb = [Rescue+EnumProc]{
        param($h, $l)
        if (-not [Rescue]::IsWindowVisible($h)) {
            $sb = New-Object System.Text.StringBuilder 256
            $n = [Rescue]::GetWindowText($h, $sb, 256)
            if ($n -gt 0) {
                [Rescue]::ShowWindow($h, 5) | Out-Null
                Write-Host "shown: $($sb.ToString())"
                $script:restored++
            }
        }
        return $true
    }
    [Rescue]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    Write-Host ""
    Write-Host "shown $restored hidden window(s)"
}
