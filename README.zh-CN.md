# IOTBT701 BLE LED Strip — Windows Control & Ambilight

> 🌐 Documentation language: [English](README.md) | [Русский](README.ru.md) | [Español](README.es.md) | **中文** | [Português](README.pt-BR.md)

对 **Zengge / IOTBT** 家族蓝牙（BLE）RGB灯带（设备名 `IOTBT701`，
MAC `08:65:F0:DE:37:01`）的逆向工程控制：在 Windows 10/11 上**无需官方
App、无需配对、无需云端**即可直接驱动，并附带现成的 **Ambilight**
程序（屏幕平均色 → 灯带，带设置界面）。

以下所有内容于 2026 年 10 月 30–31 日在真实设备上**逐项验证**。协议知识
以开源项目 [8none1/lednetwf_ble](https://github.com/8none1/lednetwf_ble)
（Home Assistant 集成）为基础，并结合对本机的大量抓包。

## 功能验证清单

| 功能 | 状态 | 通道 |
|---|---|---|
| BLE 扫描（灯带广播 `IOTBT701`） | ✅ 已验证 | BLE 广播，RSSI −56..−86 dBm |
| GATT 连接 + 保持（`GattSession.MaintainConnection`） | ✅ 已验证 | 服务 `FFFF` |
| 命令写入（write-without-response） | ✅ 已验证 | 特征 `FF01` |
| 开灯 / 关灯 | ✅ 已验证 | 载荷 `3B 23 / 3B 24` |
| 白光（饱和度 0） | ✅ 已验证 | v3 彩色指令 + 旧式 `3B A1` |
| 彩色（色相 0..359，12 色循环扫描 + 长期日常使用） | ✅ 已验证 | **`E0 01 00 A1`（colour_data_v3）** |
| RGB → HSV 转换 | ✅ 已验证 | 本仓库中的 C# 辅助函数 |
| Ambilight（屏幕平均色 → 灯带，GUI，可调参数） | ✅ 已验证 | `AmbilightOT.exe` |
| 开机自启 + 故障日志（v1.5） | ✅ 已验证 | 自启动复选框 + `ambilight_log.txt` + 自动弹出「LINK LOST」窗口 |
| Notify / 状态回读（FF02） | ✅ 已验证（用 `0x0A` 族发送查询 `81 8A 8B 40`；状态帧 `EA 81 … F0 <色相> <饱和度> <亮度>`；寄存器只反映手机 App 的状态） | 特征 `FF02` |
| 亮度刻度 | ✅ 已验证：该字节 = **百分比 0..100**；>100 会破坏颜色 | v3 亮度字节 |

**关键发现：** 该设备**忽略所有经典彩色命令**（`31/41/42/46 R G B ...`，
以及一切 hue ≠ 0 的 `3B A1 <hue> <sat> <bri>`）。唯一的可行方式是
**v3 封装 `E0 01 00 A1 ...`**（lednetwf_ble PR #101 的结论一致：
"the E0 01 envelope is load-bearing"）。

**本机特有关键发现：** 满饱和度时，色相字节对 `<hue/2> <sat>` 仅对
**偶数**色相角度成立。奇数编码（`lo = sat + 128`）会被该 firmware
**错误解读**（193° 天蓝色得到错误/无效的结果）。本仓库的解决办法：
将色相量化到最近的**偶数**度（误差 ≤ 1°，肉眼不可见）。

## 仓库结构

```
README.md            本文件（英文）
docs/PROTOCOL.md     逐字节协议：帧格式、校验和、命令、固件怪癖
docs/AMBILIGHT.md    屏幕主色的提取算法与全部常量
docs/BUILDING.md     如何不使用 Visual Studio 构建 .exe（C# 5 编译器）
docs/reports/        项目分析报告 PDF ×5（EN/RU/ES/ZH-CN/PT-BR）
src/                 源码：AmbilightOT GUI、命令行 ledctl、BLE 扫描器、
                     构建脚本
```

## 工具

| 程序 | 用途 |
|---|---|
| `AmbilightOT.exe` | Ambilight：以约每秒 1.4 次捕获主显示器，计算主导颜色并驱动灯带。WinForms 界面带实时颜色预览与参数设置。适合快捷键方式使用（关掉窗口 → 灯带保持颜色，遥控器恢复工作）。**v1.5：**「随 Windows 启动」复选框（shell:startup 快捷方式）、精确到毫秒的 `ambilight_log.txt`（512 KB 轮转）、「连接日志」按钮，以及灯带自行掉线时自动弹出完整日志的 **LINK LOST** 窗口。**v1.6：** 平滑颜色过渡——写入按步滑向目标色（设置 1–6 帧，1 = 旧的瞬间跳变）。**v1.7：** 基于时间的 HSV 平滑过渡，屏幕捕获在独立线程（平滑度 1–10，1 = 瞬间，每级 +0.25 秒），白色与颜色连续混合，色调仅取偶数 |
| `ledctl.exe` | 命令行测试器：`on`、`off`、`white`、`rgb R G B`、`hsv H S B`、12 色循环扫描（亮度钳制在 0–100 %） |
| `lednotify.exe` | FF02 状态反馈：`query` 请求状态帧，`ladder`/`l0a` 带回读的亮度实验，`listen` 监听遥控/App 事件 |
| `blescan.exe` | BLE 广播扫描器，25 秒 |

## 安全与礼仪注意事项

- 写入频率：**已验证可长期 ~17 条/秒**（3.5 分钟 soak：3600/3600 全部送达，链路稳定，无积压）；
  甚至 456/秒的突发排队也无丢失。旧的 «2.5 条/秒» 只是过度保守。
- PC 端工具占用连接时，请关闭手机官方应用（Surplife/Zengge）；灯带一次
  只接受一个 GATT 客户端。
- 关闭控制程序后灯带保持最近颜色，遥控器继续可用；不会对设备行为造成
  其他持久影响。
