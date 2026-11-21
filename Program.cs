using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Windows.Shell;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new TranslatorWindow());
    }
}

sealed class TranslatorWindow : Window
{
    const int WmHotkey = 0x0312;
    const int HotkeyTranslate = 1, HotkeyToggle = 2;
    const uint NoRepeat = 0x4000, VkF4 = 0x73, VkF5 = 0x74;
    const int DwmwaSystemBackdropType = 38, DwmwaUseImmersiveDarkMode = 20, DwmwaCornerPreference = 33;
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    readonly TextBox source = new();
    readonly TextBox translated = new();
    readonly ComboBox direction = new();
    readonly TextBlock status = new();
    readonly Button translateButton = new();
    readonly CheckBox topmost = new();
    HwndSource? hwndSource;
    CancellationTokenSource? request;

    static readonly Color White = Color.FromRgb(246, 249, 253);
    static readonly Color Muted = Color.FromRgb(190, 205, 221);
    static readonly Brush Glass = new SolidColorBrush(Color.FromArgb(214, 25, 43, 63));
    static readonly Brush FieldBrush = new SolidColorBrush(Color.FromArgb(74, 8, 22, 38));
    static readonly Brush ResultBrush = new SolidColorBrush(Color.FromArgb(62, 44, 103, 145));
    static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(74, 164, 246));

    public TranslatorWindow()
    {
        Title = "Tanslator";
        Width = 850; Height = 660; MinWidth = 650; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = false;
        Background = Glass;
        Foreground = new SolidColorBrush(White);
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 14;
        Topmost = true;
        ShowInTaskbar = true;

        WindowChrome.SetWindowChrome(this, new WindowChrome {
            CaptionHeight = 0, ResizeBorderThickness = new Thickness(8),
            CornerRadius = new CornerRadius(18), GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        var outer = new Border {
            Background = Glass, CornerRadius = new CornerRadius(18),
            BorderBrush = new SolidColorBrush(Color.FromArgb(210, 206, 220, 234)),
            BorderThickness = new Thickness(1.4), Padding = new Thickness(22),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = .22, Color = Colors.Black }
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(68) });
        outer.Child = layout;
        Content = outer;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = MakeTIcon();
        icon.Margin = new Thickness(0, 0, 16, 0);
        header.Children.Add(icon);
        var title = new TextBlock { Text = "Tanslator", FontSize = 28, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(White), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1); header.Children.Add(title);

        direction.Items.Add("英文  →  简体中文");
        direction.Items.Add("简体中文  →  英文");
        direction.SelectedIndex = 0;
        direction.Width = 210; direction.Height = 38; direction.Margin = new Thickness(10, 0, 12, 0);
        direction.VerticalContentAlignment = VerticalAlignment.Center;
        direction.Background = new SolidColorBrush(Color.FromArgb(110, 30, 49, 69));
        direction.Foreground = Brushes.White;
        direction.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 218, 231, 244));
        Grid.SetColumn(direction, 2); header.Children.Add(direction);
        var minimize = HeaderButton("—"); minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        Grid.SetColumn(minimize, 3); header.Children.Add(minimize);
        var close = HeaderButton("×"); close.Click += (_, _) => Close();
        Grid.SetColumn(close, 4); header.Children.Add(close);
        Grid.SetRow(header, 0); layout.Children.Add(header);

        var sourceLabel = SectionTitle("原文");
        Grid.SetRow(sourceLabel, 1); layout.Children.Add(sourceLabel);
        StyleEditor(source, "在这里粘贴需要翻译的文字…", false);
        source.Margin = new Thickness(0, 8, 0, 16);
        Grid.SetRow(source, 2); layout.Children.Add(source);

        var divider = new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(65, 224, 236, 247)),
            Margin = new Thickness(0, 0, 0, 14) };
        Grid.SetRow(divider, 3); layout.Children.Add(divider);

        var resultPanel = new Grid();
        resultPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        resultPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var resultLabel = SectionTitle("译文");
        resultLabel.Foreground = new SolidColorBrush(Color.FromRgb(142, 218, 209));
        resultPanel.Children.Add(resultLabel);
        StyleEditor(translated, "Translation will appear here…", true);
        translated.Margin = new Thickness(0, 8, 0, 0);
        Grid.SetRow(translated, 1); resultPanel.Children.Add(translated);
        Grid.SetRow(resultPanel, 4); layout.Children.Add(resultPanel);

        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        translateButton.Content = "翻译  ·  F5";
        translateButton.Width = 144; translateButton.Height = 44;
        translateButton.Background = Blue; translateButton.Foreground = Brushes.White;
        translateButton.BorderThickness = new Thickness(0); translateButton.FontWeight = FontWeights.SemiBold;
        translateButton.Cursor = Cursors.Hand; translateButton.Click += async (_, _) => await TranslateAsync();
        Grid.SetColumn(translateButton, 0); footer.Children.Add(translateButton);
        var hint = new TextBlock { Text = "F4  显示 / 隐藏窗口", Foreground = new SolidColorBrush(Muted),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 0, 0) };
        Grid.SetColumn(hint, 1); footer.Children.Add(hint);
        topmost.Content = "窗口置顶"; topmost.IsChecked = true; topmost.Foreground = new SolidColorBrush(Muted);
        topmost.VerticalAlignment = VerticalAlignment.Center; topmost.Checked += (_, _) => Topmost = true;
        topmost.Unchecked += (_, _) => Topmost = false;
        Grid.SetColumn(topmost, 3); footer.Children.Add(topmost);
        status.Text = "就绪"; status.Foreground = new SolidColorBrush(Muted);
        status.FontSize = 11; status.VerticalAlignment = VerticalAlignment.Bottom;
        status.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetRow(footer, 5); layout.Children.Add(footer);
        Grid.SetColumn(status, 2); footer.Children.Add(status);

        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            hwndSource = HwndSource.FromHwnd(handle);
            hwndSource?.AddHook(WndProc);
            RegisterHotKey(handle, HotkeyTranslate, NoRepeat, VkF5);
            RegisterHotKey(handle, HotkeyToggle, NoRepeat, VkF4);
            ApplyBackdrop(handle);
        };
        Closed += (_, _) => {
            request?.Cancel();
            var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HotkeyTranslate); UnregisterHotKey(handle, HotkeyToggle);
            hwndSource?.RemoveHook(WndProc);
        };
    }

    static FrameworkElement MakeTIcon()
    {
        var grid = new Grid { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center };
        var tile = new Border { CornerRadius = new CornerRadius(11),
            Background = new LinearGradientBrush(Color.FromRgb(87, 177, 246), Color.FromRgb(32, 99, 184), 45),
            BorderBrush = new SolidColorBrush(Color.FromArgb(150, 220, 240, 255)), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 9, ShadowDepth = 2, Opacity = .28, Color = Color.FromRgb(24, 111, 207) } };
        var t = new TextBlock { Text = "T", FontFamily = new FontFamily("Segoe UI"), FontSize = 32,
            FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = .3 } };
        tile.Child = t; grid.Children.Add(tile); return grid;
    }

    static Button HeaderButton(string text) => new() { Content = text, Width = 34, Height = 36,
        Margin = new Thickness(4, 0, 0, 0), Background = new SolidColorBrush(Color.FromArgb(65, 230, 240, 250)),
        Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 18, Cursor = Cursors.Hand };

    static TextBlock SectionTitle(string text) => new() { Text = text, FontSize = 16,
        FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(White),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };

    static void StyleEditor(TextBox box, string hint, bool readOnly)
    {
        box.AcceptsReturn = true; box.AcceptsTab = true; box.TextWrapping = TextWrapping.Wrap;
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        box.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        box.FontSize = 16; box.Padding = new Thickness(14, 12, 14, 12);
        box.Foreground = new SolidColorBrush(White); box.Background = readOnly ? ResultBrush : FieldBrush;
        box.BorderBrush = readOnly ? new SolidColorBrush(Color.FromArgb(175, 84, 159, 221))
            : new SolidColorBrush(Color.FromArgb(70, 217, 230, 243));
        box.BorderThickness = new Thickness(1); box.IsReadOnly = readOnly;
        box.TextChanged += (_, _) => { };
        box.ToolTip = hint;
        box.CaretBrush = Brushes.White;
    }

    static void ApplyBackdrop(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        int dark = 1, corner = 2, backdrop = 3;
        DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        DwmSetWindowAttribute(handle, DwmwaCornerPreference, ref corner, sizeof(int));
        DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey)
        {
            if (wParam.ToInt32() == HotkeyTranslate) _ = TranslateAsync();
            if (wParam.ToInt32() == HotkeyToggle)
            {
                if (IsVisible) Hide();
                else { Show(); WindowState = WindowState.Normal; Activate(); }
            }
        }
        return IntPtr.Zero;
    }

    async Task TranslateAsync()
    {
        string input = source.Text.Trim();
        if (input.Length == 0) { status.Text = "请先输入需要翻译的文字。"; return; }
        if (input.Length > 5000) { status.Text = "文本超过 5000 字符，请分段翻译。"; return; }
        request?.Cancel();
        var current = new CancellationTokenSource(); request = current;
        translateButton.IsEnabled = false; status.Text = "正在翻译…";
        string pair = direction.SelectedIndex == 1 ? "zh-CN|en" : "en|zh-CN";
        try
        {
            var pieces = Split(input, 450); var output = new List<string>();
            for (int i = 0; i < pieces.Count; i++)
            {
                current.Token.ThrowIfCancellationRequested();
                status.Text = pieces.Count > 1 ? $"正在翻译… ({i + 1}/{pieces.Count})" : "正在翻译…";
                output.Add(await TranslateChunk(pieces[i], pair, current.Token));
            }
            translated.Text = string.Join(" ", output); status.Text = "翻译完成";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!current.IsCancellationRequested) status.Text = "翻译失败：" + ex.Message; }
        finally
        {
            current.Dispose();
            if (ReferenceEquals(request, current)) { request = null; translateButton.IsEnabled = true; }
        }
    }

    static List<string> Split(string text, int limit)
    {
        var parts = new List<string>(); int start = 0;
        while (start < text.Length)
        {
            int end = Math.Min(start + limit, text.Length);
            if (end < text.Length) { int p = text.LastIndexOfAny(new char[] { ' ', (char)9, (char)13, (char)10 }, end - 1, end - start); if (p > start + limit / 2) end = p + 1; }
            parts.Add(text[start..end].Trim()); start = end;
            while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        }
        return parts.Where(x => x.Length > 0).ToList();
    }

    static async Task<string> TranslateChunk(string text, string pair, CancellationToken token)
    {
        string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) + "&langpair=" + pair;
        using var response = await Http.GetAsync(url, token); response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = doc.RootElement;
        if (root.TryGetProperty("responseStatus", out var s) && s.ValueKind == JsonValueKind.Number && s.GetInt32() != 200)
            throw new InvalidOperationException(root.TryGetProperty("responseDetails", out var d) ? d.GetString() ?? "翻译服务返回错误" : "翻译服务返回错误");
        string value = WebUtility.HtmlDecode(root.GetProperty("responseData").GetProperty("translatedText").GetString() ?? "").Trim();
        if (value.Length == 0) throw new InvalidOperationException("翻译服务没有返回有效译文。");
        return value;
    }
}