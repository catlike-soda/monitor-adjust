using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

class Row
{
    public int Display;
    public string Code;
    public TrackBar Bar;
    public Label Value;
    public Label NameLabel;
    public int Original = -1;
    public int Max = 100;          // 滑块量程上限：不同显示器进制可能不同（0-100 / 0-255）
}

class SrcRow
{
    public int Display;
    public ComboBox Combo;
    public Button SwitchButton;
    public Label Value;
    public Label NameLabel;
    public List<int> Options = new List<int>();
    public int Current = -1;
    public bool Guessed = false;
}

// 界面文案：中英文两套。Set() 一次性全量赋值，切换时再刷新已存在的控件即可。
static class L
{
    public static bool En = false;

    public static string Title, Monitor, Brightness, Contrast, InputSource;
    public static string Apply, Restore, Reload, Close, Switch;
    public static string RangeLabel, RangeAuto, LanguageLabel;
    public static string FmtNowWas, FmtNow, ReadFailed, Hint;
    public static string NoCliTitle, NoCli, NoMonitor, SwitchTitle, FmtAlreadyThere, FmtConfirmSwitch;
    public static string FmtApplied, FmtRestored, FmtRefreshed, FakeNote, FakeNoteShort, FmtLoading;
    public static string SourceComponent, SourceInputPrefix;

    public static void Set(bool en)
    {
        En = en;
        if (en)
        {
            Title = "Monitor Adjust - Brightness / Contrast / Input";
            Monitor = "Monitor";
            Brightness = "Brightness";
            Contrast = "Contrast";
            InputSource = "Input";
            Apply = "Apply";
            Restore = "Restore";
            Reload = "Refresh";
            Close = "Close";
            Switch = "Switch";
            RangeLabel = "Range:";
            RangeAuto = "Auto";
            LanguageLabel = "Language:";
            FmtNowWas = "Now {0} (was {1})";
            FmtNow = "Now {0}";
            ReadFailed = "n/a";
            Hint = "\"was\" = value when this window opened; use Switch to change input source";
            NoCliTitle = "Monitor Adjust";
            NoCli = "winddcutil.exe not found.\n\nPut it in the same folder as this program, then run again.";
            NoMonitor = "No DDC/CI capable monitor was detected.";
            SwitchTitle = "Switch input source";
            FmtAlreadyThere = "Monitor {0} is already on {1}.";
            FmtConfirmSwitch = "Switch monitor {0} to {1} (0x{2})?\n\n" +
                "Warning: if nothing is connected to that port, the display will go black or show \"no signal\", " +
                "and this program may not be able to switch it back - you would have to use the monitor's own buttons.";
            FmtApplied = "Sent brightness/contrast for {0} item(s) to the monitors ({1})";
            FmtRestored = "Restored the values from when the window opened ({0} item(s))";
            FmtRefreshed = "Refreshed (including input source); the current values are now the \"was\" values ({0})";
            FakeNote = "  [fake data mode - nothing was actually sent]";
            FakeNoteShort = "  [fake data mode]";
            FmtLoading = "Detecting monitors, please wait... ({0} found)";
            SourceComponent = "Component-";
            SourceInputPrefix = "Input 0x";
        }
        else
        {
            Title = "显示器调节 - 亮度 / 对比度 / 信号源";
            Monitor = "显示器";
            Brightness = "亮度";
            Contrast = "对比度";
            InputSource = "信号源";
            Apply = "应用";
            Restore = "恢复原值";
            Reload = "重新读取";
            Close = "关闭";
            Switch = "切换";
            RangeLabel = "亮度量程：";
            RangeAuto = "自动检测";
            LanguageLabel = "语言：";
            FmtNowWas = "当前 {0}（原值 {1}）";
            FmtNow = "当前 {0}";
            ReadFailed = "读取失败";
            Hint = "原值 = 打开窗口时的数值；信号源用「切换」按钮单独切换";
            NoCliTitle = "显示器调节";
            NoCli = "找不到 winddcutil.exe。\n\n请把它和本程序放在同一个文件夹里再运行。";
            NoMonitor = "没有检测到支持 DDC/CI 的显示器。";
            SwitchTitle = "切换信号源";
            FmtAlreadyThere = "「显示器 {0}」现在就已经是 {1} 了。";
            FmtConfirmSwitch = "把「显示器 {0}」切换到 {1}（0x{2}）？\n\n" +
                "注意：如果那个接口上没接设备，显示器会黑屏或显示「无信号」，\n" +
                "而且软件可能无法再切回来，需要用显示器自己的按键切回去。";
            FmtApplied = "已把 {0} 项亮度/对比度设置发送给显示器（{1}）";
            FmtRestored = "已恢复到打开窗口时的数值（{0} 项）";
            FmtRefreshed = "已重新读取（含信号源），并把当前数值记为新的原值（{0}）";
            FakeNote = "  [假数据模式，并没有真的发送]";
            FakeNoteShort = "  [假数据模式]";
            FmtLoading = "正在识别显示器，请稍候…（共 {0} 台）";
            SourceComponent = "分量-";
            SourceInputPrefix = "输入源 0x";
        }
    }
}

