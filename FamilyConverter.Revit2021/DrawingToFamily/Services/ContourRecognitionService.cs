using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class ContourRecognitionService
    {
        public IList<RecognizedContour> Recognize(DrawingProjectionRegion region, DrawingToFamilySettings settings, IList<string> warnings)
        {
            var result = new List<RecognizedContour>();
            if (region == null || !region.IsValid || settings == null)
            {
                return result;
            }

            double closureFeet = UnitUtilsExtensions.MmToFeet(Math.Max(0.1, settings.ClosureToleranceMm));
            double joinFeet = UnitUtilsExtensions.MmToFeet(Math.Max(0.1, Math.Min(settings.ClosureToleranceMm, 1.0)));
            double minFeet = UnitUtilsExtensions.MmToFeet(Math.Max(0.1, settings.MinimumElementSizeMm));
            var segments = new List<LineSegment>();

            foreach (DwgCurveEntity entity in region.Entities)
            {
                if (!IsBuildableGeometry(entity, settings))
                {
                    continue;
                }

                IList<XYZ> profilePoints = ToProfilePoints(entity, region);
                profilePoints = CleanPoints(profilePoints, Math.Min(closureFeet * 0.5, minFeet * 0.5));
                if (profilePoints.Count < 2)
                {
                    continue;
                }

                bool isClosed = IsClosed(profilePoints, closureFeet);
                if (isClosed && profilePoints.Count >= 3)
                {
                    RecognizedContour closed = CreateClosedContour(profilePoints, region.Type, new[] { entity }, closureFeet, minFeet, settings.MinimumElementSizeMm);
                    if (closed != null)
                    {
                        result.Add(closed);
                    }

                    continue;
                }

                for (int i = 1; i < profilePoints.Count; i++)
                {
                    XYZ start = profilePoints[i - 1];
                    XYZ end = profilePoints[i];
                    if (start.DistanceTo(end) >= minFeet)
                    {
                        segments.Add(new LineSegment
                        {
                            Start = start,
                            End = end,
                            Entity = entity,
                            Projection = region.Type
                        });
                    }
                }
            }

            foreach (RecognizedContour contour in AssembleLineLoops(segments, region.Type, joinFeet, minFeet, settings.MinimumElementSizeMm))
            {
                result.Add(contour);
            }

            foreach (LineSegment segment in segments.Where(x => !x.Used))
            {
                RecognizedContour open = CreateOpenContour(segment, region.Type);
                if (open != null)
                {
                    result.Add(open);
                }
            }

            AssignNestingAndRoles(result);
            if (!result.Any(x => x.Type == ContourType.SolidProfile))
            {
                if (warnings != null)
                {
                    warnings.Add(region.Type + ": замкнутые solid-контуры не найдены. Открытые линии будут сохранены как reference geometry.");
                }
            }

            return result;
        }

        private static bool IsBuildableGeometry(DwgCurveEntity entity, DrawingToFamilySettings settings)
        {
            return entity != null
                && entity.RecognitionRole == RecognitionRole.MainGeometry
                && !entity.IsIgnored
                && entity.LengthMm >= settings.MinimumElementSizeMm
                && !entity.IsSmallObject;
        }

        private static IList<XYZ> ToProfilePoints(DwgCurveEntity entity, DrawingProjectionRegion region)
        {
            var points = new List<XYZ>();
            if (entity == null || region == null)
            {
                return points;
            }

            foreach (XYZ source in entity.Points)
            {
                XYZ flat = GeometryToleranceUtils.Flatten(source);
                XYZ delta = flat - region.Origin;
                double u = GeometryToleranceUtils.Dot2D(delta, region.LocalXAxis) - region.LocalMinU;
                double v = GeometryToleranceUtils.Dot2D(delta, region.LocalYAxis) - region.LocalMinV;

                switch (region.Type)
                {
                    case ProjectionType.Front:
                        points.Add(new XYZ(u, 0, v));
                        break;
                    case ProjectionType.Side:
                        points.Add(new XYZ(0, u, v));
                        break;
                    default:
                        points.Add(new XYZ(u, v, 0));
                        break;
                }
            }

            return points;
        }

        private static IList<XYZ> CleanPoints(IList<XYZ> source, double toleranceFeet)
        {
            var result = new List<XYZ>();
            if (source == null)
            {
                return result;
            }

            foreach (XYZ point in source)
            {
                if (result.Count == 0 || result[result.Count - 1].DistanceTo(point) > toleranceFeet)
                {
                    result.Add(point);
                }
            }

            return result;
        }

        private static bool IsClosed(IList<XYZ> points, double toleranceFeet)
        {
            return points != null
                && points.Count >= 3
                && points[0].DistanceTo(points[points.Count - 1]) <= toleranceFeet;
        }

        private static RecognizedContour CreateClosedContour(
            IList<XYZ> points,
            ProjectionType projection,
            IEnumerable<DwgCurveEntity> sourceEntities,
            double closureFeet,
            double minFeet,
            double minimumSizeMm)
        {
            var clean = new List<XYZ>(points);
            if (clean.Count < 3)
            {
                return null;
            }

            if (clean[0].DistanceTo(clean[clean.Count - 1]) <= closureFeet)
            {
                clean.RemoveAt(clean.Count - 1);
            }

            if (clean.Count < 3)
            {
                return null;
            }

            var curves = new List<Curve>();
            for (int i = 0; i < clean.Count; i++)
            {
                XYZ start = clean[i];
                XYZ end = clean[(i + 1) % clean.Count];
                if (start.DistanceTo(end) >= minFeet)
                {
                    curves.Add(Line.CreateBound(start, end));
                }
            }

            if (curves.Count < 3)
            {
                return null;
            }

            RecognizedContour contour = CreateContourFromCurves(curves, projection, sourceEntities, true);
            return IsReliableClosedContour(contour, minimumSizeMm) ? contour : null;
        }

        private static IList<RecognizedContour> AssembleLineLoops(
            IList<LineSegment> segments,
            ProjectionType projection,
            double closureFeet,
            double minFeet,
            double minimumSizeMm)
        {
            var result = new List<RecognizedContour>();
            if (segments == null || segments.Count < 3)
            {
                return result;
            }

            foreach (LineSegment segment in segments)
            {
                if (segment.Used)
                {
                    continue;
                }

                var used = new List<LineSegment>();
                var points = new List<XYZ> { segment.Start, segment.End };
                segment.Used = true;
                used.Add(segment);

                bool closed = false;
                for (int guard = 0; guard < segments.Count; guard++)
                {
                    XYZ current = points[points.Count - 1];
                    if (current.DistanceTo(points[0]) <= closureFeet && points.Count >= 4)
                    {
                        closed = true;
                        break;
                    }

                    LineSegment next = FindNext(segments, points.Count < 2 ? null : points[points.Count - 2], current, closureFeet);
                    if (next == null)
                    {
                        break;
                    }

                    XYZ nextEnd = current.DistanceTo(next.Start) <= closureFeet ? next.End : next.Start;
                    if (current.DistanceTo(nextEnd) >= minFeet)
                    {
                        points.Add(nextEnd);
                    }
                    next.Used = true;
                    used.Add(next);
                }

                if (!closed)
                {
                    foreach (LineSegment item in used)
                    {
                        item.Used = false;
                    }

                    continue;
                }

                RecognizedContour contour = CreateClosedContour(points, projection, used.Select(x => x.Entity).Distinct(), closureFeet, minFeet, minimumSizeMm);
                if (contour != null)
                {
                    result.Add(contour);
                }
                else
                {
                    foreach (LineSegment item in used)
                    {
                        item.Used = false;
                    }
                }
            }

            return result;
        }

        private static LineSegment FindNext(IList<LineSegment> segments, XYZ previous, XYZ current, double toleranceFeet)
        {
            LineSegment best = null;
            double bestScore = double.MaxValue;
            foreach (LineSegment segment in segments)
            {
                if (segment.Used)
                {
                    continue;
                }

                bool startMatches = current.DistanceTo(segment.Start) <= toleranceFeet;
                bool endMatches = current.DistanceTo(segment.End) <= toleranceFeet;
                if (startMatches || endMatches)
                {
                    XYZ nextEnd = startMatches ? segment.End : segment.Start;
                    double score = current.DistanceTo(startMatches ? segment.Start : segment.End);
                    if (previous != null)
                    {
                        XYZ previousDirection = current - previous;
                        XYZ nextDirection = nextEnd - current;
                        double previousLength = previousDirection.GetLength();
                        double nextLength = nextDirection.GetLength();
                        if (previousLength > 1e-9 && nextLength > 1e-9)
                        {
                            double dot = previousDirection.Normalize().DotProduct(nextDirection.Normalize());
                            score += (1.0 - dot) * toleranceFeet * 10.0;
                        }
                    }

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = segment;
                    }
                }
            }

            return best;
        }

        private static RecognizedContour CreateOpenContour(LineSegment segment, ProjectionType projection)
        {
            if (segment == null || segment.Start.DistanceTo(segment.End) < 1e-9)
            {
                return null;
            }

            var curves = new List<Curve> { Line.CreateBound(segment.Start, segment.End) };
            RecognizedContour contour = CreateContourFromCurves(curves, projection, new[] { segment.Entity }, false);
            if (contour != null)
            {
                contour.Type = ContourType.OpenCurve;
                contour.IsClosed = false;
                contour.IsValidForRevit = true;
                contour.BuildResult = "Reference line candidate";
            }

            return contour;
        }

        private static RecognizedContour CreateContourFromCurves(
            IList<Curve> curves,
            ProjectionType projection,
            IEnumerable<DwgCurveEntity> sourceEntities,
            bool closed)
        {
            if (curves == null || curves.Count == 0)
            {
                return null;
            }

            double areaFeet2 = closed ? Math.Abs(SignedAreaFeet2(curves, projection)) : 0;
            var contour = new RecognizedContour
            {
                SourceProjection = projection,
                BoundingBox = BuildBox(curves),
                AreaMm2 = UnitUtilsExtensions.SquareFeetToSquareMm(areaFeet2),
                IsClosed = closed,
                IsValidForRevit = !closed || areaFeet2 > 1e-12,
                Type = closed ? ContourType.Unknown : ContourType.OpenCurve
            };

            if (contour.BoundingBox != null)
            {
                ApplySize(contour, projection);
            }

            if (sourceEntities != null)
            {
                foreach (DwgCurveEntity entity in sourceEntities.Where(x => x != null))
                {
                    contour.SourceEntities.Add(entity);
                }
            }

            contour.SourceLayer = GetSourceLayer(contour.SourceEntities);
            if (!contour.IsValidForRevit)
            {
                contour.Type = ContourType.Invalid;
                contour.ReasonIfInvalid = "Площадь контура слишком мала или равна нулю.";
            }

            foreach (Curve curve in curves)
            {
                contour.Curves.Add(curve);
            }

            return contour;
        }

        private static bool IsReliableClosedContour(RecognizedContour contour, double minimumSizeMm)
        {
            if (contour == null || !contour.IsClosed || contour.Curves.Count < 3)
            {
                return false;
            }

            double minimumDimensionMm = Math.Max(1.0, minimumSizeMm * 0.5);
            if (contour.WidthMm < minimumDimensionMm || contour.HeightMm < minimumDimensionMm)
            {
                return false;
            }

            double boxArea = Math.Max(1.0, contour.WidthMm * contour.HeightMm);
            double fillRatio = contour.AreaMm2 / boxArea;
            return contour.AreaMm2 >= minimumDimensionMm * minimumDimensionMm
                && fillRatio >= 0.015;
        }

        private static void AssignNestingAndRoles(IList<RecognizedContour> contours)
        {
            var closed = contours
                .Where(x => x.IsClosed && x.IsValidForRevit)
                .OrderByDescending(x => x.AreaMm2)
                .ToList();

            foreach (RecognizedContour contour in closed)
            {
                RecognizedContour parent = null;
                int level = 0;
                XYZ point = BoundingBoxUtils.Center(contour.BoundingBox);

                foreach (RecognizedContour candidate in closed)
                {
                    if (candidate.Id == contour.Id
                        || candidate.SourceProjection != contour.SourceProjection
                        || candidate.AreaMm2 <= contour.AreaMm2)
                    {
                        continue;
                    }

                    if (PointInsideContour(point, candidate, contour.SourceProjection))
                    {
                        level++;
                        if (parent == null || candidate.AreaMm2 < parent.AreaMm2)
                        {
                            parent = candidate;
                        }
                    }
                }

                contour.NestingLevel = level;
                contour.ParentContourId = parent == null ? (Guid?)null : parent.Id;
                contour.Type = level % 2 == 0 ? ContourType.SolidProfile : ContourType.VoidProfile;
            }

            foreach (RecognizedContour contour in contours.Where(x => !x.IsClosed && x.Type == ContourType.Unknown))
            {
                contour.Type = ContourType.OpenCurve;
            }
        }

        private static bool PointInsideContour(XYZ point, RecognizedContour contour, ProjectionType projection)
        {
            IList<XYZ> polygon = TessellateContour(contour);
            if (polygon.Count < 3)
            {
                return false;
            }

            double px;
            double py;
            ToProjection2D(point, projection, out px, out py);
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                double ix;
                double iy;
                double jx;
                double jy;
                ToProjection2D(polygon[i], projection, out ix, out iy);
                ToProjection2D(polygon[j], projection, out jx, out jy);

                bool intersects = ((iy > py) != (jy > py))
                    && (px < (jx - ix) * (py - iy) / Math.Max(1e-12, jy - iy) + ix);
                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static IList<XYZ> TessellateContour(RecognizedContour contour)
        {
            var points = new List<XYZ>();
            if (contour == null)
            {
                return points;
            }

            foreach (Curve curve in contour.Curves)
            {
                try
                {
                    IList<XYZ> curvePoints = curve.Tessellate();
                    foreach (XYZ point in curvePoints)
                    {
                        if (points.Count == 0 || points[points.Count - 1].DistanceTo(point) > 1e-9)
                        {
                            points.Add(point);
                        }
                    }
                }
                catch
                {
                    XYZ start = CurveLoopUtils.GetEndPoint(curve, 0);
                    XYZ end = CurveLoopUtils.GetEndPoint(curve, 1);
                    if (start != null)
                    {
                        points.Add(start);
                    }
                    if (end != null)
                    {
                        points.Add(end);
                    }
                }
            }

            return points;
        }

        private static double SignedAreaFeet2(IList<Curve> curves, ProjectionType projection)
        {
            double area = 0;
            foreach (Curve curve in curves)
            {
                XYZ start = CurveLoopUtils.GetEndPoint(curve, 0);
                XYZ end = CurveLoopUtils.GetEndPoint(curve, 1);
                if (start == null || end == null)
                {
                    continue;
                }

                double sx;
                double sy;
                double ex;
                double ey;
                ToProjection2D(start, projection, out sx, out sy);
                ToProjection2D(end, projection, out ex, out ey);
                area += sx * ey - ex * sy;
            }

            return area * 0.5;
        }

        private static void ToProjection2D(XYZ point, ProjectionType projection, out double x, out double y)
        {
            switch (projection)
            {
                case ProjectionType.Front:
                    x = point.X;
                    y = point.Z;
                    return;
                case ProjectionType.Side:
                    x = point.Y;
                    y = point.Z;
                    return;
                default:
                    x = point.X;
                    y = point.Y;
                    return;
            }
        }

        private static BoundingBoxXYZ BuildBox(IList<Curve> curves)
        {
            BoundingBoxXYZ box = null;
            foreach (Curve curve in curves)
            {
                box = BoundingBoxUtils.AddPoint(box, CurveLoopUtils.GetEndPoint(curve, 0));
                box = BoundingBoxUtils.AddPoint(box, CurveLoopUtils.GetEndPoint(curve, 1));
            }

            return box;
        }

        private static void ApplySize(RecognizedContour contour, ProjectionType projection)
        {
            if (projection == ProjectionType.Side)
            {
                contour.WidthMm = BoundingBoxUtils.HeightMm(contour.BoundingBox);
                contour.HeightMm = BoundingBoxUtils.DepthMm(contour.BoundingBox);
                return;
            }

            if (projection == ProjectionType.Front)
            {
                contour.WidthMm = BoundingBoxUtils.WidthMm(contour.BoundingBox);
                contour.HeightMm = BoundingBoxUtils.DepthMm(contour.BoundingBox);
                return;
            }

            contour.WidthMm = BoundingBoxUtils.WidthMm(contour.BoundingBox);
            contour.HeightMm = BoundingBoxUtils.HeightMm(contour.BoundingBox);
        }

        private static string GetSourceLayer(IList<DwgCurveEntity> entities)
        {
            if (entities == null || entities.Count == 0)
            {
                return "-";
            }

            string first = entities[0].LayerName;
            return entities.All(x => string.Equals(x.LayerName, first, StringComparison.OrdinalIgnoreCase)) ? first : "Mixed";
        }

        private class LineSegment
        {
            public XYZ Start { get; set; }
            public XYZ End { get; set; }
            public DwgCurveEntity Entity { get; set; }
            public ProjectionType Projection { get; set; }
            public bool Used { get; set; }
        }
    }
}
