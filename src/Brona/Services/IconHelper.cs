using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Brona.Services;

/// <summary>
/// 从 exe 中提取高清图标。
/// </summary>
public static class IconHelper
{
    /// <summary>提取指定尺寸的图标，失败返回 null</summary>
    public static ImageSource? Load(string path, int index = 0, int size = 64)
    {
        if (!File.Exists(path)) return null;

        var handles = new IntPtr[1];
        var ids = new uint[1];
        uint count = NativeMethods.PrivateExtractIcons(path, index, size, size, handles, ids, 1, 0);
        if (count == 0 || count == uint.MaxValue || handles[0] == IntPtr.Zero) return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(handles[0], Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze(); // 冻结后可跨线程使用且渲染更快
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            // 释放原生图标句柄，避免泄漏
            NativeMethods.DestroyIcon(handles[0]);
        }
    }
}
