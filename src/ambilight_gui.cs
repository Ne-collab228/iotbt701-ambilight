// AmbilightOT GUI - screen average -> LED strip IOTBT701, with settings window
// Protocol: colour_data_v3 = E0 01 00 A1 <hi> <lo> <bri> 00 00 00 00 14 00 00 + checksum
// Fits desk shortcut Ambilight.lnk (winexe = no console window).
// C#5-safe code (csc.exe): no string interpolation, no ?. on methods.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

static class AmbiGui
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

// Localization: 5 languages, same set as the repo READMEs (ru/en/es/zh/pt)
public static class L10n
{
    public static readonly string[] Codes = { "ru", "en", "es", "zh", "pt" };
    public static readonly string[] Names = { "Русский", "English", "Español", "中文", "Português (BR)" };

    // key ids into the string table
    public const int TITLE = 0, STRIPWAIT = 1, AVG0 = 2, AVG = 3, NOTCONN = 4,
        START = 5, STOP = 6, SETTINGS = 7, INTERVAL = 8, SAT = 9, BRI = 10,
        MINBRI = 11, WHITE = 12, ALLMON = 13, AUTOSTART = 14, CONNECTING = 15,
        CONNECTED = 16, STOPPED = 17, SCREEN = 18, STRIP = 19, WHITEF = 20,
        SENT = 21, LANG = 22;

    static readonly string[][] T =
    {
        // 0: Russian
        new []{ "Ambilight OT — лента повторяет экран",
                "лента: ждёт запуска", "средний цвет экрана: -",
                "средний цвет экрана: RGB({0},{1},{2})",
                "не подключено", "ПУСК", "СТОП", "Настройки",
                "интервал обновления, мс", "насыщенность x", "яркость x",
                "мин. яркость %", "серый -> белый при s<",
                "все мониторы", "включать при старте",
                "подключение к ленте...", "подключено",
                "остановлено; лента осталась в текущем цвете",
                "экран {0}x{1}",
                "лента: {0}  s{1}%  b{2}%", "лента: БЕЛЫЙ  b{0}%",
                "отправлено: {0}", "Язык / Language" },
        // 1: English
        new []{ "Ambilight OT — strip mirrors the screen",
                "strip: waiting to start", "screen average: -",
                "screen average: RGB({0},{1},{2})",
                "not connected", "START", "STOP", "Settings",
                "update interval, ms", "saturation x", "brightness x",
                "min brightness %", "grey -> white when s<",
                "all monitors", "start on launch",
                "connecting to strip...", "connected",
                "stopped; strip keeps its current colour",
                "screen {0}x{1}",
                "strip: {0}  s{1}%  b{2}%", "strip: WHITE  b{0}%",
                "sent: {0}", "Language" },
        // 2: Spanish
        new []{ "Ambilight OT — la tira refleja la pantalla",
                "tira: en espera", "color medio de pantalla: -",
                "color medio de pantalla: RGB({0},{1},{2})",
                "sin conexión", "INICIAR", "DETENER", "Ajustes",
                "intervalo, ms", "saturación x", "brillo x",
                "brillo mín. %", "gris -> blanco con s<",
                "todos los monitores", "arrancar al abrir",
                "conectando a la tira...", "conectado",
                "detenido; la tira conserva su color",
                "pantalla {0}x{1}",
                "tira: {0}  s{1}%  b{2}%", "tira: BLANCO  b{0}%",
                "enviado: {0}", "Idioma" },
        // 3: Chinese (simplified)
        new []{ "Ambilight OT — 灯带同步屏幕",
                "灯带：等待启动", "屏幕平均色：-",
                "屏幕平均色：RGB({0},{1},{2})",
                "未连接", "启动", "停止", "设置",
                "刷新间隔，毫秒", "饱和度 ×", "亮度 ×",
                "最低亮度 %", "灰色→白色：当 s<",
                "所有显示器", "启动时自动开灯",
                "正在连接灯带...", "已连接",
                "已停止；灯带保持当前颜色",
                "屏幕 {0}x{1}",
                "灯带：{0}  s{1}%  b{2}%", "灯带：白色  亮度{0}%",
                "已发送：{0}", "语言" },
        // 4: Portuguese (Brazil)
        new []{ "Ambilight OT — fita espelha a tela",
                "fita: aguardando início", "cor média da tela: -",
                "cor média da tela: RGB({0},{1},{2})",
                "sem conexão", "INICIAR", "PARAR", "Ajustes",
                "intervalo de atualização, ms", "saturação x", "brilho x",
                "brilho mín. %", "cinza -> branco com s<",
                "todos os monitores", "ligar ao iniciar",
                "conectando à fita...", "conectado",
                "parado; a fita mantém a cor atual",
                "tela {0}x{1}",
                "fita: {0}  s{1}%  b{2}%", "fita: BRANCO  b{0}%",
                "enviado: {0}", "Idioma" }
    };

