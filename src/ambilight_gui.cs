// AmbilightOT GUI - screen average -> LED strip IOTBT701, with settings window
// Protocol: colour_data_v3 = E0 01 00 A1 <hi> <lo> <bri> 00 00 00 00 14 00 00 + checksum
// v1.7: time-based HSV glide on a separate capture thread (fade 1..10 = tau (n-1)*0.25 s; 1 = snap)
// Fits desk shortcut Ambilight.lnk (winexe = no console window).
// C#5-safe code (csc.exe): no string interpolation, no ?. on methods.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        SENT = 21, LANG = 22, WARMHUE = 23, WARMSAT = 24, BOOT = 25, LOG = 26,
        FADE = 27, BASIC = 28, ADV = 29, S_SMOOTH = 30, S_BRI = 31, S_SAT = 32;

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
                "отправлено: {0}", "Язык / Language",
                "тёплая поправка: тон °", "тёплая поправка %",
                "запускать с Windows",
                "лог подключений",
                "плавность перехода, 1..10 (1 = резко)",
                "Основное", "Дополнительно", "Плавность", "Яркость", "Насыщенность" },
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
                "sent: {0}", "Language",
                "warm white: hue °", "warm white: mix %",
                "run at Windows startup",
                "connection log",
                "transition smoothness 1..10 (1 = snap)",
                "Basics", "Advanced", "Smoothness", "Brightness", "Saturation" },
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
                "enviado: {0}", "Idioma",
                "blanco cálido: tono °", "mezcla cálida %",
                "iniciar con Windows",
                "registro de conexión",
                "suavidad de transición 1..10 (1 = brusco)",
                "Básico", "Avanzado", "Suavidad", "Brillo", "Saturación" },
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
                "已发送：{0}", "语言",
                "暖白修正：色调 °", "暖白强度 %",
                "随 Windows 启动",
                "连接日志",
                "过渡平滑度 1..10（1 = 瞬间）",
                "基本", "高级", "平滑度", "亮度", "饱和度" },
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
                "enviado: {0}", "Idioma",
                "branco quente: tom °", "mistura quente %",
                "iniciar com o Windows",
                "log de conexão",
                "suavidade da transição 1..10 (1 = seco)",
                "Básico", "Avançado", "Suavidade", "Brilho", "Saturação" },
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
    volatile int warmHueV = 48;    // warm-white correction: amber hue, 0..120
    volatile int warmSatV = 5;     // warm-white mix strength %, 0 = off
    double briGainV = 1.4;
    volatile int fadeV = 3;        // smooth-fade step count toward the target: 1 = snap, >1 = glide
    volatile bool captureAllV = false;
    Bitmap big = null, small = null;
    Graphics gBig = null, gS = null;

    // ----- UI -----
    static readonly Color ColBg = Color.FromArgb(22, 24, 30);
    static readonly Color ColCard = Color.FromArgb(33, 36, 46);
    static readonly Color ColField = Color.FromArgb(46, 50, 63);
    static readonly Color ColText = Color.FromArgb(235, 238, 245);
    static readonly Color ColMuted = Color.FromArgb(140, 148, 165);
    static readonly Color ColAccent = Color.FromArgb(88, 166, 255);
    static readonly Color ColGreen = Color.FromArgb(46, 160, 90);
    static readonly Color ColRed = Color.FromArgb(214, 69, 69);
    static readonly Color ColTrack = Color.FromArgb(62, 67, 82);

    Swatch preview;
    Label lblTitle, lblAvg, lblStrip, lblStatus, lblLastSend;
    Label lInterval, lSat, lBri, lMin, lWhite, lLang, lWarm1, lWarm2, lFade;
    Label lcBasic, lsSmooth, lsBri, lsSat, vSmooth, vBri, vSat;
    ComboBox comboLang;
    PillButton btnStart, btnAdv, btnLog;
    CheckBox chkAll, chkAuto, chkBoot;
    Card cardBasic, cardOpt, cardAdv;
    FlatSlider slSmooth, slBri, slSat;
    bool advOpen = false;

    // users Startup folder shortcut definition
    static readonly string BootLnkPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "AmbilightOT.lnk");

    // persistent log (full connection history, for the crash window)
    static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "ambilight_log.txt");
    static readonly object logLock = new object();
    bool hadLink = false;          // a live connection existed and then died on its own
    NumericUpDown numInterval, numSat, numBri, numMin, numWhite, numWarmHue, numWarmSat, numFade;
    TextBox txtLog;

    // ----- language -----
    int langIdx = 0;   // index into L10n Codes/Names, default Russian

    static void StyleNum(NumericUpDown n)
    {
        n.BackColor = ColField; n.ForeColor = ColText; n.BorderStyle = BorderStyle.FixedSingle;
    }

    static Label MkLabel(string t, int x, int y)
    {
        Label l = new Label();
        l.Text = t; l.Location = new Point(x, y); l.AutoSize = true;
        return l;
    }

    // one slider row inside a card: name on the left, slider, value on the right
    FlatSlider MkRow(Card card, int ry, out Label name, out Label value)
    {
        name = new Label();
        name.Location = new Point(16, ry); name.Size = new Size(112, 28);
        name.TextAlign = ContentAlignment.MiddleLeft;
        card.Controls.Add(name);
        FlatSlider s = new FlatSlider();
        s.Location = new Point(132, ry); s.Size = new Size(226, 28);
        s.BackColor = ColCard; s.Accent = ColAccent; s.TrackColor = ColTrack;
        card.Controls.Add(s);
        value = new Label();
        value.Location = new Point(364, ry); value.Size = new Size(62, 28);
        value.TextAlign = ContentAlignment.MiddleRight;
        value.ForeColor = ColMuted;
        card.Controls.Add(value);
        return s;
    }

    public MainForm()
    {
        Text = "Ambilight OT";
        Font = new Font("Segoe UI", 9.5f);
        BackColor = ColBg; ForeColor = ColText;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        AutoScroll = true;
        ClientSize = new Size(480, 600);

        const int X = 20, W = 440;
        int y = 14;

        lblTitle = new Label();
        lblTitle.Text = "Ambilight";
        lblTitle.Font = new Font("Segoe UI Semibold", 20f);
        lblTitle.ForeColor = ColText;
        lblTitle.AutoSize = true; lblTitle.Location = new Point(X - 2, y);
        Controls.Add(lblTitle);
        y += 44;

        lblStatus = new Label();
        lblStatus.Location = new Point(X, y); lblStatus.Size = new Size(W, 20);
        lblStatus.ForeColor = ColMuted; lblStatus.AutoEllipsis = true;
        Controls.Add(lblStatus);
        y += 28;

        preview = new Swatch();
        preview.Location = new Point(X, y); preview.Size = new Size(W, 120);
        preview.BackColor = Color.FromArgb(20, 20, 20);
        Controls.Add(preview);
        y += 130;

        lblStrip = new Label();
        lblStrip.Location = new Point(X, y); lblStrip.Size = new Size(W, 26);
        lblStrip.Font = new Font("Segoe UI Semibold", 11.5f);
        Controls.Add(lblStrip);
        y += 28;

        lblAvg = new Label();
        lblAvg.Location = new Point(X, y); lblAvg.Size = new Size(290, 20);
        lblAvg.ForeColor = ColMuted;
        Controls.Add(lblAvg);

        lblLastSend = new Label();
        lblLastSend.Location = new Point(X + 290, y); lblLastSend.Size = new Size(150, 20);
        lblLastSend.TextAlign = ContentAlignment.MiddleRight;
        lblLastSend.ForeColor = ColMuted; lblLastSend.Text = "";
        Controls.Add(lblLastSend);
        y += 32;

        btnStart = new PillButton();
        btnStart.Location = new Point(X, y); btnStart.Size = new Size(W, 54);
        btnStart.Font = new Font("Segoe UI Semibold", 13f);
        btnStart.FillProvider = delegate { return running ? ColRed : ColGreen; };
        btnStart.Click += OnStartStop;
        Controls.Add(btnStart);
        y += 70;

        // ----- numeric models (the source of truth for save/load; shown as sliders or in Advanced) -----
        numInterval = new NumericUpDown();
        numInterval.Minimum = 50; numInterval.Maximum = 3000;
        numInterval.Increment = 50; numInterval.Value = 700;
        numInterval.Size = new Size(90, 24);
        numInterval.ValueChanged += delegate { intervalV = (int)numInterval.Value; };

        numSat = new NumericUpDown();
        numSat.Minimum = (decimal)0.5; numSat.Maximum = (decimal)3.0;
        numSat.Increment = (decimal)0.1; numSat.DecimalPlaces = 1;
        numSat.Value = (decimal)1.6;
        numSat.ValueChanged += delegate { satGainV = (double)numSat.Value; };

        numBri = new NumericUpDown();
        numBri.Minimum = (decimal)1.0; numBri.Maximum = (decimal)2.0;
        numBri.Increment = (decimal)0.1; numBri.DecimalPlaces = 1;
        numBri.Value = (decimal)1.4;
        numBri.ValueChanged += delegate { briGainV = (double)numBri.Value; };

        numMin = new NumericUpDown();
        numMin.Minimum = 0; numMin.Maximum = 40;
        numMin.Increment = 1; numMin.Value = 8;
        numMin.Size = new Size(90, 24);
        numMin.ValueChanged += delegate { minBriV = (int)numMin.Value; };

        numWhite = new NumericUpDown();
        numWhite.Minimum = 0; numWhite.Maximum = 40;
        numWhite.Increment = 1; numWhite.Value = 16;
        numWhite.Size = new Size(90, 24);
        numWhite.ValueChanged += delegate { whiteThrV = (int)numWhite.Value; };

        numWarmHue = new NumericUpDown();
        numWarmHue.Minimum = 0; numWarmHue.Maximum = 120;
        numWarmHue.Increment = 2; numWarmHue.Value = 48;
        numWarmHue.Size = new Size(90, 24);
        numWarmHue.ValueChanged += delegate { warmHueV = (int)numWarmHue.Value; };

        numWarmSat = new NumericUpDown();
        numWarmSat.Minimum = 0; numWarmSat.Maximum = 20;
        numWarmSat.Increment = 1; numWarmSat.Value = 5;
        numWarmSat.Size = new Size(90, 24);
        numWarmSat.ValueChanged += delegate { warmSatV = (int)numWarmSat.Value; };

        numFade = new NumericUpDown();
        numFade.Minimum = 1; numFade.Maximum = 10;
        numFade.Increment = 1; numFade.Value = 3;
        numFade.ValueChanged += delegate { fadeV = (int)numFade.Value; };

        // ----- card: basics (sliders) -----
        cardBasic = new Card();
        cardBasic.Location = new Point(X, y); cardBasic.Size = new Size(W, 164);
        Controls.Add(cardBasic);
        lcBasic = new Label();
        lcBasic.Location = new Point(16, 12); lcBasic.AutoSize = true;
        lcBasic.Font = new Font("Segoe UI Semibold", 10.5f);
        lcBasic.ForeColor = ColAccent;
        cardBasic.Controls.Add(lcBasic);

        slSmooth = MkRow(cardBasic, 44, out lsSmooth, out vSmooth);
        slSmooth.Minimum = 1; slSmooth.Maximum = 10; slSmooth.Value = 3;
        slSmooth.ValueChanged += delegate { numFade.Value = slSmooth.Value; SyncSliderLabels(); };
        numFade.ValueChanged += delegate { slSmooth.Value = (int)numFade.Value; SyncSliderLabels(); };

        slBri = MkRow(cardBasic, 84, out lsBri, out vBri);
        slBri.Minimum = 10; slBri.Maximum = 20; slBri.Value = 14;
        slBri.ValueChanged += delegate { numBri.Value = slBri.Value / 10m; SyncSliderLabels(); };
        numBri.ValueChanged += delegate { slBri.Value = (int)Math.Round(numBri.Value * 10m); SyncSliderLabels(); };

        slSat = MkRow(cardBasic, 124, out lsSat, out vSat);
        slSat.Minimum = 5; slSat.Maximum = 30; slSat.Value = 16;
        slSat.ValueChanged += delegate { numSat.Value = slSat.Value / 10m; SyncSliderLabels(); };
        numSat.ValueChanged += delegate { slSat.Value = (int)Math.Round(numSat.Value * 10m); SyncSliderLabels(); };
        y += 176;

        // ----- card: options -----
        cardOpt = new Card();
        cardOpt.Location = new Point(X, y); cardOpt.Size = new Size(W, 118);
        Controls.Add(cardOpt);

        chkAll = new CheckBox();
        chkAll.Location = new Point(16, 14); chkAll.AutoSize = true;
        chkAll.CheckedChanged += delegate { captureAllV = chkAll.Checked; };
        cardOpt.Controls.Add(chkAll);

        // run at Windows startup: the .lnk in the user's Startup folder is the single
        // source of truth (checked if it exists; toggling creates/deletes it)
        chkBoot = new CheckBox();
        chkBoot.Location = new Point(16, 46); chkBoot.AutoSize = true;
        chkBoot.Checked = File.Exists(BootLnkPath);
        chkBoot.CheckedChanged += delegate
        {
            try
            {
                if (chkBoot.Checked) CreateBootShortcut(); else DeleteBootShortcut();
            }
            catch (Exception ex) { Log("startup shortcut: " + ex.Message); }
        };
        cardOpt.Controls.Add(chkBoot);

        chkAuto = new CheckBox();
        chkAuto.Location = new Point(16, 78); chkAuto.AutoSize = true;
        chkAuto.CheckedChanged += delegate { SaveSettings(); };
        cardOpt.Controls.Add(chkAuto);

        lLang = MkLabel("", 252, 14);
        lLang.ForeColor = ColMuted;
        cardOpt.Controls.Add(lLang);

        comboLang = new ComboBox();
        comboLang.DropDownStyle = ComboBoxStyle.DropDownList;
        comboLang.FlatStyle = FlatStyle.Flat;
        comboLang.BackColor = ColField; comboLang.ForeColor = ColText;
        comboLang.Location = new Point(252, 36); comboLang.Size = new Size(174, 26);
        foreach (string n in L10n.Names) comboLang.Items.Add(n);
        comboLang.SelectedIndexChanged += delegate
        {
            langIdx = comboLang.SelectedIndex;
            SaveSettings();
            ApplyLang();
        };
        cardOpt.Controls.Add(comboLang);

        // connection log button (full log incl. crash/disconnect events)
        btnLog = new PillButton();
        btnLog.Location = new Point(252, 74); btnLog.Size = new Size(174, 30);
        btnLog.FillProvider = delegate { return ColTrack; };
        btnLog.Click += delegate { ShowLogWindow(false); };
        cardOpt.Controls.Add(btnLog);
        y += 130;

        // ----- advanced (collapsible) -----
        btnAdv = new PillButton();
        btnAdv.Location = new Point(X, y); btnAdv.Size = new Size(W, 34);
        btnAdv.FillProvider = delegate { return ColCard; };
        btnAdv.Click += delegate { advOpen = !advOpen; ApplyLang(); Relayout(); };
        Controls.Add(btnAdv);
        y += 44;

        cardAdv = new Card();
        cardAdv.Location = new Point(X, y); cardAdv.Size = new Size(W, 5 * 34 + 20 + 84);
        cardAdv.Visible = false;
        Controls.Add(cardAdv);

        lInterval = MkLabel("", 16, 16 + 4); cardAdv.Controls.Add(lInterval);
        lMin = MkLabel("", 16, 16 + 34 + 4); cardAdv.Controls.Add(lMin);
        lWhite = MkLabel("", 16, 16 + 68 + 4); cardAdv.Controls.Add(lWhite);
        lWarm1 = MkLabel("", 16, 16 + 102 + 4); cardAdv.Controls.Add(lWarm1);
        lWarm2 = MkLabel("", 16, 16 + 136 + 4); cardAdv.Controls.Add(lWarm2);
        lFade = new Label();                    // kept for ApplyLang compatibility (unused in layout)
        NumericUpDown[] advNums = { numInterval, numMin, numWhite, numWarmHue, numWarmSat };
        for (int i = 0; i < advNums.Length; i++)
        {
            StyleNum(advNums[i]);
            advNums[i].Location = new Point(W - 16 - 90, 16 + i * 34);
            cardAdv.Controls.Add(advNums[i]);
        }

        txtLog = new TextBox();
        txtLog.Multiline = true; txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.Font = new Font("Consolas", 9);
        txtLog.BackColor = ColField; txtLog.ForeColor = ColMuted;
        txtLog.BorderStyle = BorderStyle.None;
        txtLog.Location = new Point(16, 16 + 5 * 34 + 4);
        txtLog.Size = new Size(W - 32, 72);
        cardAdv.Controls.Add(txtLog);

        FormClosing += OnClosing;
        LoadSettings();
        comboLang.SelectedIndex = langIdx;      // fires ApplyLang
        PushUItoVolatile();
        SyncSliderLabels();
        Relayout();
        if (chkAuto.Checked) StartWork();
    }

    void SyncSliderLabels()
    {
        vSmooth.Text = slSmooth.Value + " / 10";
        vBri.Text = "x" + (slBri.Value / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
        vSat.Text = "x" + (slSat.Value / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
    }

    // window height follows the Advanced panel; clamp to the screen (form scrolls if it does not fit)
    void Relayout()
    {
        cardAdv.Visible = advOpen;
        int h = (advOpen ? cardAdv.Bottom : btnAdv.Bottom) + 16;
        int max = Screen.PrimaryScreen.WorkingArea.Height - 90;
        ClientSize = new Size(480, Math.Min(h, max));
    }

    // set every static control text from the current language table
    void ApplyLang()
    {
        Text = L10n.S(langIdx, L10n.TITLE);
        lcBasic.Text = L10n.S(langIdx, L10n.BASIC);
        lsSmooth.Text = L10n.S(langIdx, L10n.S_SMOOTH);
        lsBri.Text = L10n.S(langIdx, L10n.S_BRI);
        lsSat.Text = L10n.S(langIdx, L10n.S_SAT);
        btnAdv.Text = L10n.S(langIdx, L10n.ADV) + (advOpen ? "  ▾" : "  ▸");
        lInterval.Text = L10n.S(langIdx, L10n.INTERVAL);
        lMin.Text = L10n.S(langIdx, L10n.MINBRI);
        lWhite.Text = L10n.S(langIdx, L10n.WHITE);
        lWarm1.Text = L10n.S(langIdx, L10n.WARMHUE);
        lWarm2.Text = L10n.S(langIdx, L10n.WARMSAT);
        lLang.Text = L10n.S(langIdx, L10n.LANG);
        chkAll.Text = L10n.S(langIdx, L10n.ALLMON);
        chkAuto.Text = L10n.S(langIdx, L10n.AUTOSTART);
        chkBoot.Text = L10n.S(langIdx, L10n.BOOT);
        btnLog.Text = L10n.S(langIdx, L10n.LOG);
        btnStart.Text = running ? L10n.S(langIdx, L10n.STOP) : L10n.S(langIdx, L10n.START);
        if (!running)
        {
            lblStrip.Text = L10n.S(langIdx, L10n.STRIPWAIT);
            lblAvg.Text = L10n.S(langIdx, L10n.AVG0);
            lblStatus.Text = L10n.S(langIdx, L10n.NOTCONN);
            lblLastSend.Text = "";
        }
    }

    // ----- run-at-Windows-startup shortcut (shell:startup\AmbilightOT.lnk) -----
    static void CreateBootShortcut()
    {
        dynamic wsh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic lnk = wsh.CreateShortcut(BootLnkPath);
        lnk.TargetPath = Application.ExecutablePath;
        lnk.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
        lnk.Description = "Ambilight for IOTBT701 LED strip";
        lnk.Save();
    }

    static void DeleteBootShortcut()
    {
        if (File.Exists(BootLnkPath)) File.Delete(BootLnkPath);
    }

    void PushUItoVolatile()
    {
        intervalV = (int)numInterval.Value;
        satGainV = (double)numSat.Value;
        briGainV = (double)numBri.Value;
        minBriV = (int)numMin.Value;
        whiteThrV = (int)numWhite.Value;
        warmHueV = (int)numWarmHue.Value;
        warmSatV = (int)numWarmSat.Value;
        fadeV = (int)numFade.Value;
        captureAllV = chkAll.Checked;
        SaveSettings();          // autosave: survive hard kills / crashes
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

    // ----- shared capture state (capture thread -> render loop) -----
    sealed class StopFlag { public volatile bool stop; }
    readonly object capLock = new object();
    double capR = 0, capG = 0, capB = 0;
    bool capValid = false;

    static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

    // v1.7: screen capture runs on its own thread, so the BLE render loop below
    // ticks at a steady rate no matter how long a screenshot takes.
    void CaptureLoop(int sx, int sy, int sw, int sh, StopFlag flag)
    {
        Bitmap bigB = null, smallB = null;
        Graphics gB = null, gSm = null;
        try
        {
            bigB = new Bitmap(sw, sh);
            smallB = new Bitmap(96, 54);
            gB = Graphics.FromImage(bigB);
            gSm = Graphics.FromImage(smallB);
            while (running && !flag.stop)
            {
                try
                {
                    gB.CopyFromScreen(sx, sy, 0, 0, new Size(sw, sh));
                    gSm.DrawImage(bigB, new Rectangle(0, 0, smallB.Width, smallB.Height),
                                  new Rectangle(0, 0, sw, sh), GraphicsUnit.Pixel);
                    int r, g, b; double w;
                    Average(smallB, out r, out g, out b, out w);
                    lock (capLock) { capR = r; capG = g; capB = b; capValid = true; }
                }
                catch (Exception ex) { Log("capture: " + ex.Message); Thread.Sleep(1000); }
                Thread.Sleep(30);
            }
        }
        catch (Exception ex) { Log("capture thread: " + ex.Message); }
        finally
        {
            if (gB != null) gB.Dispose();
            if (gSm != null) gSm.Dispose();
            if (bigB != null) bigB.Dispose();
            if (smallB != null) smallB.Dispose();
        }
    }

    // ----- worker loop (render + BLE) -----
    // v1.7 smoothing: time-based (dt) exponential glide in HSV towards the target
    // colour, instead of per-tick steps. White/colour switching is a continuous
    // blend (no hard threshold pop), hue slides along the shortest arc and is
    // held/snapped while saturation is too low for hue to be visible, and every
    // quantised change is sent (no dead-band staircase).
    void WorkLoop()
    {
        StopFlag flag = new StopFlag();
        double curH = 0, curS = 0, curB = 0;
        bool haveCur = false;
        int lqH = -999, lqS = -999, lqB = -999;
        DateTime lastSend = DateTime.MinValue;
        DateTime lastTick = DateTime.Now;
        try
        {
            int sx, sy, sw, sh;
            ScreenBounds(captureAllV, out sx, out sy, out sw, out sh);
            UiSafe(delegate { lblStatus.Text = string.Format(L10n.S(langIdx, L10n.SCREEN), sw, sh); });
            Log("screen " + sw + "x" + sh + " @(" + sx + "," + sy + ")");

            lock (capLock) { capValid = false; }
            Thread cap = new Thread(delegate() { CaptureLoop(sx, sy, sw, sh, flag); });
            cap.IsBackground = true;
            cap.Start();

            while (running)
            {
                if (f01 == null)
                {
                    UiSafe(delegate { lblStatus.Text = L10n.S(langIdx, L10n.CONNECTING); });
                    if (!Connect()) { Thread.Sleep(6000); continue; }
                    if (!Send(new byte[] { 0x3B, 0x23, 0, 0, 0, 0, 0, 0, 0, 0x32, 0, 0 }, "power on"))
                    { Drop(); Thread.Sleep(4000); continue; }
                    hadLink = true;                 // link proven alive
                    lqH = -999; lqS = -999; lqB = -999;
                    haveCur = false;
                    UiSafe(delegate { lblStatus.Text = L10n.S(langIdx, L10n.CONNECTED); });
                    Thread.Sleep(1200);
                    lastTick = DateTime.Now;
                }

                Thread.Sleep(intervalV);
                if (!running) break;
                try
                {
                    double r, g, b; bool ok;
                    lock (capLock) { r = capR; g = capG; b = capB; ok = capValid; }
                    if (!ok) continue;

                    DateTime now = DateTime.Now;
                    double dt = (now - lastTick).TotalSeconds;
                    lastTick = now;
                    if (dt < 0.005) dt = 0.005;
                    if (dt > 1.0) dt = 1.0;

                    int rM = (int)Math.Round(r), gM = (int)Math.Round(g), bM = (int)Math.Round(b);
                    int hue, sat, val;
                    RgbToHsv(rM, gM, bM, out hue, out sat, out val);

                    // ----- target colour (continuous, no thresholds that "pop") -----
                    // cheap RGB LEDs render s40..70% as near-white, so mid sats are
                    // pulled up by a power curve (0.75) before the user gain.
                    double tS = Math.Pow(Math.Min(100.0, sat * satGainV) / 100.0, 0.75) * 100.0;
                    double tB = Math.Max(minBriV, Math.Min(100.0, val * briGainV));
                    double tH = hue;
                    // grey / very dark scene -> warm white; blended over a 10-point
                    // saturation ramp instead of a hard switch
                    double satRamp = Clamp01((tS - whiteThrV) / 10.0);
                    double valRamp = Clamp01((val - 6) / 8.0);
                    double wf = Math.Min(satRamp, valRamp);          // 0 = white, 1 = scene colour
                    if (wf < 1.0)
                    {
                        double warmH = warmHueV;
                        double arc = ((hue - warmH + 540.0) % 360.0) - 180.0;
                        tH = (warmH + arc * wf + 360.0) % 360.0;
                        double warmS = warmSatV;
                        tS = warmS + (tS - warmS) * wf;
                    }

                    // ----- time-based glide towards the target -----
                    if (!haveCur) { curH = tH; curS = tS; curB = tB; haveCur = true; }
                    else
                    {
                        double tau = (fadeV - 1) * 0.25;                    // seconds; 1 = snap
                        double a = tau <= 0.001 ? 1.0 : 1.0 - Math.Exp(-dt / tau);
                        if (curS < 15.0) curH = tH;                         // hue invisible: just jump
                        else if (tS >= 15.0)                                // real colour change: slide shortest arc
                        {
                            double dh = ((tH - curH + 540.0) % 360.0) - 180.0;
                            curH = (curH + dh * a + 360.0) % 360.0;
                        }                                                   // else: fading to white - hold hue
                        curS += (tS - curS) * a;
                        curB += (tB - curB) * a;
                    }

                    int qh = ((int)Math.Round(curH / 2.0) * 2) % 360;       // even hues only (firmware quirk)
                    int qs = (int)Math.Round(curS);
                    if (qs < 0) qs = 0; if (qs > 100) qs = 100;
                    int qb = (int)Math.Round(curB);
                    if (qb < 1) qb = 1; if (qb > 100) qb = 100;
                    if (qs < 1) qh = 0;

                    // preview: the colour the strip actually shows (the glide position)
                    Color c = HsvToColor(qh, qs, qb);
                    bool whiteLike = wf < 0.5;
                    int fh = qh, fs = qs, fb = qb;
                    if (!running) break;
                    UiSafe(delegate
                    {
                        preview.BackColor = c;
                        lblAvg.Text = string.Format(L10n.S(langIdx, L10n.AVG), rM, gM, bM);
                        lblStrip.Text = whiteLike
                            ? string.Format(L10n.S(langIdx, L10n.WHITEF), fb)
                            : string.Format(L10n.S(langIdx, L10n.STRIP), L10n.Hue(langIdx, fh), fs, fb);
                    });

                    // one write per tick (tick = interval knob); send whenever the
                    // quantised colour moved, plus a 25 s keepalive
                    bool changed = qh != lqH || qs != lqS || qb != lqB;
                    bool keepalive = !changed && (now - lastSend).TotalSeconds > 25;
                    if (changed || keepalive)
                    {
                        if (SendV3Color(qh, qs, qb))
                        {
                            lqH = qh; lqS = qs; lqB = qb;
                            lastSend = now;
                            if (changed)
                                UiSafe(delegate { lblLastSend.Text =
                                    string.Format(L10n.S(langIdx, L10n.SENT), DateTime.Now.ToString("HH:mm:ss")); });
                        }
                        else OnLinkLost(changed ? "color write failed" : "keepalive write failed");
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
            flag.stop = true;
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
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + m;
        try
        {
            // persistent copy of the whole session (survives crashes, needed for
            // the "link died on its own" crash window and for bug reports)
            lock (logLock)
            {
                FileInfo fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 512 * 1024)          // rotate at 512 KB
                {
                    string all = File.ReadAllText(LogPath, Encoding.UTF8);
                    File.WriteAllText(LogPath,
                        all.Substring(Math.Min(all.Length / 2, all.Length - 256 * 1024)),
                        Encoding.UTF8);
                }
                File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { }
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

    // called when a previously-alive connection dies on its own
    void OnLinkLost(string why)
    {
        Log("LINK LOST: " + why);
        Drop();
        if (hadLink)                     // true self-disconnect (not the initial login)
        {
            hadLink = false;
            Log("auto-reconnect will try every 6 s; full log opened");
            UiSafe(delegate { ShowLogWindow(true); });
        }
    }

    void ShowLogWindow(bool autoCrash)
    {
        try
        {
            string content = LogPath;
            string tail = "(log file is not yet written)";
            try { tail = File.ReadAllText(LogPath, Encoding.UTF8); } catch { }
            if (tail.Length > 200 * 1024) tail = "... (earlier half cut) " + Environment.NewLine
                                                + tail.Substring(tail.Length - 200 * 1024);
            LogForm f = new LogForm(content, tail, autoCrash);
            f.Show(this);
            f.BringToFront();
        }
        catch { }
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
            sb.AppendLine("warmHue=" + numWarmHue.Value);
            sb.AppendLine("warmSat=" + numWarmSat.Value);
            sb.AppendLine("fade=" + numFade.Value);
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
                    else if (key == "warmhue") numWarmHue.Value = Clamp(numWarmHue, decimal.Parse(v, CultureInfo.InvariantCulture));
                    else if (key == "warmsat") numWarmSat.Value = Clamp(numWarmSat, decimal.Parse(v, CultureInfo.InvariantCulture));
                    else if (key == "fade") numFade.Value = Clamp(numFade, decimal.Parse(v, CultureInfo.InvariantCulture));
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

// full-log viewer; opens automatically on a self-disconnect, or via the
// "connection log" button in Settings
public class LogForm : Form
{
    public LogForm(string logPath, string content, bool autoCrash)
    {
        Text = "AmbilogOT — " + (autoCrash
            ? "LINK LOST (auto-opened)"
            : "connection log");
        Size = new Size(780, 520);
        StartPosition = FormStartPosition.CenterScreen;

        var txt = new TextBox();
        txt.Multiline = true; txt.ReadOnly = true; txt.WordWrap = false;
        txt.ScrollBars = ScrollBars.Both;
        txt.Font = new Font("Consolas", 9);
        txt.Dock = DockStyle.Fill;
        txt.Text = content;
        txt.SelectionStart = txt.Text.Length;   // scroll to the latest entries
        txt.ScrollToCaret();

        var bar = new Panel();
        bar.Dock = DockStyle.Bottom; bar.Height = 38;
        var btnCopy = new Button();
        btnCopy.Text = "Copy log";
        btnCopy.Dock = DockStyle.Left; btnCopy.Width = 120;
        btnCopy.Click += delegate
        {
            try { Clipboard.SetText(txt.Text); btnCopy.Text = "Copied!"; }
            catch { }
        };
        var btnOpen = new Button();
        btnOpen.Text = "Show file";
        btnOpen.Dock = DockStyle.Right; btnOpen.Width = 120;
        btnOpen.Click += delegate
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe",
                    "/select,\"" + logPath + "\"");
            }
            catch { }
        };
        bar.Controls.Add(btnOpen);
        bar.Controls.Add(btnCopy);

        Controls.Add(txt);
        Controls.Add(bar);
    }
}

// ================= custom dark-theme controls (v1.8 UI) =================
static class Gfx
{
    public static GraphicsPath Round(Rectangle r, int rad)
    {
        int d = rad * 2;
        GraphicsPath p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

// rounded panel that holds other controls
public class Card : Panel
{
    public Card()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(33, 36, 46);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : BackColor);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (GraphicsPath p = Gfx.Round(new Rectangle(0, 0, Width - 1, Height - 1), 12))
        using (SolidBrush b = new SolidBrush(BackColor))
            e.Graphics.FillPath(b, p);
    }
}

// big rounded colour preview; WorkLoop just sets BackColor
public class Swatch : Panel
{
    public Swatch()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent != null ? Parent.BackColor : BackColor);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (GraphicsPath p = Gfx.Round(new Rectangle(0, 0, Width - 1, Height - 1), 16))
        {
            using (SolidBrush b = new SolidBrush(BackColor)) e.Graphics.FillPath(b, p);
            using (Pen pen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f)) e.Graphics.DrawPath(pen, p);
        }
    }
}

// flat rounded button; fill colour comes from a callback so it follows app state
public class PillButton : Button
{
    public Func<Color> FillProvider;
    bool hover = false;
    public PillButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : BackColor);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        Color fill = FillProvider != null ? FillProvider() : Color.Gray;
        if (hover) fill = ControlPaint.Light(fill, 0.25f);
        using (GraphicsPath p = Gfx.Round(new Rectangle(0, 0, Width - 1, Height - 1), Math.Min(14, Height / 2)))
        using (SolidBrush b = new SolidBrush(fill))
            g.FillPath(b, p);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

// minimal flat slider (drag with the mouse or use the arrow keys)
public class FlatSlider : Control
{
    int min = 0, max = 10, val = 0;
    bool drag = false;
    public Color Accent = Color.FromArgb(88, 166, 255);
    public Color TrackColor = Color.FromArgb(62, 67, 82);
    public event EventHandler ValueChanged;

    public FlatSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
    }
    public int Minimum { get { return min; } set { min = value; if (val < min) Value = min; Invalidate(); } }
    public int Maximum { get { return max; } set { max = value; if (val > max) Value = max; Invalidate(); } }
    public int Value
    {
        get { return val; }
        set
        {
            int v = value < min ? min : (value > max ? max : value);
            if (v == val) return;
            val = v;
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
    }
    const int Pad = 11;
    float ThumbX()
    {
        float f = (max == min) ? 0f : (float)(val - min) / (max - min);
        return Pad + f * (Width - 2 * Pad);
    }
    void SetFromX(int x)
    {
        float f = (float)(x - Pad) / Math.Max(1, Width - 2 * Pad);
        if (f < 0) f = 0; if (f > 1) f = 1;
        Value = min + (int)Math.Round(f * (max - min));
    }
    protected override bool IsInputKey(Keys k)
    {
        return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value = val - 1;
        else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value = val + 1;
        base.OnKeyDown(e);
    }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); drag = true; SetFromX(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFromX(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int cy = Height / 2;
        float tx = ThumbX();
        using (Pen pt = new Pen(TrackColor, 6f)) { pt.StartCap = LineCap.Round; pt.EndCap = LineCap.Round;
            g.DrawLine(pt, Pad, cy, Width - Pad, cy); }
        using (Pen pa = new Pen(Accent, 6f)) { pa.StartCap = LineCap.Round; pa.EndCap = LineCap.Round;
            g.DrawLine(pa, Pad, cy, tx, cy); }
        float r = Focused ? 9.5f : 8.5f;
        using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, tx - r, cy - r, 2 * r, 2 * r);
        using (Pen ring = new Pen(Accent, 2f)) g.DrawEllipse(ring, tx - r, cy - r, 2 * r, 2 * r);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}
