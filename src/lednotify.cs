// lednotify - BLE notify listener for IOTBT701 (FF02) + arbitrary command sender.
// Purpose: see what the strip REPORTS about its own state (query, remote presses,
// effects of sent commands), to replace guessing with real feedback.
//
// usage:
//   lednotify listen [seconds]      subscribe to FF02 and print every packet
//   lednotify ladder                subscribe, send bri ladder 100/150/200/255, print packets
//   lednotify send <hexbytes...>    subscribe, send arbitrary payload frame, print packets
//   lednotify query                 subscribe, send vendor state query, print packets
//
// C#5-safe for csc.exe. Same reflection bridge as ledctl.
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Windows.Foundation;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

static class LedNotify
{
    const ulong MAC = 0x0865F0DE3701;
    const string SVC = "0000ffff-0000-1000-8000-00805f9b34fb";
    const string WRS = "0000ff01-0000-1000-8000-00805f9b34fb";
    const string NRS = "0000ff02-0000-1000-8000-00805f9b34fb";

    static void Print(string m) { Console.WriteLine(m); }

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

    // IBuffer -> byte[] (COM __ComObject case: use WindowsRuntimeBufferExtensions.ToArray)
    static byte[] BufToBytes(object ibuffer)
    {
        if (ibuffer == null) return new byte[0];
        try
        {
            Type bext = typeof(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions);
            var m1 = bext.GetMethods().Where(x => x.Name == "ToArray" && x.GetParameters().Length == 1).FirstOrDefault();
            if (m1 != null)
            {
                object res = m1.Invoke(null, new object[] { ibuffer });
                return (byte[])res;
            }
            Print("buf: no single-arg ToArray");
        }
        catch (Exception e)
        {
            string inner = e.InnerException != null ? " / inner: " + e.InnerException.Message : "";
            Print("ToArray fail: " + e.Message + inner);
        }
        // fallback: IDispatch GetProperty
        try
        {
            object len = ibuffer.GetType().InvokeMember("Length",
                System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, ibuffer, null);
            Type drType = typeof(DataReader);
            object dr = drType.GetMethod("FromBuffer").Invoke(null, new object[] { ibuffer });
            drType.GetMethod("Load").Invoke(dr, new object[] { (uint)len });
            object data = drType.GetMethod("ReadBytes").Invoke(dr, new object[] { (uint)len });
            return (byte[])data;
        }
        catch (Exception e)
        {
            string inner = e.InnerException != null ? " / inner: " + e.InnerException.Message : "";
            Print("buf->bytes fail: " + e.Message + inner);
            return new byte[0];
        }
    }

    static byte counter = 0;
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

    static void Dump(string tag, byte[] b)
    {
        StringBuilder sb = new StringBuilder(tag + "  " + b.Length + "B:");
        foreach (byte x in b) sb.Append(" ").Append(x.ToString("X2"));
        Print(sb.ToString());
        DecodeStatus(tag, b);
    }

    // find status frame (EA 81 ... F0 hue sat bri ...) inside any packet
    // layout per repo docs: ea 81 .. .. [X] 0a [4 bytes dev] f0 [hue] [sat] [bri]
    static void DecodeStatus(string tag, byte[] b)
    {
        for (int i = 0; i <= b.Length - 8; i++)
        {
            if (b[i] != 0xEA || b[i + 1] != 0x81) continue;
            // search for F0 marker between i+3 and i+12 for robustness
            for (int j = i + 3; j < Math.Min(i + 13, b.Length - 3); j++)
            {
                if (b[j] == 0xF0)
                {
                    int hue = b[j + 1], sat = b[j + 2], bri = b[j + 3];
                    Print("  >> STATUS " + tag + ": hue=" + hue + " (" + (hue * 2) + "deg if even-packed), sat=" + sat + ", bri=" + bri);
                    return;
                }
            }
            Print("  >> status frame found (EA 81) but no F0 marker nearby");
            return;
        }
    }

    static BluetoothLEDevice dev;
    static GattSession session;
    static GattCharacteristic f01, f02;

    static object AwaitOpNonGeneric(object op) { return null; }

