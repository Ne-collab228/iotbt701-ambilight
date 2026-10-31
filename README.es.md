# IOTBT701 BLE LED Strip — Windows Control & Ambilight

> 🌐 Documentation language: [English](README.md) | [Русский](README.ru.md) | **Español** | [中文](README.zh-CN.md) | [Português](README.pt-BR.md)

Control desarrollado en ingeniería inversa de una tira LED Bluetooth (BLE) de la
familia **Zengge / IOTBT** («IOTBT701», MAC `08:65:F0:DE:37:01`) desde un PC con
Windows 10/11 — **sin app del fabricante, sin emparejamiento, sin nube**.
Incluye un programa **Ambilight** listo: color medio de la pantalla → tira,
con una ventana de ajustes (GUI).

Todo lo aquí indicado fue **verificado contra el dispositivo físico** el 30–31
de octubre de 2026. El conocimiento del protocolo proviene del proyecto
open-source [8none1/lednetwf_ble](https://github.com/8none1/lednetwf_ble)
(integración de Home Assistant) más nuestras propias capturas en esta unidad.

## Lo que funciona (matriz verificada)

| Capacidad | Estado | Transporte |
|---|---|---|
| Descubrimiento BLE (la tira anuncia `IOTBT701`) | ✅ verificado | BLE adv, RSSI −56..−86 dBm |
| Conexión GATT + mantener (`GattSession.MaintainConnection`) | ✅ verificado | servicio `FFFF` |
| Escritura de comandos (write-without-response) | ✅ verificado | característica `FF01` |
| ENCENDER / APAGAR | ✅ verificado | payload `3B 23 / 3B 24` |
| Blanco (saturación 0) | ✅ verificado | color v3 + legacy `3B A1` |
| Color (tono 0..359, barrido de 12 colores + uso prolongado) | ✅ verificado | **`E0 01 00 A1` (colour_data_v3)** |
| Conversión RGB → HSV | ✅ verificado | helpers C# en este repo |
| Ambilight (color medio de pantalla → tira, GUI, ajustes) | ✅ verificado | `AmbilightOT.exe` |
| Notificación/lectura de estado (FF02) | ⚠ parcial (llegan eventos, payload vacío) | característica `FF02` |

**Hallazgo clave no evidente:** el dispositivo **ignora todos los comandos de
color clásicos** (`31/41/42/46 R G B ...`, y `3B A1 <hue> <sat> <bri>` siempre
que tono ≠ 0). Solo funciona el **envoltorio v3 `E0 01 00 A1 ...`** (“the E0 01
envelope is load-bearing”, la misma conclusión del PR #101 de lednetwf_ble).

**Hallazgo clave de esta unidad:** con saturación completa, el par de bytes del
tono es `<hue/2> <sat>` solo para grados de tono **pares**. La codificación
impar (`lo = sat + 128`) es **malinterpretada** por este firmware (azul azure
193° produjo un resultado inerte/incorrecto). Solución aplicada: cuantizar el
tono al grado **par** más cercano (error ≤ 1°, invisible).

## Estructura del repositorio

```
README.md            este archivo (inglés)
docs/PROTOCOL.md     protocolo byte a byte: tramas, checksum, comandos, peculiaridades
docs/AMBILIGHT.md    cómo se extrae el color medio de la pantalla, todas las constantes
docs/BUILDING.md     cómo se compilan los .exe (sin Visual Studio, compilador C# 5)
src/                 fuentes: GUI AmbilightOT, ledctl de consola, escáner BLE,
                     scripts de compilación
```

## Herramientas

| Programa | Propósito |
|---|---|
| `AmbilightOT.exe` | Ambilight: captura el monitor principal ~1,4 veces/s, calcula el color dominante y controla la tira. GUI WinForms con vista previa del color en vivo y ajustes. Suitable para atajos (cerrar la ventana = la tira conserva el color y el mando vuelve a funcionar) |
| `ledctl.exe` | Probador de consola: `on`, `off`, `white`, `rgb R G B`, `hsv H S B`, barrido de 12 colores |
| `blescan.exe` | Escáner de anuncios BLE, 25 s |

## Notas de seguridad y etiqueta

- La tasa de escritura se mantiene en **≤ ~2,5 comandos/s** (brecha ≥ 350 ms);
  la tira tolera 1–3/s.
- La app del teléfono del fabricante (Surplife/Zengge) debe estar cerrada
  mientras la herramienta del PC mantiene el enlace; la tira acepta un solo
  cliente GATT.
- Cerrar el programa de control deja el último color en la tira y devuelve el
  control remoto; no se altera nada más del comportamiento normal del
  dispositivo.