    public static string S(int lang, int key)
    {
        if (lang < 0 || lang >= T.Length) lang = 0;
        return T[lang][key];
    }

    public static int Index(string code)
    {
        for (int i = 0; i < Codes.Length; i++) if (Codes[i] == code) return i;
        return 0;
    }

    static readonly string[][] HUES =
    {
        new []{ "КРАСНЫЙ","ОРАНЖЕВЫЙ","ЖЁЛТЫЙ","САЛАТОВЫЙ","ЗЕЛЁНЫЙ","ВЕСЕННИЙ",
                "ЦИАН","ЛАЗУРНЫЙ","СИНИЙ","ФИОЛЕТОВЫЙ","ПУРПУРНЫЙ","РОЗОВЫЙ" },
        new []{ "RED","ORANGE","YELLOW","LIME","GREEN","SPRING",
                "CYAN","AZURE","BLUE","VIOLET","MAGENTA","ROSE" },
        new []{ "ROJO","NARANJA","AMARILLO","LIMA","VERDE","PRIMAVERA",
                "CIAN","AZUL CELESTE","AZUL","VIOLETA","MAGENTA","ROSA" },
        new []{ "红色","橙色","黄色","黄绿色","绿色","春绿色",
                "青色","天蓝","蓝色","紫色","品红","玫红" },
        new []{ "VERMELHO","LARANJA","AMARELO","LIMA","VERDE","PRIMAVERA",
                "CIANO","AZUL CELESTE","AZUL","VIOLETA","MAGENTA","ROSA" }
    };

    public static string Hue(int lang, int deg)
    {
        if (deg < 0) return "-";
        if (lang < 0 || lang >= HUES.Length) lang = 0;
        return HUES[lang][((deg + 15) % 360) / 30];
    }
}

public class MainForm : Form
{
    // ----- BLE -----
    const ulong MAC = 0x0865F0DE3701;
    const string SVC = "0000ffff-0000-1000-8000-00805f9b34fb";
    const string CHR = "0000ff01-0000-1000-8000-00805f9b34fb";
    byte counter = 0;
    GattCharacteristic f01 = null;
    BluetoothLEDevice dev = null;
    GattSession session = null;

    // ----- worker -----
    Thread worker;
    volatile bool running = false;
    volatile int intervalV = 700;
    double satGainV = 1.6;
    volatile int minBriV = 8;
    volatile int whiteThrV = 16;   // scenes less saturated than this -> white
    double briGainV = 1.4;
    volatile bool captureAllV = false;
    Bitmap big = null, small = null;
    Graphics gBig = null, gS = null;

    // ----- UI -----
    Panel preview;
    Label lblAvg, lblStrip, lblStatus, lblLastSend;
    Label lInterval, lSat, lBri, lMin, lWhite, lLang;
    ComboBox comboLang;
    Button btnStart;
    CheckBox chkAll, chkAuto;
    NumericUpDown numInterval, numSat, numBri, numMin, numWhite;
    TextBox txtLog;
    GroupBox grp;

    // ----- language -----
    int langIdx = 0;   // index into L10n Codes/Names, default Russian

    public MainForm()
    {
        Text = "Ambilight OT";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 502);

        // ----- preview swatch -----
        preview = new Panel();
        preview.Location = new Point(16, 16);
        preview.Size = new Size(200, 130);
        preview.BackColor = Color.FromArgb(20, 20, 20);
        preview.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(preview);

