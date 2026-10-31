$ErrorActionPreference = 'Stop'
$root = 'C:\Users\user\AppData\Local\Temp\opencode'
$names = @(
  'System.Runtime',
  'System.ObjectModel',
  'System.Collections',
  'System.Runtime.InteropServices',
  'System.Runtime.InteropServices.WindowsRuntime',
  'System.IO',
  'System.Linq',
  'System.Text.Encoding',
  'System.Threading.Tasks',
  'System.ComponentModel'
)
$gac = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL'
$refs = New-Object System.Collections.Generic.List[string]
foreach ($n in $names) {
  $p = Join-Path $gac $n
  foreach ($sub in (Get-ChildItem $p -Directory -ErrorAction SilentlyContinue)) {
    $dll = Join-Path $sub.FullName "$n.dll"
    if (Test-Path $dll) { $refs.Add("/r:`"$dll`""); break }
  }
}
$winmds = Get-ChildItem 'C:\Windows\System32\WinMetadata\*.winmd' | ForEach-Object { "/r:`"$($_.FullName)`"" }
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$all = @(
  '/nologo', '/target:exe',
  "/out:`"$root\ledctl.exe`"",
  "/r:`"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll`"",
  "/r:`"$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll`""
) + $refs.ToArray() + $winmds + "`"$root\ledctl.cs`""
& $csc $all
