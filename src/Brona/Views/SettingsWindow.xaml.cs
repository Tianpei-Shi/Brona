using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brona.Models;
using Brona.Services;
using Microsoft.Win32;

namespace Brona.Views;

/// <summary>
/// 设置窗口：注册为默认浏览器、管理“我的浏览器”、外观与语言、测试弹窗。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<BrowserEntry> _mine = [];
    private readonly ObservableCollection<BrowserEntry> _detected = [];
    private AppConfig _config = new();
    private DispatcherTimer? _saveTimer;
    private bool _loading; // 正在把配置填入界面，此时控件事件不触发保存

    // 拖拽排序状态
    private BrowserEntry? _dragItem;
    private Point _dragStart;
    private bool _dragging;

    public SettingsWindow()
    {
        InitializeComponent();
        FlowDirection = Loc.Direction;
        MyList.ItemsSource = _mine;
        DetectedList.ItemsSource = _detected;

        SourceInitialized += (_, _) =>
        {
            ThemeHelper.ApplyTitleBar(this);
            ThemeHelper.ApplyRoundCorners(this);
        };
        // 从系统设置切回来时刷新状态
        Activated += (_, _) => RefreshStatus();
        StateChanged += (_, _) => OnStateChanged();
        Loc.Changed += OnLanguageChanged;
        Closed += (_, _) => Loc.Changed -= OnLanguageChanged;

        LoadBrowsers();
        RefreshTexts();
        ExePathText.Text = Environment.ProcessPath;
    }

    // ================= 窗口外框 =================

    /// <summary>侧边栏切换页面，带淡入上移动画</summary>
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageHome is null) return; // 初始化期间 XAML 尚未加载完

        PageHome.Visibility = NavHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageBrowsers.Visibility = NavBrowsers.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageAppearance.Visibility = NavAppearance.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = NavAbout.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        PageShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = ease });
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>最大化时内容会超出屏幕边缘，需要补上边距；同时切换按钮图标</summary>
    private void OnStateChanged()
    {
        bool max = WindowState == WindowState.Maximized;
        RootGrid.Margin = max ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = max ? "" : "";
        MaxButton.SetResourceReference(ToolTipProperty, max ? "S.Window.Restore" : "S.Window.Maximize");
    }

    // ================= 语言 =================

    /// <summary>刷新代码中设置的文字（XAML 中的文字由 DynamicResource 自动更新）</summary>
    private void RefreshTexts()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        VersionText.Text = $"v{version}";
        AboutVersionText.Text = Loc.Format("S.About.Version", version) + "  ·  " + Loc.Get("S.About.Tagline");
        RefreshStatus();
        FillLanguageBox();
        FillPrivateTriggerBox();
    }

    private void OnLanguageChanged()
    {
        FlowDirection = Loc.Direction;
        RefreshTexts();
    }

    /// <summary>语言下拉框：第一项“跟随系统”，其余为各语言的本地名称</summary>
    private void FillLanguageBox()
    {
        _loading = true;
        LanguageBox.Items.Clear();
        LanguageBox.Items.Add(new ComboBoxItem { Content = Loc.Get("S.Appearance.FollowSystem"), Tag = Loc.Auto });
        foreach (var (code, name, _) in Loc.Supported)
            LanguageBox.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == _config.Language) ?? LanguageBox.Items[0];
        _loading = false;
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: string code }) return;
        _config.Language = code;
        SaveMine();
        // 延后一帧切换，避免在下拉框事件中重建它自己的选项
        Dispatcher.BeginInvoke(() => Loc.Apply(code));
    }

    // ================= 系统注册 =================

    /// <summary>根据注册状态更新概览页</summary>
    private void RefreshStatus()
    {
        bool registered = RegistrationService.IsRegistered();
        bool isDefault = registered && RegistrationService.IsDefault();

        (string brush, string state) = (registered, isDefault) switch
        {
            (true, true) => ("SuccessBrush", "Ready"),
            (true, false) => ("WarningBrush", "Pending"),
            _ => ("MutedBrush", "None"),
        };

        StatusDot.Fill = (Brush)FindResource(brush);
        StatusTitle.Text = Loc.Get($"S.Status.{state}.Title");
        StatusDesc.Text = Loc.Get($"S.Status.{state}.Desc");

        // 每个状态只突出一个主操作
        RegisterButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
        RegisterButton.Content = Loc.Get(registered ? "S.Home.GoDefault" : "S.Home.Register");
        OpenDefaultsButton.Visibility = isDefault ? Visibility.Visible : Visibility.Collapsed;
        UnregisterButton.IsEnabled = registered;
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!RegistrationService.IsRegistered()) RegistrationService.Register();
            RefreshStatus();
            // Windows 10/11 不允许程序直接改默认浏览器，需要用户在系统设置中确认
            RegistrationService.OpenDefaultAppsSettings();
        }
        catch (Exception ex)
        {
            ShowError("S.Error.Register", ex);
        }
    }

    private void OpenDefaults_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RegistrationService.OpenDefaultAppsSettings();
        }
        catch (Exception ex)
        {
            ShowError("S.Error.OpenSettings", ex);
        }
    }

    private void Unregister_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this, Loc.Get("S.About.UnregisterConfirm"), Loc.Get("S.About.Unregister"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            RegistrationService.Unregister();
            ConfigStore.Delete();
            LoadBrowsers(autoSave: false); // 取消注册后不再写入配置
            RefreshTexts();
        }
        catch (Exception ex)
        {
            ShowError("S.Error.Unregister", ex);
        }
    }

    // ================= 我的浏览器 =================

    /// <summary>加载配置、“我的浏览器”与自动检测结果</summary>
    private void LoadBrowsers(bool autoSave = true)
    {
        _config = ConfigStore.Load();
        _mine.Clear();
        foreach (var b in _config.Browsers) _mine.Add(WithIcon(b));
        LoadAppearance();

        _detected.Clear();
        foreach (var b in BrowserDetector.Detect()) _detected.Add(WithIcon(b));

        // 首次使用：自动把检测结果保存为用户的选择
        if (autoSave && !ConfigStore.Exists && _mine.Count > 0) SaveMine();
        else RefreshLists();
    }

    /// <summary>保存并刷新界面</summary>
    private void SaveMine()
    {
        try
        {
            _config.Browsers = _mine.ToList();
            ConfigStore.Save(_config);
        }
        catch (Exception ex)
        {
            ShowError("S.Error.Save", ex);
        }
        RefreshLists();
    }

    /// <summary>刷新序号、过滤已添加的检测项、更新空状态</summary>
    private void RefreshLists()
    {
        for (int i = 0; i < _mine.Count; i++) _mine[i].Shortcut = i < 9 ? (i + 1).ToString() : "·";
        MyList.Items.Refresh();

        DetectedList.ItemsSource = _detected.Where(d => !_mine.Any(m => m.SameExe(d))).ToList();
        MyEmptyText.Visibility = _mine.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetectedEmptyText.Visibility = DetectedList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddDetected_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BrowserEntry b } || _mine.Any(m => m.SameExe(b))) return;
        _mine.Add(b);
        SaveMine();
    }

    /// <summary>手动选择一个浏览器 exe 添加</summary>
    private void AddManual_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.Get("S.Browsers.PickExe"),
            Filter = Loc.Get("S.Browsers.ExeFilter") + " (*.exe)|*.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dialog.ShowDialog(this) != true) return;

        var path = dialog.FileName;
        if (string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, Loc.Get("S.Browsers.CannotAddSelf"), "Brona", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var entry = WithIcon(BrowserDetector.FromExe(path));
        if (_mine.Any(m => m.SameExe(entry))) return;
        _mine.Add(entry);
        SaveMine();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BrowserEntry b } && _mine.Remove(b)) SaveMine();
    }

    // ================= 拖拽排序 =================

    /// <summary>按下：只有按在左侧六点手柄上才开始拖动</summary>
    private void MyList_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragItem = null;
        if (FindNamed(e.OriginalSource as DependencyObject, "Grip") is not FrameworkElement grip) return;
        _dragItem = grip.DataContext as BrowserEntry;
        _dragStart = e.GetPosition(MyList);
    }

    /// <summary>移动：超过系统拖动阈值后开始拖拽，并实时把该行移到鼠标所在位置</summary>
    private void MyList_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(MyList);

        if (!_dragging)
        {
            if (Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging = true;
            MyList.CaptureMouse();
            Mouse.OverrideCursor = Cursors.Hand; // 拖动全程保持小手
            SetRowOpacity(_dragItem, 0.5);
        }

        // 鼠标进入哪一行，就把被拖动的行移到那里；拖出列表上下边界则移到首/尾
        int from = _mine.IndexOf(_dragItem);
        int target = from;
        if (pos.Y < 0) target = 0;
        else if (pos.Y >= MyList.ActualHeight) target = _mine.Count - 1;
        else
        {
            for (int i = 0; i < _mine.Count; i++)
            {
                if (MyList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row) continue;
                var top = row.TranslatePoint(new Point(0, 0), MyList).Y;
                if (pos.Y >= top && pos.Y < top + row.ActualHeight) { target = i; break; }
            }
        }

        if (from >= 0 && target != from)
        {
            _mine.Move(from, target);
            // 移动后行会重新生成，等布局完成再设置半透明
            var item = _dragItem;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => SetRowOpacity(item, 0.5));
        }
    }

    /// <summary>松开：结束拖拽并保存新顺序</summary>
    private void MyList_MouseUp(object sender, MouseButtonEventArgs e) => EndDrag();

    private void MyList_LostCapture(object sender, MouseEventArgs e) => EndDrag();

    private void EndDrag()
    {
        var wasDragging = _dragging;
        var item = _dragItem;
        _dragging = false;
        _dragItem = null;
        if (!wasDragging) return;

        Mouse.OverrideCursor = null;
        if (MyList.IsMouseCaptured) MyList.ReleaseMouseCapture();
        if (item is not null) SetRowOpacity(item, 1);
        SaveMine(); // 保存并刷新序号（序号即弹窗中的数字快捷键）
    }

    private void SetRowOpacity(BrowserEntry? item, double opacity)
    {
        if (item is not null && MyList.ItemContainerGenerator.ContainerFromItem(item) is UIElement row)
            row.Opacity = opacity;
    }

    /// <summary>沿可视树向上查找指定名称的元素</summary>
    private static FrameworkElement? FindNamed(DependencyObject? node, string name)
    {
        while (node is not null)
        {
            if (node is FrameworkElement fe && fe.Name == name) return fe;
            // 文字片段（Run 等）不在可视树中，改走逻辑树
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    // ================= 外观 =================

    /// <summary>把配置中的外观数值显示到控件上（不触发保存）</summary>
    private void LoadAppearance()
    {
        _loading = true;
        SpacingSlider.Value = _config.TileSpacing;
        IconSizeSlider.Value = _config.IconSize;
        ShowPrivateToggle.IsChecked = _config.ShowPrivateButton;
        _loading = false;
        UpdateAppearanceLabels();
    }

    private void UpdateAppearanceLabels()
    {
        SpacingValue.Text = $"{(int)SpacingSlider.Value} px";
        IconSizeValue.Text = $"{(int)IconSizeSlider.Value} px";
    }

    /// <summary>拖动滑块：立即更新数值，停顿片刻后自动保存</summary>
    private void Appearance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // XAML 初始化期间设置 Value 也会触发此事件，此时其它控件还没创建
        if (_loading || !IsInitialized) return;
        UpdateAppearanceLabels();

        _config.TileSpacing = (int)SpacingSlider.Value;
        _config.IconSize = (int)IconSizeSlider.Value;

        // 防抖：拖动过程中不反复写文件
        _saveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick -= SaveTimer_Tick;
        _saveTimer.Tick += SaveTimer_Tick;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveTimer_Tick(object? sender, EventArgs e)
    {
        _saveTimer?.Stop();
        SaveMine();
    }

    /// <summary>恢复默认外观</summary>
    private void ResetAppearance_Click(object sender, RoutedEventArgs e)
    {
        SpacingSlider.Value = AppConfig.DefaultTileSpacing;
        IconSizeSlider.Value = AppConfig.DefaultIconSize;
        _config.PrivateTrigger = PrivateTriggers.RightClick;
        _config.ShowPrivateButton = true;
        LoadAppearance();
        FillPrivateTriggerBox();
        SaveMine();
    }

    /// <summary>无痕打开方式下拉框</summary>
    private void FillPrivateTriggerBox()
    {
        _loading = true;
        PrivateTriggerBox.Items.Clear();
        foreach (var t in PrivateTriggers.All)
            PrivateTriggerBox.Items.Add(new ComboBoxItem { Content = Loc.Get($"S.Trigger.{t}"), Tag = t });
        PrivateTriggerBox.SelectedItem = PrivateTriggerBox.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == _config.PrivateTrigger);
        _loading = false;
    }

    private void PrivateTrigger_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || PrivateTriggerBox.SelectedItem is not ComboBoxItem { Tag: string t }) return;
        _config.PrivateTrigger = t;
        SaveMine();
    }

    private void ShowPrivate_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _config.ShowPrivateButton = ShowPrivateToggle.IsChecked == true;
        SaveMine();
    }

    // ================= 测试 =================

    /// <summary>以真实方式启动一个新实例来预览弹窗</summary>
    private void Test_Click(object sender, RoutedEventArgs e)
    {
        var url = TestUrlBox.Text.Trim();
        if (url.Length == 0 || Environment.ProcessPath is not { } exe) return;
        if (!url.Contains("://")) url = "https://" + url;

        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add(url); // 自动正确转义参数
        Process.Start(psi)?.Dispose();
    }

    // ================= 工具方法 =================

    private static BrowserEntry WithIcon(BrowserEntry b)
    {
        b.Icon ??= IconHelper.Load(b.ExePath);
        return b;
    }

    private void ShowError(string titleKey, Exception ex) =>
        MessageBox.Show(this, ex.Message, Loc.Get(titleKey), MessageBoxButton.OK, MessageBoxImage.Error);
}
