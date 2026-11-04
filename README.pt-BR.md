# IOTBT701 BLE LED Strip — Controle via Windows & Ambilight

> 🌐 Idioma da documentação: [English](README.md) | [Русский](README.ru.md) | [Español](README.es.md) | [中文](README.zh-CN.md) | **Português** |

Controle por engenharia reversa de uma fita LED Bluetooth (BLE) da família
**Zengge / IOTBT** («IOTBT701», MAC `08:65:F0:DE:37:01`) a partir de um PC
Windows 10/11 — **sem app do fabricante, sem pareamento, sem nuvem**. Inclui
um programa **Ambilight** pronto: cor média da tela → fita, com janela de
ajustes (GUI).

Tudo aqui foi **verificado no dispositivo físico** nos dias 30–31/10/2026.
O conhecimento do protocolo é derivado do projeto open-source
[8none1/lednetwf_ble](https://github.com/8none1/lednetwf_ble) (integração do
Home Assistant), além das nossas próprias capturas nesta unidade.

## O que funciona (matriz verificada)

| Capacidade | Status | Transporte |
|---|---|---|
| Descoberta BLE (a fita anuncia `IOTBT701`) | ✅ verificado | BLE adv, RSSI −56..−86 dBm |
| Conexão GATT + manter (`GattSession.MaintainConnection`) | ✅ verificado | serviço `FFFF` |
| Escrita de comandos (write-without-response) | ✅ verificado | característica `FF01` |
| LIGAR / DESLIGAR | ✅ verificado | payload `3B 23 / 3B 24` |
| Branco (saturação 0) | ✅ verificado | cor v3 + legado `3B A1` |
| Cor (matiz 0..359, varredura de 12 cores + uso contínuo) | ✅ verificado | **`E0 01 00 A1` (colour_data_v3)** |
| Conversão RGB → HSV | ✅ verificado | helpers C# neste repositório |
| Ambilight (cor média da tela → fita, GUI, ajustes) | ✅ verificado | `AmbilightOT.exe` |
| Integração na inicialização + registro de falhas (v1.5) | ✅ verificado | caixa de autostart + `ambilight_log.txt` + janela automática «LINK LOST» |
| Notificação/leitura de estado (FF02) | ✅ verificado (consulta `81 8A 8B 40` na família `0x0A`; quadro de estado `EA 81 … F0 <matiz> <sat> <brilho>`; os registradores reflejam só o estado do app do celular) | característica `FF02` |
| Escala de brilho | ✅ verificado: o byte = **porcentagem 0..100**; valores >100 corrompem a cor | byte de brilho v3 |

**Descoberta chave não óbvia:** o dispositivo **ignora todos os comandos de cor
clássicos** (`31/41/42/46 R G B ...`, e também `3B A1 <hue> <sat> <bri>` sempre
que matiz ≠ 0). Só funciona o **envelope v3 `E0 01 00 A1 ...`** ("the E0 01
envelope is load-bearing", mesma conclusão do PR #101 do lednetwf_ble).

**Descoberta chave desta unidade:** com saturação total, o par de bytes da
matiz é `<hue/2> <sat>` apenas para graus de matiz **pares**. A codificação
ímpar (`lo = sat + 128`) é **mal interpretada** por este firmware (azul-azure
193° produziu resultado inerte/errado). Solução aplicada: quantizar a matiz ao
grau **par** mais próximo (erro ≤ 1°, invisível).

## Estrutura do repositório

```
README.md            este arquivo (inglês)
docs/PROTOCOL.md     protocolo byte a byte: frames, checksum, comandos, peculiaridades
docs/AMBILIGHT.md    como a cor média da tela é extraída, todas as constantes
docs/BUILDING.md     como os .exe são compilados (sem Visual Studio, compilador C# 5)
docs/reports/        relatórios analíticos do projeto em PDF ×5 (EN/RU/ES/ZH-CN/PT-BR)
src/                 fontes: GUI AmbilightOT, ledctl de console, scanner BLE,
                     scripts de build
```

## Ferramentas

| Programa | Propósito |
|---|---|
| `AmbilightOT.exe` | Ambilight: captura o monitor principal ~1,4×/s, calcula a cor dominante e controla a fita. GUI WinForms com pré-visualização da cor em tempo real e ajustes. Adequada a atalho (fechar a janela = a fita mantém a cor e o controle remoto volta a funcionar). **v1.5:** caixa «iniciar com Windows» (atalho em shell:startup), arquivo `ambilight_log.txt` com precisão de ms e rotação de 512 KB, botão «registro de conexão», e janela automática com o log completo **LINK LOST** se a fita cair sozinha. **v1.6:** transição suave de cor — a escrita desliza em passos até a cor alvo (ajuste 1–6 quadros, 1 = o pulo seco antigo). **v1.7:** deslize HSV por tempo com captura em thread separada (suavidade 1–10, 1 = seco, cada passo +0,25 s), mistura contínua branco/cor, matiz sempre par. **Nova interface escura:** cartões, pré-visualização grande da cor, um botão iniciar/parar, controles deslizantes de suavidade/brilho/saturação, seção *Avançado* recolhível |
| `ledctl.exe` | Testador de console: `on`, `off`, `white`, `rgb R G B`, `hsv H S B`, varredura de 12 cores (brilho limitado a 0–100 %) |
| `lednotify.exe` | Retroalimentação FF02: `query` pede o estado, `ladder`/`l0a` experimentos de brilho com leitura, `listen` observa eventos de controle/app |
| `blescan.exe` | Scanner de anúncios BLE, 25 s |

## Notas de segurança e etiqueta

- Taxa de escrita: **verificado até ~17 comandos/s sustentados** (soak de 3,5 min:
  3600/3600 entregues, enlace estável, sem backlog); rajadas até 456/s na fila sem
  perdas. O antigo «2,5/s» era cautela excessiva.
- O app oficial do telefone (Surplife/Zengge) deve estar fechado enquanto a
  ferramenta do PC mantém o enlace; a fita aceita **um único** cliente GATT.
- Fechar o programa de controle deixa a última cor na fita e devolve o controle
  remoto; nada além do comportamento normal do dispositivo é alterado.
