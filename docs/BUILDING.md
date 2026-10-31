# Building the tools (no Visual Studio)

All executables are plain C#.NET Framework 4.8 console/WinForms programs compiled
with the raw `csc.exe` that ships with Windows. No SDKs, no NuGet packages.

## Compiler

```
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

Language level: **C# 5** (no string interpolation `$"..."`, no `?.` on methods,
no `volatile double`). GUI projects use `/codepage:65001` so UTF-8 source with
non-ASCII literals compiles correctly (csc assumes ANSI without a BOM).

## Why WinRT calls go through reflection

Compile-time consumption of Windows Runtime types from .NET Framework requires
the "Windows SDK packs" (NuGet-targeted `.winmd` facade references). We have
neither, so:

| WinRT construct | Reflection workaround |
|---|---|
| `IAsyncOperation<T>` awaiting | `System.WindowsRuntimeSystemExtensions.AsTask<T>()` resolved via `GetMethods().First(...).MakeGenericMethod(resultType)` |
| `IBuffer` argument | `WindowsRuntimeBufferExtensions.AsBuffer(byte[])` |
| `WriteValueAsync` overload with GattWriteOption | picked via `typeof(GattCharacteristic).GetMethods()` and `Method.Invoke` |
| Result extraction | `Task.Result` via property reflection |

The same pattern makes the code trivially portable to modern .NET if compiled
with the real SDK — replace the reflection bridge with direct calls.

## References required

```
/r:System.Runtime.WindowsRuntime.dll     (Framework dir — WinRT projection helper)
/r:System.Drawing.dll                     (screen capture)
/r:System.Windows.Forms.dll               (GUI only)
+ GAC facade assemblies: System.Runtime, System.Collections, System.IO,
  System.Linq, System.Text.Encoding, System.Threading.Tasks, System.ObjectModel,
  System.ComponentModel, System.Runtime.InteropServices,
  System.Runtime.InteropServices.WindowsRuntime
  (resolved from C:\Windows\Microsoft.NET\assembly\GAC_MSIL\<name>\<ver>\<name>.dll)
+ all of C:\Windows\System32\WinMetadata\*.winmd   (Windows.ServerMetadata for WinRT)
```

## Build scripts

`src/build_ambilight.ps1` (GUI → `C:\Users\user\AppData\Local\AmbilightOT\AmbilightOT.exe`):

```
csc /nologo /target:winexe /codepage:65001
    /out:...\AmbilightOT.exe
    /r:System.Runtime.WindowsRuntime.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
    <GAC facades...> <WinMetadata\*.winmd...>
    ambilight_gui.cs
```

`src/build_ledctl.ps1` (console tester `ledctl.exe`, `/target:exe`, same references
minus WinForms) — PowerShell 5.1 compatible, no `&&` (PS does not parse it).

## Launcher shortcut (GUI)

`src/make_shortcut.ps1` creates `Desktop\Ambilight.lnk` →
`C:\Users\user\AppData\Local\AmbilightOT\AmbilightOT.exe`.
The exe directory must be outside `Temp` (Windows may clean Temp).

## PS-script pitfalls in this environment (kept for reference)

- PowerShell 5.1 reads `.ps1` saved as UTF-8 *without BOM* as ANSI → Cyrillic
  literals break parsing. Either save BOM, use ASCII-only scripts, or build
  Cyrillic paths via `[Text.Encoding]::UTF8.GetString([byte[]](...))`.
- WinRT **events** do not fire in PS 5.1 (`BluetoothLEAdvertisementWatcher`
  reports `Started` yet never delivers): any scanning/notify work must be C#.
- A single inline `powershell -Command "..."` with nested quotes is fragile;
  prefer `.ps1` files.

## Source files

| File | Contents |
|---|---|
| `ambilight_gui.cs` | complete WinForms Ambilight (capture pipeline, BLE, GUI) |
| `ambilight.cs` | console ancestor of the GUI (no UI) |
| `ledctl.cs` | console manual lamp tool (`on/off/white/rgb/hsv/sweep`) |
| `blescan.cs` | 25-s advertisement scanner |
| `build_*.ps1` | build scripts described above |
| `make_shortcut.ps1` | desktop shortcut creation |
