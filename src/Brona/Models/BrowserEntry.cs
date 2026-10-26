using System.Text.Json.Serialization;
using System.Windows.Media;

namespace Brona.Models;

/// <summary>
/// 一个可用于打开链接的浏览器。
/// </summary>
public sealed class BrowserEntry
{
    /// <summary>显示名称，如 "Google Chrome"</summary>
    public string Name { get; set; } = "";

    /// <summary>浏览器可执行文件完整路径</summary>
    public string ExePath { get; set; } = "";

    /// <summary>附加在链接前面的启动参数（可为空）</summary>
    public string Arguments { get; set; } = "";

    /// <summary>
    /// 无痕模式的启动参数，如 "--incognito"。
    /// null 表示尚未识别（读取配置时自动识别）；空字符串表示该浏览器不支持无痕。
    /// </summary>
    public string? PrivateArguments { get; set; }

    /// <summary>是否支持无痕模式</summary>
    [JsonIgnore]
    public bool SupportsPrivate => !string.IsNullOrWhiteSpace(PrivateArguments);

    /// <summary>弹窗中显示的简短名称：去掉厂商前缀，如 "Google Chrome" → "Chrome"</summary>
    [JsonIgnore]
    public string ShortName
    {
        get
        {
            foreach (var prefix in VendorPrefixes)
            {
                if (Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Name.Length > prefix.Length)
                    return Name[prefix.Length..];
            }
            return Name;
        }
    }

    private static readonly string[] VendorPrefixes = ["Google ", "Microsoft ", "Mozilla "];

    /// <summary>浏览器图标（运行时从 exe 提取，不保存到配置）</summary>
    [JsonIgnore]
    public ImageSource? Icon { get; set; }

    /// <summary>弹窗中显示的数字快捷键（运行时生成）</summary>
    [JsonIgnore]
    public string Shortcut { get; set; } = "";

    /// <summary>判断两个条目是否指向同一个浏览器程序</summary>
    public bool SameExe(BrowserEntry other) =>
        string.Equals(ExePath, other.ExePath, StringComparison.OrdinalIgnoreCase);
}
