using System.Diagnostics;
using Microsoft.Win32;

namespace Brona.Services;

/// <summary>
/// 把 Brona 注册为可选的“浏览器”，之后用户可在 Windows 设置中把它设为默认浏览器。
/// 全部写在当前用户(HKCU)下，无需管理员权限。
/// </summary>
public static class RegistrationService
{
    public const string AppKey = "Brona";
    private const string ProgIdUrl = "BronaURL";
    private const string ProgIdHtml = "BronaHTML";
    private const string ClientPath = @"Software\Clients\StartMenuInternet\" + AppKey;
    private const string CapabilitiesPath = ClientPath + @"\Capabilities";
    private const string RegisteredAppsPath = @"Software\RegisteredApplications";

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("无法获取程序路径");

    /// <summary>写入注册信息</summary>
    public static void Register()
    {
        var exe = ExePath;
        var openCmd = $"\"{exe}\" \"%1\"";
        var icon = $"\"{exe}\",0";
        var hkcu = Registry.CurrentUser;

        // 1. 链接协议处理程序（http/https）
        using (var k = hkcu.CreateSubKey($@"Software\Classes\{ProgIdUrl}"))
        {
            k.SetValue("", Loc.Get("S.Reg.LinkType"));
            k.SetValue("URL Protocol", "");
        }
        SetDefault(hkcu, $@"Software\Classes\{ProgIdUrl}\DefaultIcon", icon);
        SetDefault(hkcu, $@"Software\Classes\{ProgIdUrl}\shell\open\command", openCmd);

        // 2. 本地网页文件处理程序（.htm/.html）
        SetDefault(hkcu, $@"Software\Classes\{ProgIdHtml}", Loc.Get("S.Reg.HtmlType"));
        SetDefault(hkcu, $@"Software\Classes\{ProgIdHtml}\DefaultIcon", icon);
        SetDefault(hkcu, $@"Software\Classes\{ProgIdHtml}\shell\open\command", openCmd);

        // ProgId 的应用信息：Windows 设置切换默认浏览器时会读取（与 Chrome/Edge 的注册方式一致）
        foreach (var progId in new[] { ProgIdUrl, ProgIdHtml })
        {
            using var app = hkcu.CreateSubKey($@"Software\Classes\{progId}\Application");
            app.SetValue("AppUserModelId", AppKey);
            app.SetValue("ApplicationName", "Brona");
            app.SetValue("ApplicationIcon", $"{exe},0");
            app.SetValue("ApplicationDescription", Loc.Get("S.About.Tagline"));
            app.SetValue("ApplicationCompany", "Brona");
        }

        // 3. 浏览器客户端信息
        SetDefault(hkcu, ClientPath, "Brona");
        SetDefault(hkcu, ClientPath + @"\DefaultIcon", icon);
        SetDefault(hkcu, ClientPath + @"\shell\open\command", $"\"{exe}\"");

        using (var cap = hkcu.CreateSubKey(CapabilitiesPath))
        {
            cap.SetValue("ApplicationName", "Brona");
            cap.SetValue("ApplicationDescription", Loc.Get("S.About.Tagline"));
            cap.SetValue("ApplicationIcon", icon);
        }
        using (var url = hkcu.CreateSubKey(CapabilitiesPath + @"\URLAssociations"))
        {
            url.SetValue("http", ProgIdUrl);
            url.SetValue("https", ProgIdUrl);
        }
        using (var file = hkcu.CreateSubKey(CapabilitiesPath + @"\FileAssociations"))
        {
            file.SetValue(".htm", ProgIdHtml);
            file.SetValue(".html", ProgIdHtml);
        }
        SetValue(hkcu, CapabilitiesPath + @"\StartMenu", "StartMenuInternet", AppKey);

        // 4. 让“默认应用”设置里能看到 Brona
        SetValue(hkcu, RegisteredAppsPath, AppKey, CapabilitiesPath);

        NotifyShell();
    }

    /// <summary>删除全部注册信息</summary>
    public static void Unregister()
    {
        var hkcu = Registry.CurrentUser;
        hkcu.DeleteSubKeyTree($@"Software\Classes\{ProgIdUrl}", false);
        hkcu.DeleteSubKeyTree($@"Software\Classes\{ProgIdHtml}", false);
        hkcu.DeleteSubKeyTree(ClientPath, false);
        using (var apps = hkcu.OpenSubKey(RegisteredAppsPath, writable: true))
            apps?.DeleteValue(AppKey, false);
        NotifyShell();
    }

    /// <summary>是否已注册，且注册的程序路径就是当前程序</summary>
    public static bool IsRegistered()
    {
        using var apps = Registry.CurrentUser.OpenSubKey(RegisteredAppsPath);
        if (apps?.GetValue(AppKey) is null) return false;

        using var cmd = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgIdUrl}\shell\open\command");
        return cmd?.GetValue("") is string s && s.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Brona 是否已是 https 链接的默认打开程序</summary>
    public static bool IsDefault()
    {
        const string basePath = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https";

        // 新版 Windows 11 使用 UserChoiceLatest\ProgId，旧版使用 UserChoice
        using (var latest = Registry.CurrentUser.OpenSubKey(basePath + @"\UserChoiceLatest\ProgId"))
        {
            if (latest?.GetValue("ProgId") is string p1) return p1 == ProgIdUrl;
        }
        using var choice = Registry.CurrentUser.OpenSubKey(basePath + @"\UserChoice");
        return choice?.GetValue("ProgId") as string == ProgIdUrl;
    }

    /// <summary>打开 Windows“默认应用”设置并定位到 Brona</summary>
    public static void OpenDefaultAppsSettings()
    {
        var uri = IsRegistered() ? $"ms-settings:defaultapps?registeredAppUser={AppKey}" : "ms-settings:defaultapps";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose();
    }

    private static void SetDefault(RegistryKey hive, string path, string value) => SetValue(hive, path, "", value);

    private static void SetValue(RegistryKey hive, string path, string name, string value)
    {
        using var k = hive.CreateSubKey(path);
        k.SetValue(name, value);
    }

    /// <summary>通知资源管理器文件关联已改变</summary>
    private static void NotifyShell() =>
        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
}
