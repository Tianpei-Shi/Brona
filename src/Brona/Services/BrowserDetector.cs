using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Brona.Models;
using Microsoft.Win32;

namespace Brona.Services;

/// <summary>
/// 自动检测本机已注册的浏览器（读取 Windows 的 StartMenuInternet 注册信息）。
/// 仅作为“一键添加”的候选，最终显示哪些浏览器由用户决定。
/// </summary>
public static class BrowserDetector
{
    // 当前用户优先，其次是全机 64 位、32 位注册位置
    private static readonly (RegistryKey Hive, string Path)[] Roots =
    [
        (Registry.CurrentUser, @"SOFTWARE\Clients\StartMenuInternet"),
        (Registry.LocalMachine, @"SOFTWARE\Clients\StartMenuInternet"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet"),
    ];

    // 不作为候选的条目：已淘汰的 IE，以及本程序自身
    private static readonly HashSet<string> IgnoredKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "IEXPLORE.EXE",
        RegistrationService.AppKey,
    };

    /// <summary>检测本机浏览器列表（按程序路径去重）</summary>
    public static List<BrowserEntry> Detect()
    {
        var result = new List<BrowserEntry>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenExe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var self = Environment.ProcessPath ?? "";

        foreach (var (hive, path) in Roots)
        {
            using var root = hive.OpenSubKey(path);
            if (root is null) continue;

            foreach (var keyName in root.GetSubKeyNames())
            {
                if (IgnoredKeys.Contains(keyName) || !seenKeys.Add(keyName)) continue;

                try
                {
                    using var key = root.OpenSubKey(keyName);
                    if (key is null) continue;

                    var command = ReadDefault(key, @"shell\open\command");
                    if (string.IsNullOrWhiteSpace(command)) continue;

                    var (exe, args) = SplitCommand(command);
                    exe = Path.GetFullPath(Environment.ExpandEnvironmentVariables(exe));

                    // 程序不存在、是本程序自身、或重复的都跳过
                    if (!File.Exists(exe) || string.Equals(exe, self, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!seenExe.Add(exe)) continue;

                    result.Add(new BrowserEntry
                    {
                        Name = ResolveName(key, keyName, exe),
                        ExePath = exe,
                        Arguments = args,
                        PrivateArguments = GuessPrivateArguments(exe),
                    });
                }
                catch
                {
                    // 单个条目损坏不影响其它浏览器
                }
            }
        }

        return result.OrderBy(b => b.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>根据用户手动选择的 exe 生成浏览器条目</summary>
    public static BrowserEntry FromExe(string exePath)
    {
        var info = FileVersionInfo.GetVersionInfo(exePath);
        var name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription!
                 : !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName!
                 : Path.GetFileNameWithoutExtension(exePath);
        var full = Path.GetFullPath(exePath);
        return new BrowserEntry { Name = name.Trim(), ExePath = full, PrivateArguments = GuessPrivateArguments(full) };
    }

    // Chromium 系浏览器的用户数据目录（相对 %LocalAppData%），键为 exe 文件名（不含扩展名）
    private static readonly Dictionary<string, string> ChromiumUserData = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = @"Google\Chrome\User Data",
        ["msedge"] = @"Microsoft\Edge\User Data",
        ["brave"] = @"BraveSoftware\Brave-Browser\User Data",
        ["vivaldi"] = @"Vivaldi\User Data",
        ["chromium"] = @"Chromium\User Data",
    };

    /// <summary>
    /// 检测浏览器的多用户配置（如 Chrome 的「工作」「个人」），每个配置生成一个独立条目。
    /// 只有该浏览器有 2 个及以上配置时才返回；任何读取失败都返回空列表。
    /// </summary>
    public static List<BrowserEntry> DetectProfiles(BrowserEntry browser)
    {
        try
        {
            var exeName = Path.GetFileNameWithoutExtension(browser.ExePath);
            var profiles = ChromiumUserData.TryGetValue(exeName, out var userData)
                ? ReadChromiumProfiles(userData)
                : exeName.Equals("firefox", StringComparison.OrdinalIgnoreCase)
                    ? ReadFirefoxProfiles()
                    : [];
            if (profiles.Count < 2) return [];

            // 无痕参数沿用该浏览器的；名称用「简称 · 配置名」
            return profiles.Select(p => new BrowserEntry
            {
                Name = $"{browser.ShortName} · {p.Name}",
                ExePath = browser.ExePath,
                Arguments = p.Arguments,
                PrivateArguments = browser.PrivateArguments,
                Profile = p.Id,
            }).ToList();
        }
        catch
        {
            // 读取失败（文件被占用、格式不符等）不影响其它功能
            return [];
        }
    }

    /// <summary>读取 Chromium 的 Local State：profile.info_cache 中每个目录名对应一个配置名</summary>
    private static List<(string Id, string Name, string Arguments)> ReadChromiumProfiles(string userDataRelative)
    {
        var result = new List<(string, string, string)>();
        var localState = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), userDataRelative, "Local State");
        if (!File.Exists(localState)) return result;

        using var doc = JsonDocument.Parse(File.ReadAllText(localState));
        if (!doc.RootElement.TryGetProperty("profile", out var profile) ||
            !profile.TryGetProperty("info_cache", out var cache) || cache.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var item in cache.EnumerateObject())
        {
            var dir = item.Name;
            // 目录名会拼进命令行，只接受普通名字
            if (!IsSafeArgument(dir)) continue;

            var name = item.Value.ValueKind == JsonValueKind.Object &&
                       item.Value.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() : null;
            result.Add((dir, string.IsNullOrWhiteSpace(name) ? dir : name.Trim(), $"--profile-directory=\"{dir}\""));
        }

        // Default 排最前，其余按目录名自然排序（Profile 2 在 Profile 10 之前）
        return result
            .OrderBy(p => p.Item1.Equals("Default", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Item1, StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.NumericOrdering))
            .ToList();
    }

