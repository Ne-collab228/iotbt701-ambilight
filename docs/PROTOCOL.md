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
| 6 | brightness | 0..100, scale `%`; firmware floors at 1; **values > 100 corrupt colour** (yellow-green shift, non-monotonic output) — verified by brightness ladder + strip's own notify feedback **[V]** |
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
3B 01 00 00 <p> 00 <p> ...      standalone brightness — accepted (Success) but
                                has NO physical effect on this unit
```

The legacy `3B A1` form also physically accepts saturated colours (cyan was
displayed), so it is *not fully* dead — but it never updates the state
registers (see §3.6), while `E0 01` v3 is the form the vendor app itself uses.

### 3.6 State query + notify feedback [V]

Verified 2026-11-01 with a dedicated listener (`src/lednotify.cs`, mode
`query`):

**Request** — write to FF01, transport family **`0x0A`** (query channel),
**no checksum**; this exact frame is from the lednetwf_ble docs:

```
00 <seq> 80 00 00 04 05 0A 81 8A 8B 40
```

A variant with checksum appended (`... 81 8A 8B <csum>`) also succeeds.

**Response** — the strip pushes a notify on FF02 (and also keeps the value on
the FF02 attribute, so a plain `ReadValueAsync` works even when the change
event is not deliverable). Example from this unit (while orange 20° 100/100
was set):

```
04 <seq> 80 00 00 1B 1C 15 EA 81 01 00 AD 0A 23 25 6E 37 F0 00 64 64 00 00 01 00 64 FF FF FF FF 02 00 00 00
\___transport header___/  \_______inner status frame________________________________________________/
```

Inner frame layout (matches lednetwf_ble PR #101 sample):

| Offset (inner) | Meaning |
|---|---|
| 0–1 | `EA 81` status marker |
| 2–3 | version/flags (`01 00` on this unit, `00 00` on the sibling) |
| 4 | device-specific byte |
| 5 | `0x0A` — query-channel echo |
| 6–9 | 4 device-specific bytes (not MAC-dependent here) |
| 10 | `0xF0` marker |
| 11–13 | hue, saturation, brightness **as stored in the strip's registers** (percent scale, `0x64`=100) |

Decoding: locate `EA 81`, then the `09 F0`-adjacent triplet
`F0 <hue> <sat> <bri>`; see `DecodeStatus()` in `src/lednotify.cs`.

**Important limitation discovered on this unit**: the registers (and there­fore
the status frames) are **NOT updated by our commands**. Neither v3 colour
frames nor classic `3B A1 HSV` frames change the reported hue/sat/bri — the
registers keep the state that the **vendor phone app** last wrote (they stayed
`00 64 64` = "red 100/100" through every experiment). The physical colour
changes even though the registers do not.

So the feedback channel is reliable as a **"is the strip responsive" heartbeat
and a connect-time snapshot from the app**, but cannot be used as a live
mirror of commands issued from Windows.

WinRT reflection notes for listeners (console C#, no SDK packs):
`GattValueChangedEventArgs` property is `CharacteristicValue`; the projected
`GattCharacteristic.Value` property is not reachable via reflection — instead
call `ReadValueAsync` and convert the buffer with
`WindowsRuntimeBufferExtensions.ToArray` (single-argument overload), both via
reflection.

## 4. Brightness scale — final verdict [V]

The brightness byte is a **percentage 0..100**. Values above 100 are delivered
(write returns `Success`) but the firmware's internal scaling breaks:

| sent bri | visual result |
|---|---|
| 1..100 | correct, monotonic |
| ~120–150 | yellow-green contamination |
| ~200–255 | darker than 150, still contaminated |

Determined empirically by a stepped ladder plus the user's eye observations;
matches the 0–100 percent scale documented in every brightness field of the
lednetwf_ble protocol docs (`0x3B 0x01`, `0x3B 0xA1`, CCT brightness).

## 5. Host-side usage pattern (Windows)

1. `BluetoothLEDevice.FromBluetoothAddressAsync(0x0865F0DE3701)`.
2. `GattSession.FromDeviceIdAsync(...)`, set `MaintainConnection=true`, wait `Connected`.
3. Resolve FF01, write frames with `GattWriteOption.WriteWithoutResponse`.
4. On write failure: dispose device/session, reconnect from step 1 (the strip
   recovers on its own; a retry every ~6–10 s is enough).

## 6. RGB→HSV reference used

Standard max/min formulation; hue 0–359, saturation/val 0–100 mapped directly
onto the protocol percent bytes. See `src/ambilight_gui.cs` (`RgbToHsv`,
`PackHueSat`, `SendV3Color`) for the single source of truth.

## 7. Attribution

All framing/encoding knowledge is built on the reverse-engineering work in
[8none1/lednetwf_ble](https://github.com/8none1/lednetwf_ble)
(`ai_instructions/`, `protocol_docs/`, PR #101). Verified refinement (v3 colour,
even-hue quirk) was done on this specific IOTBT701 unit.
