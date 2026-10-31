// AmbilightOT - screen dominant colour -> LED strip IOTBT701 (BLE)
// Self-contained: screenshot -> average colour -> RGB->HSV -> colour_data_v3 -> FF01
// Protocol per lednetwf_ble docs (iotbt findings, protocol_docs_05):
//   packed = (hue << 7) | sat, hi = packed>>8, lo = packed & 0xFF
//   payload: E0 01 00 A1 <hi> <lo> <bri> 00 00 00 00 14 00 00 + checksum
//   frame:   00 seq 80 00 00 len-1 len 0B <payload+checksum>
// Quirk (empirical): odd hue (lo = sat+128) is misread by this firmware,
// so hue is quantized to the nearest EVEN degree (error <= 1 deg).
using System;
using System.Diagnostics;
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

static class Ambi
{
    const ulong MAC = 0x0865F0DE3701;                 // IOTBT701
    const string SVC = "0000ffff-0000-1000-8000-00805f9b34fb";
    const string CHR = "0000ff01-0000-1000-8000-00805f9b34fb";
    static byte counter = 0;
    static GattCharacteristic f01 = null;
    static BluetoothLEDevice dev = null;
    static GattSession session = null;

    static void Log(string m) { Console.WriteLine(DateTime.Now.ToString("HH:mm:ss ") + m); }

    // ----- WinRT via reflection (csc .NET 4.8 cannot bind WinRT types directly) -----
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

    static object BytesToBuffer(byte[] b)
    {
        Type ext = typeof(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions);
        var m = ext.GetMethod("AsBuffer", new Type[] { typeof(byte[]) });
        return m.Invoke(null, new object[] { b });
    }

    // ----- protocol framing -----
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

    static bool Send(byte[] payload, string label)
    {
        byte[] frame = Frame(payload);
        try
        {
            var wmi = typeof(GattCharacteristic).GetMethods()
                .Where(x => x.Name == "WriteValueAsync" && x.GetParameters().Length == 2).First();
            object op = wmi.Invoke(f01, new object[] { BytesToBuffer(frame), GattWriteOption.WriteWithoutResponse });
            GattCommunicationStatus st = (GattCommunicationStatus)AwaitOp(op, typeof(GattCommunicationStatus));
            if (st != GattCommunicationStatus.Success) Log(label + " : " + st);
            return st == GattCommunicationStatus.Success;
        }
        catch (Exception ex)
        {
            Log(label + " : ERROR " + ex.Message);
            return false;
        }
    }

    static bool SendV3Color(int hueDeg, int satPct, int briPct)
    {
        if (satPct < 1) hueDeg = 0;
        if (hueDeg < 0) hueDeg = 0;
        if (hueDeg > 359) hueDeg = hueDeg % 360;
        if (briPct < 1) briPct = 1;
        if (briPct > 100) briPct = 100;
        hueDeg &= ~1;                                  // quantize to even hue (firmware quirk)
        if (hueDeg < 0) hueDeg = 0;
        if (hueDeg > 359) hueDeg = 359;
        if (satPct < 0) satPct = 0;
        if (satPct > 100) satPct = 100;
        int packed = (hueDeg << 7) | satPct;
        byte hi = (byte)((packed >> 8) & 0xFF);
        byte lo = (byte)(packed & 0xFF);
        byte[] payload = new byte[] { 0xE0, 0x01, 0x00, 0xA1, hi, lo, (byte)briPct,
                                      0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00 };
        return Send(payload, "color h" + hueDeg + " s" + satPct + " b" + briPct);
    }

    // ----- connect -----
    static bool Connect()
    {
        Drop();
        for (int i = 1; i <= 4; i++)
        {
            Log("connecting (attempt " + i + ")...");
            var op = BluetoothLEDevice.FromBluetoothAddressAsync(MAC);
            if (op != null)
            {
                dev = (BluetoothLEDevice)AwaitOp(op, typeof(BluetoothLEDevice));
                if (dev != null) break;
            }
            Thread.Sleep(5000);
        }
        if (dev == null) { Log("device NOT reachable (strip off/far?)"); return false; }

        var sop = GattSession.FromDeviceIdAsync(dev.BluetoothDeviceId);
        session = (GattSession)AwaitOp(sop, typeof(GattSession));
        if (session != null) session.MaintainConnection = true;
        for (int i = 1; i <= 8; i++)
        {
            Thread.Sleep(2000);
            if (dev.ConnectionStatus == BluetoothConnectionStatus.Connected) break;
        }
        if (dev.ConnectionStatus != BluetoothConnectionStatus.Connected)
        { Log("GATT connect failed"); Drop(); return false; }
        Log("connected");

        var gsop = dev.GetGattServicesAsync();
        var gs = (GattDeviceServicesResult)AwaitOp(gsop, typeof(GattDeviceServicesResult));
        f01 = null;
        foreach (GattDeviceService svc in gs.Services)
        {
            if (svc.Uuid.ToString() != SVC) continue;
            var coop = svc.GetCharacteristicsAsync();
            var crr = (GattCharacteristicsResult)AwaitOp(coop, typeof(GattCharacteristicsResult));
            foreach (GattCharacteristic c in crr.Characteristics)
                if (c.Uuid.ToString() == CHR) f01 = c;
        }
        if (f01 == null) { Log("FF01 not found"); Drop(); return false; }
        Log("FF01 ready");
        return true;
    }

