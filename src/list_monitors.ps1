$ErrorActionPreference = 'Stop'
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class Mon {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; }
    public delegate bool EnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);
    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, EnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO info);
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct MONITORINFO {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }
}
"@
$found = New-Object System.Collections.ArrayList
$cb = [Mon+EnumProc]{
    param($hMon, $hdc, [ref]$rect, $data)
    $mi = New-Object Mon+MONITORINFO
    $mi.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf([type][Mon+MONITORINFO])
    [Mon]::GetMonitorInfo($hMon, [ref]$mi) | Out-Null
    $primary = ($mi.dwFlags -band 1) -ne 0
    $r = $mi.rcMonitor
    $found.Add([string]::Format("monitor: {0}x{1} at ({2},{3}) primary={4}", ($r.R-$r.L), ($r.B-$r.T), $r.L, $r.T, $primary)) | Out-Null
    return $true
}
[Mon]::EnumDisplayMonitors([IntPtr]::Zero, [IntPtr]::Zero, $cb, [IntPtr]::Zero) | Out-Null
$found | ForEach-Object { Write-Output $_ }
