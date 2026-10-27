using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brona.Models;
using Brona.Services;

namespace Brona.Views;

/// <summary>
/// 点击链接时弹出的浏览器选择窗口。
/// 关闭方式：选择浏览器 / 取消按钮 / Esc / 点击窗口外部。
/// </summary>
public partial class PickerWindow : Window
{
    // 弹窗出现后这段时间内忽略鼠标点击，防止“双击链接”的第二下误点到浏览器
    private static readonly TimeSpan ClickGuard = TimeSpan.FromMilliseconds(300);

    private readonly Uri? _uri;
    private readonly List<BrowserEntry> _browsers;
    private readonly Stopwatch _shownTimer = new();
    private bool _closing;
    private string _privateTrigger = PrivateTriggers.RightClick;
    private AppConfig _config = new();
    private int _initialIndex; // 弹出时获得键盘焦点的磁贴序号

    /// <param name="rawUrl">系统传入的链接</param>
    /// <param name="config">已读取的配置；省略时自行读取</param>
    public PickerWindow(string rawUrl, AppConfig? config = null)
    {
        InitializeComponent();
        FlowDirection = Loc.Direction; // 阿拉伯语等从右到左的语言整体镜像

        // 校验链接：不安全或不支持的协议直接拒绝
        if (!BrowserLauncher.TryNormalize(rawUrl, out var uri))
        {
            _browsers = [];
            Loaded += (_, _) =>
            {
                MessageBox.Show(Loc.Format("S.Picker.Unsupported", Truncate(rawUrl, 200)), "Brona",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Application.Current.Shutdown();
            };
            return;
        }
        _uri = uri;

        // 读取用户配置：浏览器列表 + 外观
        config ??= ConfigStore.Load();
        _config = config;
        ApplyAppearance(config);
        // 按使用次数排序时只影响显示顺序，不改动配置里用户手动排好的顺序
        _browsers = config.SortByUsage ? UsageTracker.SortByCount(config, config.Browsers) : config.Browsers;
        // 记住上次选择：让上次用的那一项获得初始焦点，直接按 Enter 即可打开
        if (config.RememberLast)
            _initialIndex = Math.Max(0, _browsers.FindIndex(b => string.Equals(b.Key, config.LastUsed, StringComparison.OrdinalIgnoreCase)));
        for (int i = 0; i < _browsers.Count; i++)
        {
            _browsers[i].Icon = IconHelper.Load(_browsers[i].ExePath);
            _browsers[i].Shortcut = i < 9 ? (i + 1).ToString() : "";
        }
        BrowserList.ItemsSource = _browsers;
        EmptyPanel.Visibility = _browsers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        ShowUrl(uri);
        SetupExtras(uri, config);

        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
        PreviewKeyDown += OnPreviewKeyDown;

        // 刚弹出时的鼠标按下直接吞掉，防止误触
        PreviewMouseDown += (_, e) =>
        {
            if (_shownTimer.Elapsed < ClickGuard) e.Handled = true;
        };
    }

    /// <summary>按用户设置调整磁贴间距、图标大小和磁贴尺寸</summary>
    private void ApplyAppearance(AppConfig config)
    {
        double icon = config.IconSize;
        Resources["TileMargin"] = new Thickness(0, 0, config.TileSpacing, 0);
        Resources["TileIconSize"] = icon;
        Resources["TileWidth"] = Math.Max(76, icon + 48);  // 名字至少有 76 宽可显示
        Resources["TileHeight"] = icon + 40;                 // 图标 + 文字 + 上下留白
        Resources["PrivateChipVisibility"] = config.ShowPrivateButton ? Visibility.Visible : Visibility.Collapsed;
        _privateTrigger = config.PrivateTrigger;
    }

    /// <summary>当前鼠标点击是否按设置要求以无痕打开（Ctrl / Shift + 左键）</summary>
    private bool IsPrivateClick() => _privateTrigger switch
    {
        PrivateTriggers.CtrlClick => Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
        PrivateTriggers.ShiftClick => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
        _ => false,
    };

    /// <summary>在顶部显示域名（加粗）和路径（淡色）</summary>
    private void ShowUrl(Uri uri)
    {
        if (uri.IsFile)
        {
            SchemeIcon.Text = ""; // 文档图标
            SchemeIcon.Foreground = (Brush)FindResource("TextSecondaryBrush");
            HostRun.Text = Path.GetFileName(uri.LocalPath);
            PathRun.Text = "  " + Path.GetDirectoryName(uri.LocalPath);
            UrlText.ToolTip = uri.LocalPath;
            return;
        }

        bool secure = uri.Scheme == Uri.UriSchemeHttps;
        SchemeIcon.Text = secure ? "" : ""; // 锁 / 开锁
        SchemeIcon.Foreground = (Brush)FindResource(secure ? "SuccessBrush" : "WarningBrush");
        HostRun.Text = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        var rest = Uri.UnescapeDataString(uri.PathAndQuery + uri.Fragment);
        PathRun.Text = rest == "/" ? "" : rest;
        UrlText.ToolTip = (secure ? "" : Loc.Get("S.Picker.Insecure") + "\n") + uri.AbsoluteUri;
    }

    /// <summary>按功能开关设置安全警告、二维码按钮和「记住此网站」复选框</summary>
    private void SetupExtras(Uri uri, AppConfig config)
    {
        // 链接安全检查：有警告才显示图标，ToolTip 列出原因
        if (config.EnableLinkCheck)
        {
            var warnings = LinkInspector.Inspect(uri);
            if (warnings.Count > 0)
            {
                WarnIcon.ToolTip = string.Join("\n", warnings.Select(w => "• " + w));
                WarnIcon.Visibility = Visibility.Visible;
            }
        }

        // 本地文件的二维码对手机没有意义
        QrButton.Visibility = config.ShowQrButton && !uri.IsFile ? Visibility.Visible : Visibility.Collapsed;

        // 网址规则：只有启用规则且链接有域名时才能记住
        if (config.EnableRules && RuleMatcher.RememberDomain(uri) is { } domain)
        {
            RememberBox.ToolTip = domain;
            RememberBox.Visibility = Visibility.Visible;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CenterOnCursorScreen();
        EnsureForeground();
        PlayShowAnimation();
        _shownTimer.Start();

        // 默认选中第一个浏览器（开启「记住上次选择」时为上次用的），直接按 Enter 即可打开
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => GetTile(_initialIndex)?.Focus());
    }

    /// <summary>
    /// 点击窗口外部视为取消。
    /// 但刚弹出时，发起链接的程序可能会把焦点抢回去，此时不取消，而是重新夺回焦点。
    /// </summary>
    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_shownTimer.Elapsed < TimeSpan.FromMilliseconds(800))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, EnsureForeground);
            return;
        }
        Cancel();
    }

    /// <summary>把窗口显示在鼠标所在屏幕的正中央（多屏时跟随鼠标所在的屏幕）</summary>
    private void CenterOnCursorScreen()
    {
        if (!NativeMethods.GetCursorPos(out var pt)) return;

        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        var monitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var area = info.rcWork;

        // 工作区（不含任务栏）居中
        int x = area.Left + (area.Right - area.Left - w) / 2;
        int y = area.Top + (area.Bottom - area.Top - h) / 2;

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>确保弹窗获得焦点，键盘操作立即可用</summary>
    private void EnsureForeground()
    {
        Activate();
        if (IsActive) return;

        // 系统可能阻止后台进程抢焦点：模拟一次 Alt 键后再请求前台
        const byte VK_MENU = 0x12;
        const uint KEYEVENTF_KEYUP = 0x2;
        NativeMethods.keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    /// <summary>弹出动画：淡入 + 轻微放大回弹 + 上移</summary>
    private void PlayShowAnimation()
    {
        var duration = TimeSpan.FromMilliseconds(260);
        var back = new BackEase { Amplitude = 0.3, EasingMode = EasingMode.EaseOut };
        var cubic = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, duration) { EasingFunction = back });
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, duration) { EasingFunction = back });
        CardShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = cubic });
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 数字键 1-9（主键盘和小键盘）直接选择
        int index = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad1,
            _ => -1,
        };
        // 按住 Shift 表示无痕打开
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (index >= 0)
        {
            if (index < _browsers.Count) Launch(_browsers[index], shift);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                // 二维码展开时，Esc 先收起二维码，再按一次才取消弹窗
                if (QrPanel.Visibility == Visibility.Visible) ToggleQr(false);
                else Cancel();
                e.Handled = true;
                break;
            case Key.Enter when Keyboard.FocusedElement is Button { Tag: BrowserEntry b }:
                Launch(b, shift);
                e.Handled = true;
                break;
            case Key.C when Keyboard.Modifiers == ModifierKeys.Control:
                CopyLink();
                e.Handled = true;
                break;
        }
    }

    private void Tile_Click(object sender, RoutedEventArgs e)
    {
        // 左键：普通打开；按设置的修饰键（Ctrl / Shift）点击则无痕打开
        if (sender is Button { Tag: BrowserEntry b }) Launch(b, IsPrivateClick());
    }

    /// <summary>右键：设置为“右键无痕”时以无痕打开</summary>
    private void Tile_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (_privateTrigger != PrivateTriggers.RightClick) return;
        if (_shownTimer.Elapsed < ClickGuard) return;
        if (sender is Button { Tag: BrowserEntry b })
        {
            e.Handled = true;
            Launch(b, inPrivate: true);
        }
    }

    /// <summary>磁贴下方的「无痕」按钮</summary>
    private void PrivateChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BrowserEntry b }) Launch(b, inPrivate: true);
    }

    /// <summary>鼠标悬停时把选中状态移到该磁贴，鼠标和键盘共用同一个高亮</summary>
    private void Tile_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button btn) btn.Focus();
    }

    /// <summary>打开链接并立即退出；inPrivate 为 true 时以无痕模式打开</summary>
    private void Launch(BrowserEntry browser, bool inPrivate = false)
    {
        if (_closing || _uri is null) return;
        // 要求无痕但该浏览器不支持：不打开，避免误以普通模式打开
        if (inPrivate && !browser.SupportsPrivate) return;
        _closing = true;
        Hide(); // 先隐藏，让用户感觉“瞬间”响应

        bool opened = false;
        try
        {
            BrowserLauncher.Open(browser, _uri, inPrivate);
            opened = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("S.Picker.LaunchFailed", browser.Name, ex.Message), "Brona",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        if (opened) SaveChoice(browser, inPrivate);
        Application.Current.Shutdown();
    }

    /// <summary>打开成功后记录使用统计，并按需添加「记住此网站」规则；只在有改动时写一次配置</summary>
    private void SaveChoice(BrowserEntry browser, bool inPrivate)
    {
        bool dirty = UsageTracker.Record(_config, browser);
        if (_uri is not null && RememberBox.Visibility == Visibility.Visible && RememberBox.IsChecked == true)
        {
            RuleMatcher.RememberChoice(_config, _uri, browser, inPrivate);
            dirty = true;
        }
        if (!dirty) return;

        try
        {
            ConfigStore.Save(_config);
        }
        catch
        {
            // 保存失败不影响已经打开的链接
        }
    }

    /// <summary>取消：淡出后退出，不打开任何浏览器</summary>
    private void Cancel()
    {
        if (_closing) return;
        _closing = true;

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) => Application.Current.Shutdown();
        BeginAnimation(OpacityProperty, fade);
        CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, TimeSpan.FromMilliseconds(110)));
        CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, TimeSpan.FromMilliseconds(110)));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancel();

    private void Copy_Click(object sender, RoutedEventArgs e) => CopyLink();

    private void Qr_Click(object sender, RoutedEventArgs e) => ToggleQr();

    /// <summary>
    /// 展开 / 收起链接的二维码。二维码画在弹窗内部而不是新窗口，
    /// 所以不会引起弹窗失去焦点而被取消；按钮不抢焦点，磁贴的键盘选中状态保持不变。
    /// </summary>
    private void ToggleQr(bool? show = null)
    {
        if (_uri is null) return;
        bool open = show ?? QrPanel.Visibility != Visibility.Visible;

        // 第一次展开时才生成
        if (open && QrImage.Source is null && QrHint.Text.Length == 0)
        {
            var image = QrCodeHelper.Create(_uri.AbsoluteUri);
            QrImage.Source = image;
            if (QrImage.Parent is FrameworkElement box) box.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible; // 过长时只显示提示
            QrHint.Text = Loc.Get(image is null ? "S.Qr.TooLong" : "S.Qr.Hint");
        }
        QrPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>复制链接到剪贴板，并在按钮上给出反馈</summary>
    private void CopyLink()
    {
        if (_uri is null) return;
        try
        {
            Clipboard.SetText(_uri.IsFile ? _uri.LocalPath : _uri.AbsoluteUri);
        }
        catch (COMException)
        {
            return; // 剪贴板被其它程序占用
        }

        CopyIcon.Text = ""; // 对勾
        // 复制成功：图标变成绿色对勾，稍后恢复
        CopyIcon.Foreground = (Brush)FindResource("SuccessBrush");
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            CopyIcon.Text = "";
            CopyIcon.ClearValue(TextBlock.ForegroundProperty);
        };
        timer.Start();
    }

    /// <summary>打开设置窗口（启动一个不带参数的新实例），并关闭弹窗</summary>
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (Environment.ProcessPath is { } exe)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false })?.Dispose();
        Cancel();
    }

    /// <summary>拖动标题栏移动弹窗</summary>
    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    /// <summary>取第 index 个浏览器磁贴按钮</summary>
    private Button? GetTile(int index)
    {
        // 模板结构：ContentPresenter → StackPanel → [磁贴按钮, 无痕按钮]
        if (BrowserList.ItemContainerGenerator.ContainerFromIndex(index) is not ContentPresenter cp) return null;
        if (VisualTreeHelper.GetChildrenCount(cp) == 0 || VisualTreeHelper.GetChild(cp, 0) is not StackPanel panel) return null;
        return panel.Children.Count > 0 ? panel.Children[0] as Button : null;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
