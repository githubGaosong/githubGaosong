using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TranslatorWindow());
    }
}

sealed class TranslatorWindow : Form
{
    const int HotkeyMessage = 0x0312;
    const int TranslateHotkey = 1, ToggleHotkey = 2;
    const uint NoRepeat = 0x4000, F4 = 0x73, F5 = 0x74;
    const int WmNclButtonDown = 0xA1, HtCaption = 0x2;
    const int DwmwaUseImmersiveDarkMode = 20;
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwaSystemBackdropType = 38;
    const int DwmwaBorderColor = 34;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly TextBox original = new();
    readonly TextBox result = new();
    readonly ComboBox target = new();
    readonly Button translate = new();
    readonly Label status = new();
    readonly CheckBox alwaysOnTop = new();
    readonly Panel header = new();
    CancellationTokenSource? request;

    static readonly Color Ink = Color.FromArgb(245, 248, 252);
    static readonly Color Muted = Color.FromArgb(184, 201, 218);
    static readonly Color Field = Color.FromArgb(42, 57, 73);
    static readonly Color FieldGreen = Color.FromArgb(38, 65, 66);
    static readonly Color Accent = Color.FromArgb(83, 163, 148);

    public TranslatorWindow()
    {
        Text = "Tanslator";
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(580, 550);
        MinimumSize = new Size(500, 500);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Opacity = 0.94;
        BackColor = Color.FromArgb(22, 31, 43);
        ForeColor = Ink;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        Padding = new Padding(18);

        header.SetBounds(0, 0, 544, 54);
        header.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        header.BackColor = Color.FromArgb(31, 44, 59);
        header.MouseDown += DragWindow;

        var mark = new Label {
            Text = "◈", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(157, 221, 207), Font = new Font("Segoe UI Symbol", 19),
        };
        mark.SetBounds(0, 5, 38, 42);
        mark.MouseDown += DragWindow;

        var heading = new Label {
            Text = "Tanslator", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Ink, Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold)
        };
        heading.SetBounds(40, 5, 170, 42);
        heading.MouseDown += DragWindow;

        target.Items.AddRange(new object[] { "翻译成中文", "翻译成英文" });
        target.SelectedIndex = 0;
        target.DropDownStyle = ComboBoxStyle.DropDownList;
        target.SetBounds(300, 12, 150, 30);
        target.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        target.BackColor = Color.FromArgb(53, 68, 84);
        target.ForeColor = Color.White;
        target.FlatStyle = FlatStyle.Flat;

        var minimize = HeaderButton("—");
        minimize.SetBounds(462, 10, 30, 30);
        minimize.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;

        var close = HeaderButton("×");
        close.SetBounds(500, 10, 30, 30);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.Click += (_, _) => Close();

        header.Controls.AddRange(new Control[] { mark, heading, target, minimize, close });

        var originalLabel = SectionLabel("原文");
        originalLabel.SetBounds(0, 70, 500, 22);
        original.SetBounds(0, 98, 544, 132);
        original.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        original.Multiline = true;
        original.AcceptsReturn = true;
        original.ScrollBars = ScrollBars.Vertical;
        original.Font = new Font("Microsoft YaHei UI", 10.5f);
        original.PlaceholderText = "在这里粘贴需要翻译的文字…";
        StyleTextBox(original, Field);

        var divider = new Panel {
            BackColor = Color.FromArgb(70, 128, 145, 163)
        };
        divider.SetBounds(0, 246, 544, 1);
        divider.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var resultLabel = SectionLabel("译文");
        resultLabel.ForeColor = Color.FromArgb(151, 224, 207);
        resultLabel.SetBounds(0, 262, 500, 22);
        result.SetBounds(0, 290, 544, 145);
        result.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        result.Multiline = true;
        result.ReadOnly = true;
        result.ScrollBars = ScrollBars.Vertical;
        result.Font = new Font("Microsoft YaHei UI", 11.5f);
        StyleTextBox(result, FieldGreen);

        translate.Text = "翻译  ·  F5";
        translate.SetBounds(0, 464, 132, 38);
        translate.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        translate.FlatStyle = FlatStyle.Flat;
        translate.FlatAppearance.BorderSize = 0;
        translate.FlatAppearance.MouseOverBackColor = Color.FromArgb(102, 184, 166);
        translate.BackColor = Accent;
        translate.ForeColor = Color.White;
        translate.Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        translate.Cursor = Cursors.Hand;
        translate.Click += async (_, _) => await TranslateAsync();

