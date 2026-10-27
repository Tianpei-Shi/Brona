using Brona.Models;

namespace Brona.Services;

/// <summary>
/// 浏览器使用统计：用于「按使用频率排序」和「记住上次选择」。
/// </summary>
public static class UsageTracker
{
    /// <summary>
    /// 记录一次使用。只有任一相关开关打开时才记录；返回 true 表示配置已改动，需要保存。
    /// </summary>
    public static bool Record(AppConfig config, BrowserEntry browser)
    {
        if (!config.SortByUsage && !config.RememberLast) return false;

        var key = browser.Key;
        config.Usage[key] = config.Usage.GetValueOrDefault(key) + 1;
        config.LastUsed = key;
        return true;
    }

    /// <summary>某个浏览器条目的使用次数</summary>
    public static int Count(AppConfig config, BrowserEntry browser) =>
        config.Usage.GetValueOrDefault(browser.Key);

    /// <summary>清除全部使用记录</summary>
    public static void Clear(AppConfig config)
    {
        config.Usage.Clear();
        config.LastUsed = "";
    }

    /// <summary>
    /// 按使用次数从多到少排序；次数相同的保持原有（用户手动排序的）顺序。
    /// </summary>
    public static List<BrowserEntry> SortByCount(AppConfig config, IEnumerable<BrowserEntry> browsers) =>
        browsers.OrderByDescending(b => Count(config, b)).ToList(); // OrderBy 系列是稳定排序
}
