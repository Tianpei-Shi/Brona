using System.Windows;
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

        Window window = target is null ? new SettingsWindow() : new PickerWindow(target);
        MainWindow = window;
        window.Show();
    }
}
