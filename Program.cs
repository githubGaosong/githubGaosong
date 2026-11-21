using System;
using System.Drawing;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Net;
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
    const int HOTKEY_RESULT = 2;
    const uint MOD_NOREPEAT = 0x4000;
    const uint VK_F4 = 0x73;
    const uint VK_F5 = 0x74;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    readonly Label sourceLabel = new() { Text = "游戏原文", AutoSize = true };
    readonly TextBox source = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    readonly Label resultLabel = new() { Text = "中文翻译", AutoSize = true };
    readonly TextBox translated = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = SystemColors.Window,
        Font = new Font("Microsoft YaHei UI", 11F)
    };
    readonly Button translateButton = new() { Text = "翻译 (F5)", Height = 36 };
    readonly Label status = new() { AutoSize = true, Text = "粘贴游戏文字，按 F5 翻译；F4 显示/隐藏译文区域。" };
    CancellationTokenSource? requestCts;
    bool resultVisible = true;

    public MainWindow()
    {
        Text = "GameTranslator";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 570);
        MinimumSize = new Size(460, 440);
        Font = new Font("Microsoft YaHei UI", 9F);

        sourceLabel.SetBounds(14, 12, 580, 22);
        sourceLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        source.SetBounds(14, 38, 592, 185);
        source.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        source.PlaceholderText = "在这里粘贴游戏中的文字…";

        resultLabel.SetBounds(14, 236, 580, 22);
        resultLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        translated.SetBounds(14, 262, 592, 220);
        translated.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        translateButton.SetBounds(14, 500, 126, 36);
        translateButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        translateButton.Click += async (_, _) => await TranslateAsync();

        status.SetBounds(154, 509, 450, 24);
        status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange(new Control[] { sourceLabel, source, resultLabel, translated, translateButton, status });
        FormClosing += (_, _) =>
        {
            requestCts?.Cancel();
            UnregisterHotKey(Handle, HOTKEY_TRANSLATE);
            UnregisterHotKey(Handle, HOTKEY_RESULT);
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        bool f5 = RegisterHotKey(Handle, HOTKEY_TRANSLATE, MOD_NOREPEAT, VK_F5);
        bool f4 = RegisterHotKey(Handle, HOTKEY_RESULT, MOD_NOREPEAT, VK_F4);
        if (!f5 || !f4)
            status.Text = "快捷键注册失败：可能被其他软件占用。";
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            if (m.WParam.ToInt32() == HOTKEY_TRANSLATE)
                _ = TranslateAsync();
            else if (m.WParam.ToInt32() == HOTKEY_RESULT)
                ToggleResult();
        }
        base.WndProc(ref m);
    }

    void ToggleResult()
    {
        resultVisible = !resultVisible;
        resultLabel.Visible = resultVisible;
        translated.Visible = resultVisible;
        status.Text = resultVisible ? "译文区域已显示。" : "译文区域已隐藏。";
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
                         Uri.EscapeDataString(text) + "&langpair=en|zh-CN";
            using var response = await Http.GetAsync(url, requestCts.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(requestCts.Token));
            var root = json.RootElement;
            if (root.TryGetProperty("responseStatus", out var apiStatus) &&
                apiStatus.ValueKind == JsonValueKind.Number &&
                apiStatus.GetInt32() != 200)
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
            if (!resultVisible) ToggleResult();
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
