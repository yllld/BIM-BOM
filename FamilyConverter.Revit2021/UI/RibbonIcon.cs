using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyConverter.Revit2021.UI
{
    public static class RibbonIcon
    {
        private static readonly Color AccentColor = Color.FromRgb(0x8C, 0x30, 0xFF);

        public static ImageSource Create(int size)
        {
            double scale = size / 32.0;
            var brush = new SolidColorBrush(AccentColor);
            brush.Freeze();

            var pen = new Pen(brush, 2.4 * scale)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            pen.Freeze();

            var thinPen = new Pen(brush, 1.8 * scale)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            thinPen.Freeze();

            var visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));

                Point p1 = Scale(8, 11, scale);
                Point p2 = Scale(16, 7, scale);
                Point p3 = Scale(24, 11, scale);
                Point p4 = Scale(24, 21, scale);
                Point p5 = Scale(16, 25, scale);
                Point p6 = Scale(8, 21, scale);

                context.DrawLine(pen, p1, p2);
                context.DrawLine(pen, p2, p3);
                context.DrawLine(pen, p3, p4);
                context.DrawLine(pen, p4, p5);
                context.DrawLine(pen, p5, p6);
                context.DrawLine(pen, p6, p1);

                context.DrawLine(thinPen, p1, Scale(16, 15, scale));
                context.DrawLine(thinPen, p3, Scale(16, 15, scale));
                context.DrawLine(thinPen, Scale(16, 15, scale), p5);

                context.DrawEllipse(null, thinPen, Scale(16, 16, scale), 14 * scale, 14 * scale);
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static Point Scale(double x, double y, double scale)
        {
            return new Point(x * scale, y * scale);
        }
    }
}