    static bool Connect()
    {
        var op = BluetoothLEDevice.FromBluetoothAddressAsync(MAC);
        dev = (BluetoothLEDevice)AwaitOp(op, typeof(BluetoothLEDevice));
        if (dev == null) { Print("device null"); return false; }
        var sop = GattSession.FromDeviceIdAsync(dev.BluetoothDeviceId);
        session = (GattSession)AwaitOp(sop, typeof(GattSession));
        if (session != null) session.MaintainConnection = true;
        for (int i = 0; i < 10; i++)
        {
            Thread.Sleep(1500);
            if (dev.ConnectionStatus == BluetoothConnectionStatus.Connected) break;
        }
        if (dev.ConnectionStatus != BluetoothConnectionStatus.Connected) { Print("not connected"); return false; }
        Print("connected");

        var gsop = dev.GetGattServicesAsync();
        var gs = (GattDeviceServicesResult)AwaitOp(gsop, typeof(GattDeviceServicesResult));
        foreach (GattDeviceService svc in gs.Services)
        {
            if (svc.Uuid.ToString() != SVC) continue;
            var coop = svc.GetCharacteristicsAsync();
            var crr = (GattCharacteristicsResult)AwaitOp(coop, typeof(GattCharacteristicsResult));
            foreach (GattCharacteristic c in crr.Characteristics)
            {
                if (c.Uuid.ToString() == WRS) f01 = c;
                if (c.Uuid.ToString() == NRS) f02 = c;
            }
        }
        if (f01 == null || f02 == null) { Print("FF01/FF02 not found"); return false; }
        Print("chars ready");
        return true;
    }

    static byte counter2; // (unused placeholder)

    // state query, exact transport per protocol_docs_04 example:
    //   00 seq 80 00 00 04 05 0A 81 8A 8B 40   (body length 4, [5]=4, [6]=5)
    // checksum variant appends one byte: [5]=5, [6]=6.
    static void SendQuery(bool withChecksum)
    {
        byte[] payload = { 0x81, 0x8A, 0x8B, 0x40 };
        byte[] body;
        if (withChecksum)
        {
            int csum = 0; foreach (byte b in payload) csum = (csum + b) & 0xFF;
            body = new byte[payload.Length + 1];
            Array.Copy(payload, 0, body, 0, payload.Length);
            body[payload.Length] = (byte)csum;
        }
        else
        {
            body = (byte[])payload.Clone();
        }
        counter = (byte)((counter + 1) & 0xFF);
        byte[] frame = new byte[8 + body.Length];   // 7 header + cmdId 0x0A + body
        frame[0] = 0x00; frame[1] = counter; frame[2] = 0x80;
        frame[3] = 0x00; frame[4] = 0x00;
        frame[5] = (byte)body.Length;
        frame[6] = (byte)(body.Length + 1);
        frame[7] = 0x0A;                       // cmdId 10 = query channel!
        Array.Copy(body, 0, frame, 8, body.Length);
        try
        {
            Type wmiT = typeof(GattCharacteristic);
            var wmi = wmiT.GetMethods()
                .Where(x => x.Name == "WriteValueAsync" && x.GetParameters().Length == 2).First();
            Type bext = typeof(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions);
            object bufQ = bext.GetMethod("AsBuffer", new Type[] { typeof(byte[]) }).Invoke(null, new object[] { frame });
            object wop = wmi.Invoke(f01, new object[] {
                bufQ,
                GattWriteOption.WriteWithoutResponse });
            var st = (GattCommunicationStatus)AwaitOp(wop, typeof(GattCommunicationStatus));
            Dump("QUERY(s=" + withChecksum + ") : " + st, frame);
        }
        catch (Exception e) { Print("query fail: " + e.Message); }
    }

    static object Prop(object o, string name)
    {
        if (o == null) return null;
        Type t = o.GetType();
        var p = t.GetProperty(name);
        if (p == null)
            foreach (Type it in t.GetInterfaces())
            { p = it.GetProperty(name); if (p != null) break; }
        if (p == null) return null;
        return p.GetGetMethod().Invoke(o, null);
    }

    static byte[] lastNeg = null;
    static void OnValueChanged(GattCharacteristic c, GattValueChangedEventArgs a)
    {
        try
        {
            // direct attribute read (projected Value props not reachable for GattCharacteristic)
            var rmi = typeof(GattCharacteristic).GetMethod("ReadValueAsync", new Type[0]);
            object rop = rmi.Invoke(f02, null);
            object rr = AwaitOp(rop, typeof(GattReadResult));
            object bufx = Prop(rr, "Value");
            if (bufx == null) { Print("NOTIFY: no buf prop"); return; }
            byte[] bb = BufToBytes(bufx);
            if (bb.Length == 0) { Print("NOTIFY: empty"); return; }
            Dump("NOTIFY", bb);
            lastNeg = bb;
        }
        catch (Exception e2)
        {
            string inner = e2.InnerException != null ? " / inner: " + e2.InnerException.Message : "";
            Print("notify read fail: " + e2.Message + inner);
        }
    }

