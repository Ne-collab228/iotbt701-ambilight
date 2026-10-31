$ErrorActionPreference = 'Stop'
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut('C:\Users\user\Desktop\Ambilight.lnk')
$sc.TargetPath = 'C:\Users\user\AppData\Local\AmbilightOT\AmbilightOT.exe'
$sc.WorkingDirectory = 'C:\Users\user\AppData\Local\AmbilightOT'
$sc.Description = 'Ambilight: LED strip follows screen colour'
$sc.Save()
Write-Output 'shortcut created: C:\Users\user\Desktop\Ambilight.lnk'