        lblStrip = new Label();
        lblStrip.Location = new Point(224, 20);
        lblStrip.Size = new Size(316, 44);
        lblStrip.Font = new Font(Font.FontFamily, 10, FontStyle.Bold);
        Controls.Add(lblStrip);

        lblAvg = new Label();
        lblAvg.Location = new Point(224, 66);
        lblAvg.Size = new Size(316, 20);
        Controls.Add(lblAvg);

        lblLastSend = new Label();
        lblLastSend.Location = new Point(224, 88);
        lblLastSend.Size = new Size(316, 20);
        lblLastSend.Text = "";
        Controls.Add(lblLastSend);

        lblStatus = new Label();
        lblStatus.Location = new Point(224, 112);
        lblStatus.Size = new Size(316, 34);
        Controls.Add(lblStatus);

        btnStart = new Button();
        btnStart.Location = new Point(16, 158);
        btnStart.Size = new Size(200, 40);
        btnStart.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
        btnStart.Click += OnStartStop;
        Controls.Add(btnStart);

        // ----- settings -----
        grp = new GroupBox();
        grp.Location = new Point(16, 206);
        grp.Size = new Size(528, 190);
        Controls.Add(grp);

        lInterval = MkLabel("", 14, 26);
        grp.Controls.Add(lInterval);
        numInterval = new NumericUpDown();
        numInterval.Minimum = 200; numInterval.Maximum = 3000;
        numInterval.Increment = 100; numInterval.Value = 700;
        numInterval.Location = new Point(300, 23); numInterval.Size = new Size(90, 24);
        numInterval.ValueChanged += delegate { intervalV = (int)numInterval.Value; };
        grp.Controls.Add(numInterval);

        lSat = MkLabel("", 14, 56);
        grp.Controls.Add(lSat);
        numSat = new NumericUpDown();
        numSat.Minimum = (decimal)0.5; numSat.Maximum = (decimal)3.0;
        numSat.Increment = (decimal)0.1; numSat.DecimalPlaces = 1;
        numSat.Value = (decimal)1.6;
        numSat.Location = new Point(300, 53); numSat.Size = new Size(90, 24);
        numSat.ValueChanged += delegate { satGainV = (double)numSat.Value; };
        grp.Controls.Add(numSat);

        lBri = MkLabel("", 14, 86);
        grp.Controls.Add(lBri);
        numBri = new NumericUpDown();
        numBri.Minimum = (decimal)1.0; numBri.Maximum = (decimal)2.0;
        numBri.Increment = (decimal)0.1; numBri.DecimalPlaces = 1;
        numBri.Value = (decimal)1.4;
        numBri.Location = new Point(300, 83); numBri.Size = new Size(90, 24);
        numBri.ValueChanged += delegate { briGainV = (double)numBri.Value; };
        grp.Controls.Add(numBri);

        lMin = MkLabel("", 14, 116);
        grp.Controls.Add(lMin);
        numMin = new NumericUpDown();
        numMin.Minimum = 0; numMin.Maximum = 40;
        numMin.Increment = 1; numMin.Value = 8;
        numMin.Location = new Point(300, 113); numMin.Size = new Size(90, 24);
        numMin.ValueChanged += delegate { minBriV = (int)numMin.Value; };
        grp.Controls.Add(numMin);

        lWhite = MkLabel("", 14, 146);
        grp.Controls.Add(lWhite);
        numWhite = new NumericUpDown();
        numWhite.Minimum = 0; numWhite.Maximum = 40;
        numWhite.Increment = 1; numWhite.Value = 16;
        numWhite.Location = new Point(300, 143); numWhite.Size = new Size(90, 24);
        numWhite.ValueChanged += delegate { whiteThrV = (int)numWhite.Value; };
        grp.Controls.Add(numWhite);

        chkAll = new CheckBox();
        chkAll.Location = new Point(404, 24); chkAll.AutoSize = true;
        chkAll.CheckedChanged += delegate { captureAllV = chkAll.Checked; };
        grp.Controls.Add(chkAll);

        chkAuto = new CheckBox();
        chkAuto.Location = new Point(404, 52); chkAuto.AutoSize = true;
        grp.Controls.Add(chkAuto);

        // ----- language selector (same 5 as the repo READMEs) -----
        lLang = MkLabel("", 404, 84);
        grp.Controls.Add(lLang);

