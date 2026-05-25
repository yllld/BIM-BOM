using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyConverter.Revit2021.UI
{
    public static class RibbonIcon
    {
        private const string PngBase64 =
"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";

        public static ImageSource Create(int size)
        {
            BitmapImage source = LoadSource();
            int padding = size >= 32 ? 4 : 2;
            double target = size - padding * 2;

            var visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, size, size));
                context.DrawImage(source, new Rect(padding, padding, target, target));
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static BitmapImage LoadSource()
        {
            byte[] bytes = Convert.FromBase64String(PngBase64);
            using (var stream = new MemoryStream(bytes))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = stream;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }
    }
}
