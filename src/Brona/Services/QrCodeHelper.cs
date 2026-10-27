using System.Windows;
using System.Windows.Media;
using QRCoder;

namespace Brona.Services;

/// <summary>
/// 生成二维码：用 QRCoder 得到模块矩阵，再自己画成 WPF 矢量图（不依赖 System.Drawing）。
/// </summary>
public static class QrCodeHelper
{
    // 二维码四周的空白（单位：模块），标准要求至少 4
    private const int QuietZone = 4;

    /// <summary>生成二维码图片；内容过长无法编码时返回 null</summary>
    public static ImageSource? Create(string text)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
            var matrix = data.ModuleMatrix;
            int size = matrix.Count;

            // 所有深色模块合成一个几何体，比逐个控件轻量得多
            var dark = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var ctx = dark.Open())
            {
                for (int y = 0; y < size; y++)
                {
                    // 同一行连续的深色模块合并成一个长条，减少图形数量
                    int x = 0;
                    while (x < size)
                    {
                        if (!matrix[y][x]) { x++; continue; }
                        int start = x;
                        while (x < size && matrix[y][x]) x++;
                        AddRect(ctx, start + QuietZone, y + QuietZone, x - start, 1);
                    }
                }
            }
            dark.Freeze();

            int total = size + QuietZone * 2;
            var group = new DrawingGroup();
            // 白底（含空白边）+ 黑色模块：无论深浅主题都保证可扫描
            group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, total, total))));
            group.Children.Add(new GeometryDrawing(Brushes.Black, null, dark));
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            // 内容超出二维码容量等
            return null;
        }
    }

    private static void AddRect(StreamGeometryContext ctx, double x, double y, double w, double h)
    {
        ctx.BeginFigure(new Point(x, y), isFilled: true, isClosed: true);
        ctx.LineTo(new Point(x + w, y), isStroked: false, isSmoothJoin: false);
        ctx.LineTo(new Point(x + w, y + h), isStroked: false, isSmoothJoin: false);
        ctx.LineTo(new Point(x, y + h), isStroked: false, isSmoothJoin: false);
    }
}
