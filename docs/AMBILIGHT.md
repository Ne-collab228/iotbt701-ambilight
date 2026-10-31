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
| satGain | 1.6 | saturation multiplier: averages are washed-out, boost restores vivid colour. 1.0 = untouched average, 3.0 = aggressive (amplifies tiny blue tints of grey scenes → the "why is it azure" effect). A built-in 0.75 power curve on top pulls the washed-out middle (s40–70%) up, because cheap RGB LEDs render mid saturations as near-white |
| minBri | 8 | brightness floor, % |
| briGain | 1.4 | brightness multiplier (screens average ~40–60% bright) |
| whiteThr | 16 | saturation below this value (before white-out) ⇒ **pure white** output. This is the fix for "grey scene → bluish strip": low-sat blue is forced to white |
| warmHue | 48 | hue of the tint mixed into white output, degrees (amber ≈ 48°) |
| warmSat | 5 | strength of that tint, %. **Why:** an RGB strip has no calibrated white chip — "white" = R+G+B mixed, and cheap strips are biased cold (visually blue), while a camera's auto white-balance flattens the same light back to grey. A small warm/amber admixture (sat 3–8%) raises R+G and visually neutralises the cast. 0 = off (send clean `sat=0`) |
| dark scene | val<14 | average value below 14% ⇒ white, dim (night scenes in games are actually dark-blue pixels) |

```
sat = 100 * ((min(100, avg_sat * satGain) / 100) ^ 0.75)
```
The extra power curve (exponent 0.75) exists because cheap RGB LEDs render
mid saturations (40–70%) as near-white: the curve pulls the middle up so
"half-coloured" scenes still read as coloured on the strip.

```
whiteOut = avg_val < 14  or  sat < whiteThr
if whiteOut:  hue = warmHue (even-quantized), sat = warmSat   -> warm white
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
  (`interval=`, `satGain=`, `briGain=`, `minBri=`, `whiteThr=`, `warmHue=`, `warmSat=`,
  `allScreens=`, `autostart=`, `lang=`).
- **UI languages: Russian, English, Spanish, Chinese (simplified), Portuguese (BR)** —
  the same set as the repo READMEs. A drop-down selector ("Язык / Language", in the
  Settings box) switches every static label, button, status line and the hue-name
  list at runtime; the choice is saved to `lang=` in the settings file (default `ru`).
  The format strings with placeholders (`strip: {0} s{1}% b{2}%`) live in the `L10n` table in
  `src/ambilight_gui.cs`; adding a 6th language is a one-array edit.
- WinForms window: 200×130 px live swatch, avg-RGB readout, strip readout
  (`ORANGE s83% b36%`), status, log pane, Start/Stop toggle.
- Closing the window stops the loop, disposes the GATT session; the strip keeps
  its last colour and the vendor remote keeps working.

## 7. Known perceptual caveat (and its fix)

LED rendering of low-saturation colours differs from sRGB preview:
10% blue mixed into white looks plainly "bluish" to the eye in a dark room
while a camera's auto white-balance flattens it back to grey. Two separate
effects stack here:

1. the screen average itself may carry a faint blue tint (fixed by `whiteThr`
   forcing white), and
2. the strip's own **white mix is calibrated cold** — with `sat=0` the R+G+B
   channels are driven equally, but the blue LED visually dominates, so "pure
   white" still reads bluish on the wall.

Effect 2 is what `warmHue`/`warmSat` correct: a small amber admixture in white
output shifts the perceived balance back to neutral (like a warm-white lamp).
Tune it by eye: start at hue 48°, mix 5%, raise the mix until the wall looks
neutral; too much and the white goes cream/yellow. Camera photos will still
show near-grey either way — trust the eye on this one.
