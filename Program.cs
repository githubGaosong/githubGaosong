using System;
using System.Drawing;
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
    const int HOTKEY_OVERLAY = 2;
    const uint MOD_NOREPEAT = 0x4000;
    const uint VK_F4 = 0x73;
    const uint VK_F5 = 0x74;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    readonly TextBox source = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    readonly Button translateButton = new() { Text = "翻译 (F5)", Height = 34 };
    readonly Label status = new() { AutoSize = true, Text = "粘贴游戏文字，按 F5 翻译；F4 显示/隐藏弹窗。" };
    readonly OverlayWindow overlay = new();
    CancellationTokenSource? requestCts;

    public MainWindow()
    {
        Text = "GameTranslator";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 330);
        MinimumSize = new Size(420, 280);
        Font = new Font("Microsoft YaHei UI", 9F);

        source.SetBounds(12, 12, 496, 215);
        source.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        source.PlaceholderText = "在这里粘贴游戏中的文字…";

        translateButton.SetBounds(12, 238, 120, 34);
        translateButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        translateButton.Click += async (_, _) => await TranslateAsync();

        status.SetBounds(145, 247, 360, 24);
        status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange(new Control[] { source, translateButton, status });
        FormClosing += (_, _) =>
        {
            requestCts?.Cancel();
            overlay.Close();
            UnregisterHotKey(Handle, HOTKEY_TRANSLATE);
            UnregisterHotKey(Handle, HOTKEY_OVERLAY);
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        bool f5 = RegisterHotKey(Handle, HOTKEY_TRANSLATE, MOD_NOREPEAT, VK_F5);
        bool f4 = RegisterHotKey(Handle, HOTKEY_OVERLAY, MOD_NOREPEAT, VK_F4);
        if (!f5 || !f4)
            status.Text = "快捷键注册失败：可能被其他软件占用。";
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            if (m.WParam.ToInt32() == HOTKEY_TRANSLATE)
                _ = TranslateAsync();
            else if (m.WParam.ToInt32() == HOTKEY_OVERLAY)
                overlay.Toggle();
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
        try
        {
            string url = "https://api.mymemory.translated.net/get?q=" +
                         Uri.EscapeDataString(text) + "&langpair=auto|zh-CN";
            using var response = await Http.GetAsync(url, requestCts.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(requestCts.Token));
            string translated = json.RootElement
                .GetProperty("responseData").GetProperty("translatedText").GetString() ?? "";

            if (string.IsNullOrWhiteSpace(translated))
                throw new InvalidOperationException("翻译服务没有返回结果。");

            overlay.SetText(translated);
            overlay.ShowOverlay();
            status.Text = "翻译完成。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            status.Text = "翻译失败：" + ex.Message;
        }
        finally
        {
            translateButton.Enabled = true;
        }
    }
}

sealed class OverlayWindow : Form
{
    readonly Label result = new()
    {
        Dock = DockStyle.Fill,
        ForeColor = Color.White,
        BackColor = Color.Transparent,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = new Font("Microsoft YaHei UI", 14F),
        Padding = new Padding(14),
        AutoEllipsis = true
    };

    [DllImport("user32.dll")]
    static extern bool ReleaseCapture();
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(28, 30, 36);
        Opacity = 0.92;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(80, 80, 560, 150);
        Controls.Add(result);
        result.MouseDown += DragWindow;
        MouseDown += DragWindow;
    }

    void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }
    }

    public void SetText(string text) => result.Text = text;

    public void ShowOverlay()
    {
        if (!Visible) Show();
        TopMost = true;
    }

    public void Toggle()
    {
        if (Visible) Hide();
        else ShowOverlay();
    }
}
