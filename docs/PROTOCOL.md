# Protocol — IOTBT701 (Zengge / IOTBT family, BLE)

Precision notes from the physical unit `IOTBT701`, MAC `08:65:F0:DE:37:01`.
Every table marked **[V]** was verified against the device; items marked **[I]**
are inferred from captures of sibling devices in the lednetwf_ble project.

## 1. BLE transport

| Item | Value |
|---|---|
| Advertised name | `IOTBT701` |
| MAC | `08:65:F0:DE:37:01` (ulong `0x0865F0DE3701`) |
| Service | `0000ffff-0000-1000-8000-00805f9b34fb` |
| Write characteristic | `0000ff01-0000-1000-8000-00805f9b34fb` |
| Notify characteristic | `0000ff02-0000-1000-8000-00805f9b34fb` |
| Write mode | Write Without Response works **[V]** |
| Keepalive | `GattSession.MaintainConnection = true` — link holds indefinitely **[V]** |
| Single client | vendor app must be closed; one GATT client at a time **[V]** |
| Safe command rate | ≥ 350 ms between writes; 700–900 ms typical **[V]** |
| Disconnect behaviour | closing the app leaves current state on the strip; IR/remote resumes **[V]** |

Windows specifics: a console C# program (.NET Framework 4.8, `csc.exe`) can drive
WinRT `BluetoothLEDevice`/`GattCharacteristic` **only through reflection**
(compile-time WinRT projection fails without NuGet "Windows SDK packs");
`IAsyncOperation<T>.AsTask` via `MakeGenericMethod`, `IBuffer` via
`WindowsRuntimeBufferExtensions.AsBuffer`, `WriteValueAsync` via `Method.Invoke`.
PowerShell 5.1 is **not usable** for scanning/notify (WinRT events are not
delivered), only for one-shot writes.

## 2. Frame format (transport envelope) [V]

One "frame" = one write to FF01:

| Offset | Size | Meaning |
|---|---|---|
| 0 | 1 | `0x00` — flags |
| 1 | 1 | `seq` — sequence counter (any value is fine; increments used here) |
| 2 | 1 | `0x80` — "last fragment" flag |
| 3–4 | 2 | `0x00 0x00` — fragment counter |
| 5 | 1 | `N-1` where `N` = length of the block from byte 7 (the `0x0B`) through the checksum inclusive |
| 6 | 1 | `N` |
| 7 | 1 | `0x0B` — command-channel marker |
| 8.. | P | payload (the actual command, **without** checksum) |
| last | 1 | **checksum** = (sum of all payload bytes) `& 0xFF` |

So `N = 1 + P + 1`. Example, power ON: payload `P=12`, `N=14`:

```
00 XX 80 00 00 0D 0E 0B 3B 23 00 00 00 00 00 00 00 32 00 00 90
                      \_______payload 12B______/\_csum/
```

## 3. Payload catalogue

### 3.1 Power [V]

| Action | Payload (12 B) |
|---|---|
| ON | `3B 23 00 00 00 00 00 00 00 32 00 00` → checksum `0x90` |
| OFF | `3B 24 00 00 00 00 00 00 00 32 00 00` → checksum `0x91` |

Bytes 2–8 are zero, byte 9 = `0x32`, bytes 10–11 zero (constant template).

### 3.2 Colour — `colour_data_v3` (the only working colour form) [V]

14-byte payload:

| Offset | Meaning | Notes |
|---|---|---|
| 0 | `0xE0` | v3 marker |
| 1 | `0x01` | (with `0x00` at offset 2: fixed prefix) |
| 2 | `0x00` | |
| 3 | `0xA1` | mode byte (solid colour) |
| 4 | hue high byte | see packing below |
| 5 | hue low / sat packed | see packing below |
| 6 | brightness | 0..100, scale `%`; firmware floors at 1 |
| 7–10 | `00 00 00 00` | constant tail |
| 11 | `0x14` | transition/gradient parameter (constant in app captures) |
| 12–13 | `00 00` | |

