using System;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainWindow());
    }
}

sealed class MainWindow : Form
{
    const int WM_HOTKEY = 0x0312;
    const int HOTKEY_TRANSLATE = 1;
    const int HOTKEY_WINDOW = 2;
    const uint MOD_NOREPEAT = 0x4000;
    const uint VK_F4 = 0x73;
    const uint VK_F5 = 0x74;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Windows acrylic backdrop (no neon glow); falls back to a translucent window if unavailable.
    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }
    [DllImport("user32.dll", SetLastError = true)]
    static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly Color Glass = Color.FromArgb(36, 43, 52);
    static readonly Color Surface = Color.FromArgb(90, 101, 113);
    static readonly Color TextColor = Color.FromArgb(245, 247, 250);
    static readonly Color Muted = Color.FromArgb(205, 213, 222);
    static readonly Color Accent = Color.FromArgb(135, 220, 205);

    readonly Label sourceLabel = new() { Text = "原文", AutoSize = true };
    readonly TextBox source = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle };
    readonly Label resultLabel = new() { Text = "译文", AutoSize = true };
    readonly TextBox translated = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle };
    readonly ComboBox targetLanguage = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Button translateButton = new() { Text = "翻译 (F5)", Height = 38, FlatStyle = FlatStyle.Flat };
    readonly Label status = new() { AutoSize = true, Text = "F5 翻译  ·  F4 显示/隐藏窗口" };
    CancellationTokenSource? requestCts;

    public MainWindow()
    {
        Text = "GameTranslator";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(680, 590);
        MinimumSize = new Size(500, 460);
        TopMost = true;
        BackColor = Glass;
        ForeColor = TextColor;
        Font = new Font("Microsoft YaHei UI", 10F);
        Padding = new Padding(18);

        var header = new Label { Text = "GAME TRANSLATOR", Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold), ForeColor = TextColor, AutoSize = true };
        header.SetBounds(18, 14, 260, 30);

        var languageLabel = new Label { Text = "目标语言", AutoSize = true, ForeColor = Muted };
        languageLabel.SetBounds(385, 17, 76, 28);
        targetLanguage.Items.AddRange(new object[] { "翻译成中文", "翻译成英文" });
        targetLanguage.SelectedIndex = 0;
        targetLanguage.SetBounds(465, 10, 190, 34);
        targetLanguage.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        targetLanguage.FlatStyle = FlatStyle.Flat;
        targetLanguage.BackColor = Color.FromArgb(65, 75, 86);
        targetLanguage.ForeColor = TextColor;

        sourceLabel.SetBounds(0, 65, 580, 26);
        sourceLabel.ForeColor = Muted;
        sourceLabel.Font = new Font(Font, FontStyle.Bold);
        sourceLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        source.SetBounds(0, 94, 644, 175);
        source.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        source.BackColor = Color.FromArgb(75, 84, 95);
        source.ForeColor = TextColor;
        source.Font = new Font("Microsoft YaHei UI", 11F);
        source.PlaceholderText = "在这里粘贴需要翻译的文字…";

        resultLabel.SetBounds(0, 288, 580, 26);
        resultLabel.ForeColor = Accent;
        resultLabel.Font = new Font(Font, FontStyle.Bold);
        resultLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        translated.SetBounds(0, 317, 644, 190);
        translated.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        translated.BackColor = Color.FromArgb(66, 91, 91);
        translated.ForeColor = TextColor;
        translated.Font = new Font("Microsoft YaHei UI", 14F);

        translateButton.SetBounds(0, 524, 140, 40);
        translateButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        translateButton.BackColor = Color.FromArgb(105, 155, 148);
        translateButton.ForeColor = Color.White;
        translateButton.FlatAppearance.BorderSize = 0;
        translateButton.Click += async (_, _) => await TranslateAsync();

        status.SetBounds(158, 534, 480, 26);
        status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        status.ForeColor = Muted;

        Controls.AddRange(new Control[] { header, languageLabel, targetLanguage, sourceLabel, source, resultLabel, translated, translateButton, status });
        FormClosing += (_, _) =>
        {
            requestCts?.Cancel();
            UnregisterHotKey(Handle, HOTKEY_TRANSLATE);
            UnregisterHotKey(Handle, HOTKEY_WINDOW);
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyAcrylic();
        bool f5 = RegisterHotKey(Handle, HOTKEY_TRANSLATE, MOD_NOREPEAT, VK_F5);
        bool f4 = RegisterHotKey(Handle, HOTKEY_WINDOW, MOD_NOREPEAT, VK_F4);
        if (!f5 || !f4)
            status.Text = "快捷键注册失败：可能被其他软件占用。";
    }

    void ApplyAcrylic()
    {
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
                AccentFlags = 2,
                GradientColor = unchecked((int)0xCC252B32),
                AnimationId = 0
            };
            int size = Marshal.SizeOf<AccentPolicy>();
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = size };
                if (SetWindowCompositionAttribute(Handle, ref data) == 0)
                    Opacity = 0.94;
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { Opacity = 0.94; }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            if (m.WParam.ToInt32() == HOTKEY_TRANSLATE)
                _ = TranslateAsync();
            else if (m.WParam.ToInt32() == HOTKEY_WINDOW)
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
        base.WndProc(ref m);
    }

    async Task TranslateAsync()
    {
        string text = source.Text.Trim();
        if (text.Length == 0)
        {
            status.Text = "请先粘贴需要翻译的文字。";
            return;
        }

        requestCts?.Cancel();
        requestCts?.Dispose();
        requestCts = new CancellationTokenSource();
        translateButton.Enabled = false;
        status.Text = "正在翻译…";
        string langPair = targetLanguage.SelectedIndex == 1 ? "zh-CN|en" : "en|zh-CN";

        try
        {
            string url = "https://api.mymemory.translated.net/get?q=" +
                         Uri.EscapeDataString(text) + "&langpair=" + langPair;
            using var response = await Http.GetAsync(url, requestCts.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(requestCts.Token));
            var root = json.RootElement;
            if (root.TryGetProperty("responseStatus", out var apiStatus) &&
                apiStatus.ValueKind == JsonValueKind.Number && apiStatus.GetInt32() != 200)
            {
                string detail = root.TryGetProperty("responseDetails", out var details)
                    ? details.GetString() ?? "未知错误" : "未知错误";
                throw new InvalidOperationException("翻译接口返回错误：" + detail);
            }
            if (!root.TryGetProperty("responseData", out var responseData) ||
                !responseData.TryGetProperty("translatedText", out var translatedElement))
                throw new InvalidOperationException("翻译接口没有返回译文，请稍后重试。");

            string result = WebUtility.HtmlDecode(translatedElement.GetString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(result))
                throw new InvalidOperationException("接口返回的译文为空。请检查原文是否过长，或稍后重试。");

            translated.Text = result;
            status.Text = "翻译完成。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = "翻译失败：" + ex.Message; }
        finally { translateButton.Enabled = true; }
    }
}
