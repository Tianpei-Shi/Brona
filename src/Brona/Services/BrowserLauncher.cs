using System.Diagnostics;
using Brona.Models;

namespace Brona.Services;

/// <summary>
/// 安全地校验链接并用指定浏览器打开。
/// </summary>
public static class BrowserLauncher
{
    // 允许交给浏览器的链接协议，其它协议（如 javascript:、自定义协议）一律拒绝
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeFile,
    };

    // 本地文件只允许网页类型
    private static readonly HashSet<string> AllowedFileExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".htm", ".html", ".xhtml", ".shtml", ".mht", ".mhtml", ".svg", ".pdf",
    };

    /// <summary>
    /// 校验并规范化传入的链接。通过返回 true，并输出规范化后的 Uri。
    /// </summary>
    public static bool TryNormalize(string raw, out Uri uri)
    {
        uri = null!;
        raw = raw.Trim().Trim('"');
        if (raw.Length is 0 or > 32768) return false;

        // 不带协议的本地路径（打开 .html 文件时系统传入的就是路径）
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var parsed)) return false;
        if (!AllowedSchemes.Contains(parsed.Scheme)) return false;

        if (parsed.IsFile && !AllowedFileExt.Contains(Path.GetExtension(parsed.LocalPath))) return false;

        uri = parsed;
        return true;
    }

    /// <summary>用浏览器打开链接；inPrivate 为 true 时以无痕模式打开</summary>
    public static void Open(BrowserEntry browser, Uri uri, bool inPrivate = false)
    {
        if (!File.Exists(browser.ExePath))
            throw new FileNotFoundException("找不到浏览器程序", browser.ExePath);

        // AbsoluteUri 会把空格、引号等字符转义为 %20、%22，
        // 因此链接无法“逃出”引号去注入额外的命令行参数
        var target = uri.IsFile ? uri.LocalPath : uri.AbsoluteUri;
        if (target.Contains('"')) throw new ArgumentException("链接包含非法字符");

        // 无痕参数放在链接前面
        var privateArgs = inPrivate && browser.SupportsPrivate ? browser.PrivateArguments!.Trim() + " " : "";
        var quoted = $"\"{target}\"";
        var args = browser.Arguments.Contains("%1")
            ? browser.Arguments.Replace("\"%1\"", privateArgs + quoted).Replace("%1", privateArgs + quoted)
            : $"{browser.Arguments} {privateArgs}{quoted}".Trim();

        var psi = new ProcessStartInfo(browser.ExePath, args)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(browser.ExePath) ?? "",
        };
        Process.Start(psi)?.Dispose();
    }
}
