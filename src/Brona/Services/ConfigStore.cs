using System.Text.Encodings.Web;
using System.Text.Json;
using Brona.Models;

namespace Brona.Services;

/// <summary>
/// 用户配置（浏览器列表 + 弹窗外观）。只保存这一份小配置，不产生任何缓存。
/// 位置：%AppData%\Brona\browsers.json
/// </summary>
public static class ConfigStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Brona");

    private static readonly string FilePath = Path.Combine(Dir, "browsers.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 中文保持可读
    };

    /// <summary>用户是否已经保存过配置</summary>
    public static bool Exists => File.Exists(FilePath);

    /// <summary>
    /// 读取配置；从未配置过时自动使用检测到的浏览器和默认外观。
    /// </summary>
    public static AppConfig Load()
    {
        AppConfig? config = null;
        try
        {
            if (Exists)
            {
                var json = File.ReadAllText(FilePath).TrimStart();
                // 兼容旧版本：旧配置是纯浏览器数组
                config = json.StartsWith('[')
                    ? new AppConfig { Browsers = JsonSerializer.Deserialize<List<BrowserEntry>>(json, JsonOptions) ?? [] }
                    : JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            }
        }
        catch
        {
            // 配置损坏时退回默认值，不影响使用
            config = null;
        }

        config ??= new AppConfig { Browsers = BrowserDetector.Detect() };
        config.Normalize();
        // 浏览器被卸载后自动从列表中隐藏
        config.Browsers = config.Browsers
            .Where(b => !string.IsNullOrWhiteSpace(b.ExePath) && File.Exists(b.ExePath))
            .ToList();

        // 旧配置没有无痕参数：自动识别补上
        foreach (var b in config.Browsers.Where(b => b.PrivateArguments is null))
            b.PrivateArguments = BrowserDetector.GuessPrivateArguments(b.ExePath);
        return config;
    }

    /// <summary>只读取语言设置（启动时用，避免触发浏览器检测）</summary>
    public static string LoadLanguage()
    {
        try
        {
            if (!Exists) return "auto";
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(nameof(AppConfig.Language), out var v) &&
                   v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? "auto"
                : "auto";
        }
        catch
        {
            return "auto";
        }
    }

    /// <summary>保存配置</summary>
    public static void Save(AppConfig config)
    {
        config.Normalize();
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(config, JsonOptions);

        // 先写临时文件再替换，避免写一半导致配置损坏
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>删除配置（取消注册时调用，不留痕迹）</summary>
    public static void Delete()
    {
        try
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        }
        catch
        {
            // 忽略删除失败
        }
    }
}
