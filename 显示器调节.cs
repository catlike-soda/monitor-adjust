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
    public int Original = -1;
    public int Max = 100;          // 滑块量程上限：不同显示器进制可能不同（0-100 / 0-255）
}

class SrcRow
{
    public int Display;
    public ComboBox Combo;
    public Label Value;
    public List<int> Options = new List<int>();
    public int Current = -1;
    public bool Guessed = false;
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
            case 0x0C: return "分量-1";
            case 0x0D: return "分量-2";
            case 0x0E: return "分量-3";
            case 0x0F: return "DP-1";
            case 0x10: return "DP-2";
            case 0x11: return "HDMI-1";
            case 0x12: return "HDMI-2";
            case 0x13: return "HDMI-3";
            case 0x14: return "HDMI-4";
            case 0x15: return "MDDI";
            case 0x16: return "DP-3";
            case 0x17: return "DP-4";
            default: return "输入源 0x" + v.ToString("X2");
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
        string o = (r.Original < 0) ? "读取失败" : r.Original.ToString();
        r.Value.Text = "当前 " + r.Bar.Value + "（原值 " + o + "）";
    }

    static void UpdateSrcLabel(SrcRow s)
    {
        if (s.Current < 0) s.Value.Text = "当前 读取失败";
        else s.Value.Text = "当前 " + SourceName(s.Current);
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
    // 不再写死任何个人机器上的路径。
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

    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 先把 --fake N 摘出来，剩下的参数按原来的规则解释
        List<string> rest = new List<string>();
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
            rest.Add(args[i]);
        }
        string[] a = rest.ToArray();

        bool dumpMode = (a.Length >= 2 && a[0] == "--dump");
        bool renderMode = (a.Length >= 2 && a[0] == "--render");
        bool interactive = !dumpMode && !renderMode;

        Log("=== Main args=" + string.Join("|", args) + (FakeMode ? "   [FAKE " + FakeMonitors + "]" : ""));

        if (FakeMode)
        {
            CliPath = "(假数据模式，不调用 winddcutil)";
            CliTempDir = Path.GetTempPath();
        }
        else
        {
            CliPath = FindCli();
            if (CliPath == null)
            {
                Log("CliPath NOT FOUND");
                if (dumpMode) { File.WriteAllText(a[1], "错误：找不到 winddcutil.exe。\r\n请把它和本程序放在同一个文件夹里。\r\n", Encoding.UTF8); return; }
                if (renderMode) return;
                MessageBox.Show("找不到 winddcutil.exe。\n\n请把它和本程序放在同一个文件夹里再运行。",
                    "显示器调节", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            string msg = "没有检测到支持 DDC/CI 的显示器。\r\nCLI = " + CliPath +
                         "\r\n（如果是在受限沙箱里跑，winddcutil 可能无法启动，请换完整权限的终端重试。）\r\n";
            Log("no monitors");
            if (dumpMode) { File.WriteAllText(a[1], msg, Encoding.UTF8); return; }
            if (renderMode) return;
            MessageBox.Show(msg, "显示器调节", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Form form = new Form();
        form.Text = "显示器调节 - 亮度 / 对比度 / 信号源";
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
            loading.Text = "正在识别显示器，请稍候…（共 " + monitors + " 台）";
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

        for (int d = 1; d <= monitors; d++)
        {
            int c = (d - 1) / rows;          // 先竖着排，排满一列再排下一列
            int r = (d - 1) % rows;

            GroupBox grp = new GroupBox();
            grp.Text = "显示器 " + d;
            grp.Location = new Point(sidePad + c * colW, topPad + r * rowH);
            grp.Size = new Size(grpW, grpH);
            pnlScroll.Controls.Add(grp);

            string[] names = { "亮度", "对比度" };
            string[] codes = { "0x10", "0x12" };
            int[] ys = { 22, 58 };

            for (int i = 0; i < 2; i++)
            {
                Label ln = new Label();
                ln.Text = names[i];
                ln.Location = new Point(14, ys[i] + 6);
                ln.Size = new Size(50, 20);
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
                bar.Location = new Point(66, ys[i]);
                bar.Size = new Size(250, 36);
                if (o.HasValue) bar.Value = Math.Max(0, Math.Min(initMax, o.Value));
                grp.Controls.Add(bar);

                Label lv = new Label();
                lv.Location = new Point(322, ys[i] + 6);
                lv.Size = new Size(150, 20);
                grp.Controls.Add(lv);

                Row rw = new Row();
                rw.Display = d;
                rw.Code = codes[i];
                rw.Bar = bar;
                rw.Value = lv;
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
            ls.Text = "信号源";
            ls.Location = new Point(14, 104);
            ls.Size = new Size(50, 20);
            grp.Controls.Add(ls);

            SrcRow sr = new SrcRow();
            sr.Display = d;

            ComboBox combo = new ComboBox();
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.Location = new Point(66, 100);
            combo.Size = new Size(210, 24);
            grp.Controls.Add(combo);
            sr.Combo = combo;

            Button btnSwitch = new Button();
            btnSwitch.Text = "切换";
            btnSwitch.Location = new Point(282, 99);
            btnSwitch.Size = new Size(70, 26);
            grp.Controls.Add(btnSwitch);

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
                    MessageBox.Show("「显示器 " + sr2.Display + "」现在就已经是 " + SourceName(target) + " 了。",
                        "切换信号源", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                DialogResult dr = MessageBox.Show(
                    "把「显示器 " + sr2.Display + "」切换到 " + SourceName(target) + "（0x" + target.ToString("X2") + "）？\n\n" +
                    "注意：如果那个接口上没接设备，显示器会黑屏或显示「无信号」，\n" +
                    "而且软件可能无法再切回来，需要用显示器自己的按键切回去。",
                    "切换信号源", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
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
        bApply.Text = "应用";
        bApply.Location = new Point(bx, 8);
        bApply.Size = new Size(btnW, btnH);
        pnlBottom.Controls.Add(bApply);

        Button bRestore = new Button();
        bRestore.Text = "恢复原值";
        bRestore.Location = new Point(bx + (btnW + gapB), 8);
        bRestore.Size = new Size(btnW, btnH);
        pnlBottom.Controls.Add(bRestore);

        Button bReload = new Button();
        bReload.Text = "重新读取";
        bReload.Location = new Point(bx + (btnW + gapB) * 2, 8);
        bReload.Size = new Size(btnW, btnH);
        pnlBottom.Controls.Add(bReload);

        Button bClose = new Button();
        bClose.Text = "关闭";
        bClose.Location = new Point(bx + (btnW + gapB) * 3, 8);
        bClose.Size = new Size(btnW, btnH);
        pnlBottom.Controls.Add(bClose);

        Label lRange = new Label();
        lRange.Text = "亮度量程：";
        lRange.Location = new Point(12, 50);
        lRange.Size = new Size(76, 20);
        pnlBottom.Controls.Add(lRange);

        ComboBox cmbRange = new ComboBox();
        cmbRange.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbRange.Location = new Point(90, 46);
        cmbRange.Size = new Size(86, 24);
        cmbRange.Items.Add("自动检测");
        cmbRange.Items.Add("0-100");
        cmbRange.Items.Add("0-255");
        cmbRange.SelectedIndex = 0;
        pnlBottom.Controls.Add(cmbRange);

        Label status = new Label();
        status.Location = new Point(12, 74);
        status.Size = new Size(clientW - 24, 18);
        status.ForeColor = Color.DimGray;
        status.Text = "原值 = 打开窗口时的数值；信号源用「切换」按钮单独切换";
        pnlBottom.Controls.Add(status);

        if (loading != null) form.Controls.Remove(loading);
        form.ClientSize = new Size(clientW, clientH);
        pnlScroll.Bounds = new Rectangle(0, 0, clientW, clientH - bottomH);
        pnlBottom.Bounds = new Rectangle(0, clientH - bottomH, clientW, bottomH);

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

        cmbRange.SelectedIndexChanged += delegate { ApplyRanges(); };
        ApplyRanges();

        bApply.Click += delegate
        {
            foreach (Row rw in rowsList) SetVcp(rw.Display, rw.Code, rw.Bar.Value);
            status.Text = "已把 " + rowsList.Count + " 项亮度/对比度设置发送给显示器（" + DateTime.Now.ToString("HH:mm:ss") + "）"
                + (FakeMode ? "  [假数据模式，并没有真的发送]" : "");
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
            status.Text = "已恢复到打开窗口时的数值（" + n + " 项）" + (FakeMode ? "  [假数据模式]" : "");
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
            status.Text = "已重新读取（含信号源），并把当前数值记为新的原值（" + DateTime.Now.ToString("HH:mm:ss") + "）";
        };

        bClose.Click += delegate { form.Close(); };

        if (dumpMode)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("CLI = " + CliPath + (FakeMode ? "   [假数据模式]" : ""));
            sb.AppendLine("显示器数 = " + monitors);
            sb.AppendLine("布局 = " + cols + " 列 x " + rows + " 行");
            foreach (Row rw in rowsList)
                sb.AppendLine("  显示器 " + rw.Display + " " + rw.Code + " 原值=" + rw.Original +
                              " 滑块=" + rw.Bar.Value + " 量程=0-" + rw.Max);
            foreach (SrcRow s in srcs)
            {
                sb.Append("  显示器 " + s.Display + " 信号源 当前=" + s.Current + " (" + SourceName(s.Current) + ")");
                sb.Append(" 列表来源=" + (s.Guessed ? "标准兜底" : "显示器上报") + " 可选=");
                foreach (int v in s.Options) sb.Append("0x" + v.ToString("X2") + " ");
                sb.AppendLine();
            }
            sb.AppendLine("窗口尺寸 = " + form.ClientSize.Width + " x " + form.ClientSize.Height);
            sb.AppendLine("屏幕工作区 = " + wa.Width + " x " + wa.Height);
            sb.AppendLine("顶层控件数 = " + form.Controls.Count);
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
