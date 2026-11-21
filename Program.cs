using System.Drawing;
using System.Net;
using System.Net.Http;
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

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr window, int id);

    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(20) };
    readonly TextBox original = new();
    readonly TextBox result = new();
    readonly ComboBox target = new();
    readonly Button translate = new();
    readonly Label status = new();
    CancellationTokenSource? request;

    public TranslatorWindow()
    {
        Text = "Translator";
        ClientSize = new Size(680, 590);
        MinimumSize = new Size(500, 460);
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Opacity = 0.82;
        BackColor = Color.FromArgb(36, 43, 52);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10);
        Padding = new Padding(18);

        var heading = new Label { Text = "TRANSLATOR", AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        heading.SetBounds(0, 0, 260, 30);

        var targetLabel = new Label { Text = "目标语言", AutoSize = true, ForeColor = Color.Gainsboro };
        targetLabel.SetBounds(385, 3, 76, 28);
        target.Items.AddRange(new object[] { "翻译成中文", "翻译成英文" });
        target.SelectedIndex = 0;
        target.DropDownStyle = ComboBoxStyle.DropDownList;
        target.SetBounds(465, -4, 180, 34);
        target.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        var originalLabel = new Label { Text = "原文", AutoSize = true, ForeColor = Color.Gainsboro, Font = new Font(Font, FontStyle.Bold) };
        originalLabel.SetBounds(0, 51, 580, 26);
        original.SetBounds(0, 80, 644, 175);
        original.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        original.Multiline = true;
        original.ScrollBars = ScrollBars.Vertical;
        original.Font = new Font("Microsoft YaHei UI", 11);
        original.PlaceholderText = "在这里粘贴需要翻译的文字…";
        original.BackColor = Color.FromArgb(75, 84, 95);
        original.ForeColor = Color.White;

        var resultLabel = new Label { Text = "译文", AutoSize = true, ForeColor = Color.FromArgb(180, 235, 220), Font = new Font(Font, FontStyle.Bold) };
        resultLabel.SetBounds(0, 274, 580, 26);
        result.SetBounds(0, 303, 644, 204);
        result.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        result.Multiline = true;
        result.ReadOnly = true;
        result.ScrollBars = ScrollBars.Vertical;
        result.Font = new Font("Microsoft YaHei UI", 14);
        result.BackColor = Color.FromArgb(66, 91, 91);
        result.ForeColor = Color.White;

        translate.Text = "翻译 (F5)";
        translate.SetBounds(0, 524, 140, 40);
        translate.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        translate.FlatStyle = FlatStyle.Flat;
        translate.FlatAppearance.BorderSize = 0;
        translate.BackColor = Color.FromArgb(105, 155, 148);
        translate.ForeColor = Color.White;
        translate.Click += async (_, _) => await TranslateAsync();

        status.Text = "F5 翻译  ·  F4 显示/隐藏窗口";
        status.AutoSize = true;
        status.SetBounds(158, 534, 480, 26);
        status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        status.ForeColor = Color.Gainsboro;

        Controls.AddRange(new Control[] { heading, targetLabel, target, originalLabel, original, resultLabel, result, translate, status });
        FormClosing += (_, _) =>
        {
            request?.Cancel();
            UnregisterHotKey(Handle, TranslateHotkey);
            UnregisterHotKey(Handle, ToggleHotkey);
        };
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
                else { Show(); WindowState = FormWindowState.Normal; TopMost = true; BringToFront(); Activate(); }
            }
        }
        base.WndProc(ref message);
    }

    async Task TranslateAsync()
    {
        var input = original.Text.Trim();
        if (input.Length == 0) { status.Text = "请先粘贴需要翻译的文字。"; return; }

        request?.Cancel();
        request?.Dispose();
        request = new CancellationTokenSource();
        translate.Enabled = false;
        status.Text = "正在翻译…";
        var pair = target.SelectedIndex == 1 ? "zh-CN|en" : "en|zh-CN";

        try
        {
            var url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(input) + "&langpair=" + pair;
            using var response = await Client.GetAsync(url, request.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(request.Token));
            var root = json.RootElement;

            if (root.TryGetProperty("responseStatus", out var apiStatus) &&
                apiStatus.ValueKind == JsonValueKind.Number && apiStatus.GetInt32() != 200)
                throw new InvalidOperationException(root.TryGetProperty("responseDetails", out var detail)
                    ? detail.GetString() ?? "翻译服务返回错误" : "翻译服务返回错误");

            var translated = root.GetProperty("responseData").GetProperty("translatedText").GetString();
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