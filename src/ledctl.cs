using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

static class Led
{
    const ulong MAC = 0x0865F0DE3701;
    static byte counter = 0;
    static GattCharacteristic f01 = null;
    static BluetoothLEDevice dev = null;
    static GattSession session = null;

    static void Print(string m) { Console.WriteLine(m); }

    // reflection bridge: IAsyncOperation<T> -> Task<T>
    static object AwaitOp(object op, Type resultType)
    {
        Type ext = typeof(System.WindowsRuntimeSystemExtensions);
        var m = ext.GetMethods().Where(x => x.Name == "AsTask" && x.GetParameters().Length == 1
                 && x.GetParameters()[0].ParameterType.Name == "IAsyncOperation`1").First()
                 .MakeGenericMethod(resultType);
        Task task = (Task)m.Invoke(null, new object[] { op });
        task.Wait();
        var pi = task.GetType().GetProperty("Result");
        return pi != null ? pi.GetGetMethod().Invoke(task, null) : null;
    }

    static byte[] Frame(byte[] payload)
    {
        counter = (byte)((counter + 1) & 0xFF);
        int csum = 0;
        foreach (byte b in payload) csum = (csum + b) & 0xFF;
        byte[] cmd = new byte[payload.Length + 2];
        cmd[0] = 0x0B;
        Array.Copy(payload, 0, cmd, 1, payload.Length);
        cmd[cmd.Length - 1] = (byte)csum;
        byte[] frame = new byte[7 + cmd.Length];
        frame[0] = 0x00; frame[1] = counter; frame[2] = 0x80;
        frame[3] = 0x00; frame[4] = 0x00;
        frame[5] = (byte)(cmd.Length - 1); frame[6] = (byte)cmd.Length;
        Array.Copy(cmd, 0, frame, 7, cmd.Length);
        return frame;
    }

    // create IBuffer from byte[] via reflection (compile-time type never mentioned)
    static object BytesToBuffer(byte[] b)
    {
        Type ext = typeof(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions);
        var m = ext.GetMethod("AsBuffer", new Type[] { typeof(byte[]) });
        return m.Invoke(null, new object[] { b });
    }

    static bool Send(byte[] payload, string label)
    {
        byte[] frame = Frame(payload);
        try
        {
            var wmi = typeof(GattCharacteristic).GetMethods()
                .Where(x => x.Name == "WriteValueAsync" && x.GetParameters().Length == 2).First();
            object op = wmi.Invoke(f01, new object[] { BytesToBuffer(frame), GattWriteOption.WriteWithoutResponse });
            GattCommunicationStatus st = (GattCommunicationStatus)AwaitOp(op, typeof(GattCommunicationStatus));
            Print(label + " : " + st + "  [" + Hex(frame) + "]");
            return st == GattCommunicationStatus.Success;
        }
        catch (Exception ex)
        {
            Print(label + " : ERROR " + ex.Message);
            return false;
        }
    }

    static string Hex(byte[] b)
    {
        StringBuilder sb = new StringBuilder();
        foreach (byte x in b) sb.Append(x.ToString("X2")).Append(' ');
        return sb.ToString().TrimEnd();
    }

    static bool Connect()
    {
        for (int i = 1; i <= 6; i++)
        {
            Print("connect attempt " + i);
            var op = BluetoothLEDevice.FromBluetoothAddressAsync(MAC);
            if (op != null)
            {
                dev = (BluetoothLEDevice)AwaitOp(op, typeof(BluetoothLEDevice));
                if (dev != null) { Print("device object OK"); break; }
            }
            Print("attempt " + i + " - not available, wait 5s");
            Thread.Sleep(5000);
        }
        if (dev == null) { Print("device NOT reachable"); return false; }

        var sop = GattSession.FromDeviceIdAsync(dev.BluetoothDeviceId);
        session = (GattSession)AwaitOp(sop, typeof(GattSession));
        session.MaintainConnection = true;
        for (int i = 1; i <= 8; i++)
        {
            Thread.Sleep(2000);
            if (dev.ConnectionStatus == BluetoothConnectionStatus.Connected) break;
            Print("waiting conn... " + i);
        }
        Print("conn=" + dev.ConnectionStatus);
        if (dev.ConnectionStatus != BluetoothConnectionStatus.Connected) return false;

        var gsop = dev.GetGattServicesAsync();
        var gs = (GattDeviceServicesResult)AwaitOp(gsop, typeof(GattDeviceServicesResult));
        Type chrType = typeof(GattCharacteristicsResult);
        foreach (GattDeviceService svc in gs.Services)
        {
            var coop = svc.GetCharacteristicsAsync();
            var crr = (GattCharacteristicsResult)AwaitOp(coop, chrType);
            foreach (GattCharacteristic c in crr.Characteristics)
            {
                if (c.Uuid.ToString() == "0000ff01-0000-1000-8000-00805f9b34fb") f01 = c;
            }
        }
        if (f01 == null) { Print("FF01 not found"); return false; }
        Print("FF01 ready");
        return true;
    }

