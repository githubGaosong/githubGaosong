using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
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
    readonly ComboBox sourceLang = new();
    readonly ComboBox targetLang = new();
    readonly TextBlock charCount = new();
    readonly TextBlock status = new();
    readonly Button translateButton = new();
    readonly CheckBox topmost = new();
    HwndSource? hwndSource;
    CancellationTokenSource? request;
    Point dragStart;
    bool dragPending;

    static readonly Color White = Color.FromRgb(246, 249, 253);
    static readonly Color Muted = Color.FromRgb(190, 205, 221);
    static readonly Brush WhiteBrush = new SolidColorBrush(Color.FromRgb(250, 250, 255));
    static readonly Brush MutedBrush = new SolidColorBrush(Color.FromArgb(220, 238, 239, 255));
    static readonly Brush GlassPanel = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255));
    static readonly Brush GlassBorder = new SolidColorBrush(Color.FromArgb(90, 238, 245, 255));
    static readonly Brush Accent = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));

    public TranslatorWindow()
    {
        Title = "Translate";
        Width = 540; Height = 460; MinWidth = 180; MinHeight = 120;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Foreground = WhiteBrush; FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 15; Topmost = true; ShowInTaskbar = true; UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Auto);

        var root = new Grid { ClipToBounds = true, SnapsToDevicePixels = true };
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Auto);
        root.SizeChanged += (_, _) => root.Clip = new RectangleGeometry(
            new Rect(0, 0, root.ActualWidth, root.ActualHeight), 26, 26);
        root.Children.Add(CreateBackdrop());
        var frame = new Border {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(26),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(220,255,255,255), Color.FromArgb(85,180,220,255), 90),
            BorderThickness = new Thickness(1.5), Padding = new Thickness(10)
        };
        var layout = new Grid { ClipToBounds = true, SnapsToDevicePixels = true };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.08, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        frame.Child = layout; root.Children.Add(frame); Content = root;
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Translate", FontSize = 18, FontWeight = FontWeights.SemiBold,
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
        languageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetupLanguage(sourceLang, "自动检测");
        SetupLanguage(targetLang, "简体中文");
        sourceLang.Width = 190; targetLang.Width = 190;
        sourceLang.HorizontalAlignment = HorizontalAlignment.Center;
        targetLang.HorizontalAlignment = HorizontalAlignment.Center;
        languageRow.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(sourceLang, 0); languageRow.Children.Add(sourceLang);
        var swap = new TextBlock { Text = "→", FontSize = 25, Foreground = WhiteBrush,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(14, 0, 14, 0) };
        Grid.SetColumn(swap, 1); languageRow.Children.Add(swap);
        Grid.SetColumn(targetLang, 2); languageRow.Children.Add(targetLang);
        Grid.SetRow(languageRow, 1); layout.Children.Add(languageRow);

        var inputPanel = new Border { Background = GlassPanel, BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(22), Padding = new Thickness(20,16,20,12),
            Margin = new Thickness(0,0,0,20) };
        var inputGrid = new Grid();
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var inputArea = new Grid();
        StyleEditor(source, false);
        source.FontSize = 16; source.FontWeight = FontWeights.Medium; source.Padding = new Thickness(0); source.Background = Brushes.Transparent;
        source.BorderThickness = new Thickness(0);
        var inputHint = new TextBlock { Text = "请输入要翻译的内容...", FontSize = 16, Foreground = new SolidColorBrush(Color.FromArgb(190,255,255,255)),
            IsHitTestVisible = false, Margin = new Thickness(2,2,48,0), VerticalAlignment = VerticalAlignment.Top };
        source.TextChanged += (_, _) => { inputHint.Visibility = string.IsNullOrEmpty(source.Text) ? Visibility.Visible : Visibility.Collapsed; charCount.Text = $"{source.Text.Length} / 5000"; };
        inputArea.Children.Add(source); inputArea.Children.Add(inputHint);
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
        outputGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        outputGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var outputHeader = new Grid { Margin = new Thickness(24,0,24,0) };
        var outputLabel = new TextBlock { Text = "译文", FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = WhiteBrush, VerticalAlignment = VerticalAlignment.Center };
        outputHeader.Children.Add(outputLabel);
        outputGrid.Children.Add(outputHeader);
        var outputDivider = new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(55,255,255,255)),
            VerticalAlignment = VerticalAlignment.Bottom };
        outputHeader.Children.Add(outputDivider);
        StyleEditor(translated, true); translated.FontSize = 14; translated.FontWeight = FontWeights.Normal; translated.Padding = new Thickness(14,10,14,10);
        // Match the input editor's 20px panel inset so both vertical scrollbars share one vertical line.
        translated.Margin = new Thickness(0, 0, 20, 0);
        translated.Background = Brushes.Transparent; translated.BorderThickness = new Thickness(0);
        translated.ToolTip = "翻译结果将显示在这里...";
        Grid.SetRow(translated,1); outputGrid.Children.Add(translated);
        outputPanel.Child = outputGrid;
        Grid.SetRow(outputPanel,3); layout.Children.Add(outputPanel);

        var footer = new Grid { Margin = new Thickness(0,0,0,0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        translateButton.Content = "翻译  ·  F5"; translateButton.Height = 42; translateButton.Margin = new Thickness(0,0,12,0);
        translateButton.Background = Accent; translateButton.Foreground = Brushes.White;
        translateButton.BorderBrush = GlassBorder;
        translateButton.BorderThickness = new Thickness(1.2); translateButton.FontSize = 15; translateButton.FontWeight = FontWeights.Bold;
        translateButton.Cursor = Cursors.Hand; translateButton.Click += async (_, _) => await TranslateAsync();
        translateButton.Template = PillTemplate();
        Grid.SetColumn(translateButton,0); footer.Children.Add(translateButton);

        var hide = PillButton("隐藏窗口 · F4"); hide.Margin = new Thickness(5,0,5,0);
        hide.Click += (_, _) => Hide(); Grid.SetColumn(hide,1); footer.Children.Add(hide);
        var pin = PillButton("置顶"); pin.Margin = new Thickness(12,0,0,0);
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

    static bool IsInteractiveElement(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is TextBoxBase || element is ComboBox || element is ComboBoxItem ||
                element is ButtonBase || element is ScrollBar || element is Slider ||
                element is ScrollViewer || element is PasswordBox)
                return true;

            DependencyObject? parent = null;
            if (element is Visual || element is Visual3D)
                parent = VisualTreeHelper.GetParent(element);
            parent ??= LogicalTreeHelper.GetParent(element);
            element = parent;
        }
        return false;
    }

    UIElement CreateBackdrop()
    {
        // Colorless transparent window background: desktop remains visible through the glass UI.
        return new Grid { Background = new SolidColorBrush(Color.FromArgb(58, 10, 17, 29)), IsHitTestVisible = false };
    }

    static readonly (string Name, string Code)[] Languages =
    {
        ("自动检测", "autodetect"), ("英语 · English", "en"), ("简体中文", "zh-CN"), ("繁體中文", "zh-TW"),
        ("日语 · 日本語", "ja"), ("韩语 · 한국어", "ko"), ("法语 · Français", "fr"), ("德语 · Deutsch", "de"),
        ("西班牙语 · Español", "es"), ("意大利语 · Italiano", "it"), ("葡萄牙语 · Português", "pt"), ("俄语 · Русский", "ru"),
        ("阿拉伯语 · العربية", "ar"), ("泰语 · ไทย", "th"), ("越南语 · Tiếng Việt", "vi"), ("荷兰语 · Nederlands", "nl"),
        ("波兰语 · Polski", "pl"), ("土耳其语 · Türkçe", "tr"), ("印尼语 · Bahasa Indonesia", "id"), ("印地语 · हिन्दी", "hi"),
        ("孟加拉语 · বাংলা", "bn"), ("乌尔都语 · اردو", "ur"), ("波斯语 · فارسی", "fa"), ("希伯来语 · עברית", "he"),
        ("希腊语 · Ελληνικά", "el"), ("瑞典语 · Svenska", "sv"), ("挪威语 · Norsk", "no"), ("丹麦语 · Dansk", "da"),
        ("芬兰语 · Suomi", "fi"), ("捷克语 · Čeština", "cs"), ("斯洛伐克语 · Slovenčina", "sk"), ("匈牙利语 · Magyar", "hu"),
        ("罗马尼亚语 · Română", "ro"), ("保加利亚语 · Български", "bg"), ("乌克兰语 · Українська", "uk"), ("塞尔维亚语 · Српски", "sr"),
        ("克罗地亚语 · Hrvatski", "hr"), ("斯洛文尼亚语 · Slovenščina", "sl"), ("立陶宛语 · Lietuvių", "lt"), ("拉脱维亚语 · Latviešu", "lv"),
        ("爱沙尼亚语 · Eesti", "et"), ("马来语 · Bahasa Melayu", "ms"), ("菲律宾语 · Filipino", "tl"), ("斯瓦希里语 · Kiswahili", "sw"),
        ("南非荷兰语 · Afrikaans", "af"), ("冰岛语 · Íslenska", "is"), ("爱尔兰语 · Gaeilge", "ga"), ("加泰罗尼亚语 · Català", "ca"),
        ("巴斯克语 · Euskara", "eu"), ("加利西亚语 · Galego", "gl"), ("亚美尼亚语 · Հայերեն", "hy"), ("格鲁吉亚语 · ქართული", "ka"),
        ("阿塞拜疆语 · Azərbaycan", "az"), ("哈萨克语 · Қазақша", "kk"), ("乌兹别克语 · Oʻzbek", "uz"), ("蒙古语 · Монгол", "mn"),
        ("尼泊尔语 · नेपाली", "ne"), ("僧伽罗语 · සිංහල", "si"), ("高棉语 · ខ្មែរ", "km"), ("缅甸语 · မြန်မာ", "my"),
        ("老挝语 · ລາວ", "lo"), ("马拉地语 · मराठी", "mr"), ("泰米尔语 · தமிழ்", "ta"), ("泰卢固语 · తెలుగు", "te"),
        ("古吉拉特语 · ગુજરાતી", "gu"), ("旁遮普语 · ਪੰਜਾਬੀ", "pa"), ("约鲁巴语 · Yorùbá", "yo"), ("豪萨语 · Hausa", "ha"),
        ("阿姆哈拉语 · አማርኛ", "am")
    };

    static void SetupLanguage(ComboBox box, string selected)
    {
        foreach (var language in Languages) box.Items.Add(language.Name);
        box.SelectedItem = selected == "自动检测" ? Languages[0].Name : Languages[2].Name;
        box.Width = double.NaN; box.Height = 42; box.FontSize = 13; box.FontWeight = FontWeights.SemiBold;
        box.Foreground = Brushes.White; box.Background = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255));
        box.BorderBrush = new SolidColorBrush(Color.FromArgb(115, 225, 238, 255));
        box.BorderThickness = new Thickness(1.2); box.HorizontalContentAlignment = HorizontalAlignment.Center;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.16, Color = Colors.Black };
        box.SnapsToDevicePixels = true; box.Focusable = true; box.IsHitTestVisible = true;
        box.Padding = new Thickness(10, 0, 28, 0);
        box.Template = LanguageComboTemplate();
        box.ItemContainerStyle = new Style(typeof(ComboBoxItem))
        {
            Setters =
            {
                new Setter(Control.ForegroundProperty, Brushes.White),
                new Setter(Control.BackgroundProperty, Brushes.Transparent),
                new Setter(Control.PaddingProperty, new Thickness(14, 10, 14, 10)),
                new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left),
                new Setter(Control.FontSizeProperty, 13.0),
                new Setter(Control.FontWeightProperty, FontWeights.Medium),
                new Setter(Control.TemplateProperty, LanguageItemTemplate())
            }
        };
    }

    static ControlTemplate LanguageComboTemplate()
    {
        // Keep the ToggleButton's visual tree inside its own ControlTemplate.
        // Previously its Border was appended as a child of ToggleButton itself,
        // which can prevent the dropdown toggle from receiving clicks reliably.
        var grid = new FrameworkElementFactory(typeof(Grid));

        var toggle = new FrameworkElementFactory(typeof(ToggleButton));
        toggle.SetValue(ToggleButton.FocusableProperty, false);
        toggle.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        var toggleBorder = new FrameworkElementFactory(typeof(Border));
        toggleBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(20));
        toggleBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        toggle.SetValue(Control.TemplateProperty, new ControlTemplate(typeof(ToggleButton))
        {
            VisualTree = toggleBorder
        });
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(nameof(ComboBox.IsDropDownOpen))
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent,
            Mode = System.Windows.Data.BindingMode.TwoWay
        });
        grid.AppendChild(toggle);

        var selected = new FrameworkElementFactory(typeof(ContentPresenter));
        selected.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ComboBox.SelectionBoxItemProperty));
        selected.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        selected.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        selected.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 22, 0));
        selected.SetValue(UIElement.IsHitTestVisibleProperty, false);
        grid.AppendChild(selected);

        var arrow = new FrameworkElementFactory(typeof(TextBlock));
        arrow.SetValue(TextBlock.TextProperty, "⌄");
        arrow.SetValue(TextBlock.FontSizeProperty, 17.0);
        arrow.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        arrow.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromArgb(225, 245, 249, 255)));
        arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 11, 3));
        arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
        grid.AppendChild(arrow);

        var popup = new FrameworkElementFactory(typeof(Popup));
        popup.Name = "PART_Popup";
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.FocusableProperty, false);
        popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Fade);
        popup.SetBinding(Popup.PlacementTargetProperty, new System.Windows.Data.Binding
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        popup.SetBinding(Popup.IsOpenProperty, new System.Windows.Data.Binding(nameof(ComboBox.IsDropDownOpen))
        {
            RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent
        });
        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(248, 24, 31, 44)));
        popupBorder.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(210, 205, 220, 242)));
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
        popupBorder.SetValue(Border.MarginProperty, new Thickness(0, 6, 0, 0));
        var popupScroll = new FrameworkElementFactory(typeof(ScrollViewer));
        popupScroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        popupScroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        popupScroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        popupScroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        popupBorder.AppendChild(popupScroll);
        popup.AppendChild(popupBorder);
        grid.AppendChild(popup);

        return new ControlTemplate(typeof(ComboBox)) { VisualTree = grid };
    }

    static ControlTemplate LanguageItemTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "headerSurface";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
        border.SetValue(Border.RenderTransformProperty, new ScaleTransform(1, 1));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = border };
        var hover = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(65, 150, 185, 230))));
        template.Triggers.Add(hover);
        return template;
    }

    static ControlTemplate PillTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "surface";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(40));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
        border.SetValue(UIElement.RenderTransformProperty, new ScaleTransform(1, 1));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(68, 255, 255, 255))));
        hover.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(150, 255, 255, 255))));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(145, 255, 255, 255))));
        pressed.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(220, 255, 255, 255))));
        pressed.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.78));
        pressed.Setters.Add(new Setter(Border.RenderTransformProperty, new ScaleTransform(0.96, 0.96)) { TargetName = "surface" });
        template.Triggers.Add(pressed);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
        template.Triggers.Add(disabled);
        return template;
    }

    static Button HeaderButton(string text, double size)
    {
        return new Button {
            Content = text == "—" ? "−" : "×", Width = 34, Height = 34,
            Margin = new Thickness(6, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
            BorderThickness = new Thickness(1), FontFamily = new FontFamily("Segoe UI"),
            FontSize = text == "—" ? 17 : 16, FontWeight = FontWeights.Normal,
            Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
            Focusable = false, Template = HeaderButtonTemplate()
        };
    }

    static ControlTemplate HeaderButtonTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "headerSurface";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
        border.SetValue(Border.RenderTransformProperty, new ScaleTransform(1, 1));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(65, 255, 255, 255))));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(105, 255, 255, 255))));
        pressed.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
        pressed.Setters.Add(new Setter(Border.RenderTransformProperty, new ScaleTransform(0.94, 0.94)) { TargetName = "headerSurface" });
        template.Triggers.Add(pressed);
        return template;
    }

    static Button PillButton(string text) => new() { Content = text, Height = 42, Background = GlassPanel,
        BorderBrush = GlassBorder, BorderThickness = new Thickness(1.2), Foreground = Brushes.White,
        FontSize = 14, FontWeight = FontWeights.Medium, Cursor = Cursors.Hand, Template = PillTemplate() };

    static void StyleEditor(TextBox box, bool readOnly)
    {
        box.AcceptsReturn = true; box.AcceptsTab = true; box.TextWrapping = TextWrapping.Wrap;
        box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        box.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        box.FontSize = 17; box.FontWeight = FontWeights.Medium; box.Padding = new Thickness(10,8,10,8);
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
        // Larger corner zones make diagonal resizing easier to grab.
        const int edge = 30;
        const int corner = 48;
        bool left = x >= r.Left && x < r.Left + edge;
        bool right = x < r.Right && x >= r.Right - edge;
        bool top = y >= r.Top && y < r.Top + edge;
        bool bottom = y < r.Bottom && y >= r.Bottom - edge;
        bool nearLeft = x >= r.Left && x < r.Left + corner;
        bool nearRight = x < r.Right && x >= r.Right - corner;
        bool nearTop = y >= r.Top && y < r.Top + corner;
        bool nearBottom = y < r.Bottom && y >= r.Bottom - corner;
        if (nearTop && nearLeft) hit = HtTopLeft;
        else if (nearTop && nearRight) hit = HtTopRight;
        else if (nearBottom && nearLeft) hit = HtBottomLeft;
        else if (nearBottom && nearRight) hit = HtBottomRight;
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
        string pair = $"{LanguageCode(sourceLang.SelectedItem?.ToString(), true)}|{LanguageCode(targetLang.SelectedItem?.ToString(), false)}";
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

    static string LanguageCode(string? name, bool source)
    {
        foreach (var language in Languages)
            if (language.Name == name)
                return language.Code;
        return source ? "autodetect" : "zh-CN";
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