        comboLang = new ComboBox();
        comboLang.DropDownStyle = ComboBoxStyle.DropDownList;
        comboLang.Location = new Point(404, 104); comboLang.Size = new Size(120, 24);
        foreach (string n in L10n.Names) comboLang.Items.Add(n);
        comboLang.SelectedIndexChanged += delegate
        {
            langIdx = comboLang.SelectedIndex;
            SaveSettings();
            ApplyLang();
        };
        grp.Controls.Add(comboLang);

        // ----- log -----
        txtLog = new TextBox();
        txtLog.Multiline = true; txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Font = new Font("Consolas", 9);
        txtLog.Location = new Point(16, 402);
        txtLog.Size = new Size(528, 86);
        Controls.Add(txtLog);

        FormClosing += OnClosing;
        LoadSettings();
        comboLang.SelectedIndex = langIdx;      // fires ApplyLang
        PushUItoVolatile();
        if (chkAuto.Checked) StartWork();
    }

    // set every static control text from the current language table
    void ApplyLang()
    {
        Text = L10n.S(langIdx, L10n.TITLE);
        grp.Text = L10n.S(langIdx, L10n.SETTINGS);
        lInterval.Text = L10n.S(langIdx, L10n.INTERVAL);
        lSat.Text = L10n.S(langIdx, L10n.SAT);
        lBri.Text = L10n.S(langIdx, L10n.BRI);
        lMin.Text = L10n.S(langIdx, L10n.MINBRI);
        lWhite.Text = L10n.S(langIdx, L10n.WHITE);
        lLang.Text = L10n.S(langIdx, L10n.LANG);
        chkAll.Text = L10n.S(langIdx, L10n.ALLMON);
        chkAuto.Text = L10n.S(langIdx, L10n.AUTOSTART);
        btnStart.Text = running ? L10n.S(langIdx, L10n.STOP) : L10n.S(langIdx, L10n.START);
        if (!running)
        {
            lblStrip.Text = L10n.S(langIdx, L10n.STRIPWAIT);
            lblAvg.Text = L10n.S(langIdx, L10n.AVG0);
            lblStatus.Text = L10n.S(langIdx, L10n.NOTCONN);
            lblLastSend.Text = "";
        }
    }

    static Label MkLabel(string t, int x, int y)
    {
        Label l = new Label();
        l.Text = t; l.Location = new Point(x, y); l.AutoSize = true;
        return l;
    }

    void PushUItoVolatile()
    {
        intervalV = (int)numInterval.Value;
        satGainV = (double)numSat.Value;
        briGainV = (double)numBri.Value;
        minBriV = (int)numMin.Value;
        whiteThrV = (int)numWhite.Value;
        captureAllV = chkAll.Checked;
    }

    // ----- start / stop -----
    void OnStartStop(object sender, EventArgs e)
    {
        if (running) StopWork(); else StartWork();
    }

    void StartWork()
    {
        if (running) return;
        running = true;
        PushUItoVolatile();
        btnStart.Text = L10n.S(langIdx, L10n.STOP);
        worker = new Thread(WorkLoop);
        worker.IsBackground = true;
        worker.Start();
    }

    void StopWork()
    {
        if (!running) return;
        running = false;
        btnStart.Text = L10n.S(langIdx, L10n.START);
        lblStatus.Text = L10n.S(langIdx, L10n.STOPPED);
        // worker exits loop itself: closes bitmaps and BLE session
    }

    void OnClosing(object sender, FormClosingEventArgs e)
    {
        running = false;
        SaveSettings();
        Thread.Sleep(150);          // let worker finish its tick
        Drop();
    }

    // ----- worker loop -----
    void WorkLoop()
    {
        int lastH = -999, lastS = -999, lastB = -999;
        DateTime lastSend = DateTime.MinValue;
        double smR = -1, smG = -1, smB = -1;
        try
        {
            int sx, sy, sw, sh;
            ScreenBounds(captureAllV, out sx, out sy, out sw, out sh);
            UiSafe(delegate { lblStatus.Text = string.Format(L10n.S(langIdx, L10n.SCREEN), sw, sh); });
            Log("screen " + sw + "x" + sh + " @(" + sx + "," + sy + ")");

            big = new Bitmap(sw, sh);
            small = new Bitmap(96, 54);
            gBig = Graphics.FromImage(big);
            gS = Graphics.FromImage(small);

            while (running)
            {
                if (f01 == null)
                {
                    UiSafe(delegate { lblStatus.Text = L10n.S(langIdx, L10n.CONNECTING); });
                    if (!Connect()) { Thread.Sleep(6000); continue; }
                    if (!Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "power on"))
                    { Drop(); Thread.Sleep(4000); continue; }
                    lastH = -999; lastS = -999; lastB = -999;
                    smR = smG = smB = -1;
                    UiSafe(delegate { lblStatus.Text = L10n.S(langIdx, L10n.CONNECTED); });
                    Thread.Sleep(1200);
                }

                Thread.Sleep(intervalV);
                if (!running) break;
                try
                {
                    gBig.CopyFromScreen(sx, sy, 0, 0, new Size(sw, sh));
                    gS.DrawImage(big, new Rectangle(0, 0, small.Width, small.Height),
                                 new Rectangle(0, 0, sw, sh), GraphicsUnit.Pixel);
                    int rAvg, gAvg, bAvg; double wSum;
                    Average(small, out rAvg, out gAvg, out bAvg, out wSum);

                    double delta = (smR < 0) ? 999 :
                        (Math.Abs(rAvg - smR) + Math.Abs(gAvg - smG) + Math.Abs(bAvg - smB));
                    double k = delta > 40 ? 0.7 : 0.35;
                    if (smR < 0) { smR = rAvg; smG = gAvg; smB = bAvg; }
                    else { smR += (rAvg - smR) * k; smG += (gAvg - smG) * k; smB += (bAvg - smB) * k; }

                    int rM = (int)smR, gM = (int)smG, bM = (int)smB;
                    int hue, sat, val;
                    RgbToHsv(rM, gM, bM, out hue, out sat, out val);
                    double sg = satGainV;
                    sat = (int)Math.Min(100, sat * sg);
                    if (val < 14 || sat < whiteThrV) { sat = 0; hue = 0; }
                    int bri = Math.Max(minBriV, (int)Math.Min(100, val * briGainV));

                    // preview: the colour the strip actually shows
                    Color c = HsvToColor(hue, sat, bri);
                    bool changed = Math.Abs(hue - lastH) > 2 || Math.Abs(sat - lastS) > 2
                                   || Math.Abs(bri - lastB) > 2;
                    if (!running) break;
                    UiSafe(delegate
                    {
                        preview.BackColor = c;
                        lblAvg.Text = string.Format(L10n.S(langIdx, L10n.AVG), rM, gM, bM);
                        lblStrip.Text = (sat == 0)
                            ? string.Format(L10n.S(langIdx, L10n.WHITEF), bri)
                            : string.Format(L10n.S(langIdx, L10n.STRIP), L10n.Hue(langIdx, hue), sat, bri);
                    });

                    if (changed && (DateTime.Now - lastSend).TotalMilliseconds >= 350)
                    {
                        if (SendV3Color(hue, sat, bri))
                        {
                            lastH = hue; lastS = sat; lastB = bri;
                            lastSend = DateTime.Now;
                            UiSafe(delegate { lblLastSend.Text =
                                string.Format(L10n.S(langIdx, L10n.SENT), DateTime.Now.ToString("HH:mm:ss")); });
                        }
                        else Drop();
                    }
                    else if (!changed && (DateTime.Now - lastSend).TotalSeconds > 25)
                    {
                        if (SendV3Color(hue, sat, bri))
                        { lastSend = DateTime.Now; lastH = hue; lastS = sat; lastB = bri; }
                        else Drop();
                    }
                }
                catch (Exception ex)
                {
                    Log("loop: " + ex.Message);
                    Drop();
                    Thread.Sleep(3000);
                }
            }
        }
        catch (Exception ex) { Log("worker: " + ex.Message); }
        finally
        {
            if (big != null) big.Dispose();
            if (small != null) small.Dispose();
            if (gBig != null) gBig.Dispose();
            if (gS != null) gS.Dispose();
            big = null; small = null; gBig = null; gS = null;
            Drop();
            UiSafe(delegate { if (!running) lblStatus.Text = L10n.S(langIdx, L10n.STOPPED); });
            Log("stopped");
        }
    }

    delegate void D();
    void UiSafe(D d)
    {
        if (!IsHandleCreated) return;
        try { BeginInvoke(d); } catch { }
    }

    void Log(string m)
    {
        UiSafe(delegate
        {
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss ") + m + Environment.NewLine);
            if (txtLog.Lines.Length > 300)
            {
                txtLog.Text = string.Join(Environment.NewLine,
                    txtLog.Lines, txtLog.Lines.Length - 200, 200);
            }
        });
    }

    // ----- settings ini -----
    string IniPath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ambilight_settings.txt");
    }
    void SaveSettings()
    {
        try
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("interval=" + numInterval.Value);
            sb.AppendLine("satGain=" + numSat.Value.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("briGain=" + numBri.Value.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("minBri=" + numMin.Value);
            sb.AppendLine("whiteThr=" + numWhite.Value);
            sb.AppendLine("allScreens=" + (chkAll.Checked ? "1" : "0"));
            sb.AppendLine("autostart=" + (chkAuto.Checked ? "1" : "0"));
            sb.AppendLine("lang=" + L10n.Codes[langIdx]);
            File.WriteAllText(IniPath(), sb.ToString());
        }
        catch { }
    }
    void LoadSettings()
    {
        try
        {
            if (!File.Exists(IniPath())) return;
            string[] lines = File.ReadAllLines(IniPath());
            foreach (string ln0 in lines)
            {
                string ln = ln0.Trim();
                int eq = ln.IndexOf('=');
                if (eq < 0) continue;
                string key = ln.Substring(0, eq).Trim().ToLower();
                string v = ln.Substring(eq + 1).Trim();
                try
                {
                    if (key == "interval") numInterval.Value = Clamp(numInterval, decimal.Parse(v, CultureInfo.InvariantCulture));
                    else if (key == "satgain") numSat.Value = Clamp(numSat, decimal.Parse(v, CultureInfo.InvariantCulture));
                    else if (key == "brigain") numBri.Value = Clamp(numBri, decimal.Parse(v, CultureInfo.InvariantCulture));
                    else if (key == "minbri") numMin.Value = Clamp(numMin, decimal.Parse(v, CultureInfo.InvariantCulture));
                    else if (key == "whitethr") numWhite.Value = Clamp(numWhite, decimal.Parse(v, CultureInfo.InvariantCulture));
                    else if (key == "allscreens") chkAll.Checked = (v == "1");
                    else if (key == "autostart") chkAuto.Checked = (v == "1");
                    else if (key == "lang") langIdx = L10n.Index(v);
                }
                catch { }
            }
        }
        catch { }
    }
    static decimal Clamp(NumericUpDown n, decimal v)
    {
        if (v < n.Minimum) return n.Minimum;
        if (v > n.Maximum) return n.Maximum;
        return v;
    }

    // ================= protocol (same as console version) =================
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

    byte[] Frame(byte[] payload)
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

    bool Send(byte[] payload, string label)
    {
        byte[] frame = Frame(payload);
        try
        {
            var wmi = typeof(GattCharacteristic).GetMethods()
                .Where(x => x.Name == "WriteValueAsync" && x.GetParameters().Length == 2).First();
            object op = wmi.Invoke(f01, new object[] { BytesToBuffer(frame), GattWriteOption.WriteWithoutResponse });
            GattCommunicationStatus st = (GattCommunicationStatus)AwaitOp(op, typeof(GattCommunicationStatus));
            if (st != GattCommunicationStatus.Success) Log("send " + label + " : " + st);
            return st == GattCommunicationStatus.Success;
        }
        catch (Exception ex)
        {
            Log("send " + label + " : " + ex.Message);
            return false;
        }
    }

    bool SendV3Color(int hueDeg, int satPct, int briPct)
    {
        if (satPct < 1) hueDeg = 0;
        if (hueDeg < 0) hueDeg = 0;
        if (hueDeg > 359) hueDeg = hueDeg % 360;
        if (briPct < 1) briPct = 1;
        if (briPct > 100) briPct = 100;
        hueDeg &= ~1;                                 // firmware quirk: even hues only
        if (hueDeg < 0) hueDeg = 0;
        if (hueDeg > 359) hueDeg = 359;
        if (satPct < 0) satPct = 0;
        if (satPct > 100) satPct = 100;
        int packed = (hueDeg << 7) | satPct;
        byte hi = (byte)((packed >> 8) & 0xFF);
        byte lo = (byte)(packed & 0xFF);
        return Send(new byte[] { 0xE0, 0x01, 0x00, 0xA1, hi, lo, (byte)briPct,
                                 0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x00 },
                    "h" + hueDeg + " s" + satPct + " b" + briPct);
    }

    bool Connect()
    {
        Drop();
        for (int i = 1; i <= 4 && running; i++)
        {
            Log("connecting (" + i + ")...");
            var op = BluetoothLEDevice.FromBluetoothAddressAsync(MAC);
            if (op != null)
            {
                dev = (BluetoothLEDevice)AwaitOp(op, typeof(BluetoothLEDevice));
                if (dev != null) break;
            }
            Thread.Sleep(4000);
        }
        if (dev == null) { Log("device NOT reachable"); return false; }

        var sop = GattSession.FromDeviceIdAsync(dev.BluetoothDeviceId);
        session = (GattSession)AwaitOp(sop, typeof(GattSession));
        if (session != null) session.MaintainConnection = true;
        for (int i = 1; i <= 8 && running; i++)
        {
            Thread.Sleep(1500);
            if (dev.ConnectionStatus == BluetoothConnectionStatus.Connected) break;
        }
        if (dev == null || dev.ConnectionStatus != BluetoothConnectionStatus.Connected)
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

    void Drop()
    {
        if (session != null) { try { session.Dispose(); } catch { } session = null; }
        if (dev != null) { try { dev.Dispose(); } catch { } dev = null; }
        f01 = null;
    }

    // ================= colour / screen =================
    static void RgbToHsv(int r, int g, int b, out int hueDeg, out int satPct, out int valPct)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double mx = Math.Max(rf, Math.Max(gf, bf)), mn = Math.Min(rf, Math.Min(gf, bf));
        double d = mx - mn;
        double h = 0;
        if (d > 1e-9)
        {
            if (mx == rf) h = 60 * (((gf - bf) / d) % 6);
            else if (mx == gf) h = 60 * (((bf - rf) / d) + 2);
            else h = 60 * (((rf - gf) / d) + 4);
            if (h < 0) h += 360;
        }
        hueDeg = (int)Math.Round(h) % 360;
        satPct = (int)Math.Round((mx <= 1e-9 ? 0 : d / mx) * 100);
        valPct = (int)Math.Round(mx * 100);
    }

    static Color HsvToColor(int hue, int sat, int val)
    {
        double h = hue % 360, s = Math.Min(100, Math.Max(0, sat)) / 100.0;
        double v = Math.Min(100, Math.Max(0, val)) / 100.0;
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

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
                double satN = (mx <= 0) ? 0 : (double)(mx - mn) / mx;
                double vN = mx / 255.0;
                double w = (0.25 + 1.75 * satN) * (0.35 + 0.65 * vN);
                if (mx < 12) w = 0.02;
                rs += w * rr; gs += w * gg; bs += w * bb; ws += w;
            }
        }
        src.UnlockBits(d);
        wSum = ws;
        rAvg = ws > 0.1 ? (int)(rs / ws) : 0;
        gAvg = ws > 0.1 ? (int)(gs / ws) : 0;
        bAvg = ws > 0.1 ? (int)(bs / ws) : 0;
    }

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    static void ScreenBounds(bool all, out int x, out int y, out int w, out int h)
    {
        try { SetProcessDPIAware(); } catch { }
        if (all)
        {
            x = GetSystemMetrics(76); y = GetSystemMetrics(77);
            w = GetSystemMetrics(78); h = GetSystemMetrics(79);
            if (w <= 0 || h <= 0) { x = 0; y = 0; w = GetSystemMetrics(0); h = GetSystemMetrics(1); }
        }
        else { x = 0; y = 0; w = GetSystemMetrics(0); h = GetSystemMetrics(1); }
    }
}