    static byte Parse(string s)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToByte(s.Substring(2), 16);
        return Convert.ToByte(s);
    }

    // v0 envelope with 8th byte = family (e.g. 0x0A): 00 seq 80 00 lenHi lenLo len+1 fam
    static bool Send0A(byte[] payload, string label)
    {
        counter = (byte)((counter + 1) & 0xFF);
        int csum = 0;
        foreach (byte b in payload) csum = (csum + b) & 0xFF;
        byte[] body = new byte[payload.Length + 1];
        Array.Copy(payload, 0, body, 0, payload.Length);
        body[body.Length - 1] = (byte)csum;
        // 8-byte header: 00 seq 80 00 lenHi lenLo len+1 family ; then body
        byte[] frame = new byte[8 + body.Length];
        frame[0] = 0x00; frame[1] = counter; frame[2] = 0x80;
        frame[3] = 0x00; frame[4] = 0x00;                      // lenHi
        frame[5] = (byte)body.Length;                          // lenLo
        frame[6] = (byte)(body.Length + 1);                    // len+1
        frame[7] = 0x0A;                                       // cmd family
        Array.Copy(body, 0, frame, 8, body.Length);
        try
        {
            var wmi = typeof(GattCharacteristic).GetMethods()
                .Where(x => x.Name == "WriteValueAsync" && x.GetParameters().Length == 2).First();
            object op = wmi.Invoke(f01, new object[] { BytesToBuffer(frame), GattWriteOption.WriteWithoutResponse });
            GattCommunicationStatus st = (GattCommunicationStatus)AwaitOp(op, typeof(GattCommunicationStatus));
            Print(label + " : " + st + "  [" + Hex(frame) + "]");
            return st == GattCommunicationStatus.Success;
        }
        catch (Exception ex)
        {
            Print(label + " : ERROR " + ex.Message);
            return false;
        }
    }

    static string HueName(int deg)
    {
        string[] names = { "RED", "ORANGE", "YELLOW", "CHARTREUSE", "GREEN", "SPRING",
                           "CYAN", "AZURE", "BLUE", "VIOLET", "MAGENTA", "ROSE" };
        return names[(deg / 30) % 12];
    }

    // ===== v3 colour helpers (protocol: colour_data_v3 = E0 01 00 A1 ...) =====

    // RGB (0..255) -> hue in degrees 0..359, saturation 0..100, value 0..100
    static void RgbToHsv(int r, int g, int b, out int hueDeg, out int satPct, out int valPct)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf)), min = Math.Min(rf, Math.Min(gf, bf));
        double d = max - min;
        double h = 0;
        if (d > 1e-9)
        {
            if (max == rf) h = 60 * (((gf - bf) / d) % 6);
            else if (max == gf) h = 60 * (((bf - rf) / d) + 2);
            else h = 60 * (((rf - gf) / d) + 4);
            if (h < 0) h += 360;
        }
        double s = (max <= 1e-9) ? 0 : d / max;
        hueDeg = (int)Math.Round(h) % 360;
        satPct = (int)Math.Round(s * 100);
        valPct = (int)Math.Round(max * 100);
    }

    // pack_hue_saturation() per protocol_docs_05_basic_commands.md:
    //   packed = (hue << 7) | sat ;  hi = packed>>8 ;  lo = packed & 0xFF
    // (lo == sat, or sat+128 on odd hue degrees)
    // EMPIRICAL (this lamp, 30.10.2026): odd hue -> lo = sat+128 is REJECTED or
    // misread (cyan 193 stayed orange / turned wrong). Even hue at sat<=100 is
    // numerically identical in both packings, so we quantize to the nearest
    // EVEN hue degree (error <= 1 degree - invisible).
    static void PackHueSat(int hueDeg, int satPct, out byte hi, out byte lo)
    {
        if (satPct < 1) hueDeg = 0;                    // pure white: canonical hue 0
        if (hueDeg < 0) hueDeg = 0;
        if (hueDeg > 359) hueDeg = hueDeg % 360;
        hueDeg &= ~1;                                  // DISABLED odd-hue path: quantize to even
        if (satPct < 0) satPct = 0;
        if (satPct > 100) satPct = 100;
        int packed = (hueDeg << 7) | satPct;
        hi = (byte)((packed >> 8) & 0xFF);
        lo = (byte)(packed & 0xFF);
    }

    // v3 colour command: E0 01 00 A1 <hi> <lo> <bri> 00 00 00 00 14 00 00 + checksum
    static bool SendV3Color(int hueDeg, int satPct, int briPct, string label)
    {
        if (satPct < 1) hueDeg = 0;
        if (hueDeg < 0) hueDeg = 0;
        if (hueDeg > 359) hueDeg = hueDeg % 360;
        if (briPct < 1) briPct = 1;
        if (briPct > 100) briPct = 100;   // brightness byte is a 0-100 PERCENT.
                                          // Values >100 accepted but corrupt colour
                                          // (yellow-green shift, non-monotonic) - verified
                                          // by ladder experiments + notify feedback, 2026-11-01.
        byte hi, lo;
        PackHueSat(hueDeg, satPct, out hi, out lo);
        return Send(new byte[] { 0xE0, 0x01, 0x00, 0xA1, hi, lo, (byte)briPct,
                                 0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00 }, label);
    }

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    // virtual screen bounds (multi-monitor) via SM_XVIRTUALSCREEN..SM_CYVIRTUALSCREEN
    static void ScreenBounds(out int x, out int y, out int w, out int h)
    {
        x = GetSystemMetrics(76); y = GetSystemMetrics(77);
        w = GetSystemMetrics(78); h = GetSystemMetrics(79);
        if (w <= 0 || h <= 0) { x = 0; y = 0; w = GetSystemMetrics(0); h = GetSystemMetrics(1); }
    }

    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLower() : "probe";
        try
        {
            if (mode == "probe")
            {
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
                Print(">>> 10 sec: make sure strip is STATIC <<<");
                Thread.Sleep(10000);
                Send(new byte[] { 0x31, 0xff, 0x00, 0x00, 0x00, 0x00, 0xf0, 0x0f }, "A 31 RED ");
                Thread.Sleep(12000);
                Send(new byte[] { 0x41, 0x00, 0xff, 0x00, 0x00, 0x00, 0x0f }, "B 41 GREEN ");
                Thread.Sleep(12000);
                Send(new byte[] { 0x42, 0x00, 0x00, 0xff, 0x00, 0x00, 0xf0, 0x0f }, "C 42 BLUE ");
                Thread.Sleep(12000);
                Send(new byte[] { 0x46, 0xff, 0xff, 0x00, 0x00, 0x00 }, "D 46 YELLOW ");
                Thread.Sleep(12000);
                Send(new byte[] { 0x41, 0x02, 0x00, 0x32, 0x00, 0xff, 0x00, 0x00, 0x00, 0x00, 0x32, 0x00, 0x00, 0xf0 }, "E 41-LONG RED ");
                Thread.Sleep(12000);
                Send(new byte[] { 0x3B, 0xA1, 0x00, 0x00, 0x64, 0, 0, 0, 0, 0, 0 }, "F HSV WHITE ");
                Thread.Sleep(5000);
            }
            else if (mode == "on")
            {
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
                Thread.Sleep(3000);
            }
            else if (mode == "off")
            {
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send(new byte[] { 0x3B, 0x24, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR OFF");
                Thread.Sleep(3000);
            }
            else if (mode == "white")
            {
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
                Thread.Sleep(3000);
                Send(new byte[] { 0x3B, 0xA1, 0x00, 0x00, 0x64, 0, 0, 0, 0, 0, 0 }, "WHITE");
                Thread.Sleep(3000);
            }
            else if (mode == "hsv" && args.Length >= 4)
            {
                // v3 form: ledctl hsv <hue_degrees 0..359> <sat_percent> <bri_percent>
                int h = ((int.Parse(args[1]) % 360) + 360) % 360;
                int s = int.Parse(args[2]);
                int br = int.Parse(args[3]);
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
                Thread.Sleep(2000);
                byte hi, lo;
                PackHueSat(h, s, out hi, out lo);
                SendV3Color(h, s, br, "HSV3 " + h + "deg (" + HueName(h) + "), s=" + s + ", b=" + br +
                            "  [hi=" + hi + " lo=" + lo + "]");
                Thread.Sleep(2000);
            }
            else if (mode == "rgb" && args.Length >= 4)
            {
                // ledctl rgb r g b  -> convert to HSV -> v3 colour
                int r = int.Parse(args[1]), g = int.Parse(args[2]), b = int.Parse(args[3]);
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
                Thread.Sleep(2000);
                int hue, sat, val;
                RgbToHsv(r, g, b, out hue, out sat, out val);
                SendV3Color(hue, sat, val, "RGB(" + r + "," + g + "," + b + ") -> h" +
                            hue + " " + HueName(hue) + " s" + sat + " b" + val);
                Thread.Sleep(2000);
            }
            else if (mode == "ambient")
            {
                // Ambilight: screenshot -> average colour -> v3 colour, ~1x/sec.
                // ledctl ambient [interval_ms] [satGain]
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
                Thread.Sleep(2000);
                int interval = args.Length >= 2 ? int.Parse(args[1]) : 1000;
                double satGain = args.Length >= 3
                    ? double.Parse(args[2].Replace(',', '.'), CultureInfo.InvariantCulture) : 1.6;
                int minBri = args.Length >= 4 ? int.Parse(args[3]) : 10;
                int sx, sy, sw, sh;
                ScreenBounds(out sx, out sy, out sw, out sh);
                Print(">>> AMBIENT ON: screen " + sw + "x" + sh + "@(" + sx + "," + sy +
                      "), interval " + interval + "ms, satGain " + satGain + ", minBri " + minBri + " <<<");
                Print(">>> Ctrl+C to stop. Lamp keeps last colour, remote keeps working <<<");
                Bitmap big = new Bitmap(sw, sh);
                Bitmap small = new Bitmap(96, 54);
                Graphics gBig = Graphics.FromImage(big);
                Graphics gS = Graphics.FromImage(small);
                int lastH = -999, lastS = -999, lastB = -999;
                DateTime lastSend = DateTime.MinValue;
                while (true)
                {
                    Thread.Sleep(interval);
                    try
                    {
                        gBig.CopyFromScreen(sx, sy, 0, 0, new Size(sw, sh));
                        gS.DrawImage(big, new Rectangle(0, 0, 96, 54),
                                     new Rectangle(0, 0, sw, sh), GraphicsUnit.Pixel);
                        long rr = 0, gg = 0, bb = 0; int n = 0;
                        for (int y = 0; y < 54; y += 2)
                            for (int x = 0; x < 96; x += 2)
                            {
                                Color c = small.GetPixel(x, y);
                                rr += c.R; gg += c.G; bb += c.B; n++;
                            }
                        int rAvg = (int)(rr / n), gAvg = (int)(gg / n), bAvg = (int)(bb / n);
                        int hue, sat, val;
                        RgbToHsv(rAvg, gAvg, bAvg, out hue, out sat, out val);
                        sat = (int)Math.Min(100, sat * satGain);      // video frames are washed-out
                        if (sat < 12) { sat = 0; hue = 0; }           // near-grey -> white
                        int bri = Math.Max(minBri, val);
                        bool changed = Math.Abs(hue - lastH) > 4 || Math.Abs(sat - lastS) > 4
                                       || Math.Abs(bri - lastB) > 4;
                        if (changed && (DateTime.Now - lastSend).TotalMilliseconds >= 500)
                        {
                            SendV3Color(hue, sat, bri,
                                "AMB avg(" + rAvg + "," + gAvg + "," + bAvg + ") -> h" +
                                hue + " " + HueName(hue) + " s" + sat + " b" + bri);
                            lastH = hue; lastS = sat; lastB = bri;
                            lastSend = DateTime.Now;
                        }
                    }
                    catch (Exception ex)
                    {
                        Print("ambient loop err: " + ex.Message);
                        Thread.Sleep(3000);
                    }
                }
            }
            else if (mode == "sweep")
            {
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
                Thread.Sleep(4000);
                // 12 hues: red -> ... -> rose, hue = degrees/2, sat=100, bri=100
                for (int deg = 0; deg < 360; deg += 30)
                {
                    byte h = (byte)(deg / 2);
                    Send(new byte[] { 0x3B, 0xA1, h, 0x64, 0x64, 0, 0, 0, 0, 0 },
                        "HUE " + deg + "deg (byte " + h + ") -> " + HueName(deg));
                    Thread.Sleep(12000);
                }
            }
            else if (mode == "sweep2")
            {
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
                Thread.Sleep(4000);
                Print(">>> v3 colour_data_v3 (E0 01, family 0B), sat=100 bri=100, 12s per colour <<<");
                for (int deg = 0; deg < 360; deg += 30)
                {
                    byte hi = (byte)(deg / 2); // (hue<<7|sat)>>8 == hue/2 for even hues
                    Send(new byte[] { 0xE0, 0x01, 0x00, 0xA1, hi, 0x64, 0x64, 0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00 },
                        "HUE " + deg + "deg (hi=" + hi + ") -> " + HueName(deg));
                    Thread.Sleep(12000);
                }
            }
            else if (mode == "sweep3")
            {
                // v3 colour via 8-byte envelope with family byte 0x0A
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send0A(new byte[] { 0xE0, 0x01, 0x00, 0xA1, 0x00, 0x64, 0x64, 0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00 }, "TEST RED (family 0A)");
                Thread.Sleep(12000);
                Print(">>> v3 colour_data_v3 (E0 01, family 0A), sat=100 bri=100, 12s per colour <<<");
                for (int deg = 0; deg < 360; deg += 30)
                {
                    byte hi = (byte)(deg / 2);
                    Send0A(new byte[] { 0xE0, 0x01, 0x00, 0xA1, hi, 0x64, 0x64, 0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00 },
                        "HUE " + deg + "deg -> " + HueName(deg));
                    Thread.Sleep(12000);
                }
            }
            else if (mode == "color" && args.Length >= 4)
            {
                byte r = Parse(args[1]), g = Parse(args[2]), b = Parse(args[3]);
                if (!Connect()) { Print("FAILED CONNECT"); return; }
                Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
                Thread.Sleep(3000);
                Send(new byte[] { 0x3B, 0xA1, 0x00, 0x00, 0x64, 0, 0, 0, 0, 0, 0 }, "HSV WHITE ref");
                Thread.Sleep(8000);
                Send(new byte[] { 0x31, r, g, b, 0x00, 0x00, 0xf0, 0x0f }, "COLOR 31");
                Thread.Sleep(8000);
                Send(new byte[] { 0x41, 0x02, 0x00, 0x32, 0x00, r, g, b, 0x00, 0x00, 0x32, 0x00, 0x00, 0xf0 }, "COLOR 41-LONG");
                Thread.Sleep(3000);
            }
            else
            {
                Print("usage: ledctl [on|off|white|hsv hue_deg sat bri|rgb r g b|sweep2|ambient [ms] [satGain] [minBri]|sweep3|color r g b|probe]");
            }
        }
        finally
        {
            // session drops on exit, remote stays functional
        }
        Print("done");
    }
}
