using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Brona.Services;

/// <summary>
/// 跟随系统深浅色模式。
/// </summary>
public static class ThemeHelper
{
    /// <summary>系统应用是否为深色模式</summary>
    public static bool IsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
    }

    /// <summary>替换配色字典</summary>
    public static void ApplyTheme(Application app)
    {
        if (!IsDark()) return; // 默认已是浅色
        app.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Themes/Dark.xaml", UriKind.Absolute),
        };
    }

    /// <summary>Windows 11 下使用系统圆角窗口</summary>
    public static void ApplyRoundCorners(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    /// <summary>深色模式下让系统标题栏也变为深色</summary>
    public static void ApplyTitleBar(Window window)
    {
        if (!IsDark()) return;
        var hwnd = new WindowInteropHelper(window).Handle;
        int on = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
    }
}
