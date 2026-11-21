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
    const int WmNcHitTest = 0x0084;
    const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14, HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
    readonly TextBox source = new();
    readonly TextBox translated = new();
    readonly ComboBox targetLang = new();
    readonly TextBlock charCount = new();
    readonly TextBlock status = new();
    readonly Button translateButton = new();
    readonly CheckBox topmost = new();
    HwndSource? hwndSource;
    CancellationTokenSource? request;

    static readonly Color White = Color.FromRgb(246, 249, 253);
    static readonly Color Muted = Color.FromRgb(190, 205, 221);
    static readonly Brush WhiteBrush = new SolidColorBrush(Color.FromRgb(250, 250, 255));
    static readonly Brush MutedBrush = new SolidColorBrush(Color.FromArgb(220, 238, 239, 255));
    static readonly Brush GlassPanel = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
    static readonly Brush GlassBorder = new SolidColorBrush(Color.FromArgb(90, 238, 245, 255));
    static readonly Brush Accent = new LinearGradientBrush(Color.FromRgb(38, 190, 255), Color.FromRgb(0, 91, 255), 90);

    public TranslatorWindow()
    {
        Title = "Tanslator";
        Width = 780; Height = 660; MinWidth = 560; MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Foreground = WhiteBrush; FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 16; Topmost = true; ShowInTaskbar = true; UseLayoutRounding = true; SnapsToDevicePixels = true;

        var root = new Grid { ClipToBounds = true, SnapsToDevicePixels = true };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Grayscale);
        root.SizeChanged += (_, _) => root.Clip = new RectangleGeometry(
            new Rect(0, 0, root.ActualWidth, root.ActualHeight), 28, 28);
        root.Children.Add(CreateBackdrop());
        var frame = new Border {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(28),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(220,255,255,255), Color.FromArgb(85,180,220,255), 90),
            BorderThickness = new Thickness(1.5), Padding = new Thickness(20)
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.08, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
        frame.Child = layout; root.Children.Add(frame); Content = root;

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Tanslator", FontSize = 28, FontWeight = FontWeights.SemiBold,
            Foreground = WhiteBrush, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll };
        title.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) { DragMove(); e.Handled = true; } };
        header.Children.Add(title);
        var minimize = HeaderButton("—", 40); minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        Grid.SetColumn(minimize, 1); header.Children.Add(minimize);
        var close = HeaderButton("×", 40); close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2); header.Children.Add(close);
        Grid.SetRow(header, 0); layout.Children.Add(header);

        var languageRow = new Grid { Margin = new Thickness(0, 4, 0, 12) };
        languageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        languageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        SetupLanguage(targetLang, "简体中文");
        targetLang.Width = 250;
        targetLang.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(targetLang, 1); languageRow.Children.Add(targetLang);
        Grid.SetRow(languageRow, 1); layout.Children.Add(languageRow);

        var inputPanel = new Border { Background = GlassPanel, BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(22), Padding = new Thickness(20,16,20,12),
            Margin = new Thickness(0,0,0,20) };
        var inputGrid = new Grid();
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var inputArea = new Grid();
        StyleEditor(source, false);
        source.FontSize = 17; source.Padding = new Thickness(0); source.Background = Brushes.Transparent;
        source.BorderThickness = new Thickness(0);
        var inputHint = new TextBlock { Text = "请输入要翻译的内容...", FontSize = 18, Foreground = new SolidColorBrush(Color.FromArgb(190,255,255,255)),
            IsHitTestVisible = false, Margin = new Thickness(2,2,48,0), VerticalAlignment = VerticalAlignment.Top };
        source.TextChanged += (_, _) => { inputHint.Visibility = string.IsNullOrEmpty(source.Text) ? Visibility.Visible : Visibility.Collapsed; charCount.Text = $"{source.Text.Length} / 5000"; };
        inputArea.Children.Add(source); inputArea.Children.Add(inputHint);
        var copyInput = IconButton("▢", 38); copyInput.HorizontalAlignment = HorizontalAlignment.Right; copyInput.VerticalAlignment = VerticalAlignment.Top;
        copyInput.Click += (_, _) => CopyText(source.Text);
        inputArea.Children.Add(copyInput);
        inputGrid.Children.Add(inputArea);
        charCount.Text = "0 / 5000"; charCount.FontSize = 14; charCount.Foreground = MutedBrush;
        charCount.HorizontalAlignment = HorizontalAlignment.Right; charCount.Margin = new Thickness(0,8,2,0);
        Grid.SetRow(charCount,1); inputGrid.Children.Add(charCount);
        inputPanel.Child = inputGrid;
        Grid.SetRow(inputPanel,2); layout.Children.Add(inputPanel);

        var outputPanel = new Border { Background = GlassPanel, BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(22), Padding = new Thickness(0),
            Margin = new Thickness(0,0,0,20) };
        var outputGrid = new Grid();
        outputGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        outputGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var outputHeader = new Grid { Margin = new Thickness(24,0,24,0) };
        var outputLabel = new TextBlock { Text = "译文", FontSize = 19, FontWeight = FontWeights.SemiBold,
            Foreground = WhiteBrush, VerticalAlignment = VerticalAlignment.Center };
        outputHeader.Children.Add(outputLabel);
        var copyOutput = IconButton("▢", 38); copyOutput.HorizontalAlignment = HorizontalAlignment.Right;
        copyOutput.VerticalAlignment = VerticalAlignment.Center; copyOutput.Click += (_, _) => CopyText(translated.Text);
        outputHeader.Children.Add(copyOutput);
        outputGrid.Children.Add(outputHeader);
        var outputDivider = new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(55,255,255,255)),
            VerticalAlignment = VerticalAlignment.Bottom };
        outputHeader.Children.Add(outputDivider);
        StyleEditor(translated, true); translated.FontSize = 17; translated.Padding = new Thickness(28,22,28,18);
        translated.Background = Brushes.Transparent; translated.BorderThickness = new Thickness(0);
        translated.ToolTip = "翻译结果将显示在这里...";
        Grid.SetRow(translated,1); outputGrid.Children.Add(translated);
        outputPanel.Child = outputGrid;
        Grid.SetRow(outputPanel,3); layout.Children.Add(outputPanel);

        var footer = new Grid { Margin = new Thickness(0,0,0,0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        translateButton.Content = "翻译  ·  F5"; translateButton.Height = 56; translateButton.Margin = new Thickness(0,0,20,0);
        translateButton.Background = Accent; translateButton.Foreground = Brushes.White;
        translateButton.BorderBrush = new SolidColorBrush(Color.FromArgb(170,100,225,255));
        translateButton.BorderThickness = new Thickness(1.2); translateButton.FontSize = 18; translateButton.FontWeight = FontWeights.Bold;
        translateButton.Cursor = Cursors.Hand; translateButton.Click += async (_, _) => await TranslateAsync();
        translateButton.Template = PillTemplate();
        Grid.SetColumn(translateButton,0); footer.Children.Add(translateButton);

        var hide = PillButton("◉  隐藏窗口 · F4"); hide.Margin = new Thickness(8,0,8,0);
        hide.Click += (_, _) => Hide(); Grid.SetColumn(hide,1); footer.Children.Add(hide);
        var pin = PillButton("◆  置顶"); pin.Margin = new Thickness(20,0,0,0);
        topmost.Content = pin.Content; topmost.IsChecked = true; topmost.Foreground = WhiteBrush;
        topmost.VerticalAlignment = VerticalAlignment.Center; topmost.HorizontalAlignment = HorizontalAlignment.Stretch;
        topmost.Checked += (_, _) => Topmost = true; topmost.Unchecked += (_, _) => Topmost = false;
        pin.Click += (_, _) => topmost.IsChecked = !(topmost.IsChecked ?? false);
        Grid.SetColumn(pin,2); footer.Children.Add(pin);
        Grid.SetRow(footer,4); layout.Children.Add(footer);

        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            hwndSource = HwndSource.FromHwnd(handle); hwndSource?.AddHook(WndProc);
            RegisterHotKey(handle, HotkeyTranslate, NoRepeat, VkF5);
            RegisterHotKey(handle, HotkeyToggle, NoRepeat, VkF4);
        };
        Closed += (_, _) => {
            request?.Cancel(); var handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HotkeyTranslate); UnregisterHotKey(handle, HotkeyToggle);
            hwndSource?.RemoveHook(WndProc);
        };
    }

    UIElement CreateBackdrop()
    {
        // Colorless transparent window background: desktop remains visible through the glass UI.
        return new Grid { Background = Brushes.Transparent, IsHitTestVisible = false };
    }

    static void SetupLanguage(ComboBox box, string selected)
    {
        box.Items.Add("英文"); box.Items.Add("简体中文"); box.SelectedItem = selected;
        box.Width = double.NaN; box.Height = 48; box.FontSize = 17; box.FontWeight = FontWeights.SemiBold;
        box.Foreground = Brushes.White; box.Background = new SolidColorBrush(Color.FromArgb(38,255,255,255));
        box.BorderBrush = new SolidColorBrush(Color.FromArgb(105,240,245,255));
        box.BorderThickness = new Thickness(1.5); box.HorizontalContentAlignment = HorizontalAlignment.Center;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = .18, Color = Colors.White };
    }

    static ControlTemplate PillTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(40));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp); return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    static Button HeaderButton(string text, double size) => new() { Content = text, Width = size, Height = size,
        Margin = new Thickness(8,0,0,0), Background = new SolidColorBrush(Color.FromArgb(30,255,255,255)),
        Foreground = Brushes.White, BorderThickness = new Thickness(0), FontSize = 32, Cursor = Cursors.Hand };

    static Button IconButton(string text, double size) => new() { Content = text, Width = size, Height = size,
        Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new Thickness(0),
        FontSize = 36, Cursor = Cursors.Hand };

    static Button PillButton(string text) => new() { Content = text, Height = 56, Background = GlassPanel,
        BorderBrush = GlassBorder, BorderThickness = new Thickness(1.5), Foreground = Brushes.White,
        FontSize = 16, Cursor = Cursors.Hand, Template = PillTemplate() };

    static void StyleEditor(TextBox box, bool readOnly)
    {
        box.AcceptsReturn = true; box.AcceptsTab = true; box.TextWrapping = TextWrapping.Wrap;
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        box.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        box.FontSize = 20; box.Padding = new Thickness(14,12,14,12);
        box.Foreground = Brushes.White; box.Background = Brushes.Transparent;
        box.BorderThickness = new Thickness(0); box.IsReadOnly = readOnly;
        box.CaretBrush = Brushes.White;
    }

    void CopyText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text)) { Clipboard.SetText(text); status.Text = "已复制"; }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcHitTest && ResizeHitTest(hwnd, lParam, out int hit))
        {
            handled = true;
            return new IntPtr(hit);
        }
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

    bool ResizeHitTest(IntPtr hwnd, IntPtr lParam, out int hit)
    {
        hit = 0;
        if (!GetWindowRect(hwnd, out RECT r)) return false;
        long packed = lParam.ToInt64();
        int x = unchecked((short)(packed & 0xffff));
        int y = unchecked((short)((packed >> 16) & 0xffff));
        const int edge = 9;
        bool left = x >= r.Left && x < r.Left + edge;
        bool right = x < r.Right && x >= r.Right - edge;
        bool top = y >= r.Top && y < r.Top + edge;
        bool bottom = y < r.Bottom && y >= r.Bottom - edge;
        if (top && left) hit = HtTopLeft;
        else if (top && right) hit = HtTopRight;
        else if (bottom && left) hit = HtBottomLeft;
        else if (bottom && right) hit = HtBottomRight;
        else if (left) hit = HtLeft;
        else if (right) hit = HtRight;
        else if (top) hit = HtTop;
        else if (bottom) hit = HtBottom;
        return hit != 0;
    }

    async Task TranslateAsync()
    {
        string input = source.Text.Trim();
        if (input.Length == 0) { status.Text = "请先输入需要翻译的文字。"; return; }
        if (input.Length > 5000) { status.Text = "文本超过 5000 字符，请分段翻译。"; return; }
        request?.Cancel();
        var current = new CancellationTokenSource(); request = current;
        translateButton.IsEnabled = false; status.Text = "正在翻译…";
        string pair = "autodetect|zh-CN";
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