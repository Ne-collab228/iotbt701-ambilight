using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Devices.Bluetooth.Advertisement;

class BleScan
{
    static object lk = new object();
    static Dictionary<string, string> seen = new Dictionary<string, string>();
    static int packets = 0;

    static void Main(string[] args)
    {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 25;
        var w = new BluetoothLEAdvertisementWatcher();
        w.ScanningMode = BluetoothLEScanningMode.Active;
        w.Received += (s, e) =>
        {
            lock (lk)
            {
                packets++;
                string name = e.Advertisement.LocalName;
                string mac = Mac(e.BluetoothAddress);
                bool isNew = !seen.ContainsKey(mac);
                if (isNew) seen[mac] = name ?? "(noname)";
                if (!string.IsNullOrEmpty(name))
                {
                    if (isNew || (seen[mac] == "(noname)"))
                    {
                        seen[mac] = name;
                        Console.WriteLine("SEEN: '{0}' MAC={1} RSSI={2}", name, mac, e.RawSignalStrengthInDBm);
                    }
                }
            }
        };
        w.Start();
        Console.WriteLine("scanning {0}s...", seconds);
        for (int t = seconds; t > 0; t -= 5)
        {
            Thread.Sleep(5000);
            Console.WriteLine("  ...{0} packets, {1} devices", packets, seen.Count);
        }
        w.Stop();
        Console.WriteLine("=== DEVICES ===");
        lock (lk)
        {
            foreach (var kv in seen)
                Console.WriteLine("{0} -> '{1}'", kv.Key, kv.Value == "(noname)" ? "(noname)" : kv.Value);
        }
        Console.WriteLine("total packets={0}, devices={1}", packets, seen.Count);
    }

    static string Mac(ulong a)
    {
        byte[] b = BitConverter.GetBytes(a);
        return string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
            b[5], b[4], b[3], b[2], b[1], b[0]);
    }
}
