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
                points.Add(GeometryToleranceUtils.Flatten(picked));
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

            region.EntityCount = region.Entities.Count;
            region.WarningMessage = region.EntityCount == 0
                ? "В выбранной области нет объектов DWG."
                : null;
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

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;
            foreach (XYZ point in points)
            {
                XYZ flat = GeometryToleranceUtils.Flatten(point);
                minX = Math.Min(minX, flat.X);
                maxX = Math.Max(maxX, flat.X);
                minY = Math.Min(minY, flat.Y);
                maxY = Math.Max(maxY, flat.Y);
            }

            XYZ a = new XYZ(minX, minY, 0);
            XYZ b = new XYZ(maxX, minY, 0);
            XYZ c = new XYZ(maxX, maxY, 0);
            XYZ d = new XYZ(minX, maxY, 0);
            region.PickedPoints.Add(a);
            region.PickedPoints.Add(b);
            region.PickedPoints.Add(c);
            region.PickedPoints.Add(d);
            region.BoundingBox = BoundingBoxUtils.AddPoint(region.BoundingBox, a);
            region.BoundingBox = BoundingBoxUtils.AddPoint(region.BoundingBox, c);

            region.Origin = a;
            region.LocalXAxis = XYZ.BasisX;
            region.LocalYAxis = XYZ.BasisY;
            region.LocalMinU = 0;
            region.LocalMinV = 0;
            region.WidthMm = UnitUtilsExtensions.FeetToMm(Math.Abs(maxX - minX));
            region.HeightMm = UnitUtilsExtensions.FeetToMm(Math.Abs(maxY - minY));
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
                XYZ point = GeometryToleranceUtils.Flatten(entity.Points[i]);
                if (PointInPolygon(point, region.PickedPoints))
                {
                    return true;
                }

                if (i > 0 && SegmentIntersectsPolygon(entity.Points[i - 1], entity.Points[i], region.PickedPoints))
                {
                    return true;
                }
            }

            XYZ center = BoundingBoxUtils.Center(entity.BoundingBox);
            return PointInPolygon(GeometryToleranceUtils.Flatten(center), region.PickedPoints);
        }

        private static bool SegmentIntersectsPolygon(XYZ start, XYZ end, IList<XYZ> polygon)
        {
            XYZ a = GeometryToleranceUtils.Flatten(start);
            XYZ b = GeometryToleranceUtils.Flatten(end);
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
