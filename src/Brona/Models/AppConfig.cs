namespace Brona.Models;

/// <summary>
/// 用户配置：选择的浏览器 + 弹窗外观。
/// </summary>
public sealed class AppConfig
{
    public const int DefaultTileSpacing = 30;
    public const int MinTileSpacing = 0;
    public const int MaxTileSpacing = 80;

    public const int DefaultIconSize = 24;
    public const int MinIconSize = 16;
    public const int MaxIconSize = 40;

    /// <summary>弹窗中显示的浏览器（按顺序）</summary>
    public List<BrowserEntry> Browsers { get; set; } = [];

    /// <summary>浏览器磁贴之间的间距（像素）</summary>
    public int TileSpacing { get; set; } = DefaultTileSpacing;

    /// <summary>浏览器图标大小（像素）</summary>
    public int IconSize { get; set; } = DefaultIconSize;

    /// <summary>
    /// 鼠标无痕打开方式："RightClick"（右键，默认）、"CtrlClick"（Ctrl + 左键）、"ShiftClick"（Shift + 左键）
    /// </summary>
    public string PrivateTrigger { get; set; } = PrivateTriggers.RightClick;

    /// <summary>是否在磁贴下方显示「无痕」按钮</summary>
    public bool ShowPrivateButton { get; set; } = true;

    /// <summary>界面语言："auto"（跟随系统）或语言代码，如 "zh-CN"、"en"</summary>
    public string Language { get; set; } = "auto";

    /// <summary>是否启用网址规则（命中规则时不弹窗，直接用指定浏览器打开）</summary>
    public bool EnableRules { get; set; } = true;

    /// <summary>网址规则（按顺序匹配，第一条命中的生效）</summary>
    public List<UrlRule> Rules { get; set; } = [];

    /// <summary>设置里是否列出浏览器的多用户配置（Chrome / Edge / Firefox 等）供添加</summary>
    public bool DetectProfiles { get; set; } = true;

    /// <summary>弹窗中是否按使用次数从多到少排序</summary>
    public bool SortByUsage { get; set; }

    /// <summary>弹窗中是否默认选中上次使用的浏览器</summary>
    public bool RememberLast { get; set; }

    /// <summary>使用次数统计：键为 exe|启动参数（见 BrowserEntry.Key），值为次数</summary>
    public Dictionary<string, int> Usage { get; set; } = [];

    /// <summary>上次使用的浏览器（键同 Usage）</summary>
    public string LastUsed { get; set; } = "";

    /// <summary>是否在弹窗中对链接做本地安全检查（不联网）</summary>
    public bool EnableLinkCheck { get; set; } = true;

    /// <summary>是否在弹窗中显示二维码按钮</summary>
    public bool ShowQrButton { get; set; } = true;

    /// <summary>把数值限制在合法范围内，防止配置被手改成异常值</summary>
    public void Normalize()
    {
        Browsers ??= [];
        if (string.IsNullOrWhiteSpace(Language)) Language = "auto";
        TileSpacing = Math.Clamp(TileSpacing, MinTileSpacing, MaxTileSpacing);
        IconSize = Math.Clamp(IconSize, MinIconSize, MaxIconSize);
        if (!PrivateTriggers.All.Contains(PrivateTrigger)) PrivateTrigger = PrivateTriggers.RightClick;

        // 旧配置没有这些字段，或被手改成 null / 非法值：丢弃无效规则，补齐默认值
        Rules ??= [];
        Rules = Rules
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.Pattern) && !string.IsNullOrWhiteSpace(r.BrowserExe)
                        && RuleMatchTypes.All.Contains(r.MatchType))
            .ToList();
        foreach (var r in Rules) r.BrowserArguments ??= "";

        // 使用统计改用不区分大小写的字典，并去掉非正数的次数
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, count) in Usage ?? [])
        {
            if (string.IsNullOrWhiteSpace(key) || count <= 0) continue;
            usage[key] = usage.GetValueOrDefault(key) + count;
        }
        Usage = usage;
        LastUsed ??= "";
    }
}

/// <summary>鼠标无痕打开方式的取值</summary>
public static class PrivateTriggers
{
    public const string RightClick = "RightClick";
    public const string CtrlClick = "CtrlClick";
    public const string ShiftClick = "ShiftClick";
    public static readonly string[] All = [RightClick, CtrlClick, ShiftClick];
}
