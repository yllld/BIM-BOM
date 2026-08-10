using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class ProjectionRegionPicker
    {
        private readonly UIDocument _uidoc;
        private readonly IList<DwgCurveEntity> _entities;

        public ProjectionRegionPicker(UIDocument uidoc, IList<DwgCurveEntity> entities)
        {
            _uidoc = uidoc;
            _entities = entities ?? new List<DwgCurveEntity>();
        }

        public DrawingProjectionRegion Pick(ProjectionType type, DrawingToFamilySettings settings)
        {
            if (_uidoc == null)
            {
                return null;
            }

            var points = new List<XYZ>();
            for (int i = 1; i <= 4; i++)
            {
                string prompt = string.Format("Укажите {0}-й угол области {1}", i, GetProjectionTitle(type));
                XYZ picked = _uidoc.Selection.PickPoint(prompt);
                points.Add(picked);
            }

            DrawingProjectionRegion region = CreateRegion(type, points);
            RefreshRegionEntities(region, settings);
            return region;
        }

        public void RefreshRegionEntities(DrawingProjectionRegion region, DrawingToFamilySettings settings)
        {
            if (region == null)
            {
                return;
            }

            region.Entities.Clear();
            foreach (DwgCurveEntity entity in _entities)
            {
                if (entity == null || entity.BoundingBox == null || entity.Points.Count == 0)
                {
                    continue;
                }

                if (IntersectsRegion(entity, region))
                {
                    region.Entities.Add(entity);
                }
            }

            if (region.Entities.Count == 0)
            {
                RefreshFlatDwgFallbackEntities(region);
            }

            region.EntityCount = region.Entities.Count;
            region.WarningMessage = region.EntityCount == 0
                ? "В выбранной области нет объектов DWG."
                : null;
        }

        private void RefreshFlatDwgFallbackEntities(DrawingProjectionRegion region)
        {
            if (region == null || !region.IsValid || region.PickedPoints.Count < 3 || !IsFlatDwg())
            {
                return;
            }

            IList<XYZ> polygon = RegionPolygon2D(region);
            AddFallbackEntities(region, polygon, ToFlatXYPoint);
            if (region.Entities.Count == 0)
            {
                AddFallbackEntities(region, polygon, ToFlatYXPoint);
            }
        }

        private void AddFallbackEntities(
            DrawingProjectionRegion region,
            IList<XYZ> polygon,
            Func<XYZ, XYZ> pointMapper)
        {
            foreach (DwgCurveEntity entity in _entities)
            {
                if (entity == null || entity.BoundingBox == null || entity.Points.Count == 0)
                {
                    continue;
                }

                if (IntersectsMappedRegion(entity, polygon, pointMapper))
                {
                    region.Entities.Add(entity);
                }
            }
        }

        private static DrawingProjectionRegion CreateRegion(ProjectionType type, IList<XYZ> points)
        {
            var region = new DrawingProjectionRegion
            {
                Type = type,
                IsValid = points != null && points.Count == 4
            };

            if (!region.IsValid)
            {
                region.WarningMessage = "Область проекции не выбрана.";
                return region;
            }

            XYZ axisU;
            XYZ axisV;
            ResolveRegionAxes(type, points, out axisU, out axisV);

            double minU = double.MaxValue;
            double maxU = double.MinValue;
            double minV = double.MaxValue;
            double maxV = double.MinValue;
            XYZ planeAnchor = XYZ.Zero;
            foreach (XYZ point in points)
            {
                planeAnchor += point;
                double u = point.DotProduct(axisU);
                double v = point.DotProduct(axisV);
                minU = Math.Min(minU, u);
                maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v);
                maxV = Math.Max(maxV, v);
            }

            planeAnchor = planeAnchor.Divide(points.Count);
            XYZ normal = axisU.CrossProduct(axisV);
            double planeOffset = planeAnchor.DotProduct(normal);

            XYZ a = PointFromLocal(axisU, axisV, normal, minU, minV, planeOffset);
            XYZ b = PointFromLocal(axisU, axisV, normal, maxU, minV, planeOffset);
            XYZ c = PointFromLocal(axisU, axisV, normal, maxU, maxV, planeOffset);
            XYZ d = PointFromLocal(axisU, axisV, normal, minU, maxV, planeOffset);
            region.PickedPoints.Add(a);
            region.PickedPoints.Add(b);
            region.PickedPoints.Add(c);
            region.PickedPoints.Add(d);
            region.BoundingBox = BoundingBoxUtils.AddPoint(region.BoundingBox, a);
            region.BoundingBox = BoundingBoxUtils.AddPoint(region.BoundingBox, b);
            region.BoundingBox = BoundingBoxUtils.AddPoint(region.BoundingBox, c);
            region.BoundingBox = BoundingBoxUtils.AddPoint(region.BoundingBox, d);

            region.Origin = a;
            region.LocalXAxis = axisU;
            region.LocalYAxis = axisV;
            region.LocalMinU = minU;
            region.LocalMinV = minV;
            region.WidthMm = UnitUtilsExtensions.FeetToMm(Math.Abs(maxU - minU));
            region.HeightMm = UnitUtilsExtensions.FeetToMm(Math.Abs(maxV - minV));
            if (region.WidthMm <= 0 || region.HeightMm <= 0)
            {
                region.IsValid = false;
                region.WarningMessage = "Выбранная область имеет нулевой размер.";
            }

            return region;
        }

        private static bool IntersectsRegion(DwgCurveEntity entity, DrawingProjectionRegion region)
        {
            if (entity == null || region == null || !region.IsValid || region.PickedPoints.Count < 3)
            {
                return false;
            }

            for (int i = 0; i < entity.Points.Count; i++)
            {
                XYZ point = ToRegionPoint(entity.Points[i], region);
                IList<XYZ> polygon = RegionPolygon2D(region);
                if (PointInPolygon(point, polygon))
                {
                    return true;
                }

                if (i > 0 && SegmentIntersectsPolygon(entity.Points[i - 1], entity.Points[i], region))
                {
                    return true;
                }
            }

            XYZ center = BoundingBoxUtils.Center(entity.BoundingBox);
            return PointInPolygon(ToRegionPoint(center, region), RegionPolygon2D(region));
        }

        private static bool SegmentIntersectsPolygon(XYZ start, XYZ end, DrawingProjectionRegion region)
        {
            XYZ a = ToRegionPoint(start, region);
            XYZ b = ToRegionPoint(end, region);
            IList<XYZ> polygon = RegionPolygon2D(region);
            for (int i = 0; i < polygon.Count; i++)
            {
                XYZ c = polygon[i];
                XYZ d = polygon[(i + 1) % polygon.Count];
                if (SegmentsIntersect(a, b, c, d))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ResolveRegionAxes(ProjectionType type, IList<XYZ> points, out XYZ axisU, out XYZ axisV)
        {
            double spanX = CoordinateSpan(points, XYZ.BasisX);
            double spanY = CoordinateSpan(points, XYZ.BasisY);
            double spanZ = CoordinateSpan(points, XYZ.BasisZ);
            const double eps = 1.0e-9;

            if (type == ProjectionType.Front && spanX > eps && spanZ > eps)
            {
                axisU = XYZ.BasisX;
                axisV = XYZ.BasisZ;
                return;
            }

            if ((type == ProjectionType.Side || type == ProjectionType.Isometric) && spanY > eps && spanZ > eps)
            {
                axisU = XYZ.BasisY;
                axisV = XYZ.BasisZ;
                return;
            }

            if (type == ProjectionType.Plan && spanX > eps && spanY > eps)
            {
                axisU = XYZ.BasisX;
                axisV = XYZ.BasisY;
                return;
            }

            XYZ[] axes = new[] { XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ };
            double[] spans = new[] { spanX, spanY, spanZ };
            int first = 0;
            int second = 1;
            for (int i = 0; i < spans.Length; i++)
            {
                if (spans[i] > spans[first])
                {
                    second = first;
                    first = i;
                }
                else if (i != first && spans[i] > spans[second])
                {
                    second = i;
                }
            }

            axisU = axes[first];
            axisV = axes[second];
        }

        private static double CoordinateSpan(IList<XYZ> points, XYZ axis)
        {
            if (points == null || points.Count == 0)
            {
                return 0;
            }

            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (XYZ point in points)
            {
                double value = point.DotProduct(axis);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            return max - min;
        }

        private static XYZ PointFromLocal(XYZ axisU, XYZ axisV, XYZ normal, double u, double v, double planeOffset)
        {
            return axisU.Multiply(u) + axisV.Multiply(v) + normal.Multiply(planeOffset);
        }

        private static XYZ ToRegionPoint(XYZ point, DrawingProjectionRegion region)
        {
            if (point == null || region == null)
            {
                return XYZ.Zero;
            }

            return new XYZ(point.DotProduct(region.LocalXAxis), point.DotProduct(region.LocalYAxis), 0);
        }

        private static IList<XYZ> RegionPolygon2D(DrawingProjectionRegion region)
        {
            var polygon = new List<XYZ>();
            if (region == null)
            {
                return polygon;
            }

            foreach (XYZ point in region.PickedPoints)
            {
                polygon.Add(ToRegionPoint(point, region));
            }

            return polygon;
        }

        private static bool IntersectsMappedRegion(
            DwgCurveEntity entity,
            IList<XYZ> polygon,
            Func<XYZ, XYZ> pointMapper)
        {
            if (entity == null || polygon == null || polygon.Count < 3 || pointMapper == null)
            {
                return false;
            }

            for (int i = 0; i < entity.Points.Count; i++)
            {
                XYZ point = pointMapper(entity.Points[i]);
                if (PointInPolygon(point, polygon))
                {
                    return true;
                }

                if (i > 0 && SegmentIntersectsPolygon(entity.Points[i - 1], entity.Points[i], polygon, pointMapper))
                {
                    return true;
                }
            }

            XYZ center = BoundingBoxUtils.Center(entity.BoundingBox);
            return PointInPolygon(pointMapper(center), polygon);
        }

        private static bool SegmentIntersectsPolygon(
            XYZ start,
            XYZ end,
            IList<XYZ> polygon,
            Func<XYZ, XYZ> pointMapper)
        {
            XYZ a = pointMapper(start);
            XYZ b = pointMapper(end);
            for (int i = 0; i < polygon.Count; i++)
            {
                XYZ c = polygon[i];
                XYZ d = polygon[(i + 1) % polygon.Count];
                if (SegmentsIntersect(a, b, c, d))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsFlatDwg()
        {
            foreach (DwgCurveEntity entity in _entities)
            {
                if (entity == null || entity.BoundingBox == null)
                {
                    continue;
                }

                if (Math.Abs(entity.BoundingBox.Max.Z - entity.BoundingBox.Min.Z) > 1.0e-9)
                {
                    return false;
                }
            }

            return true;
        }

        private static XYZ ToFlatXYPoint(XYZ point)
        {
            return point == null ? XYZ.Zero : new XYZ(point.X, point.Y, 0);
        }

        private static XYZ ToFlatYXPoint(XYZ point)
        {
            return point == null ? XYZ.Zero : new XYZ(point.Y, point.X, 0);
        }

        private static bool SegmentsIntersect(XYZ a, XYZ b, XYZ c, XYZ d)
        {
            double o1 = Orientation(a, b, c);
            double o2 = Orientation(a, b, d);
            double o3 = Orientation(c, d, a);
            double o4 = Orientation(c, d, b);

            if (o1 * o2 < 0 && o3 * o4 < 0)
            {
                return true;
            }

            const double eps = 1e-9;
            return Math.Abs(o1) < eps && OnSegment(a, c, b)
                || Math.Abs(o2) < eps && OnSegment(a, d, b)
                || Math.Abs(o3) < eps && OnSegment(c, a, d)
                || Math.Abs(o4) < eps && OnSegment(c, b, d);
        }

        private static double Orientation(XYZ a, XYZ b, XYZ c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static bool OnSegment(XYZ a, XYZ p, XYZ b)
        {
            const double eps = 1e-9;
            return p.X >= Math.Min(a.X, b.X) - eps
                && p.X <= Math.Max(a.X, b.X) + eps
                && p.Y >= Math.Min(a.Y, b.Y) - eps
                && p.Y <= Math.Max(a.Y, b.Y) + eps;
        }

        private static bool PointInPolygon(XYZ point, IList<XYZ> polygon)
        {
            bool inside = false;
            int count = polygon.Count;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                XYZ pi = polygon[i];
                XYZ pj = polygon[j];
                bool intersects = ((pi.Y > point.Y) != (pj.Y > point.Y))
                    && (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / Math.Max(1e-12, pj.Y - pi.Y) + pi.X);
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static string GetProjectionTitle(ProjectionType type)
        {
            switch (type)
            {
                case ProjectionType.Plan:
                    return "вида сверху";
                case ProjectionType.Front:
                    return "вида спереди";
                case ProjectionType.Side:
                    return "вида сбоку";
                case ProjectionType.Isometric:
                    return "3D/ISO вида";
                default:
                    return "проекции";
            }
        }
    }
}
