namespace Brona.Models;

/// <summary>
/// 一条网址规则：链接匹配 Pattern 时，直接用指定的浏览器打开。
/// </summary>
public sealed class UrlRule
{
    /// <summary>匹配内容：域名、通配符表达式或正则表达式（由 MatchType 决定）</summary>
    public string Pattern { get; set; } = "";

    /// <summary>匹配方式："domain"（域名及其子域）、"wildcard"（* ? 通配符）、"regex"（正则）</summary>
    public string MatchType { get; set; } = RuleMatchTypes.Domain;

    /// <summary>目标浏览器的程序路径（与 BrowserArguments 一起定位 Browsers 中的某一项）</summary>
    public string BrowserExe { get; set; } = "";

    /// <summary>目标浏览器的启动参数（同一个 exe 可能有多个配置，如不同的用户配置）</summary>
    public string BrowserArguments { get; set; } = "";

    /// <summary>是否以无痕模式打开</summary>
    public bool Private { get; set; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>规则匹配方式的取值</summary>
public static class RuleMatchTypes
{
    public const string Domain = "domain";
    public const string Wildcard = "wildcard";
    public const string Regex = "regex";
    public static readonly string[] All = [Domain, Wildcard, Regex];
}
