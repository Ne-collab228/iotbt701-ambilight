# Ambilight algorithm (as implemented in `AmbilightOT.exe`)

What the program does every update tick (~700 ms default, configurable 200–3000 ms):

## 1. Screen capture

- Primary monitor only (virtual-screen capture of **all** monitors is optional;
  typical setup: game on primary, other monitor content must not pollute the average).
- `SetProcessDPIAware()` is called so the capture is in physical pixels
  (otherwise Windows DPI scaling returns virtualized resolutions like 1536×960
  for a 1920×1200 panel and geometry drifts).
- Full-screen `Graphics.CopyFromScreen` into a full-res bitmap, downscaled to a
  **96×54** sample grid with `DrawImage`.

## 2. Weighted dominant-colour extraction

Every one of the 5184 sample pixels is read via `LockBits`/`Marshal.ReadByte`
(BGR24) and weighted:

```
satN = (max-min)/max                    0..1 pixel saturation
vN   = max/255                          0..1 pixel value
w    = (0.25 + 1.75 * satN) * (0.35 + 0.65 * vN)    0.09 .. 2.0
w    = 0.02                              if max < 12 (near-black pixels ignored)
avg   = Σ(w·RGB)/Σw
```

Rationale: a plain average lets large desaturated regions (grey sky, dark navy
wallpapers) drag the hue towards cyan/blue. Vivid and bright pixels therefore
count up to 8× more than washed-out ones, near-black counts almost zero.
This is what fixed "mostly-black night scene → blue glow".

## 3. Temporal smoothing (adaptive EMA)

```
delta = |rAvg−smR| + |gAvg−smG| + |bAvg−smB|
k     = 0.7   if delta > 40     (scene change: arrives in ~2 ticks, ~1–2 s)
k     = 0.35  otherwise          (small changes drift softly)
sm    += (avg − sm) * k
```

## 4. Colour shaping (all user-tunable in the GUI)

| Parameter | Default | Meaning |
|---|---|---|
| interval | 700 ms | capture→update period |
| satGain | 1.6 | saturation multiplier: averages are washed-out, boost restores vivid colour. 1.0 = untouched average, 3.0 = aggressive (amplifies tiny blue tints of grey scenes → the "why is it azure" effect) |
| minBri | 8 | brightness floor, % |
| briGain | 1.4 | brightness multiplier (screens average ~40–60% bright) |
| whiteThr | 16 | saturation below this value (before white-out) ⇒ **pure white** output. This is the fix for "grey scene → bluish strip": low-sat blue is forced to white |
| dark scene | val<14 | average value below 14% ⇒ white, dim (night scenes in games are actually dark-blue pixels) |

```
sat = clamp(avg_sat * satGain, 0, 100)
if avg_val < 14  or  sat < whiteThr:  sat=0, hue=0     -> white
bri = max(minBri, min(100, avg_val * briGain))
hue: quantized to the nearest EVEN degree (firmware quirk, see PROTOCOL.md)
```

## 5. Output to the strip

- Predominant hue/sat/bri → `E0 01 00 A1 <hi> <lo> <bri> 00 00 00 00 14 00 00`
  + checksum (see PROTOCOL.md), Write Without Response on FF01.
- Yellow conference: only send when the value changed meaningfully
  (hue/sat/bri delta > 2 units) **and** ≥350 ms since the previous write.
- 25-s keepalive re-send of the same colour otherwise.
- The GUI swatch shows the **exact colour the strip will render**: the HSV values
  are converted back to sRGB (`HsvToColor`) with the same numbers that go into
  the command — hence preview == strip.
- On write failure or missing device: full disconnect + retry cycle every ~6 s;
  strip recovers even if it was rebooted mid-session.

## 6. Persistence & UI

- Settings live in `ambilight_settings.txt` next to the exe
  (`interval=`, `satGain=`, `briGain=`, `minBri=`, `whiteThr=`, `allScreens=`, `autostart=`).
- WinForms window: 200×130 px live swatch, avg-RGB readout, strip readout
  (`ORANGE s83% b36%`), status, log pane, Start/Stop toggle.
- Closing the window stops the loop, disposes the GATT session; the strip keeps
  its last colour and the vendor remote keeps working.

## 7. Known perceptual caveat

LED rendering of low-saturation colours differs from sRGB preview:
10% blue mixed into white looks plainly "bluish" to the eye in a dark room
while a camera's auto white-balance flattens it back to grey. This is
perception, not a bug — the fix is the `whiteThr` parameter (raise it and
everything under the threshold becomes pure white).
