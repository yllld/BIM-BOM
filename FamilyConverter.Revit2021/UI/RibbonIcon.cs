using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyConverter.Revit2021.UI
{
    public enum RibbonIconKind
    {
        SimpleConvert,
        TurboFreeForm,
        ReportsFolder,
        Donate,
        Support
    }

    public static class RibbonIcon
    {
        public static ImageSource Create(RibbonIconKind kind, int size)
        {
            BitmapImage source = LoadSource(GetFileName(kind));
            double padding = size >= 32 ? 1.5 : 1.0;
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

        private static string GetFileName(RibbonIconKind kind)
        {
            switch (kind)
            {
                case RibbonIconKind.SimpleConvert:
                    return "simple-convert.png";
                case RibbonIconKind.TurboFreeForm:
                    return "turbo-freeform.png";
                case RibbonIconKind.ReportsFolder:
                    return "reports-folder.png";
                case RibbonIconKind.Donate:
                    return "donate.png";
                case RibbonIconKind.Support:
                    return "support.png";
                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        private static BitmapImage LoadSource(string fileName)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(x => x.EndsWith(".Assets.Ribbon." + fileName, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                throw new InvalidOperationException("Ribbon icon resource was not found: " + fileName);
            }

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Ribbon icon resource could not be opened: " + fileName);
                }

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
