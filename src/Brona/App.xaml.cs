using System.Windows;
using Brona.Models;
using Brona.Services;
using Brona.Views;

namespace Brona;

/// <summary>
/// 程序入口：带链接参数启动时显示浏览器选择弹窗，否则显示设置窗口。
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 跟随系统深浅色
        ThemeHelper.ApplyTheme(this);
        // 界面语言（读取用户设置，默认跟随系统）
        Loc.Apply(ConfigStore.LoadLanguage());

        // 命令行注册 / 取消注册（供安装脚本使用），执行后直接退出
        if (e.Args is ["--register"] or ["--unregister"])
        {
            int code = 0;
            try
            {
                if (e.Args[0] == "--register") RegistrationService.Register();
                else RegistrationService.Unregister();
            }
            catch
            {
                code = 1;
            }
            Shutdown(code);
            return;
        }

        // 系统以 "Brona.exe" "%1" 方式调用，第一个非空参数即为链接
        var target = e.Args.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

        Window window;
        if (target is null)
        {
            window = new SettingsWindow();
        }
        else
        {
            var config = ConfigStore.Load();
            // 命中网址规则：不弹窗，直接用指定浏览器打开
            if (TryOpenByRule(config, target))
            {
                Shutdown();
                return;
            }
            window = new PickerWindow(target, config);
        }
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// 启用了网址规则时，尝试按规则直接打开链接。成功打开返回 true；
    /// 没有命中、按住 Shift（跳过规则强制弹窗）或打开失败时返回 false，由调用方显示弹窗。
    /// </summary>
    private static bool TryOpenByRule(AppConfig config, string target)
    {
        const int VK_SHIFT = 0x10;
        if (!config.EnableRules || config.Rules.Count == 0) return false;
        if ((NativeMethods.GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) return false;
        if (!BrowserLauncher.TryNormalize(target, out var uri)) return false;
        if (RuleMatcher.Resolve(config, uri) is not { } hit) return false;
        var (rule, browser) = hit;

        try
        {
            BrowserLauncher.Open(browser, uri, rule.Private);
        }
        catch
        {
            return false; // 浏览器启动失败时回到弹窗，让用户手动选择
        }

        // 规则打开也计入使用统计；保存失败不影响已经打开的链接
        try
        {
            if (UsageTracker.Record(config, browser)) ConfigStore.Save(config);
        }
        catch
        {
            // 忽略保存失败
        }
        return true;
    }
}