    // arbitrary command with payload + checksum, wrapped in family 0x0A
    // (same layout as SendQuery(true): 00 seq 80 00 00 len len+1 0A payload csum)
    static void SendPayload0A(byte[] payload, string label)
    {
        byte[] body = null;
        byte[] frame = null;
        frame = new byte[8 + payload.Length + 1];
        body = new byte[payload.Length + 1];
        Array.Copy(payload, 0, body, 0, payload.Length);
        int csum = 0; foreach (byte b in payload) csum = (csum + b) & 0xFF;
        body[payload.Length] = (byte)csum;
        counter = (byte)((counter + 1) & 0xFF);
        frame[0] = 0x00; frame[1] = counter; frame[2] = 0x80;
        frame[3] = 0x00; frame[4] = 0x00;
        frame[5] = (byte)body.Length;
        frame[6] = (byte)(body.Length + 1);
        frame[7] = 0x0A;
        Array.Copy(body, 0, frame, 8, body.Length);
        try
        {
            Type wmiT = typeof(GattCharacteristic);
            var wmi = wmiT.GetMethods()
                .Where(x => x.Name == "WriteValueAsync" && x.GetParameters().Length == 2).First();
            Type bext = typeof(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions);
            object bufS = bext.GetMethod("AsBuffer", new Type[] { typeof(byte[]) }).Invoke(null, new object[] { frame });
            object wop = wmi.Invoke(f01, new object[] { bufS, GattWriteOption.WriteWithoutResponse });
            var st = (GattCommunicationStatus)AwaitOp(wop, typeof(GattCommunicationStatus));
            Dump("SENT " + label + " : " + st, frame);
        }
        catch (Exception e) { Print("sendA fail: " + e.Message); }
    }

    static void SubscribeNotify()
    {
        // initial read: some devices only start reporting after a first ReadValue
        try
        {
            var rop = f02.ReadValueAsync();
            object rr = AwaitOp(rop, typeof(GattReadResult));
            object st0 = Prop(rr, "Status");
            Print("initial read: status=" + st0);
            object buf0 = Prop(rr, "Value");
            if (buf0 != null)
            {
                byte[] b = BufToBytes(buf0);
                if (b.Length > 0) Dump("READ", b);
                else Print("initial read: value EMPTY (len 0)");
            }
        }
        catch (Exception e) { Print("initial read fail: " + e.Message); }

        try
        {
            var sop = f02.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            var st = (GattCommunicationStatus)AwaitOp(sop, typeof(GattCommunicationStatus));
            Print("subscribe: " + st);
        }
        catch (Exception e) { Print("subscribe fail: " + e.Message); }

        try
        {
            f02.ValueChanged += OnValueChanged;
            Print("event handler attached (+=)");
        }
        catch (Exception e) { Print("event attach fail: " + e.Message); }
    }

