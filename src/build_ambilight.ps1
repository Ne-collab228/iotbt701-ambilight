$ErrorActionPreference = 'Stop'
$outDir = 'C:\Users\user\AppData\Local\AmbilightOT'
New-Item -ItemType Directory -Force $outDir | Out-Null
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
$fx = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc = "$fx\csc.exe"
$all = @(
  '/nologo', '/target:winexe',
  '/codepage:65001',
  "/out:`"$outDir\AmbilightOT.exe`"",
  "/r:`"$fx\System.Runtime.WindowsRuntime.dll`"",
  "/r:`"$fx\System.Drawing.dll`"",
  "/r:`"$fx\System.Windows.Forms.dll`""
) + $refs.ToArray() + $winmds + '"C:\Users\user\AppData\Local\Temp\opencode\ambilight_gui.cs"'
& $csc $all
if ($LASTEXITCODE -eq 0) { Write-Output "OK: $outDir\AmbilightOT.exe" }
