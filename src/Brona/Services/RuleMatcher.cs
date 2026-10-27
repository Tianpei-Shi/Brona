using System.Text.RegularExpressions;
using Brona.Models;

namespace Brona.Services;

/// <summary>
/// 网址规则的匹配与维护：判断链接命中哪条规则、规则指向哪个浏览器、记住某个网站的选择。
/// </summary>
public static class RuleMatcher
{
    // 正则 / 通配符的匹配超时，防止恶意或写错的表达式卡死
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>判断一条规则是否匹配链接；规则无效或匹配出错一律视为不匹配</summary>
    public static bool IsMatch(UrlRule rule, Uri uri)
    {
        try
        {
            var pattern = rule.Pattern.Trim();
            if (pattern.Length == 0) return false;

            switch (rule.MatchType)
            {
                case RuleMatchTypes.Domain:
                {
                    // 文件链接没有域名；兼容用户写成 "*.example.com" 或 ".example.com"
                    if (uri.IsFile) return false;
                    var domain = pattern.TrimStart('*', '.').ToLowerInvariant();
                    if (domain.Length == 0) return false;
                    var host = uri.IdnHost.ToLowerInvariant();
                    return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
                }
                case RuleMatchTypes.Wildcard:
                {
                    // * 匹配任意长度，? 匹配单个字符，必须匹配整个链接
                    var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                    return Regex.IsMatch(uri.AbsoluteUri, regex,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, RegexTimeout);
                }
                case RuleMatchTypes.Regex:
                    return Regex.IsMatch(uri.AbsoluteUri, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                        RegexTimeout);
                default:
                    return false;
            }
        }
        catch (Exception)
        {
            // 正则非法、超时等：视为不匹配
            return false;
        }
    }

    /// <summary>检查正则表达式是否合法（添加规则时给出提示）</summary>
    public static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>规则指向的浏览器：按 exe + 启动参数在浏览器列表中定位，找不到返回 null</summary>
    public static BrowserEntry? FindBrowser(AppConfig config, UrlRule rule) =>
        config.Browsers.FirstOrDefault(b =>
            string.Equals(b.ExePath, rule.BrowserExe, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(b.Arguments ?? "", rule.BrowserArguments ?? "", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 按顺序找到第一条命中的规则。该规则指向的浏览器必须仍在列表中，否则视为未命中（回到弹窗）。
    /// </summary>
    public static (UrlRule Rule, BrowserEntry Browser)? Resolve(AppConfig config, Uri uri)
    {
        foreach (var rule in config.Rules)
        {
            if (!rule.Enabled || !IsMatch(rule, uri)) continue;
            // 命中的第一条规则决定结果；它的浏览器已被移除时不再往下找，交给用户选择
            return FindBrowser(config, rule) is { } browser ? (rule, browser) : null;
        }
        return null;
    }

    /// <summary>用于「记住此网站的选择」的域名：主机名并去掉开头的 www.；文件链接返回 null</summary>
    public static string? RememberDomain(Uri uri)
    {
        if (uri.IsFile) return null;
        var host = uri.IdnHost.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        return host.Length == 0 ? null : host;
    }

    /// <summary>
    /// 添加一条 domain 规则。同一域名已有规则时先移除旧的；新规则放在最前面，保证最近的选择优先生效。
    /// </summary>
    public static void RememberChoice(AppConfig config, Uri uri, BrowserEntry browser, bool inPrivate)
    {
        var domain = RememberDomain(uri);
        if (domain is null) return;

        config.Rules.RemoveAll(r => r.MatchType == RuleMatchTypes.Domain &&
                                    string.Equals(r.Pattern.Trim().TrimStart('*', '.'), domain, StringComparison.OrdinalIgnoreCase));
        config.Rules.Insert(0, new UrlRule
        {
            Pattern = domain,
            MatchType = RuleMatchTypes.Domain,
            BrowserExe = browser.ExePath,
            BrowserArguments = browser.Arguments ?? "",
            Private = inPrivate,
            Enabled = true,
        });
    }
}
