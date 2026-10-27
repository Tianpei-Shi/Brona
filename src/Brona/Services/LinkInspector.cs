using System.Net;

namespace Brona.Services;

/// <summary>
/// 链接安全检查：纯本地启发式规则，不联网。返回需要提醒用户的警告列表（已本地化）。
/// 仅作提示，不会阻止打开。
/// </summary>
public static class LinkInspector
{
    // 超过这个长度的链接值得留意
    private const int MaxNormalLength = 2000;

    // 常见短链服务：真实目标被隐藏
    private static readonly string[] Shorteners =
    [
        "bit.ly", "t.co", "tinyurl.com", "goo.gl", "is.gd", "v.gd", "t.cn", "dwz.cn", "url.cn",
        "ow.ly", "buff.ly", "rebrand.ly", "cutt.ly", "rb.gy", "tiny.cc", "shorturl.at", "s.id", "suo.im",
    ];

    /// <summary>检查链接，返回警告文字；没有问题返回空列表（本地文件链接不检查）</summary>
    public static List<string> Inspect(Uri uri)
    {
        var warnings = new List<string>();
        if (uri.IsFile) return warnings;

        var host = uri.IdnHost; // 国际化域名会转成 xn-- 形式

        // 1. 国际化域名（punycode）和混合文字（如拉丁字母夹杂西里尔字母的仿冒域名）
        if (host.Split('.').Any(label => label.StartsWith("xn--", StringComparison.OrdinalIgnoreCase)))
            warnings.Add(Loc.Get("S.Link.Punycode"));
        if (HasMixedScripts(uri.Host)) warnings.Add(Loc.Get("S.Link.Mixed"));

        // 2. 主机是纯 IP 地址
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 || IPAddress.TryParse(host, out _))
            warnings.Add(Loc.Get("S.Link.Ip"));

        // 3. 未加密的 http
        if (uri.Scheme == Uri.UriSchemeHttp) warnings.Add(Loc.Get("S.Link.Http"));

        // 4. 用户信息（http://good.com@evil.com 一类的伪装）
        if (!string.IsNullOrEmpty(uri.UserInfo)) warnings.Add(Loc.Get("S.Link.UserInfo"));

        // 5. 非默认端口
        if (!uri.IsDefaultPort) warnings.Add(Loc.Format("S.Link.Port", uri.Port));

        // 6. 超长链接
        if (uri.AbsoluteUri.Length > MaxNormalLength) warnings.Add(Loc.Get("S.Link.Long"));

        // 7. 短链服务
        var lower = host.ToLowerInvariant();
        var shortener = Shorteners.FirstOrDefault(s => lower == s || lower.EndsWith("." + s, StringComparison.Ordinal));
        if (shortener is not null) warnings.Add(Loc.Format("S.Link.Shortener", shortener));

        return warnings;
    }

    /// <summary>域名的某一段内是否混用了多种文字（拉丁 / 希腊 / 西里尔 / 东亚 / 其它）</summary>
    private static bool HasMixedScripts(string host)
    {
        foreach (var label in host.Split('.'))
        {
            var scripts = new HashSet<int>();
            foreach (var ch in label)
            {
                if (!char.IsLetter(ch)) continue; // 数字、连字符不参与判断
                scripts.Add(ScriptOf(ch));
            }
            if (scripts.Count > 1) return true;
        }
        return false;
    }

    /// <summary>粗略划分文字类别（只需区分常见的仿冒组合，不追求完整）</summary>
    private static int ScriptOf(char ch) => ch switch
    {
        < 'ɐ' => 0,                    // 拉丁（含扩展）
        >= 'Ͱ' and < 'Ѐ' => 1,    // 希腊
        >= 'Ѐ' and < '԰' => 2,    // 西里尔
        >= '぀' and < '㄀' => 3,    // 假名
        >= '一' and < '鿿' => 3,    // 汉字（与假名同组，中日文混用很正常）
        >= '가' and < 'ힰ' => 3,    // 谚文
        _ => 4,                             // 其它文字
    };
}
