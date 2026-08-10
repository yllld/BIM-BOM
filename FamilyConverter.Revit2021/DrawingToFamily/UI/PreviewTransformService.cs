using System;
using System.Windows;
using Autodesk.Revit.DB;
using WpfPoint = System.Windows.Point;

namespace FamilyConverter.Revit2021.DrawingToFamily.UI
{
    public class PreviewTransformService
    {
        private const double MinScale = 0.000001;
        private const double MaxScale = 1000000.0;

        public PreviewTransformService()
        {
            Scale = 1.0;
            Offset = new Vector(20, 20);
        }

        public double Scale { get; private set; }
        public Vector Offset { get; private set; }

        public WpfPoint ModelToScreen(XYZ model)
        {
            if (model == null)
            {
                return new WpfPoint();
            }

            return new WpfPoint(model.X * Scale + Offset.X, -model.Y * Scale + Offset.Y);
        }

        public XYZ ScreenToModel(WpfPoint screen)
        {
            double scale = Math.Max(MinScale, Scale);
            return new XYZ((screen.X - Offset.X) / scale, -(screen.Y - Offset.Y) / scale, 0);
        }

        public void Pan(Vector delta)
        {
            Offset += delta;
        }

        public void Reset()
        {
            Scale = 1.0;
            Offset = new Vector(20, 20);
        }

        public void ZoomAt(WpfPoint screenPoint, double factor)
        {
            if (factor <= 0)
            {
                return;
            }

            XYZ before = ScreenToModel(screenPoint);
            Scale = Math.Max(MinScale, Math.Min(MaxScale, Scale * factor));
            WpfPoint after = ModelToScreen(before);
            Offset += screenPoint - after;
        }

        public void Fit(BoundingBoxXYZ box, Size viewport)
        {
            if (box == null || viewport.Width <= 1 || viewport.Height <= 1)
            {
                Reset();
                return;
            }

            double width = Math.Abs(box.Max.X - box.Min.X);
            double height = Math.Abs(box.Max.Y - box.Min.Y);
            if (width < 1e-9)
            {
                width = 1.0;
            }
            if (height < 1e-9)
            {
                height = 1.0;
            }

            double padding = 30.0;
            double scaleX = Math.Max(1.0, viewport.Width - padding * 2.0) / width;
            double scaleY = Math.Max(1.0, viewport.Height - padding * 2.0) / height;
            Scale = Math.Max(MinScale, Math.Min(MaxScale, Math.Min(scaleX, scaleY)));

            XYZ center = new XYZ((box.Min.X + box.Max.X) * 0.5, (box.Min.Y + box.Max.Y) * 0.5, 0);
            WpfPoint centerScreen = ModelToScreen(center);
            Offset += new Vector(viewport.Width * 0.5 - centerScreen.X, viewport.Height * 0.5 - centerScreen.Y);
        }
    }
}