    // fallback: polling read of FF02. Returns true when a NEW value arrived.
    static bool PollOnce(string tag)
    {
        try
        {
            Type rtype = typeof(GattCharacteristic);
            var rmi = rtype.GetMethod("ReadValueAsync", new Type[0]);
            object rop = rmi.Invoke(f02, null);
            object rr = AwaitOp(rop, typeof(GattReadResult));
            object st0 = Prop(rr, "Status");
            object buf = Prop(rr, "Value");
            if (buf == null) { return false; }
            byte[] bb = BufToBytes(buf);
            if (bb.Length == 0) return false;
            bool same = lastNeg != null && bb.Length == lastNeg.Length;
            if (same) { for (int i = 0; i < bb.Length; i++) if (bb[i] != lastNeg[i]) { same = false; break; } }
            if (same) return false;
            Dump(tag + " " + st0, bb);
            lastNeg = bb;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static void PollUntilNew(string tag)
    {
        for (int k = 0; k < 12; k++)
        {
            if (PollOnce(tag)) return;
            Thread.Sleep(250);
        }
        Print(tag + ": no response captured");
    }

    static void SendPayload(byte[] payload, string label)
    {
        byte[] frame = Frame(payload);
        try
        {
            var wmi = typeof(GattCharacteristic).GetMethods()
                .Where(x => x.Name == "WriteValueAsync" && x.GetParameters().Length == 2).First();
            Type bext2 = typeof(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions);
            object bufS = bext2.GetMethod("AsBuffer", new Type[] { typeof(byte[]) }).Invoke(null, new object[] { frame });
            object wop2 = wmi.Invoke(f01, new object[] {
                bufS,
                GattWriteOption.WriteWithoutResponse });
            var st = (GattCommunicationStatus)AwaitOp(wop2, typeof(GattCommunicationStatus));
            Dump("SENT " + label + " : " + st, frame);
        }
        catch (Exception e) { Print("send fail: " + e.Message); }
    }

    static byte[] HexArgs(string[] args, int from)
    {
        byte[] payload = new byte[args.Length - from];
        for (int i = from; i < args.Length; i++)
            payload[i - from] = Convert.ToByte(args[i], 16);
        return payload;
    }

    static void Wait(int seconds)
    {
        for (int s = seconds; s > 0; s--)
        {
            Console.Write("\rlistening... " + s + "s ");
            Thread.Sleep(1000);
            if (s % 2 == 0) PollOnce("POLL");
        }
        Console.WriteLine();
    }

    [STAThread]
    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0].ToLower() : "listen";
        if (!Connect()) { Print("FAILED CONNECT"); return; }
        SubscribeNotify();
        Thread.Sleep(1500);

        if (mode == "ladder")
        {
            SendPayload(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
            Thread.Sleep(3000);
            int[] brs = { 100, 130, 160, 190, 220, 255, 100 };
            foreach (int b in brs)
            {
                Print("--- sending bri=" + b + " ---");
                SendPayload(new byte[] { 0xE0, 0x01, 0x00, 0xA1, 0x0A, 0x64, (byte)b,
                                         0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00 },
                            "hsv 20 100 " + b);
                Thread.Sleep(2500);
                SendQuery(false);          // ask the strip what it actually holds
                Thread.Sleep(400);
                PollUntilNew("FEEDBACK bri=" + b);
            }
            Print("--- watching for strip reports ---");
            Wait(15);
        }
        else if (mode == "l0a")
        {
            // brightness ladder via family 0x0A command (like the phone app sends),
            // then state query after each step to see what the strip registers.
            SendPayload(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "PWR ON");
            Thread.Sleep(3000);
            int[] brs = { 100, 130, 160, 190, 220, 255, 100 };
            foreach (int b in brs)
            {
                Print("--- l0a sending bri=" + b + " ---");
                SendPayload0A(new byte[] { 0xE0, 0x01, 0x00, 0xA1, 0x0A, 0x64, (byte)b,
                                           0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00 },
                              "l0a hsv 20 100 " + b);
                Thread.Sleep(2000);
                SendQuery(false);
                Thread.Sleep(2000);
            }
            Print("--- finishing ---");
            Wait(10);
        }
        else if (mode == "state")
        {
            // 1) baseline registers
            SendQuery(false);
            Thread.Sleep(2000);
            // 2) classic 0x3B A1 HSV command (cyan hue=200, sat=100, bri=80).
            //    If the strip turns cyan AND the registers show hue!=0,
            //    the registers are updated by the classic path, not by v3.
            Print("--- classic 0x3B A1 hsv 200 100 80 ---");
            SendPayload(new byte[] { 0x3B, 0xA1, 0x64, 0x64, 80, 0, 0, 0, 0, 0, 0, 0 },
                        "3B A1 hsv 200 100 80");
            Thread.Sleep(2000);
            SendQuery(false);
            Thread.Sleep(2000);
            // 3) standalone brightness ladder via 0x3B 0x01 (docs: value 0-100 percent)
            int[] ps = { 25, 50, 75, 100 };
            foreach (int p in ps)
            {
                Print("--- brightness percent=" + p + " ---");
                SendPayload(new byte[] { 0x3B, 0x01, 0x00, 0x00, (byte)p, 0x00, (byte)p, 0, 0, 0, 0, 0 },
                            "3B 01 bri " + p);
                Thread.Sleep(2000);
                SendQuery(false);
                Thread.Sleep(2000);
            }
            // 4) restore colour so the screen is not stuck dark
            SendPayload(new byte[] { 0xE0, 0x01, 0x00, 0xA1, 0x0A, 0x64, 0x64,
                                     0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00 },
                        "restore hsv 20 100 100");
            Thread.Sleep(1500);
            Print("--- final query ---");
            SendQuery(false);
            Wait(10);
        }
        else if (mode == "send" && args.Length >= 2)
        {
            SendPayload(HexArgs(args, 1), "custom");
            Thread.Sleep(1000);
            Print("--- watching for strip reports ---");
            Wait(30);
        }
        else if (mode == "query")
        {
            Print("--- sending state query in both variants (family 0x0A) ---");
            SendQuery(false);
            Thread.Sleep(4000);
            SendQuery(true);
            Print("--- watching for strip reports ---");
            Wait(30);
        }
        else
        {
            int secs = 60;
            if (args.Length >= 2) int.TryParse(args[1], out secs);
            Print("listening " + secs + "s (press the IR remote / use phone app now)");
            Wait(secs);
        }

        Print("done");
    }
}