        alwaysOnTop.Text = "窗口置顶";
        alwaysOnTop.Checked = true;
        alwaysOnTop.AutoSize = true;
        alwaysOnTop.SetBounds(154, 472, 95, 25);
        alwaysOnTop.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        alwaysOnTop.ForeColor = Muted;
        alwaysOnTop.BackColor = Color.Transparent;
        alwaysOnTop.CheckedChanged += (_, _) => TopMost = alwaysOnTop.Checked;

        status.Text = "F4 显示 / 隐藏窗口";
        status.AutoSize = true;
        status.SetBounds(0, 514, 520, 22);
        status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        status.ForeColor = Muted;

        Controls.AddRange(new Control[] {
            header, originalLabel, original, divider, resultLabel, result,
            translate, alwaysOnTop, status
        });

        Resize += (_, _) => ApplyRoundedRegion();
        Shown += (_, _) => {
            ApplyRoundedRegion();
            ApplyDwmBackdrop();
        };
        FormClosing += (_, _) => {
            request?.Cancel();
            request?.Dispose();
            UnregisterHotKey(Handle, TranslateHotkey);
            UnregisterHotKey(Handle, ToggleHotkey);
        };
    }

    static Button HeaderButton(string text)
    {
        return new Button {
            Text = text, FlatStyle = FlatStyle.Flat, ForeColor = Muted,
            BackColor = Color.FromArgb(45, 61, 78), Font = new Font("Segoe UI", 12),
            Cursor = Cursors.Hand, TabStop = false
        }.Also(b => {
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(77, 97, 117);
        });
    }

    static Label SectionLabel(string text) => new() {
        Text = text, AutoSize = true, ForeColor = Color.FromArgb(207, 221, 236),
        Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold)
    };

    static void StyleTextBox(TextBox box, Color background)
    {
        box.BackColor = background;
        box.ForeColor = Ink;
        box.BorderStyle = BorderStyle.FixedSingle;
    }

    void ApplyDwmBackdrop()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        SetDwm(DwmwaUseImmersiveDarkMode, 1);
        SetDwm(DwmwaWindowCornerPreference, 2);
        SetDwm(DwmwaSystemBackdropType, 3);
        SetDwm(DwmwaBorderColor, unchecked((int)0x00A7B8C7));
    }

    void SetDwm(int attribute, int value)
    {
        _ = DwmSetWindowAttribute(Handle, attribute, ref value, sizeof(int));
    }

    void ApplyRoundedRegion()
    {
        if (Width < 2 || Height < 2) return;
        using var path = new GraphicsPath();
        int d = 18;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(Width - d - 1, 0, d, d, 270, 90);
        path.AddArc(Width - d - 1, Height - d - 1, d, d, 0, 90);
        path.AddArc(0, Height - d - 1, d, d, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WmNclButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotKey(Handle, TranslateHotkey, NoRepeat, F5);
        RegisterHotKey(Handle, ToggleHotkey, NoRepeat, F4);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyMessage)
        {
            if (message.WParam.ToInt32() == TranslateHotkey) _ = TranslateAsync();
            if (message.WParam.ToInt32() == ToggleHotkey)
            {
                if (Visible) Hide();
                else {
                    Show();
                    WindowState = FormWindowState.Normal;
                    BringToFront();
                    Activate();
                }
            }
        }
        base.WndProc(ref message);
    }

    async Task TranslateAsync()
    {
        var input = original.Text.Trim();
        if (input.Length == 0) {
            status.Text = "请先粘贴需要翻译的文字。";
            return;
        }

        request?.Cancel();
        request?.Dispose();
        request = new CancellationTokenSource();
        var token = request.Token;
        translate.Enabled = false;
        status.Text = "正在翻译…";
        var pair = target.SelectedIndex == 1 ? "zh-CN|en" : "en|zh-CN";

        try {
            var url = "https://api.mymemory.translated.net/get?q=" +
                      Uri.EscapeDataString(input) + "&langpair=" + pair;
            using var response = await Client.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            var root = json.RootElement;
            if (root.TryGetProperty("responseStatus", out var apiStatus) &&
                apiStatus.ValueKind == JsonValueKind.Number && apiStatus.GetInt32() != 200)
                throw new InvalidOperationException(
                    root.TryGetProperty("responseDetails", out var detail)
                        ? detail.GetString() ?? "翻译服务返回错误" : "翻译服务返回错误");

            var translated = WebUtility.HtmlDecode(
                root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(translated))
                throw new InvalidOperationException("翻译服务没有返回有效译文。");
            result.Text = translated;
            status.Text = "翻译完成。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { status.Text = "翻译失败：" + error.Message; }
        finally {
            if (!IsDisposed) translate.Enabled = true;
        }
    }
}

static class ControlExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}