    /// <summary>读取 Firefox 的 profiles.ini：每个 [ProfileN] 段的 Name 是配置名</summary>
    private static List<(string Id, string Name, string Arguments)> ReadFirefoxProfiles()
    {
        var result = new List<(string, string, string)>();
        var ini = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Mozilla\Firefox\profiles.ini");
        if (!File.Exists(ini)) return result;

        bool inProfile = false;
        foreach (var raw in File.ReadLines(ini))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                // 只认 [Profile0]、[Profile1]…，跳过 [General]、[Install…]
                inProfile = line.StartsWith("[Profile", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (inProfile && line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
            {
                var name = line[5..].Trim();
                if (IsSafeArgument(name)) result.Add((name, name, $"-P \"{name}\""));
            }
        }
        return result;
    }

    /// <summary>名称会放进双引号里作为命令行参数：不能为空，不能含引号或控制字符</summary>
    private static bool IsSafeArgument(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(c => c != '"' && !char.IsControl(c));

    /// <summary>
    /// 根据浏览器程序推断无痕模式参数；无法识别时返回空字符串（视为不支持）。
    /// </summary>
    public static string GuessPrivateArguments(string exePath)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
            var dir = Path.GetDirectoryName(exePath) ?? "";

            // 有专属参数的浏览器
            if (name == "msedge") return "--inprivate";
            if (exePath.Contains(@"\Opera", StringComparison.OrdinalIgnoreCase)) return "--private";

            // Firefox 系（Firefox、LibreWolf、Waterfox、Floorp、Zen 等）：目录中有 omni.ja
            if (File.Exists(Path.Combine(dir, "omni.ja"))) return "-private-window";

            // Chromium 内核（Chrome、Brave、Vivaldi、国产双核浏览器等）：带 resources.pak
            if (HasFileNearby(dir, "resources.pak")) return "--incognito";
        }
        catch
        {
            // 无法访问目录时视为不支持
        }
        return "";
    }

    /// <summary>在程序目录及其下一级子目录（Chromium 的版本号目录）中查找文件</summary>
    private static bool HasFileNearby(string dir, string fileName)
    {
        if (File.Exists(Path.Combine(dir, fileName))) return true;
        return Directory.EnumerateDirectories(dir).Any(sub => File.Exists(Path.Combine(sub, fileName)));
    }

    /// <summary>读取子键的默认值</summary>
    private static string? ReadDefault(RegistryKey key, string subKey)
    {
        using var sub = key.OpenSubKey(subKey);
        return sub?.GetValue("") as string;
    }

    /// <summary>取浏览器显示名：键默认值 → 应用名 → 文件描述 → 键名</summary>
    private static string ResolveName(RegistryKey key, string keyName, string exe)
    {
        if (key.GetValue("") is string n && !string.IsNullOrWhiteSpace(n)) return n.Trim();

        using (var cap = key.OpenSubKey("Capabilities"))
        {
            // 以 @ 开头的是资源引用，无法直接显示，跳过
            if (cap?.GetValue("ApplicationName") is string an && !string.IsNullOrWhiteSpace(an) && !an.StartsWith('@'))
                return an.Trim();
        }

        var desc = FileVersionInfo.GetVersionInfo(exe).FileDescription;
        return string.IsNullOrWhiteSpace(desc) ? keyName : desc.Trim();
    }

    /// <summary>把命令行拆成“程序路径 + 其余参数”</summary>
    internal static (string Exe, string Args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            if (end > 0) return (command[1..end], command[(end + 1)..].Trim());
        }

        // 未加引号的路径：截到 .exe 为止，兼容路径中带空格的情况
        int exeEnd = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeEnd >= 0) return (command[..(exeEnd + 4)], command[(exeEnd + 4)..].Trim());

        int space = command.IndexOf(' ');
        return space < 0 ? (command, "") : (command[..space], command[(space + 1)..].Trim());
    }
}