Frame for **red (0°, s=100, b=100)** — captured and tested byte-exact:

```
00 XX 80 00 00 0F 10 0B E0 01 00 A1 00 64 64 00 00 00 00 14 00 00 5E
```

Yellow (60°): `... 0B E0 01 00 A1 1E 64 64 00 00 00 00 14 00 00 7C`
Blue (240°): `... 0B E0 01 00 A1 78 64 64 00 00 00 00 14 00 00 D6`

### 3.3 Hue + saturation packing

Per `protocol_docs/05_basic_commands.md`:

```
packed = (hue_degrees << 7) | saturation_percent     ; hue 0..359, sat 0..100
hi     = packed >> 8                                 ; == hue/2 for even hues
lo     = packed & 0xFF                               ; == sat, or sat+128 on odd hue
```

Examples (sat=100): 0° → `00 64`; 60° → `1E 64`; 240° → `78 64`.

### 3.4 Firmware quirks of THIS unit [V]

1. `E0 01` envelope is mandatory for colour. Identical payloads wearing no
   envelope, or the legacy `31/41/42/46 R G B ...` forms, are **ignored**.
2. `3B A1 <hi> <lo> <bri> ...` works **only white** (sat = 0):
   `3B A1 00 00 64 00 00 00 00 00 00` — hue ≠ 0 in the legacy form is ignored.
3. **Odd hue degrees malfunction**: `A1 60 E4 64` (193°, sat=100 → lo=sat+128)
   produced an inert/wrong result on the strip; even-hue equivalents work.
   Robust workaround: **quantize hue to the nearest even degree** (≤ 1° error).
4. White via v3: set `sat=0` and canonical `hue=0`:
   `E0 01 00 A1 00 00 64 00 00 00 00 14 00 00` → verified in continuous use.
5. The `0x14` byte at offset 11 was kept at all times; with value changed it was
   never tested — treat it as constant unless experimenting.
6. Family byte of the transport envelope: this unit accepts the generic `0x0B`
   channel for everything **[V]** (segment devices use `0x0B`, some other IOTBT
   units used `0x0A` for state-changing commands per sibling captures).

### 3.5 Non-working forms (recorded so nobody retries them) [V]

```
31 R G B 00 00 F0 0F            (colour_data)
41 R G B 00 00 0F               (music_colour_data)
42 R G B 00 00 F0 0F
46 R G B 00 00
3B A1 <hi> <lo> <bri> ...  with hue != 0
```

### 3.6 Other commands (from vendor captures, not exercised here) [I]

| Purpose | Payload |
|---|---|
| Query state | `81 8A 8B 40` (14-byte reply hoped on FF02) |
| Brightness | `47 <percent>` |
| Scene | `38 <mode> <speed> <bright>` |
| v3 scene | `E0 02 ...` |

## 4. Host-side usage pattern (Windows)

1. `BluetoothLEDevice.FromBluetoothAddressAsync(0x0865F0DE3701)`.
2. `GattSession.FromDeviceIdAsync(...)`, set `MaintainConnection=true`, wait `Connected`.
3. Resolve FF01, write frames with `GattWriteOption.WriteWithoutResponse`.
4. On write failure: dispose device/session, reconnect from step 1 (the strip
   recovers on its own; a retry every ~6–10 s is enough).

## 5. RGB→HSV reference used

Standard max/min formulation; hue 0–359, saturation/val 0–100 mapped directly
onto the protocol percent bytes. See `src/ambilight_gui.cs` (`RgbToHsv`,
`PackHueSat`, `SendV3Color`) for the single source of truth.

## 6. Attribution

All framing/encoding knowledge is built on the reverse-engineering work in
[8none1/lednetwf_ble](https://github.com/8none1/lednetwf_ble)
(`ai_instructions/`, `protocol_docs/`, PR #101). Verified refinement (v3 colour,
even-hue quirk) was done on this specific IOTBT701 unit.
