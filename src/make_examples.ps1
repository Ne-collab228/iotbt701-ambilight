$ErrorActionPreference = 'Stop'
$enc = [System.Text.Encoding]::UTF8

function Ru([byte[]]$b) { return $enc.GetString($b) }

# Cyrillic folder names as UTF-8 byte sequences (avoid ps1 file encoding issues)
$mods    = Ru @(0xD0,0xBC,0xD0,0xBE,0xD0,0xB4,0xD1,0x8B)                                # "моды"
$strip   = Ru @(0xD0,0xBB,0xD0,0xB5,0xD0,0xBD,0xD1,0x82,0xD0,0xB0)                      # "лента"
$examples= Ru @(0xD0,0xBF,0xD1,0x80,0xD0,0xB8,0xD0,0xBC,0xD0,0xB5,0xD1,0x80,0xD1,0x8B)   # "примеры"

$dst = Join-Path (Join-Path (Join-Path 'C:\Users\user\Desktop' $mods) $strip) $examples
New-Item -ItemType Directory -Force -Path $dst | Out-Null

# copy the repo protocol example docs
$src = 'C:\Users\user\AppData\Local\Temp\opencode\docs'
Get-ChildItem -Path $src -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $dst $_.Name) -Force }

Write-Output ("destination: " + $dst)
Write-Output ("files copied: " + (Get-ChildItem -Path $dst -File).Count)
Get-ChildItem -Path $dst -File | ForEach-Object { Write-Output ($_.Name + "  " + $_.Length + " B") }
