using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace FamilyConverter.Revit2021.DrawingToFamily.UI
{
    public class DrawingPreviewCanvas : FrameworkElement
    {
        private readonly PreviewTransformService _transform;
        private readonly PreviewHitTestService _hitTest;
        private WpfPoint _lastPanPoint;
        private bool _isPanning;

        public DrawingPreviewCanvas()
        {
            _transform = new PreviewTransformService();
            _hitTest = new PreviewHitTestService();
            Focusable = true;
            ClipToBounds = true;
            Background = Brushes.White;
        }

        public Brush Background { get; set; }
        public IList<PreviewContourItem> Items { get; set; }
        public ProjectionType Projection { get; set; }
        public bool ShowSolid { get; set; }
        public bool ShowVoid { get; set; }
        public bool ShowReference { get; set; }
        public bool ShowOpen { get; set; }
        public bool ShowInvalid { get; set; }
        public bool ShowDisabled { get; set; }
        public bool ShowOnlyProblems { get; set; }
        public bool ShowOnlyIncluded { get; set; }
        public event EventHandler<PreviewContourSelectedEventArgs> ContourSelected;

        public void FitToView()
        {
            _transform.Fit(CalculateBounds(VisibleItems().ToList()), RenderSize);
            InvalidateVisual();
        }

        public void ResetView()
        {
            _transform.Reset();
            InvalidateVisual();
        }

        public void ZoomSelected()
        {
            PreviewContourItem selected = Items == null ? null : Items.FirstOrDefault(x => x.IsSelected);
            _transform.Fit(selected == null || selected.Contour == null ? CalculateBounds(VisibleItems().ToList()) : selected.Contour.BoundingBox, RenderSize);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
            DrawGrid(drawingContext);
            foreach (PreviewContourItem item in VisibleItems())
            {
                DrawContour(drawingContext, item);
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            _transform.ZoomAt(e.GetPosition(this), e.Delta > 0 ? 1.15 : 1.0 / 1.15);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Left && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _isPanning = true;
                _lastPanPoint = e.GetPosition(this);
                CaptureMouse();
                e.Handled = true;
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                PreviewContourItem hit = _hitTest.HitTest(VisibleItems(), e.GetPosition(this), _transform, 8.0);
                if (hit != null && ContourSelected != null)
                {
                    ContourSelected(this, new PreviewContourSelectedEventArgs(hit));
                    e.Handled = true;
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isPanning)
            {
                WpfPoint current = e.GetPosition(this);
                _transform.Pan(current - _lastPanPoint);
                _lastPanPoint = current;
                InvalidateVisual();
                e.Handled = true;
            }
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private IEnumerable<PreviewContourItem> VisibleItems()
        {
            if (Items == null)
            {
                yield break;
            }

            foreach (PreviewContourItem item in Items)
            {
                if (item == null || item.Contour == null || item.Projection != Projection)
                {
                    continue;
                }

                if (ShowOnlyIncluded && !item.IsIncluded)
                {
                    continue;
                }

                if (ShowOnlyProblems && item.Role != ContourType.Invalid && item.Role != ContourType.OpenCurve && item.Role != ContourType.ReferenceCurve && string.IsNullOrWhiteSpace(item.Contour.ReasonIfInvalid))
                {
                    continue;
                }

                if (!ShouldShowRole(item))
                {
                    continue;
                }

                yield return item;
            }
        }

        private bool ShouldShowRole(PreviewContourItem item)
        {
            if (!item.IsIncluded)
            {
                return ShowDisabled;
            }

            if (item.Role == ContourType.SolidProfile)
            {
                return ShowSolid;
            }
            if (item.Role == ContourType.VoidProfile)
            {
                return ShowVoid;
            }
            if (item.Role == ContourType.Invalid)
            {
                return ShowInvalid;
            }
            if (item.Role == ContourType.OpenCurve)
            {
                return ShowOpen;
            }

            return ShowReference;
        }

        private void DrawGrid(DrawingContext context)
        {
            var pen = new Pen(new SolidColorBrush(WpfColor.FromRgb(238, 235, 245)), 1);
            for (double x = 0; x < RenderSize.Width; x += 50)
            {
                context.DrawLine(pen, new WpfPoint(x, 0), new WpfPoint(x, RenderSize.Height));
            }
            for (double y = 0; y < RenderSize.Height; y += 50)
            {
                context.DrawLine(pen, new WpfPoint(0, y), new WpfPoint(RenderSize.Width, y));
            }
        }

        private void DrawContour(DrawingContext context, PreviewContourItem item)
        {
            Pen pen = CreatePen(item);
            foreach (Curve curve in item.Contour.Curves)
            {
                IList<XYZ> points = Tessellate(curve);
                for (int i = 1; i < points.Count; i++)
                {
                    context.DrawLine(pen, _transform.ModelToScreen(points[i - 1]), _transform.ModelToScreen(points[i]));
                }
            }

            if (item.IsSelected && item.Contour.BoundingBox != null)
            {
                DrawBox(context, item.Contour.BoundingBox, new Pen(new SolidColorBrush(WpfColor.FromRgb(24, 20, 38)), 2));
            }

            if (item.Role == ContourType.OpenCurve || item.Role == ContourType.Invalid)
            {
                DrawEndpointMarkers(context, item);
            }
        }

        private Pen CreatePen(PreviewContourItem item)
        {
            WpfColor color = WpfColor.FromRgb(80, 80, 88);
            if (!item.IsIncluded)
            {
                color = WpfColor.FromRgb(184, 180, 190);
            }
            else if (item.Role == ContourType.SolidProfile)
            {
                color = WpfColor.FromRgb(35, 111, 71);
            }
            else if (item.Role == ContourType.VoidProfile)
            {
                color = WpfColor.FromRgb(39, 100, 190);
            }
            else if (item.Role == ContourType.Invalid)
            {
                color = WpfColor.FromRgb(190, 55, 45);
            }
            else if (item.Role == ContourType.OpenCurve)
            {
                color = WpfColor.FromRgb(190, 126, 32);
            }

            double thickness = item.IsSelected ? 3.0 : item.IsIncluded ? 1.6 : 1.0;
            var pen = new Pen(new SolidColorBrush(color), thickness);
            if (item.Role == ContourType.VoidProfile)
            {
                pen.DashStyle = DashStyles.Dash;
            }
            else if (item.Role == ContourType.ReferenceCurve || item.Role == ContourType.OpenCurve)
            {
                pen.DashStyle = DashStyles.DashDot;
            }
            else if (!item.IsIncluded)
            {
                pen.DashStyle = DashStyles.Dot;
            }

            pen.Freeze();
            return pen;
        }

        private void DrawEndpointMarkers(DrawingContext context, PreviewContourItem item)
        {
            if (item.Contour.Curves == null || item.Contour.Curves.Count == 0)
            {
                return;
            }

            try
            {
                WpfPoint start = _transform.ModelToScreen(item.Contour.Curves[0].GetEndPoint(0));
                WpfPoint end = _transform.ModelToScreen(item.Contour.Curves[item.Contour.Curves.Count - 1].GetEndPoint(1));
                Brush brush = item.Role == ContourType.Invalid ? Brushes.Firebrick : Brushes.DarkOrange;
                context.DrawEllipse(brush, null, start, 4, 4);
                context.DrawEllipse(brush, null, end, 4, 4);
            }
            catch
            {
            }
        }

        private void DrawBox(DrawingContext context, BoundingBoxXYZ box, Pen pen)
        {
            WpfPoint a = _transform.ModelToScreen(new XYZ(box.Min.X, box.Min.Y, 0));
            WpfPoint b = _transform.ModelToScreen(new XYZ(box.Max.X, box.Max.Y, 0));
            context.DrawRectangle(null, pen, new Rect(a, b));
        }

        private static IList<XYZ> Tessellate(Curve curve)
        {
            if (curve == null)
            {
                return new List<XYZ>();
            }

            try
            {
                return curve.Tessellate();
            }
            catch
            {
                var points = new List<XYZ>();
                try
                {
                    points.Add(curve.GetEndPoint(0));
                    points.Add(curve.GetEndPoint(1));
                }
                catch
                {
                }

                return points;
            }
        }

        private static BoundingBoxXYZ CalculateBounds(IList<PreviewContourItem> items)
        {
            BoundingBoxXYZ box = null;
            foreach (PreviewContourItem item in items ?? new List<PreviewContourItem>())
            {
                if (item == null || item.Contour == null || item.Contour.BoundingBox == null)
                {
                    continue;
                }

                if (box == null)
                {
                    box = new BoundingBoxXYZ { Min = item.Contour.BoundingBox.Min, Max = item.Contour.BoundingBox.Max };
                }
                else
                {
                    box.Min = new XYZ(Math.Min(box.Min.X, item.Contour.BoundingBox.Min.X), Math.Min(box.Min.Y, item.Contour.BoundingBox.Min.Y), 0);
                    box.Max = new XYZ(Math.Max(box.Max.X, item.Contour.BoundingBox.Max.X), Math.Max(box.Max.Y, item.Contour.BoundingBox.Max.Y), 0);
                }
            }

            return box;
        }
    }

    public class PreviewContourSelectedEventArgs : EventArgs
    {
        public PreviewContourSelectedEventArgs(PreviewContourItem contour)
        {
            Contour = contour;
        }

        public PreviewContourItem Contour { get; private set; }
    }
}
