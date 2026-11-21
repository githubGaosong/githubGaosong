using System.Drawing;
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

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly TextBox original = new();
    readonly TextBox result = new();
    readonly ComboBox target = new();
    readonly Button translate = new();
    readonly Label status = new();
    CancellationTokenSource? request;

    public TranslatorWindow()
    {
        Text = "Tanslator";
        ClientSize = new Size(560, 480);
        MinimumSize = new Size(460, 420);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;

        // Form opacity is applied by Windows to the complete window surface,
        // including every child control, so the whole interface stays translucent.
        Opacity = 0.88;
        BackColor = Color.FromArgb(25, 33, 43);
        ForeColor = Color.FromArgb(245, 248, 252);
        Font = new Font("Microsoft YaHei UI", 9.5f);
        Padding = new Padding(16);

        var heading = new Label
        {
            Text = "Tanslator",
            AutoSize = true,
            ForeColor = Color.FromArgb(239, 246, 255),
            Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold)
        };
        heading.SetBounds(0, 0, 190, 34);
        heading.MouseDown += DragWindow;

        var targetLabel = new Label
        {
            Text = "目标语言",
            AutoSize = true,
            ForeColor = Color.FromArgb(202, 216, 230)
        };
        targetLabel.SetBounds(220, 8, 68, 28);
        target.Items.AddRange(new object[] { "翻译成中文", "翻译成英文" });
        target.SelectedIndex = 0;
        target.DropDownStyle = ComboBoxStyle.DropDownList;
        target.SetBounds(292, 2, 220, 32);
        target.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        target.BackColor = Color.FromArgb(54, 68, 83);
        target.ForeColor = Color.White;

        var originalLabel = new Label
        {
            Text = "原文",
            AutoSize = true,
            ForeColor = Color.FromArgb(207, 221, 236),
            Font = new Font(Font, FontStyle.Bold)
        };
        originalLabel.SetBounds(0, 48, 500, 22);

        original.SetBounds(0, 73, 528, 135);
        original.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        original.Multiline = true;
        original.ScrollBars = ScrollBars.Vertical;
        original.Font = new Font("Microsoft YaHei UI", 10.5f);
        original.PlaceholderText = "在这里粘贴需要翻译的文字…";
        original.BackColor = Color.FromArgb(48, 61, 76);
        original.ForeColor = Color.FromArgb(248, 250, 253);
        original.BorderStyle = BorderStyle.FixedSingle;

        var resultLabel = new Label
        {
            Text = "译文",
            AutoSize = true,
            ForeColor = Color.FromArgb(151, 224, 207),
            Font = new Font(Font, FontStyle.Bold)
        };
        resultLabel.SetBounds(0, 222, 500, 22);

        result.SetBounds(0, 247, 528, 155);
        result.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        result.Multiline = true;
        result.ReadOnly = true;
        result.ScrollBars = ScrollBars.Vertical;
        result.Font = new Font("Microsoft YaHei UI", 12);
        result.BackColor = Color.FromArgb(39, 68, 69);
        result.ForeColor = Color.FromArgb(235, 250, 245);
        result.BorderStyle = BorderStyle.FixedSingle;

        translate.Text = "翻译  ·  F5";
        translate.SetBounds(0, 419, 126, 36);
        translate.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        translate.FlatStyle = FlatStyle.Flat;
        translate.FlatAppearance.BorderSize = 0;
        translate.BackColor = Color.FromArgb(67, 139, 128);
        translate.ForeColor = Color.White;
        translate.Cursor = Cursors.Hand;
        translate.Click += async (_, _) => await TranslateAsync();

        status.Text = "F4 显示 / 隐藏窗口";
        status.AutoSize = true;
        status.SetBounds(142, 426, 360, 24);
        status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        status.ForeColor = Color.FromArgb(190, 207, 220);

        Controls.AddRange(new Control[] {
            heading, targetLabel, target, originalLabel, original,
            resultLabel, result, translate, status
        });

        MouseDown += DragWindow;
        FormClosing += (_, _) =>
        {
            request?.Cancel();
            UnregisterHotKey(Handle, TranslateHotkey);
            UnregisterHotKey(Handle, ToggleHotkey);
        };
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
        if (!RegisterHotKey(Handle, TranslateHotkey, NoRepeat, F5) ||
            !RegisterHotKey(Handle, ToggleHotkey, NoRepeat, F4))
            status.Text = "快捷键注册失败：可能已被其他软件占用。";
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == HotkeyMessage)
        {
            if (message.WParam.ToInt32() == TranslateHotkey) _ = TranslateAsync();
            if (message.WParam.ToInt32() == ToggleHotkey)
            {
                if (Visible) Hide();
                else
                {
                    Show();
                    WindowState = FormWindowState.Normal;
                    TopMost = true;
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
        if (input.Length == 0)
        {
            status.Text = "请先粘贴需要翻译的文字。";
            return;
        }

        request?.Cancel();
        request?.Dispose();
        request = new CancellationTokenSource();
        translate.Enabled = false;
        status.Text = "正在翻译…";
        var pair = target.SelectedIndex == 1 ? "zh-CN|en" : "en|zh-CN";

        try
        {
            var url = "https://api.mymemory.translated.net/get?q=" +
                      Uri.EscapeDataString(input) + "&langpair=" + pair;
            using var response = await Client.GetAsync(url, request.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(request.Token));
            var root = json.RootElement;

            if (root.TryGetProperty("responseStatus", out var apiStatus) &&
                apiStatus.ValueKind == JsonValueKind.Number &&
                apiStatus.GetInt32() != 200)
                throw new InvalidOperationException(
                    root.TryGetProperty("responseDetails", out var detail)
                        ? detail.GetString() ?? "翻译服务返回错误"
                        : "翻译服务返回错误");

            var translated = root.GetProperty("responseData")
                                 .GetProperty("translatedText").GetString();
            translated = WebUtility.HtmlDecode(translated ?? "").Trim();
            if (string.IsNullOrWhiteSpace(translated))
                throw new InvalidOperationException("翻译服务没有返回有效译文。");

            result.Text = translated;
            status.Text = "翻译完成。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { status.Text = "翻译失败：" + error.Message; }
        finally { translate.Enabled = true; }
    }
}