    static void Drop()
    {
        if (session != null) { try { session.Dispose(); } catch { } session = null; }
        if (dev != null) { try { dev.Dispose(); } catch { } dev = null; }
        f01 = null;
    }

    // ----- colour math -----
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
        hueDeg = (int)Math.Round(h) % 360;
        satPct = (int)Math.Round((max <= 1e-9 ? 0 : d / max) * 100);
        valPct = (int)Math.Round(max * 100);
    }

    // weighted average of EVERY pixel: vivid + bright pixels count more;
    // near-black pixels are almost ignored (they are just "no light")
    static void Average(Bitmap src, out int rAvg, out int gAvg, out int bAvg, out double wSum)
    {
        Rectangle rect = new Rectangle(0, 0, src.Width, src.Height);
        BitmapData d = src.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        double rs = 0, gs = 0, bs = 0, ws = 0;
        for (int y = 0; y < d.Height; y++)
        {
            int rowOff = y * d.Stride;
            for (int x = 0; x < d.Width; x++)
            {
                int off = rowOff + x * 3;
                int bb = Marshal.ReadByte(d.Scan0, off);
                int gg = Marshal.ReadByte(d.Scan0, off + 1);
                int rr = Marshal.ReadByte(d.Scan0, off + 2);
                int mx = Math.Max(rr, Math.Max(gg, bb)), mn = Math.Min(rr, Math.Min(gg, bb));
                double satN = (mx <= 0) ? 0 : (double)(mx - mn) / mx;   // 0..1
                double vN = mx / 255.0;                                 // 0..1
                double w = (0.25 + 1.75 * satN) * (0.35 + 0.65 * vN);   // 0.09..2.0
                if (mx < 12) w = 0.02;                                  // near-black: ignore
                rs += w * rr; gs += w * gg; bs += w * bb; ws += w;
            }
        }
        src.UnlockBits(d);
        wSum = ws;
        rAvg = ws > 0.1 ? (int)(rs / ws) : 0;
        gAvg = ws > 0.1 ? (int)(gs / ws) : 0;
        bAvg = ws > 0.1 ? (int)(bs / ws) : 0;
    }

    // ----- screen -----
    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    // default: PRIMARY monitor only (game usually lives there); "all" = every monitor
    static bool captureAll = false;

    static void ScreenBounds(out int x, out int y, out int w, out int h)
    {
        if (captureAll)
        {
            x = GetSystemMetrics(76); y = GetSystemMetrics(77);
            w = GetSystemMetrics(78); h = GetSystemMetrics(79);
            if (w <= 0 || h <= 0) { x = 0; y = 0; w = GetSystemMetrics(0); h = GetSystemMetrics(1); }
        }
        else
        {
            x = 0; y = 0;
            w = GetSystemMetrics(0); h = GetSystemMetrics(1);   // primary screen
        }
    }

    static void Main(string[] args)
    {
        Console.Title = "Ambilight OT (лента)";
        try { SetProcessDPIAware(); } catch { }         // real pixels, no scaling blur
        Log("Ambilight OT - screen average -> LED strip (IOTBT701)");
        captureAll = Array.Exists(args, a => a.ToLower() == "all");
        int interval = 700;
        foreach (string a in args) { int v; if (int.TryParse(a, out v)) { interval = v; break; } }
        double satGain = args.Length >= 2
            ? double.Parse(args[1].Replace(',', '.'), CultureInfo.InvariantCulture) : 1.6;   // saturation-weighted avg is already vivid
        int minBri = args.Length >= 3 ? int.Parse(args[2]) : 8;
        double briGain = args.Length >= 4
            ? double.Parse(args[3].Replace(',', '.'), CultureInfo.InvariantCulture) : 1.4;   // brightness boost
        Log("mode: update " + interval + " ms, satGain x" + satGain + ", minBri " + minBri + "%, briGain x" + briGain);
        Log("STOP: just close this window. Strip stays at its last colour.");

        int sx, sy, sw, sh;
        ScreenBounds(out sx, out sy, out sw, out sh);
        Log("screen: " + sw + "x" + sh + " @(" + sx + "," + sy + ")");

        Bitmap big = null, small = new Bitmap(96, 54);
        Graphics gBig = null, gS = Graphics.FromImage(small);
        bool prepared = false;
        try
        {
            big = new Bitmap(sw, sh);
            gBig = Graphics.FromImage(big);
            prepared = true;
        }
        catch (Exception ex) { Log("нет доступа к экрану: " + ex.Message); Environment.Exit(1); }
        if (!prepared) Environment.Exit(1);

        int lastH = -999, lastS = -999, lastB = -999;
        DateTime lastSend = DateTime.MinValue;
        double smR = -1, smG = -1, smB = -1;                // smoothed average

        while (true)
        {
            // --- connection state machine ---
            if (f01 == null)
            {
                if (!Connect()) { Thread.Sleep(10000); continue; }
                if (!Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "power on"))
                { Drop(); Thread.Sleep(5000); continue; }
                lastH = -999; lastS = -999; lastB = -999;      // force refresh
                Thread.Sleep(1500);
            }

            Thread.Sleep(interval);
            try
            {
                gBig.CopyFromScreen(sx, sy, 0, 0, new Size(sw, sh));
                gS.DrawImage(big, new Rectangle(0, 0, small.Width, small.Height),
                             new Rectangle(0, 0, sw, sh), GraphicsUnit.Pixel);
                int rAvg, gAvg, bAvg; double wSum;
                Average(small, out rAvg, out gAvg, out bAvg, out wSum);

                // adaptive smoothing: tiny scene changes drift softly,
                // big scene change catches up fast (~1 sec to arrive)
                double delta = (smR < 0) ? 999 :
                    (Math.Abs(rAvg - smR) + Math.Abs(gAvg - smG) + Math.Abs(bAvg - smB));
                double k = delta > 40 ? 0.7 : 0.35;
                if (smR < 0) { smR = rAvg; smG = gAvg; smB = bAvg; }
                else { smR += (rAvg - smR) * k; smG += (gAvg - smG) * k; smB += (bAvg - smB) * k; }

                int rM = (int)smR, gM = (int)smG, bM = (int)smB;
                int hue, sat, val;
                RgbToHsv(rM, gM, bM, out hue, out sat, out val);
                sat = (int)Math.Min(100, sat * satGain);
                if (val < 14 || sat < 8) { sat = 0; hue = 0; } // dark or grey scene -> white
                int bri = Math.Max(minBri, (int)Math.Min(100, val * briGain));

                bool changed = Math.Abs(hue - lastH) > 2 || Math.Abs(sat - lastS) > 2
                               || Math.Abs(bri - lastB) > 2;
                if (changed && (DateTime.Now - lastSend).TotalMilliseconds >= 350)
                {
                    if (SendV3Color(hue, sat, bri))
                    {
                        lastH = hue; lastS = sat; lastB = bri;
                        lastSend = DateTime.Now;
                        Log("avg=(" + rM + "," + gM + "," + bM + ") -> strip: " +
                            HueName(hue) + " s" + sat + " b" + bri);
                    }
                    else Drop();                                // write failed -> reconnect next loop
                }
                else if (!changed && (DateTime.Now - lastSend).TotalSeconds > 25)
                {
                    // keepalive: resend same colour so the strip doesn't drift
                    if (SendV3Color(hue, sat, bri)) { lastSend = DateTime.Now; lastH = hue; lastS = sat; lastB = bri; }
                    else Drop();
                }
            }
            catch (Exception ex)
            {
                Log("цикл: " + ex.Message);
                Drop();
                Thread.Sleep(4000);
            }
        }
    }

    static string HueName(int deg)
    {
        if (deg < 0) return "?";
        string[] names = { "RED", "ORANGE", "YELLOW", "CHARTREUSE", "GREEN", "SPRING",
                           "CYAN", "AZURE", "BLUE", "VIOLET", "MAGENTA", "ROSE" };
        return names[((deg + 15) % 360) / 30];
    }
}
