using System.Runtime.InteropServices;
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
using Microsoft.Win32;



sealed class TranslateWindow : Window
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

    readonly TextBox source = new();
    readonly TextBox translated = new();
    readonly ComboBox sourceLang = new();
    readonly ComboBox targetLang = new();
    readonly ComboBox backgroundChoice = new();
    Grid? backdropLayer;
    ImageBrush? uploadedBackground;
    TextBlock? titleText, inputHint, outputLabel;
    Button? hideButton, pinButton;
    string currentBackground = "半透明玻璃 · 默认";
    readonly TextBlock charCount = new();
    readonly TextBlock status = new();
    readonly Button translateButton = new();
    readonly CheckBox topmost = new();
    HwndSource? hwndSource;
    CancellationTokenSource? request;
    Point dragStart;
    bool dragPending;

    static readonly Brush WhiteBrush = new SolidColorBrush(Color.FromRgb(250, 250, 255));
    static readonly Brush MutedBrush = new SolidColorBrush(Color.FromArgb(220, 238, 239, 255));
    static readonly Brush GlassPanel = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255));
    static readonly Brush GlassBorder = new SolidColorBrush(Color.FromArgb(90, 238, 245, 255));

    public TranslateWindow()
    {
        Title = "Translate";
        Width = 540; Height = 460; MinWidth = 420; MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Foreground = WhiteBrush; FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 15; Topmost = true; ShowInTaskbar = true; UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Auto);

        var root = new Grid { ClipToBounds = true, SnapsToDevicePixels = true };
        // Drag the window by pressing and moving on any non-interactive surface.
        // Editors, selectors, scrollbars and buttons retain their normal behavior.
        root.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsInteractiveElement(e.OriginalSource as DependencyObject))
            {
                dragPending = false;
                return;
            }
            dragStart = e.GetPosition(this);
            dragPending = true;
        };
        root.PreviewMouseMove += (_, e) =>
        {
            if (!dragPending || e.LeftButton != MouseButtonState.Pressed) return;
            Point current = e.GetPosition(this);
            if (Math.Abs(current.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(current.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            dragPending = false;
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        root.PreviewMouseLeftButtonUp += (_, _) => dragPending = false;
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Auto);
        root.SizeChanged += (_, _) => root.Clip = new RectangleGeometry(
            new Rect(0, 0, root.ActualWidth, root.ActualHeight), 26, 26);
        root.Children.Add(CreateBackdrop());
        var frame = new Border {
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(26),
            BorderBrush = new LinearGradientBrush(Color.FromArgb(220,255,255,255), Color.FromArgb(85,180,220,255), 90),
            BorderThickness = new Thickness(1.5), Padding = new Thickness(18, 16, 18, 14)
        };
        var layout = new Grid { ClipToBounds = false, SnapsToDevicePixels = true };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.08, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
        frame.Child = layout; root.Children.Add(frame); Content = root;
        var header = new Grid { Margin = new Thickness(8, 0, 4, 0), ClipToBounds = false };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Translate", FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = WhiteBrush, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.SizeAll };
        title.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) { DragMove(); e.Handled = true; } };
        titleText = title;
        header.Children.Add(title);
        SetupBackgroundSelector();
        backgroundChoice.Margin = new Thickness(0, 0, 0, 0);
        Grid.SetColumn(backgroundChoice, 1); header.Children.Add(backgroundChoice);
        var minimize = HeaderButton("—", 40); minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        Grid.SetColumn(minimize, 2); header.Children.Add(minimize);
        var close = HeaderButton("×", 40); close.Click += (_, _) => Close();
        Grid.SetColumn(close, 3); header.Children.Add(close);
        Grid.SetRow(header, 0); layout.Children.Add(header);

        var languageRow = new Grid { Margin = new Thickness(8, 3, 8, 8), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = false, MinHeight = 48 };
        languageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        languageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        languageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        SetupLanguage(sourceLang, "自动检测");
        SetupLanguage(targetLang, "简体中文");
        // Responsive language selectors: use the available width instead of fixed widths.
        sourceLang.Width = double.NaN; targetLang.Width = double.NaN;
        sourceLang.HorizontalAlignment = HorizontalAlignment.Stretch;
        targetLang.HorizontalAlignment = HorizontalAlignment.Stretch;
        sourceLang.Margin = new Thickness(0, 2, 0, 2);
        targetLang.Margin = new Thickness(0, 2, 0, 2);
        sourceLang.Height = 42; targetLang.Height = 42; sourceLang.MinWidth = 0; targetLang.MinWidth = 0;
        sourceLang.VerticalAlignment = VerticalAlignment.Center;
        targetLang.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(sourceLang, 0); languageRow.Children.Add(sourceLang);
        var swap = new TextBlock { Text = "→", FontSize = 20, Foreground = WhiteBrush,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0) };
        Grid.SetColumn(swap, 1); languageRow.Children.Add(swap);
        targetLang.SelectionChanged += (_, _) => UpdateUiLanguage();
        Grid.SetColumn(targetLang, 2); languageRow.Children.Add(targetLang);
        Grid.SetRow(languageRow, 1); layout.Children.Add(languageRow);

        var inputPanel = new Border { Background = GlassPanel, BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1.2), CornerRadius = new CornerRadius(22), Padding = new Thickness(20,16,20,12),
            Margin = new Thickness(0,0,0,14) };
        var inputGrid = new Grid();
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        inputGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var inputArea = new Grid();
        StyleEditor(source, false);
        source.FontSize = 16; source.FontWeight = FontWeights.Medium; source.Padding = new Thickness(0); source.Background = Brushes.Transparent;
        source.BorderThickness = new Thickness(0);
        inputHint = new TextBlock { Text = "请输入要翻译的内容...", FontSize = 16, Foreground = new SolidColorBrush(Color.FromArgb(190,255,255,255)),
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
            Margin = new Thickness(0,0,0,14) };
        var outputGrid = new Grid();
        outputGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        outputGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var outputHeader = new Grid { Margin = new Thickness(24,0,24,0) };
        outputHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outputHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var outputLabel = new TextBlock { Text = "译文", FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = WhiteBrush, VerticalAlignment = VerticalAlignment.Center };
        this.outputLabel = outputLabel;
        Grid.SetColumn(outputLabel, 0); outputHeader.Children.Add(outputLabel);
        var clearTranslation = PillButton("清空");
        clearTranslation.Width = 54; clearTranslation.Height = 28; clearTranslation.FontSize = 12;
        clearTranslation.Margin = new Thickness(8, 0, 0, 0);
        clearTranslation.ToolTip = "清空译文";
        clearTranslation.Click += (_, _) => { translated.Clear(); translated.Focus(); };
        Grid.SetColumn(clearTranslation, 1); outputHeader.Children.Add(clearTranslation);
        outputGrid.Children.Add(outputHeader);
        var outputDivider = new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(55,255,255,255)),
            VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetColumnSpan(outputDivider, 2); outputHeader.Children.Add(outputDivider);
        StyleEditor(translated, false); translated.IsReadOnly = false; translated.IsEnabled = true; translated.SelectionBrush = new SolidColorBrush(Color.FromArgb(150, 90, 170, 255)); translated.FontSize = 16; translated.FontWeight = FontWeights.Normal; translated.Padding = new Thickness(14,10,14,10);
        // Match the input editor's 20px panel inset so both vertical scrollbars share one vertical line.
        translated.Margin = new Thickness(0, 0, 20, 0);
        translated.Background = Brushes.Transparent; translated.BorderThickness = new Thickness(0);
        translated.ToolTip = "可直接编辑译文；支持选中、删除、替换和补充文字。";
        Grid.SetRow(translated,1); outputGrid.Children.Add(translated);
        outputPanel.Child = outputGrid;
        Grid.SetRow(outputPanel,3); layout.Children.Add(outputPanel);

        var footer = new Grid { Margin = new Thickness(0,0,0,0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        translateButton.Content = "翻译  ·  F5"; translateButton.Height = 44; translateButton.Margin = new Thickness(0,0,8,0);
        translateButton.Background = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255));
        translateButton.Foreground = Brushes.White;
        translateButton.BorderBrush = GlassBorder;
        translateButton.BorderThickness = new Thickness(1.1); translateButton.FontSize = 13.5; translateButton.FontWeight = FontWeights.SemiBold;
        translateButton.Cursor = Cursors.Hand; translateButton.Click += async (_, _) => await TranslateAsync();
        translateButton.Template = PillTemplate();
        Grid.SetColumn(translateButton,0); footer.Children.Add(translateButton);

        var hide = PillButton("隐藏窗口  ·  F4"); hideButton = hide; hide.Height = 44; hide.FontSize = 13.5; hide.FontWeight = FontWeights.SemiBold; hide.Margin = new Thickness(4,0,4,0);
        hide.Background = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255));
        hide.BorderBrush = GlassBorder;
        hide.Click += (_, _) => Hide(); Grid.SetColumn(hide,1); footer.Children.Add(hide);
        var pin = PillButton("置顶"); pinButton = pin; pin.Height = 44; pin.Margin = new Thickness(8,0,0,0);
        pin.Background = new SolidColorBrush(Color.FromArgb(72, 255, 255, 255));
        pin.BorderBrush = GlassBorder;
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
        backdropLayer = new Grid { IsHitTestVisible = false };
        ApplyBackground("半透明玻璃 · 默认");
        return backdropLayer;
    }

    void SetupBackgroundSelector()
    {
        backgroundChoice.Items.Add("半透明玻璃 · 默认");
        backgroundChoice.Items.Add("彩虹渐变");
        backgroundChoice.Items.Add("冰透白");
        backgroundChoice.Items.Add("雾霭蓝");
        backgroundChoice.Items.Add("暮光紫");
        backgroundChoice.Items.Add("深海蓝");
        backgroundChoice.Items.Add("极光紫");
        backgroundChoice.Items.Add("樱花粉");
        backgroundChoice.Items.Add("薄荷绿");
        backgroundChoice.Items.Add("暖阳橙");
        backgroundChoice.Items.Add("石墨黑");
        backgroundChoice.Items.Add("海盐青");
        backgroundChoice.Items.Add("暮色玫瑰");
        backgroundChoice.Items.Add("冰川蓝");
        backgroundChoice.Items.Add("薰衣草");
        backgroundChoice.Items.Add("森林绿");
        backgroundChoice.Items.Add("奶油黄");
        backgroundChoice.Items.Add("上传图片…");
        backgroundChoice.SelectedIndex = 0;
        backgroundChoice.Width = 104;
        backgroundChoice.Height = 28;
        backgroundChoice.FontSize = 10;
        backgroundChoice.FontWeight = FontWeights.SemiBold;
        backgroundChoice.Foreground = Brushes.White;
        backgroundChoice.Background = new LinearGradientBrush(Color.FromArgb(92, 92, 158, 205), Color.FromArgb(62, 145, 112, 190), 25);
        backgroundChoice.BorderBrush = new SolidColorBrush(Color.FromArgb(205, 180, 225, 255));
        backgroundChoice.BorderThickness = new Thickness(1.3);
        backgroundChoice.Padding = new Thickness(8, 0, 8, 0);
        backgroundChoice.VerticalContentAlignment = VerticalAlignment.Center;
        backgroundChoice.HorizontalContentAlignment = HorizontalAlignment.Center;
        backgroundChoice.Effect = new DropShadowEffect { BlurRadius = 9, ShadowDepth = 1, Opacity = 0.14, Color = Colors.Black };
        backgroundChoice.Template = LanguageComboTemplate();
        backgroundChoice.ItemContainerStyle = new Style(typeof(ComboBoxItem))
        {
            Setters =
            {
                new Setter(Control.ForegroundProperty, Brushes.White),
                new Setter(Control.BackgroundProperty, Brushes.Transparent),
                new Setter(Control.PaddingProperty, new Thickness(14, 10, 14, 10)),
                new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(Control.FontSizeProperty, 12.0),
                new Setter(Control.TemplateProperty, LanguageItemTemplate())
            }
        };
        backgroundChoice.ToolTip = "选择窗口背景颜色";
        backgroundChoice.SelectionChanged += (_, _) =>
        {
            string choice = backgroundChoice.SelectedItem?.ToString() ?? "半透明玻璃 · 默认";
            if (choice == "上传图片…") { ChooseBackgroundImage(); return; }
            currentBackground = choice;
            ApplyBackground(choice);
        };
    }

    void ApplyBackground(string choice)
    {
        if (backdropLayer == null) return;
        Brush brush = choice switch
        {
            "半透明玻璃 · 默认" => new LinearGradientBrush(Color.FromArgb(112, 225, 240, 255), Color.FromArgb(82, 125, 165, 205), 35),
            "冰透白" => new LinearGradientBrush(Color.FromArgb(118, 245, 250, 255), Color.FromArgb(78, 170, 205, 235), 35),
            "雾霭蓝" => new LinearGradientBrush(Color.FromArgb(138, 70, 125, 165), Color.FromArgb(92, 125, 185, 210), 35),
            "暮光紫" => new LinearGradientBrush(Color.FromArgb(145, 90, 65, 145), Color.FromArgb(95, 170, 115, 185), 35),
            "深海蓝" => new LinearGradientBrush(Color.FromArgb(235, 8, 35, 75), Color.FromArgb(235, 20, 105, 145), 35),
            "极光紫" => new LinearGradientBrush(Color.FromArgb(235, 45, 25, 90), Color.FromArgb(235, 135, 65, 155), 35),
            "樱花粉" => new LinearGradientBrush(Color.FromArgb(235, 100, 38, 72), Color.FromArgb(235, 220, 125, 165), 35),
            "薄荷绿" => new LinearGradientBrush(Color.FromArgb(235, 15, 70, 65), Color.FromArgb(235, 65, 155, 125), 35),
            "暖阳橙" => new LinearGradientBrush(Color.FromArgb(235, 100, 45, 18), Color.FromArgb(235, 220, 135, 55), 35),
            "石墨黑" => new LinearGradientBrush(Color.FromArgb(242, 18, 21, 29), Color.FromArgb(242, 58, 65, 78), 35),
            "海盐青" => new LinearGradientBrush(Color.FromArgb(238, 10, 75, 82), Color.FromArgb(238, 75, 170, 160), 35),
            "暮色玫瑰" => new LinearGradientBrush(Color.FromArgb(238, 75, 35, 65), Color.FromArgb(238, 190, 100, 130), 35),
            "冰川蓝" => new LinearGradientBrush(Color.FromArgb(238, 35, 85, 130), Color.FromArgb(238, 145, 205, 225), 35),
            "薰衣草" => new LinearGradientBrush(Color.FromArgb(238, 80, 65, 125), Color.FromArgb(238, 175, 145, 210), 35),
            "森林绿" => new LinearGradientBrush(Color.FromArgb(238, 20, 65, 48), Color.FromArgb(238, 90, 145, 95), 35),
            "奶油黄" => new LinearGradientBrush(Color.FromArgb(238, 115, 85, 35), Color.FromArgb(238, 225, 195, 115), 35),
            _ => new LinearGradientBrush(new GradientStopCollection {
                new GradientStop(Color.FromArgb(235, 30, 65, 125), 0.0),
                new GradientStop(Color.FromArgb(235, 115, 55, 145), 0.34),
                new GradientStop(Color.FromArgb(235, 35, 130, 145), 0.68),
                new GradientStop(Color.FromArgb(235, 180, 90, 120), 1.0)
            }, 35)
        };
        backdropLayer.Background = uploadedBackground != null && choice == "用户图片" ? uploadedBackground : brush;
        // Keep the compact background selector visually tied to the active theme.
        if (backgroundChoice != null)
        {
            backgroundChoice.Background = new LinearGradientBrush(
                Color.FromArgb(92, 255, 255, 255),
                Color.FromArgb(48, 255, 255, 255), 35);
            backgroundChoice.BorderBrush = new SolidColorBrush(Color.FromArgb(150, 245, 248, 255));
            backgroundChoice.Foreground = Brushes.White;
        }
    }

    void ChooseBackgroundImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            int restore = backgroundChoice.Items.IndexOf(currentBackground);
            backgroundChoice.SelectedIndex = restore >= 0 ? restore : 0;
            return;
        }
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(dialog.FileName);
            bitmap.EndInit();
            bitmap.Freeze();
            uploadedBackground = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            currentBackground = "用户图片";
            backdropLayer!.Background = uploadedBackground;
            backgroundChoice.SelectedItem = "上传图片…";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "无法加载图片：" + ex.Message, "背景图片", MessageBoxButton.OK, MessageBoxImage.Warning);
            backgroundChoice.SelectedIndex = 0;
        }
    }

    void UpdateUiLanguage()
    {
        string code = LanguageCatalog.Languages.FirstOrDefault(x => x.Name == targetLang.SelectedItem?.ToString()).Code ?? "en";
        var words = LanguageCatalog.UiWords.TryGetValue(code, out var localized) ? localized : LanguageCatalog.UiWords["en"];
        Title = words[0];
        if (titleText != null) titleText.Text = words[0];
        if (inputHint != null) inputHint.Text = words[1];
        if (outputLabel != null) outputLabel.Text = words[2];
        translateButton.Content = words[3];
        if (hideButton != null) hideButton.Content = words[4];
        if (pinButton != null) pinButton.Content = words[5];
        charCount.ToolTip = words[6];
        backgroundChoice.ToolTip = words[7];
    }
}
