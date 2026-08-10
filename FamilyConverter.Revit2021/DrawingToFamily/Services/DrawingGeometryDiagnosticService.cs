using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class DrawingGeometryDiagnosticService
    {
        public IList<DrawingDiagnosticIssue> Analyze(
            IList<DwgCurveEntity> entities,
            IList<DrawingProjectionRegion> projections,
            IList<RecognizedContour> contours,
            DrawingToFamilySettings settings)
        {
            var result = new List<DrawingDiagnosticIssue>();
            double toleranceMm = settings == null ? 2.0 : Math.Max(0.1, settings.ClosureToleranceMm);
            double minimumMm = settings == null ? 10.0 : Math.Max(0.1, settings.MinimumElementSizeMm);

            AnalyzeEntities(entities ?? new List<DwgCurveEntity>(), toleranceMm, minimumMm, result);
            AnalyzeContours(contours ?? new List<RecognizedContour>(), toleranceMm, minimumMm, result);
            AnalyzeProjections(projections ?? new List<DrawingProjectionRegion>(), toleranceMm, result);
            AnalyzeScale(entities ?? new List<DwgCurveEntity>(), result);

            return result;
        }

        private static void AnalyzeEntities(
            IList<DwgCurveEntity> entities,
            double toleranceMm,
            double minimumMm,
            IList<DrawingDiagnosticIssue> issues)
        {
            var seen = new Dictionary<string, DwgCurveEntity>(StringComparer.Ordinal);
            foreach (DwgCurveEntity entity in entities)
            {
                if (entity == null)
                {
                    continue;
                }

                if (entity.LengthMm > 0 && entity.LengthMm < minimumMm)
                {
                    issues.Add(CreateIssue(
                        DrawingDiagnosticSeverity.Warning,
                        "MicroSegment",
                        "DWG object is shorter than the configured minimum element size.",
                        "Exclude this layer/object from build or lower the minimum size only if this detail is intentional.",
                        entity.LayerName,
                        entity.Id,
                        null,
                        ProjectionType.Unknown,
                        FirstPoint(entity),
                        entity.LengthMm,
                        minimumMm));
                }

                foreach (string warning in entity.Warnings ?? new List<string>())
                {
                    issues.Add(CreateIssue(
                        DrawingDiagnosticSeverity.Warning,
                        "CurveReadWarning",
                        warning,
                        "Review this curve in DWG; Revit exposed it with a warning during geometry reading.",
                        entity.LayerName,
                        entity.Id,
                        null,
                        ProjectionType.Unknown,
                        FirstPoint(entity),
                        0,
                        toleranceMm));
                }

                string key = SegmentKey(entity, toleranceMm);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                DwgCurveEntity existing;
                if (seen.TryGetValue(key, out existing))
                {
                    issues.Add(CreateIssue(
                        DrawingDiagnosticSeverity.Warning,
                        "DuplicateSegment",
                        "Two DWG objects have matching endpoints within tolerance. Duplicate geometry can confuse contour recognition.",
                        "Remove duplicate CAD lines or disable one of the duplicated layers before building.",
                        entity.LayerName,
                        entity.Id,
                        null,
                        ProjectionType.Unknown,
                        FirstPoint(entity),
                        0,
                        toleranceMm));
                }
                else
                {
                    seen[key] = entity;
                }
            }

            if (entities.Count > 5000)
            {
                issues.Add(CreateIssue(
                    DrawingDiagnosticSeverity.Warning,
                    "HeavyDwg",
                    "The selected DWG exposes a large number of curve objects to Revit.",
                    "Disable reference/detail layers before build to reduce Revit transaction risk.",
                    null,
                    null,
                    null,
                    ProjectionType.Unknown,
                    null,
                    entities.Count,
                    5000));
            }
        }

        private static void AnalyzeContours(
            IList<RecognizedContour> contours,
            double toleranceMm,
            double minimumMm,
            IList<DrawingDiagnosticIssue> issues)
        {
            foreach (RecognizedContour contour in contours)
            {
                if (contour == null)
                {
                    continue;
                }

                if (contour.Type == ContourType.Invalid || !contour.IsValidForRevit)
                {
                    issues.Add(CreateIssue(
                        DrawingDiagnosticSeverity.Warning,
                        "InvalidContour",
                        string.IsNullOrWhiteSpace(contour.ReasonIfInvalid) ? "Contour is not valid for Revit build." : contour.ReasonIfInvalid,
                        "Use this contour as reference, exclude it, or repair the source DWG.",
                        contour.SourceLayer,
                        null,
                        contour.Id,
                        contour.SourceProjection,
                        BoxCenter(contour.BoundingBox),
                        0,
                        toleranceMm));
                }

                if (contour.Type == ContourType.OpenCurve || contour.Type == ContourType.ReferenceCurve || !contour.IsClosed)
                {
                    double gapMm = OpenGapMm(contour);
                    issues.Add(CreateIssue(
                        gapMm > 0 && gapMm <= toleranceMm ? DrawingDiagnosticSeverity.Info : DrawingDiagnosticSeverity.Warning,
                        "OpenContour",
                        gapMm > 0
                            ? "Contour is open; endpoint gap is " + gapMm.ToString("0.###", CultureInfo.InvariantCulture) + " mm."
                            : "Contour is open or represented as reference geometry.",
                        gapMm > 0 && gapMm <= toleranceMm
                            ? "Automatic closing is possible if the user allows the configured tolerance."
                            : "Repair the DWG gap, increase tolerance carefully, or keep the contour as reference.",
                        contour.SourceLayer,
                        null,
                        contour.Id,
                        contour.SourceProjection,
                        BoxCenter(contour.BoundingBox),
                        gapMm,
                        toleranceMm));
                }

                if ((contour.WidthMm > 0 && contour.WidthMm < minimumMm)
                    || (contour.HeightMm > 0 && contour.HeightMm < minimumMm)
                    || contour.AreaMm2 > 0 && contour.AreaMm2 < minimumMm * minimumMm)
                {
                    issues.Add(CreateIssue(
                        DrawingDiagnosticSeverity.Warning,
                        "TinyContour",
                        "Contour is smaller than the configured minimum build size.",
                        "Exclude tiny details or reduce the minimum size only after checking the DWG scale.",
                        contour.SourceLayer,
                        null,
                        contour.Id,
                        contour.SourceProjection,
                        BoxCenter(contour.BoundingBox),
                        Math.Min(PositiveOrMax(contour.WidthMm), PositiveOrMax(contour.HeightMm)),
                        minimumMm));
                }

                if (contour.IsClosed && HasSelfIntersection(contour))
                {
                    issues.Add(CreateIssue(
                        DrawingDiagnosticSeverity.Error,
                        "SelfIntersection",
                        "Contour has crossing non-adjacent segments.",
                        "Repair the contour in CAD or exclude it from solid/void build.",
                        contour.SourceLayer,
                        null,
                        contour.Id,
                        contour.SourceProjection,
                        BoxCenter(contour.BoundingBox),
                        0,
                        toleranceMm));
                }
            }
        }

        private static void AnalyzeProjections(
            IList<DrawingProjectionRegion> projections,
            double toleranceMm,
            IList<DrawingDiagnosticIssue> issues)
        {
            DrawingProjectionRegion plan = projections.FirstOrDefault(x => x != null && x.Type == ProjectionType.Plan);
            DrawingProjectionRegion front = projections.FirstOrDefault(x => x != null && x.Type == ProjectionType.Front);
            DrawingProjectionRegion side = projections.FirstOrDefault(x => x != null && x.Type == ProjectionType.Side);

            if (plan != null && front != null)
            {
                AddProjectionMismatch(plan.WidthMm, front.WidthMm, "PlanFrontWidthMismatch", "Plan and Front widths differ.", plan, front, toleranceMm, issues);
            }

            if (plan != null && side != null)
            {
                AddProjectionMismatch(plan.HeightMm, side.WidthMm, "PlanSideDepthMismatch", "Plan depth and Side width differ.", plan, side, toleranceMm, issues);
            }

            if (front != null && side != null)
            {
                AddProjectionMismatch(front.HeightMm, side.HeightMm, "FrontSideHeightMismatch", "Front and Side heights differ.", front, side, toleranceMm, issues);
            }
        }

        private static void AnalyzeScale(IList<DwgCurveEntity> entities, IList<DrawingDiagnosticIssue> issues)
        {
            BoundingBoxXYZ box = null;
            foreach (DwgCurveEntity entity in entities)
            {
                if (entity == null || entity.BoundingBox == null)
                {
                    continue;
                }

                box = BoundingBoxUtils.AddPoint(box, entity.BoundingBox.Min);
                box = BoundingBoxUtils.AddPoint(box, entity.BoundingBox.Max);
            }

            if (box == null)
            {
                return;
            }

            double widthMm = UnitUtilsExtensions.FeetToMm(Math.Abs(box.Max.X - box.Min.X));
            double depthMm = UnitUtilsExtensions.FeetToMm(Math.Abs(box.Max.Y - box.Min.Y));
            double maxMm = Math.Max(widthMm, depthMm);
            if (maxMm < 10.0 || maxMm > 100000.0)
            {
                issues.Add(CreateIssue(
                    DrawingDiagnosticSeverity.Warning,
                    "SuspiciousScale",
                    "Overall DWG size is suspicious: " + widthMm.ToString("0.#", CultureInfo.InvariantCulture) + " x " + depthMm.ToString("0.#", CultureInfo.InvariantCulture) + " mm.",
                    "Check DWG import units before building. The command does not perform a separate scale correction.",
                    null,
                    null,
                    null,
                    ProjectionType.Unknown,
                    BoxCenter(box),
                    maxMm,
                    maxMm < 10.0 ? 10.0 : 100000.0));
            }
        }

        private static void AddProjectionMismatch(
            double firstMm,
            double secondMm,
            string code,
            string message,
            DrawingProjectionRegion first,
            DrawingProjectionRegion second,
            double toleranceMm,
            IList<DrawingDiagnosticIssue> issues)
        {
            if (firstMm <= 0 || secondMm <= 0)
            {
                return;
            }

            double delta = Math.Abs(firstMm - secondMm);
            double allowed = Math.Max(toleranceMm * 3.0, Math.Max(firstMm, secondMm) * 0.05);
            if (delta <= allowed)
            {
                return;
            }

            issues.Add(CreateIssue(
                DrawingDiagnosticSeverity.Warning,
                code,
                message + " Difference is " + delta.ToString("0.#", CultureInfo.InvariantCulture) + " mm.",
                "Re-check selected projection windows or use report-only mode before creating 3D geometry.",
                null,
                null,
                null,
                first == null ? ProjectionType.Unknown : first.Type,
                first == null ? null : BoxCenter(first.BoundingBox),
                delta,
                allowed));
        }

        private static DrawingDiagnosticIssue CreateIssue(
            DrawingDiagnosticSeverity severity,
            string code,
            string message,
            string action,
            string layerName,
            Guid? geometryId,
            Guid? contourId,
            ProjectionType projection,
            XYZ location,
            double valueMm,
            double toleranceMm)
        {
            return new DrawingDiagnosticIssue
            {
                Severity = severity,
                Code = code,
                Message = message,
                SuggestedAction = action,
                LayerName = layerName,
                GeometryId = geometryId,
                ContourId = contourId,
                Projection = projection,
                Location = location,
                ValueMm = valueMm,
                ToleranceMm = toleranceMm
            };
        }

        private static string SegmentKey(DwgCurveEntity entity, double toleranceMm)
        {
            if (entity == null || entity.PointsMm == null || entity.PointsMm.Count < 2)
            {
                return null;
            }

            XYZ start = entity.PointsMm[0];
            XYZ end = entity.PointsMm[entity.PointsMm.Count - 1];
            string a = PointKey(start, toleranceMm);
            string b = PointKey(end, toleranceMm);
            return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
        }

        private static string PointKey(XYZ pointMm, double toleranceMm)
        {
            double grid = Math.Max(0.1, toleranceMm);
            long x = (long)Math.Round(pointMm.X / grid);
            long y = (long)Math.Round(pointMm.Y / grid);
            long z = (long)Math.Round(pointMm.Z / grid);
            return x + ":" + y + ":" + z;
        }

        private static XYZ FirstPoint(DwgCurveEntity entity)
        {
            return entity == null || entity.Points == null || entity.Points.Count == 0 ? null : entity.Points[0];
        }

        private static XYZ BoxCenter(BoundingBoxXYZ box)
        {
            return box == null ? null : new XYZ((box.Min.X + box.Max.X) * 0.5, (box.Min.Y + box.Max.Y) * 0.5, (box.Min.Z + box.Max.Z) * 0.5);
        }

        private static double OpenGapMm(RecognizedContour contour)
        {
            if (contour == null || contour.Curves == null || contour.Curves.Count == 0)
            {
                return 0;
            }

            Curve first = contour.Curves[0];
            Curve last = contour.Curves[contour.Curves.Count - 1];
            if (first == null || last == null)
            {
                return 0;
            }

            try
            {
                return UnitUtilsExtensions.FeetToMm(first.GetEndPoint(0).DistanceTo(last.GetEndPoint(1)));
            }
            catch
            {
                return 0;
            }
        }

        private static bool HasSelfIntersection(RecognizedContour contour)
        {
            IList<XYZ> points = ContourPoints(contour);
            if (points.Count < 4)
            {
                return false;
            }

            for (int i = 0; i < points.Count; i++)
            {
                XYZ a1 = points[i];
                XYZ a2 = points[(i + 1) % points.Count];
                for (int j = i + 1; j < points.Count; j++)
                {
                    if (Math.Abs(i - j) <= 1 || i == 0 && j == points.Count - 1)
                    {
                        continue;
                    }

                    XYZ b1 = points[j];
                    XYZ b2 = points[(j + 1) % points.Count];
                    if (SegmentsIntersect2D(a1, a2, b1, b2))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IList<XYZ> ContourPoints(RecognizedContour contour)
        {
            var result = new List<XYZ>();
            if (contour == null || contour.Curves == null)
            {
                return result;
            }

            foreach (Curve curve in contour.Curves)
            {
                if (curve == null)
                {
                    continue;
                }

                try
                {
                    result.Add(curve.GetEndPoint(0));
                }
                catch
                {
                    // A curve without endpoints cannot participate in the lightweight diagnostic loop.
                }
            }

            return result;
        }

        private static bool SegmentsIntersect2D(XYZ a, XYZ b, XYZ c, XYZ d)
        {
            double o1 = Orientation(a, b, c);
            double o2 = Orientation(a, b, d);
            double o3 = Orientation(c, d, a);
            double o4 = Orientation(c, d, b);
            return o1 * o2 < 0 && o3 * o4 < 0;
        }

        private static double Orientation(XYZ a, XYZ b, XYZ c)
        {
            return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        }

        private static double PositiveOrMax(double value)
        {
            return value > 0 ? value : double.MaxValue;
        }
    }
}