static class Program
{
    static string CliPath = null;
    static string CliTempDir = null;

    // 假数据模式（命令行加 --fake N）：完全不调用 winddcutil，用编造的数据画界面。
    // 用途：没有显示器 / 没有 CLI / 受限沙箱里验证排版，以及给别人演示界面。
    static bool FakeMode = false;
    static int FakeMonitors = 2;

    // 只在测试排版时用（--screen 1366x768）：假装屏幕只有这么大，
    // 用来验证小屏幕 + 多显示器时会不会溢出、滚动条和按钮是否正常。
    static int OverrideScreenW = 0;
    static int OverrideScreenH = 0;

    static string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gui-log.txt");
    static bool logChecked = false;

    static void Log(string s)
    {
        try
        {
            if (!logChecked)
            {
                logChecked = true;
                FileInfo fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 100000) fi.Delete();
            }
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s + "\r\n");
        }
        catch { }
    }

    static string Run(string arguments)
    {
        if (FakeMode) return "";     // 假数据模式：绝不碰真实显示器
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo(CliPath, arguments);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            try
            {
                foreach (System.Collections.DictionaryEntry de in Environment.GetEnvironmentVariables())
                    psi.EnvironmentVariables[(string)de.Key] = (string)de.Value;
                psi.EnvironmentVariables["TEMP"] = CliTempDir;
                psi.EnvironmentVariables["TMP"] = CliTempDir;
            }
            catch { }
            using (Process p = Process.Start(psi))
            {
                string outp = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit(10000);
                Log("run: " + arguments + "  -> " + outp.Trim() + " " + err.Trim());
                return outp + "\n" + err;
            }
        }
        catch (Exception ex)
        {
            Log("run FAILED: " + arguments + " : " + ex.Message);
            return "";
        }
    }

    static int GetDisplayCount()
    {
        if (FakeMode) return FakeMonitors;
        int n = 0;
        foreach (string line in Run("detect").Split('\n'))
        {
            Match m = Regex.Match(line.Trim(), @"^(\d+)\s");
            if (m.Success)
            {
                int v = int.Parse(m.Groups[1].Value);
                if (v > n) n = v;
            }
        }
        return n;
    }

    static int? GetVcp(int display, string code)
    {
        if (FakeMode)
        {
            // 偶数号故意给一个 >100 的值，用来验证「自动量程」能识别 0-255
            if (code == "0x10") return (display % 2 == 0) ? 130 : 45;
            if (code == "0x12") return 60;
            if (code == "0x60") return (display % 2 == 1) ? 0x12 : 0x0F;
            return null;
        }
        Match m = Regex.Match(Run("getvcp " + display + " " + code), @"VCP\s+0x[0-9a-fA-F]+\s+(\d+)");
        if (m.Success) return int.Parse(m.Groups[1].Value);
        return null;
    }

    static void SetVcp(int display, string code, int value)
    {
        if (FakeMode) { Log("FAKE setvcp " + display + " " + code + " " + value); return; }
        Run("setvcp " + display + " " + code + " " + value.ToString());
    }

    // 接口名两套语言基本都写英文缩写，只有「分量」和兜底名需要跟着切
    static string SourceName(int v)
    {
        switch (v)
        {
            case 0x01: return "VGA-1";
            case 0x02: return "VGA-2";
            case 0x03: return "DVI-1";
            case 0x04: return "DVI-2";
            case 0x05: return "AV-1";
            case 0x06: return "AV-2";
            case 0x07: return "S-Video-1";
            case 0x08: return "S-Video-2";
            case 0x09: return "TV-1";
            case 0x0A: return "TV-2";
            case 0x0B: return "TV-3";
            case 0x0C: return L.SourceComponent + "1";
            case 0x0D: return L.SourceComponent + "2";
            case 0x0E: return L.SourceComponent + "3";
            case 0x0F: return "DP-1";
            case 0x10: return "DP-2";
            case 0x11: return "HDMI-1";
            case 0x12: return "HDMI-2";
            case 0x13: return "HDMI-3";
            case 0x14: return "HDMI-4";
            case 0x15: return "MDDI";
            case 0x16: return "DP-3";
            case 0x17: return "DP-4";
            default: return L.SourceInputPrefix + v.ToString("X2");
        }
    }

    static List<int> ParseSources(string cap)
    {
        List<int> list = new List<int>();
        Match m = Regex.Match(cap, @"(?<![0-9A-Fa-f])60\(([^)]*)\)");
        if (!m.Success) return list;
        string inner = m.Groups[1].Value.Replace(" ", "").Replace("\t", "").Trim();
        for (int i = 0; i + 1 < inner.Length; i += 2)
        {
            int v;
            if (int.TryParse(inner.Substring(i, 2), System.Globalization.NumberStyles.HexNumber, null, out v))
                if (!list.Contains(v)) list.Add(v);
        }
        return list;
    }

    static List<int> GetSources(int display, out bool guessed)
    {
        guessed = false;
        List<int> list = ParseSources(Run("capabilities " + display));
        if (list.Count == 0)
        {
            guessed = true;
            list = new List<int>(new int[] { 0x11, 0x12, 0x13, 0x14, 0x0F, 0x10, 0x03, 0x04, 0x01, 0x02 });
        }
        return list;
    }

    static void UpdateLabel(Row r)
    {
        string o = (r.Original < 0) ? L.ReadFailed : r.Original.ToString();
        r.Value.Text = string.Format(L.FmtNowWas, r.Bar.Value, o);
    }

    static void UpdateSrcLabel(SrcRow s)
    {
        if (s.Current < 0) s.Value.Text = string.Format(L.FmtNow, L.ReadFailed);
        else s.Value.Text = string.Format(L.FmtNow, SourceName(s.Current));
    }

    static void FillCombo(SrcRow s)
    {
        s.Combo.Items.Clear();
        foreach (int v in s.Options)
            s.Combo.Items.Add(SourceName(v) + " (0x" + v.ToString("X2") + ")");
        int idx = s.Options.IndexOf(s.Current);
        if (idx >= 0) s.Combo.SelectedIndex = idx;
        else if (s.Combo.Items.Count > 0) s.Combo.SelectedIndex = 0;
    }

    // winddcutil 只装在 exe 自己旁边（分发包就是这样），其次是子目录 / 当前目录 / PATH。
    // 不写死任何个人机器上的路径。
    static string FindCli()
    {
        List<string> cands = new List<string>();
        try { cands.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "winddcutil.exe")); }
        catch { }
        try { cands.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "winddcutil", "winddcutil.exe")); }
        catch { }
        try { cands.Add(Path.Combine(Directory.GetCurrentDirectory(), "winddcutil.exe")); }
        catch { }

        foreach (string c in cands)
            if (File.Exists(c)) return c;

        try
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (path != null)
            {
                foreach (string p in path.Split(';'))
                {
                    string t = p.Trim().Trim('"');
                    if (t.Length == 0) continue;
                    try
                    {
                        string c = Path.Combine(t, "winddcutil.exe");
                        if (File.Exists(c)) return c;
                    }
                    catch { }
                }
            }
        }
        catch { }
        return null;
    }

    // ---- 语言选择的持久化：先试 exe 同目录（便携），不行再退到 %LOCALAPPDATA% ----
    static string[] SettingsFiles()
    {
        string local = "";
        try
        {
            local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "monitor-adjust");
        }
        catch { }
        return new string[] {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monitor-adjust.ini"),
            Path.Combine(local, "settings.ini")
        };
    }

    static bool? LoadLangSetting()
    {
        foreach (string f in SettingsFiles())
        {
            try
            {
                if (!File.Exists(f)) continue;
                foreach (string line in File.ReadAllLines(f))
                {
                    string s = line.Trim();
                    if (s.StartsWith("lang=", StringComparison.OrdinalIgnoreCase))
                    {
                        string v = s.Substring(5).Trim().ToLowerInvariant();
                        if (v.StartsWith("en")) return true;
                        if (v.StartsWith("zh")) return false;
                    }
                }
            }
            catch { }
        }
        return null;
    }

    static void SaveLangSetting(bool en)
    {
        foreach (string f in SettingsFiles())
        {
            try
            {
                string d = Path.GetDirectoryName(f);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                File.WriteAllText(f, "lang=" + (en ? "en" : "zh") + "\r\n", Encoding.ASCII);
                return;
            }
            catch { }
        }
    }

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 先把 --fake N / --screen WxH / --lang xx 摘出来，剩下的参数按原来的规则解释
        List<string> rest = new List<string>();
        string langArg = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--fake" && i + 1 < args.Length)
            {
                int n;
                if (int.TryParse(args[i + 1], out n) && n >= 1 && n <= 16)
                {
                    FakeMode = true;
                    FakeMonitors = n;
                }
                i++;
                continue;
            }
            if (args[i] == "--screen" && i + 1 < args.Length)
            {
                Match sm = Regex.Match(args[i + 1], @"^(\d+)x(\d+)$");
                if (sm.Success)
                {
                    OverrideScreenW = int.Parse(sm.Groups[1].Value);
                    OverrideScreenH = int.Parse(sm.Groups[2].Value);
                }
                i++;
                continue;
            }
            if (args[i] == "--lang" && i + 1 < args.Length)
            {
                langArg = args[i + 1].Trim().ToLowerInvariant();
                i++;
                continue;
            }
            rest.Add(args[i]);
        }
        string[] a = rest.ToArray();

        bool dumpMode = (a.Length >= 2 && a[0] == "--dump");
        bool renderMode = (a.Length >= 2 && a[0] == "--render");
        bool interactive = !dumpMode && !renderMode;

        // 语言优先级：命令行 > 存档 > 系统语言
        bool startEn;
        if (langArg != null) startEn = langArg.StartsWith("en");
        else
        {
            bool? saved = LoadLangSetting();
            if (saved.HasValue) startEn = saved.Value;
            else
            {
                try { startEn = !System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase); }
                catch { startEn = false; }
            }
        }
        L.Set(startEn);

        Log("=== Main args=" + string.Join("|", args) +
            (FakeMode ? "   [FAKE " + FakeMonitors + "]" : "") + "   [lang=" + (L.En ? "en" : "zh") + "]");

        if (FakeMode)
        {
            CliPath = "(fake data mode, winddcutil not called)";
            CliTempDir = Path.GetTempPath();
        }
        else
        {
            CliPath = FindCli();
            if (CliPath == null)
            {
                Log("CliPath NOT FOUND");
                if (dumpMode) { File.WriteAllText(a[1], "ERROR: winddcutil.exe not found.\r\nPut it in the same folder as this program.\r\n", Encoding.UTF8); return; }
                if (renderMode) return;
                MessageBox.Show(L.NoCli, L.NoCliTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Log("CliPath=" + CliPath);

            string[] tempCands = new string[] {
                Path.GetDirectoryName(CliPath),
                AppDomain.CurrentDomain.BaseDirectory,
                Path.GetTempPath(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winddcutil-gui")
            };
            CliTempDir = null;
            foreach (string tc in tempCands)
            {
                if (string.IsNullOrEmpty(tc)) continue;
                try
                {
                    if (!Directory.Exists(tc)) Directory.CreateDirectory(tc);
                    string probe = Path.Combine(tc, ".wtest");
                    File.WriteAllText(probe, "x");
                    File.Delete(probe);
                    CliTempDir = tc;
                    break;
                }
                catch { }
            }
            if (CliTempDir == null) CliTempDir = Path.GetTempPath();
        }
        Log("CliTempDir=" + CliTempDir);

        int monitors = GetDisplayCount();
        Log("monitors=" + monitors);
        if (monitors < 1)
        {
            string msg = L.NoMonitor + "\r\nCLI = " + CliPath +
                         "\r\n(If this runs inside a restricted sandbox, winddcutil may fail to start.)\r\n";
            Log("no monitors");
            if (dumpMode) { File.WriteAllText(a[1], msg, Encoding.UTF8); return; }
            if (renderMode) return;
            MessageBox.Show(msg, L.NoCliTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Form form = new Form();
        form.Text = L.Title;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.AutoScaleMode = AutoScaleMode.None;
        form.Font = new Font("Microsoft YaHei UI", 9f);

        // ---------- 自适应网格：按显示器数量和屏幕大小决定几列几行 ----------
        const int grpW = 476, grpH = 132, rowH = 140, topPad = 12, sidePad = 12, gapX = 12;
        const int colW = grpW + gapX;      // 488
        const int bottomH = 96;

        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        if (OverrideScreenW > 0 && OverrideScreenH > 0)
            wa = new Rectangle(0, 0, OverrideScreenW, OverrideScreenH);
        int maxW = Math.Max(360, wa.Width - 40);
        int maxH = Math.Max(260, wa.Height - 60);

        int rowsFit = Math.Max(1, (maxH - bottomH - topPad - 8) / rowH);
        int colsFit = Math.Max(1, (maxW - sidePad * 2) / colW);
        int cols = (int)Math.Ceiling(monitors / (double)rowsFit);
        if (cols > colsFit) cols = colsFit;
        if (cols < 1) cols = 1;
        int rows = (int)Math.Ceiling(monitors / (double)cols);

        int contentW = sidePad + cols * colW;
        int scrollContentH = topPad + rows * rowH + 8;
        int clientW = Math.Min(contentW, maxW);
        int clientH = Math.Min(scrollContentH + bottomH, maxH);

        // 先显示一个"正在识别"的窗口，边读边填 —— 显示器多的时候不至于白屏假死
        Label loading = null;
        if (interactive)
        {
            loading = new Label();
            loading.Text = string.Format(L.FmtLoading, monitors);
            loading.Location = new Point(16, 40);
            loading.Size = new Size(460, 24);
            form.Controls.Add(loading);
            form.ClientSize = new Size(500, 150);
            form.Show();
            Application.DoEvents();
        }

        Panel pnlScroll = new Panel();
        pnlScroll.AutoScroll = true;
        pnlScroll.Bounds = new Rectangle(0, 0, clientW, clientH - bottomH);
        form.Controls.Add(pnlScroll);

        Panel pnlBottom = new Panel();
        pnlBottom.Bounds = new Rectangle(0, clientH - bottomH, clientW, bottomH);
        form.Controls.Add(pnlBottom);

        List<Row> rowsList = new List<Row>();
        List<SrcRow> srcs = new List<SrcRow>();
        List<GroupBox> groups = new List<GroupBox>();

        for (int d = 1; d <= monitors; d++)
        {
            int c = (d - 1) / rows;          // 先竖着排，排满一列再排下一列
            int r = (d - 1) % rows;

            GroupBox grp = new GroupBox();
            grp.Text = L.Monitor + " " + d;
            grp.Location = new Point(sidePad + c * colW, topPad + r * rowH);
            grp.Size = new Size(grpW, grpH);
            pnlScroll.Controls.Add(grp);
            groups.Add(grp);

            string[] names = { L.Brightness, L.Contrast };
            string[] codes = { "0x10", "0x12" };
            int[] ys = { 22, 58 };

            for (int i = 0; i < 2; i++)
            {
                Label ln = new Label();
                ln.Text = names[i];
                ln.Location = new Point(12, ys[i] + 6);
                ln.Size = new Size(78, 20);       // 78 是为了放得下英文 "Brightness" / "Contrast"
                grp.Controls.Add(ln);

                int? o = GetVcp(d, codes[i]);

                // getvcp 不返回最大值，只能从读到的值猜：>100 基本就是 0-255 进制
                int initMax = (o.HasValue && o.Value > 100) ? 255 : 100;

                TrackBar bar = new TrackBar();
                bar.Minimum = 0;
                bar.Maximum = initMax;
                bar.TickFrequency = (initMax == 255) ? 25 : 10;
                bar.SmallChange = 1;
                bar.LargeChange = 5;
                bar.Location = new Point(92, ys[i]);
                bar.Size = new Size(228, 36);
                if (o.HasValue) bar.Value = Math.Max(0, Math.Min(initMax, o.Value));
                grp.Controls.Add(bar);

                Label lv = new Label();
                lv.Location = new Point(324, ys[i] + 6);
                lv.Size = new Size(146, 20);
                grp.Controls.Add(lv);

                Row rw = new Row();
                rw.Display = d;
                rw.Code = codes[i];
                rw.Bar = bar;
                rw.Value = lv;
                rw.NameLabel = ln;
                rw.Original = o.HasValue ? o.Value : -1;
                rw.Max = initMax;
                rowsList.Add(rw);

                UpdateLabel(rw);
                bar.Tag = rw;
                bar.Scroll += delegate(object s, EventArgs e)
                {
                    Row rr = (Row)((TrackBar)s).Tag;
                    UpdateLabel(rr);
                };
            }

            Label ls = new Label();
            ls.Text = L.InputSource;
            ls.Location = new Point(12, 104);
            ls.Size = new Size(78, 20);
            grp.Controls.Add(ls);

            SrcRow sr = new SrcRow();
            sr.Display = d;
            sr.NameLabel = ls;

            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Location = new Point(92, 100);
            combo.Size = new Size(184, 24);
            grp.Controls.Add(combo);
            sr.Combo = combo;

            Button btnSwitch = new Button();
            btnSwitch.Text = L.Switch;
            btnSwitch.Location = new Point(282, 99);
            btnSwitch.Size = new Size(70, 26);
            grp.Controls.Add(btnSwitch);
            sr.SwitchButton = btnSwitch;

            Label lsrc = new Label();
            lsrc.Location = new Point(358, 104);
            lsrc.Size = new Size(112, 20);
            grp.Controls.Add(lsrc);
            sr.Value = lsrc;

            int? cur = GetVcp(d, "0x60");
            bool guessed;
            sr.Options = GetSources(d, out guessed);
            sr.Guessed = guessed;
            sr.Current = cur.HasValue ? cur.Value : -1;
            if (sr.Current >= 0 && !sr.Options.Contains(sr.Current)) sr.Options.Insert(0, sr.Current);
            FillCombo(sr);
            UpdateSrcLabel(sr);
            srcs.Add(sr);

            btnSwitch.Tag = sr;
            btnSwitch.Click += delegate(object s, EventArgs e)
            {
                SrcRow sr2 = (SrcRow)((Button)s).Tag;
                if (sr2.Combo.SelectedIndex < 0) return;
                int target = sr2.Options[sr2.Combo.SelectedIndex];
                if (target == sr2.Current)
                {
                    MessageBox.Show(string.Format(L.FmtAlreadyThere, sr2.Display, SourceName(target)),
                        L.SwitchTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult dr = MessageBox.Show(
                    string.Format(L.FmtConfirmSwitch, sr2.Display, SourceName(target), target.ToString("X2")),
                    L.SwitchTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (dr != DialogResult.OK) return;
                SetVcp(sr2.Display, "0x60", target);
                sr2.Current = target;
                UpdateSrcLabel(sr2);
            };

            if (interactive) Application.DoEvents();     // 让每台显示器一读完就现出来
        }

        // ---------- 底部：按钮常驻（不随显示器数量被顶出屏幕） ----------
        int btnW = 100, btnH = 32, gapB = 10;
        int totalBtnW = btnW * 4 + gapB * 3;
        int bx = Math.Max(sidePad, (clientW - totalBtnW) / 2);

        Button bApply = new Button();
        bApply.Text = L.Apply;
        bApply.Location = new Point(bx, 8);
        bApply.Size = new Size(btnW, btnH);
        pnlBottom.Controls.Add(bApply);

        Button bRestore = new Button();
        bRestore.Text = L.Restore;
        bRestore.Location = new Point(bx + (btnW + gapB), 8);
        bRestore.Size = new Size(btnW, btnH);
        pnlBottom.Controls.Add(bRestore);

        Button bReload = new Button();
        bReload.Text = L.Reload;
        bReload.Location = new Point(bx + (btnW + gapB) * 2, 8);
        bReload.Size = new Size(btnW, btnH);
        pnlBottom.Controls.Add(bReload);

        Button bClose = new Button();
        bClose.Text = L.Close;
        bClose.Location = new Point(bx + (btnW + gapB) * 3, 8);
        bClose.Size = new Size(btnW, btnH);
        pnlBottom.Controls.Add(bClose);

        Label lRange = new Label();
        lRange.Text = L.RangeLabel;
        lRange.Location = new Point(12, 50);
        lRange.Size = new Size(76, 20);
        pnlBottom.Controls.Add(lRange);

        ComboBox cmbRange = new ComboBox();
        cmbRange.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbRange.Location = new Point(90, 46);
        cmbRange.Size = new Size(86, 24);
        cmbRange.Items.Add(L.RangeAuto);
        cmbRange.Items.Add("0-100");
        cmbRange.Items.Add("0-255");
        cmbRange.SelectedIndex = 0;
        pnlBottom.Controls.Add(cmbRange);

        Label lLang = new Label();
        lLang.Text = L.LanguageLabel;
        lLang.Location = new Point(196, 50);
        lLang.Size = new Size(72, 20);
        pnlBottom.Controls.Add(lLang);

        ComboBox cmbLang = new ComboBox();
        cmbLang.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbLang.Location = new Point(272, 46);
        cmbLang.Size = new Size(100, 24);
        cmbLang.Items.Add("中文");
        cmbLang.Items.Add("English");
        cmbLang.SelectedIndex = L.En ? 1 : 0;
        pnlBottom.Controls.Add(cmbLang);

        Label status = new Label();
        status.Location = new Point(12, 74);
        status.Size = new Size(clientW - 24, 18);
        status.ForeColor = Color.DimGray;
        status.Text = L.Hint;
        pnlBottom.Controls.Add(status);

        if (loading != null) form.Controls.Remove(loading);
        form.ClientSize = new Size(clientW, clientH);
        pnlScroll.Bounds = new Rectangle(0, 0, clientW, clientH - bottomH);
        pnlBottom.Bounds = new Rectangle(0, clientH - bottomH, clientW, bottomH);

        bool applying = false;   // 语言刷新期间抑制下拉框事件，避免递归

        // 量程：0=自动（按读到的原值判断），100 / 255 = 强制
        Action ApplyRanges = delegate
        {
            int ov = 0;
            if (cmbRange.SelectedIndex == 1) ov = 100;
            else if (cmbRange.SelectedIndex == 2) ov = 255;
            foreach (Row rw in rowsList)
            {
                int m = (ov != 0) ? ov : ((rw.Original > 100) ? 255 : 100);
                rw.Max = m;
                rw.Bar.Maximum = m;
                if (rw.Bar.Value > m) rw.Bar.Value = m;
                UpdateLabel(rw);
            }
        };

        // 用当前语言刷新所有已存在的控件文字
        Action ApplyLanguage = delegate
        {
            applying = true;
            form.Text = L.Title;
            for (int i = 0; i < groups.Count; i++)
                groups[i].Text = L.Monitor + " " + (i + 1);
            foreach (Row rw in rowsList)
            {
                rw.NameLabel.Text = (rw.Code == "0x10") ? L.Brightness : L.Contrast;
                UpdateLabel(rw);
            }
            foreach (SrcRow s in srcs)
            {
                s.NameLabel.Text = L.InputSource;
                s.SwitchButton.Text = L.Switch;
                FillCombo(s);
                UpdateSrcLabel(s);
            }
            bApply.Text = L.Apply;
            bRestore.Text = L.Restore;
            bReload.Text = L.Reload;
            bClose.Text = L.Close;
            lRange.Text = L.RangeLabel;
            lLang.Text = L.LanguageLabel;
            int ri = cmbRange.SelectedIndex;
            cmbRange.Items.Clear();
            cmbRange.Items.Add(L.RangeAuto);
            cmbRange.Items.Add("0-100");
            cmbRange.Items.Add("0-255");
            cmbRange.SelectedIndex = (ri < 0) ? 0 : ri;
            cmbLang.SelectedIndex = L.En ? 1 : 0;
            status.Text = L.Hint;
            applying = false;
        };

        cmbRange.SelectedIndexChanged += delegate { if (!applying) ApplyRanges(); };
        cmbLang.SelectedIndexChanged += delegate
        {
            if (applying) return;
            L.Set(cmbLang.SelectedIndex == 1);
            SaveLangSetting(L.En);
            ApplyLanguage();
            Log("language -> " + (L.En ? "en" : "zh"));
        };

        ApplyLanguage();

        bApply.Click += delegate
        {
            foreach (Row rw in rowsList) SetVcp(rw.Display, rw.Code, rw.Bar.Value);
            status.Text = string.Format(L.FmtApplied, rowsList.Count, DateTime.Now.ToString("HH:mm:ss"))
                + (FakeMode ? L.FakeNote : "");
        };

        bRestore.Click += delegate
        {
            int n = 0;
            foreach (Row rw in rowsList)
            {
                if (rw.Original >= 0) rw.Bar.Value = Math.Max(0, Math.Min(rw.Max, rw.Original));
                SetVcp(rw.Display, rw.Code, rw.Bar.Value);
                UpdateLabel(rw);
                n++;
            }
            status.Text = string.Format(L.FmtRestored, n) + (FakeMode ? L.FakeNoteShort : "");
        };

        bReload.Click += delegate
        {
            foreach (Row rw in rowsList)
            {
                int? v = GetVcp(rw.Display, rw.Code);
                rw.Original = v.HasValue ? v.Value : -1;
                UpdateLabel(rw);
            }
            ApplyRanges();
            foreach (SrcRow s in srcs)
            {
                int? c2 = GetVcp(s.Display, "0x60");
                s.Current = c2.HasValue ? c2.Value : -1;
                bool g2;
                s.Options = GetSources(s.Display, out g2);
                s.Guessed = g2;
                if (s.Current >= 0 && !s.Options.Contains(s.Current)) s.Options.Insert(0, s.Current);
                FillCombo(s);
                UpdateSrcLabel(s);
            }
            status.Text = string.Format(L.FmtRefreshed, DateTime.Now.ToString("HH:mm:ss"));
        };

        bClose.Click += delegate { form.Close(); };

        // ---- dump / render 的输出固定英文，方便脚本解析，不受界面语言影响 ----
        if (dumpMode)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CLI = " + CliPath + (FakeMode ? "   [fake data mode]" : ""));
            sb.AppendLine("language = " + (L.En ? "en" : "zh"));
            sb.AppendLine("monitors = " + monitors);
            sb.AppendLine("layout = " + cols + " col x " + rows + " row");
            foreach (Row rw in rowsList)
                sb.AppendLine("  monitor " + rw.Display + " " + rw.Code + " original=" + rw.Original +
                              " slider=" + rw.Bar.Value + " range=0-" + rw.Max);
            foreach (SrcRow s in srcs)
            {
                sb.Append("  monitor " + s.Display + " source current=" + s.Current + " (" + SourceName(s.Current) + ")");
                sb.Append(" list=" + (s.Guessed ? "fallback" : "reported") + " options=");
                foreach (int v in s.Options) sb.Append("0x" + v.ToString("X2") + " ");
                sb.AppendLine();
            }
            sb.AppendLine("window = " + form.ClientSize.Width + " x " + form.ClientSize.Height);
            sb.AppendLine("screen = " + wa.Width + " x " + wa.Height);
            sb.AppendLine("topLevelControls = " + form.Controls.Count);
            File.WriteAllText(a[1], sb.ToString(), Encoding.UTF8);
            Log("dump -> " + a[1]);
            return;
        }

        if (renderMode)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-4000, -4000);
            form.Show();
            for (int i = 0; i < 8; i++)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(120);
            }
            Bitmap bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(a[1], System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
            form.Close();
            return;
        }

        Application.Run(form);
    }
}
