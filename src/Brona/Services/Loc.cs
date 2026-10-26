using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace Brona.Services;

/// <summary>
/// 多语言：每种语言一个 JSON 文件（Strings/*.json，编译为嵌入资源）。
/// XAML 用 {DynamicResource 键名} 引用，代码用 Get / Format 读取。
/// 先加载英文作为兜底，再叠加目标语言，因此翻译不全也不会出现空白。
/// 新增语言：复制 Strings/en.json 改名翻译，并加入 Supported 列表。
/// </summary>
public static class Loc
{
    /// <summary>"auto" 表示跟随系统</summary>
    public const string Auto = "auto";

    /// <summary>支持的语言（代码, 本地名称, 是否从右到左书写）</summary>
    public static readonly (string Code, string Name, bool Rtl)[] Supported =
    [
        ("zh-CN", "简体中文", false),
        ("zh-TW", "繁體中文", false),
        ("en", "English", false),
        ("ja", "日本語", false),
        ("ko", "한국어", false),
        ("es", "Español", false),
        ("fr", "Français", false),
        ("de", "Deutsch", false),
        ("it", "Italiano", false),
        ("pt", "Português", false),
        ("ru", "Русский", false),
        ("ar", "العربية", true),
        ("hi", "हिन्दी", false),
        ("bn", "বাংলা", false),
        ("id", "Bahasa Indonesia", false),
        ("ms", "Bahasa Melayu", false),
        ("vi", "Tiếng Việt", false),
        ("th", "ไทย", false),
        ("tr", "Türkçe", false),
        ("pl", "Polski", false),
        ("nl", "Nederlands", false),
        ("sv", "Svenska", false),
        ("nb", "Norsk bokmål", false),
        ("da", "Dansk", false),
        ("fi", "Suomi", false),
        ("cs", "Čeština", false),
        ("sk", "Slovenčina", false),
        ("hu", "Magyar", false),
        ("ro", "Română", false),
        ("el", "Ελληνικά", false),
        ("uk", "Українська", false),
        ("he", "עברית", true),
        ("fa", "فارسی", true),
        ("ur", "اردو", true),
        ("fil", "Filipino", false),
        ("sw", "Kiswahili", false),
        ("bg", "Български", false),
        ("hr", "Hrvatski", false),
        ("sr", "Srpski", false),
        ("ca", "Català", false),
    ];

    // 语言字典在 App.xaml 合并字典中的位置（0 配色、1 控件样式、2 语言）
    private const int DictionaryIndex = 2;

    /// <summary>当前生效的语言代码</summary>
    public static string Current { get; private set; } = "en";

    /// <summary>当前语言是否从右到左书写（阿拉伯语、希伯来语等）</summary>
    public static bool IsRtl => Supported.Any(s => s.Code == Current && s.Rtl);

    /// <summary>界面方向，窗口据此设置 FlowDirection</summary>
    public static FlowDirection Direction => IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>语言切换后触发，供窗口刷新代码里设置的文字</summary>
    public static event Action? Changed;

    /// <summary>把配置中的语言设置解析为实际语言代码</summary>
    public static string Resolve(string? setting)
    {
        if (Supported.Any(s => s.Code == setting)) return setting!;

        // 跟随系统：先精确匹配，再按语言大类匹配，都没有则用英文
        var culture = CultureInfo.CurrentUICulture;
        var name = culture.Name;
        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return name.Contains("TW") || name.Contains("HK") || name.Contains("MO") || name.Contains("Hant") ? "zh-TW" : "zh-CN";
        if (Supported.Any(s => s.Code == name)) return name;

        var two = culture.TwoLetterISOLanguageName switch
        {
            "no" or "nn" => "nb",  // 挪威语统一用书面挪威语
            "tl" => "fil",         // 他加禄语用菲律宾语
            var t => t,
        };
        return Supported.Any(s => s.Code == two) ? two : "en";
    }

    /// <summary>应用语言（替换资源字典），已打开的窗口文字会立即更新</summary>
    public static void Apply(string? setting)
    {
        var code = Resolve(setting);
        var dict = new ResourceDictionary();
        LoadInto(dict, "en");                 // 英文兜底
        if (code != "en") LoadInto(dict, code); // 叠加目标语言

        var dicts = Application.Current.Resources.MergedDictionaries;
        if (dicts.Count > DictionaryIndex) dicts[DictionaryIndex] = dict;
        else dicts.Add(dict);

        Current = code;
        Changed?.Invoke();
    }

    /// <summary>读取文字；找不到时返回键名，便于发现漏翻译</summary>
    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    /// <summary>读取带占位符 {0} {1} 的文字</summary>
    public static string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(Get(key), args);
        }
        catch (FormatException)
        {
            return Get(key); // 翻译中的占位符写错时，至少显示原文
        }
    }

    /// <summary>从嵌入资源读取某语言的 JSON，写入资源字典</summary>
    private static void LoadInto(ResourceDictionary dict, string code)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream($"Brona.Strings.{code}.json");
        if (stream is null) return;

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            if (map is null) return;
            foreach (var (key, value) in map)
            {
                if (!string.IsNullOrEmpty(value)) dict[key] = value;
            }
        }
        catch (JsonException)
        {
            // 语言文件损坏时保留英文兜底
        }
    }
}
