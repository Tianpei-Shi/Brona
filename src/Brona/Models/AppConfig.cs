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

    /// <summary>把数值限制在合法范围内，防止配置被手改成异常值</summary>
    public void Normalize()
    {
        Browsers ??= [];
        if (string.IsNullOrWhiteSpace(Language)) Language = "auto";
        TileSpacing = Math.Clamp(TileSpacing, MinTileSpacing, MaxTileSpacing);
        IconSize = Math.Clamp(IconSize, MinIconSize, MaxIconSize);
        if (!PrivateTriggers.All.Contains(PrivateTrigger)) PrivateTrigger = PrivateTriggers.RightClick;
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
