using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class ProjectionFusionService
    {
        public IList<BuildCandidate> CreateCandidates(
            IList<RecognizedContour> contours,
            DrawingProjectionRegion plan,
            DrawingProjectionRegion front,
            DrawingProjectionRegion side,
            DrawingToFamilySettings settings,
            IList<string> warnings)
        {
            var result = new List<BuildCandidate>();
            IList<RecognizedContour> all = contours ?? new List<RecognizedContour>();
            var planSolids = SolidContours(all, ProjectionType.Plan);
            var frontSolids = SolidContours(all, ProjectionType.Front);
            var sideSolids = SolidContours(all, ProjectionType.Side);

            IList<RecognizedContour> dominantPlanSolids = SelectDictatingPlanSolids(planSolids, plan, settings, all, warnings);
            foreach (RecognizedContour contour in dominantPlanSolids)
            {
                BuildCandidate candidate = CreatePlanCandidate(contour, all, frontSolids, sideSolids, front, side, settings, warnings);
                result.Add(candidate);
            }

            if (warnings != null)
            {
                int frontOnly = frontSolids.Count(x => !IsRepresentedBy(x, dominantPlanSolids, settings));
                int sideOnly = sideSolids.Count(x => !IsRepresentedBy(x, dominantPlanSolids, settings));
                int open = all.Count(x => x.Type == ContourType.OpenCurve || x.Type == ContourType.ReferenceCurve);
                if (frontOnly > 0)
                {
                    warnings.Add("Front View contains " + frontOnly + " solid contour(s) that are used only for measuring/comparison. Geometry is built from Plan View only.");
                }
                if (sideOnly > 0)
                {
                    warnings.Add("Side View contains " + sideOnly + " solid contour(s) that are used only for measuring/comparison. Geometry is built from Plan View only.");
                }
                if (open > 0)
                {
                    warnings.Add(open + " open/reference contour(s) were recorded in the report but not sent to Revit model creation in crash-safe Plan-only mode.");
                }
            }

            return result;
        }

        private static IList<RecognizedContour> SelectDictatingPlanSolids(
            IList<RecognizedContour> planSolids,
            DrawingProjectionRegion plan,
            DrawingToFamilySettings settings,
            IList<RecognizedContour> allContours,
            IList<string> warnings)
        {
            var result = new List<RecognizedContour>();
            RecognizedContour envelope = CreatePlanEnvelopeContour(plan, settings, warnings);
            RecognizedContour main = planSolids == null || planSolids.Count == 0
                ? null
                : planSolids.OrderByDescending(x => x.AreaMm2).First();

            if (main == null)
            {
                if (envelope != null)
                {
                    AddSyntheticContour(allContours, envelope);
                    result.Add(envelope);
                    if (warnings != null)
                    {
                        warnings.Add("Plan View: замкнутые контуры не найдены, поэтому основной footprint построен по габариту всех основных линий внутри выбранной области.");
                    }
                }

                return result;
            }

            bool useEnvelope = ShouldUseEnvelope(envelope, main, settings);
            RecognizedContour selected = useEnvelope ? envelope : main;
            if (useEnvelope && envelope != null)
            {
                AddSyntheticContour(allContours, envelope);
                if (warnings != null)
                {
                    warnings.Add(string.Format(
                        "Plan View: найденный замкнутый контур покрывает только часть выбранной плановой геометрии ({0:0.#} x {1:0.#} мм вместо {2:0.#} x {3:0.#} мм). Основной footprint построен по всем основным линиям Plan View.",
                        main.WidthMm,
                        main.HeightMm,
                        envelope.WidthMm,
                        envelope.HeightMm));
                }
            }

            result.Add(selected);

            foreach (RecognizedContour skipped in planSolids.Where(x => x.Id != main.Id))
            {
                skipped.BuildResult = "Skipped: Plan View dictating mode builds only the dominant outer plan contour.";
            }
            if (useEnvelope)
            {
                main.BuildResult = "Skipped: Plan envelope is used as dictating footprint because this closed contour covers only part of Plan View.";
            }

            if (warnings != null && planSolids.Count > 1)
            {
                warnings.Add("Plan View contains " + planSolids.Count + " solid contour(s). Dictating Plan mode builds one main body from the largest outer plan contour and records the rest as details/reference.");
            }

            return result;
        }

        private static BuildCandidate CreatePlanCandidate(
            RecognizedContour contour,
            IList<RecognizedContour> all,
            IList<RecognizedContour> frontSolids,
            IList<RecognizedContour> sideSolids,
            DrawingProjectionRegion front,
            DrawingProjectionRegion side,
            DrawingToFamilySettings settings,
            IList<string> warnings)
        {
            RecognizedContour frontMatch = FindWidthMatch(contour.WidthMm, frontSolids, settings);
            RecognizedContour sideMatch = FindWidthMatch(contour.HeightMm, sideSolids, settings);
            double frontHeightMm = frontMatch != null ? frontMatch.HeightMm : ProjectionMainGeometryHeightMm(front, settings);
            double sideHeightMm = sideMatch != null ? sideMatch.HeightMm : ProjectionMainGeometryHeightMm(side, settings);
            double heightMm = ChooseExtrusionHeight(frontHeightMm, sideHeightMm, front, side, settings, contour, warnings);
            double confidence = frontMatch != null ? MatchConfidence(contour.WidthMm, frontMatch.WidthMm, settings) : 0.5;

            var candidate = new BuildCandidate
            {
                PrimaryContour = contour,
                MatchedFrontContour = frontMatch,
                MatchedSideContour = sideMatch,
                Direction = BuildDirection.ExtrudeZ_FromPlan,
                WidthMm = contour.WidthMm,
                DepthMm = contour.HeightMm,
                HeightMm = heightMm,
                Confidence = confidence,
                CanBuild = contour.IsValidForRevit && heightMm > 0
            };

            AddImmediateVoids(candidate, contour, all);
            if (frontMatch == null)
            {
                string warning = "Plan contour " + ShortId(contour) + ": height uses overall Front View height because no matching front contour was found.";
                candidate.Warnings.Add(warning);
                if (warnings != null)
                {
                    warnings.Add(warning);
                }
            }

            if (sideHeightMm > 0 && heightMm > 0)
            {
                double delta = Math.Abs(sideHeightMm - heightMm);
                if (delta > Math.Max(settings.ClosureToleranceMm, heightMm * 0.15))
                {
                    string warning = "Plan contour " + ShortId(contour) + ": Side View height differs from selected extrusion height.";
                    candidate.Warnings.Add(warning);
                    if (warnings != null)
                    {
                        warnings.Add(warning);
                    }
                }
            }

            if (!candidate.CanBuild)
            {
                candidate.SkipReason = "Cannot determine extrusion height. Select Front View or Side View with valid geometry.";
            }

            return candidate;
        }

        private static double ChooseExtrusionHeight(
            double frontHeightMm,
            double sideHeightMm,
            DrawingProjectionRegion front,
            DrawingProjectionRegion side,
            DrawingToFamilySettings settings,
            RecognizedContour contour,
            IList<string> warnings)
        {
            if (frontHeightMm <= 0 && front != null)
            {
                frontHeightMm = front.HeightMm;
            }
            if (sideHeightMm <= 0 && side != null)
            {
                sideHeightMm = side.HeightMm;
            }

            if (frontHeightMm > 0 && sideHeightMm > 0)
            {
                double tolerance = Math.Max(settings.ClosureToleranceMm, frontHeightMm * 0.15);
                if (Math.Abs(frontHeightMm - sideHeightMm) > tolerance)
                {
                    string warning = "Plan contour " + ShortId(contour) + ": Front/Side heights differ (" + frontHeightMm.ToString("0.#") + " / " + sideHeightMm.ToString("0.#") + " mm). Using Front View height; Side View is treated as a check.";
                    if (warnings != null)
                    {
                        warnings.Add(warning);
                    }
                }

                return frontHeightMm;
            }

            if (frontHeightMm > 0)
            {
                return frontHeightMm;
            }

            return sideHeightMm;
        }

        private static double ProjectionMainGeometryHeightMm(DrawingProjectionRegion region, DrawingToFamilySettings settings)
        {
            ProjectionExtents extents = CalculateMainGeometryExtents(region, settings);
            return extents.IsValid ? extents.HeightMm : 0;
        }

        private static ProjectionExtents CalculateMainGeometryExtents(DrawingProjectionRegion region, DrawingToFamilySettings settings)
        {
            var extents = new ProjectionExtents();
            if (region == null || !region.IsValid || region.Entities == null)
            {
                return extents;
            }

            double minimumElementSizeMm = settings == null ? 10.0 : settings.MinimumElementSizeMm;
            foreach (DwgCurveEntity entity in region.Entities)
            {
                if (entity == null
                    || entity.RecognitionRole != RecognitionRole.MainGeometry
                    || entity.IsIgnored
                    || entity.IsSmallObject
                    || entity.LengthMm < minimumElementSizeMm)
                {
                    continue;
                }

                foreach (XYZ source in entity.Points)
                {
                    double u;
                    double v;
                    if (!TryProjectToRegion(region, source, out u, out v))
                    {
                        continue;
                    }

                    double widthFeet = UnitUtilsExtensions.MmToFeet(Math.Max(0, region.WidthMm));
                    double heightFeet = UnitUtilsExtensions.MmToFeet(Math.Max(0, region.HeightMm));
                    extents.Add(Clamp(u, 0, widthFeet), Clamp(v, 0, heightFeet), entity);
                }
            }

            return extents;
        }

        private static ProjectionExtents CalculateDominantAxisPlanExtents(
            DrawingProjectionRegion plan,
            DrawingToFamilySettings settings,
            ProjectionExtents fallback)
        {
            var extents = new ProjectionExtents();
            if (plan == null || !plan.IsValid || plan.Entities == null || fallback == null || !fallback.IsValid)
            {
                return extents;
            }

            double minimumElementSizeMm = settings == null ? 10.0 : settings.MinimumElementSizeMm;
            double axisToleranceFeet = UnitUtilsExtensions.MmToFeet(Math.Max(settings == null ? 2.0 : settings.ClosureToleranceMm * 2.0, 2.0));
            double horizontalMinimumFeet = UnitUtilsExtensions.MmToFeet(Math.Max(minimumElementSizeMm * 3.0, fallback.WidthMm * 0.25));
            double verticalMinimumFeet = UnitUtilsExtensions.MmToFeet(Math.Max(minimumElementSizeMm * 3.0, fallback.HeightMm * 0.25));
            var horizontals = new List<AxisSample>();
            var verticals = new List<AxisSample>();

            foreach (DwgCurveEntity entity in plan.Entities)
            {
                if (entity == null
                    || entity.RecognitionRole != RecognitionRole.MainGeometry
                    || entity.IsIgnored
                    || entity.IsSmallObject
                    || entity.Points.Count < 2)
                {
                    continue;
                }

                for (int i = 1; i < entity.Points.Count; i++)
                {
                    double u1;
                    double v1;
                    double u2;
                    double v2;
                    if (!TryProjectToRegion(plan, entity.Points[i - 1], out u1, out v1)
                        || !TryProjectToRegion(plan, entity.Points[i], out u2, out v2))
                    {
                        continue;
                    }

                    double du = Math.Abs(u2 - u1);
                    double dv = Math.Abs(v2 - v1);
                    if (du >= horizontalMinimumFeet && dv <= Math.Max(axisToleranceFeet, du * 0.025))
                    {
                        horizontals.Add(new AxisSample(Math.Min(u1, u2), Math.Max(u1, u2), (v1 + v2) * 0.5, (v1 + v2) * 0.5, entity));
                    }
                    else if (dv >= verticalMinimumFeet && du <= Math.Max(axisToleranceFeet, dv * 0.025))
                    {
                        verticals.Add(new AxisSample((u1 + u2) * 0.5, (u1 + u2) * 0.5, Math.Min(v1, v2), Math.Max(v1, v2), entity));
                    }
                }
            }

            if (horizontals.Count < 2 && verticals.Count < 2)
            {
                return extents;
            }

            double minU = verticals.Count >= 2 ? verticals.Min(x => x.MinU) : horizontals.Min(x => x.MinU);
            double maxU = verticals.Count >= 2 ? verticals.Max(x => x.MaxU) : horizontals.Max(x => x.MaxU);
            double minV = horizontals.Count >= 2 ? horizontals.Min(x => x.MinV) : verticals.Min(x => x.MinV);
            double maxV = horizontals.Count >= 2 ? horizontals.Max(x => x.MaxV) : verticals.Max(x => x.MaxV);

            extents.Add(minU, minV, null);
            extents.Add(maxU, maxV, null);
            foreach (AxisSample sample in horizontals.Concat(verticals))
            {
                extents.Add(sample.MinU, sample.MinV, sample.Entity);
                extents.Add(sample.MaxU, sample.MaxV, sample.Entity);
            }

            return extents;
        }

        private static RecognizedContour CreatePlanEnvelopeContour(DrawingProjectionRegion plan, DrawingToFamilySettings settings, IList<string> warnings)
        {
            ProjectionExtents allExtents = CalculateMainGeometryExtents(plan, settings);
            ProjectionExtents axisExtents = CalculateDominantAxisPlanExtents(plan, settings, allExtents);
            ProjectionExtents extents = ShouldUseAxisExtents(axisExtents, allExtents) ? axisExtents : allExtents;
            double minimumElementSizeMm = settings == null ? 10.0 : settings.MinimumElementSizeMm;
            if (!extents.IsValid
                || extents.WidthMm < minimumElementSizeMm
                || extents.HeightMm < minimumElementSizeMm)
            {
                return null;
            }

            if (axisExtents != null && axisExtents.IsValid && ReferenceEquals(extents, axisExtents) && warnings != null)
            {
                warnings.Add(string.Format(
                    "Plan View: footprint привязан к доминирующим длинным горизонтальным/вертикальным линиям ({0:0.#} x {1:0.#} мм), а не к случайным мелким контурам.",
                    extents.WidthMm,
                    extents.HeightMm));
            }

            XYZ a = new XYZ(extents.MinU, extents.MinV, 0);
            XYZ b = new XYZ(extents.MaxU, extents.MinV, 0);
            XYZ c = new XYZ(extents.MaxU, extents.MaxV, 0);
            XYZ d = new XYZ(extents.MinU, extents.MaxV, 0);

            var contour = new RecognizedContour
            {
                SourceProjection = ProjectionType.Plan,
                SourceLayer = "Plan footprint",
                Type = ContourType.SolidProfile,
                NestingLevel = 0,
                BoundingBox = new BoundingBoxXYZ { Min = a, Max = c },
                WidthMm = extents.WidthMm,
                HeightMm = extents.HeightMm,
                AreaMm2 = extents.WidthMm * extents.HeightMm,
                IsClosed = true,
                IsValidForRevit = true,
                BuildResult = "Synthetic dictating Plan footprint from all main Plan View entities."
            };

            contour.Curves.Add(Line.CreateBound(a, b));
            contour.Curves.Add(Line.CreateBound(b, c));
            contour.Curves.Add(Line.CreateBound(c, d));
            contour.Curves.Add(Line.CreateBound(d, a));
            foreach (DwgCurveEntity entity in extents.SourceEntities)
            {
                contour.SourceEntities.Add(entity);
            }

            return contour;
        }

        private static bool ShouldUseAxisExtents(ProjectionExtents axisExtents, ProjectionExtents allExtents)
        {
            if (axisExtents == null || !axisExtents.IsValid)
            {
                return false;
            }

            if (allExtents == null || !allExtents.IsValid)
            {
                return true;
            }

            return axisExtents.WidthMm >= allExtents.WidthMm * 0.65
                && axisExtents.HeightMm >= allExtents.HeightMm * 0.65;
        }

        private static bool TryProjectToRegion(DrawingProjectionRegion region, XYZ source, out double u, out double v)
        {
            u = 0;
            v = 0;
            if (region == null || source == null || region.Origin == null || region.LocalXAxis == null || region.LocalYAxis == null)
            {
                return false;
            }

            XYZ flat = GeometryToleranceUtils.Flatten(source);
            XYZ delta = flat - region.Origin;
            u = GeometryToleranceUtils.Dot2D(delta, region.LocalXAxis) - region.LocalMinU;
            v = GeometryToleranceUtils.Dot2D(delta, region.LocalYAxis) - region.LocalMinV;
            return true;
        }

        private static bool ShouldUseEnvelope(RecognizedContour envelope, RecognizedContour main, DrawingToFamilySettings settings)
        {
            if (envelope == null || main == null)
            {
                return false;
            }

            double envelopeArea = Math.Max(envelope.AreaMm2, 1.0);
            double coverage = main.AreaMm2 / envelopeArea;
            double widthGap = RelativeGap(main.WidthMm, envelope.WidthMm);
            double depthGap = RelativeGap(main.HeightMm, envelope.HeightMm);
            double closureToleranceMm = settings == null ? 2.0 : settings.ClosureToleranceMm;
            double tolerance = Math.Max(closureToleranceMm, Math.Max(envelope.WidthMm, envelope.HeightMm) * 0.03);

            if (Math.Abs(main.WidthMm - envelope.WidthMm) <= tolerance
                && Math.Abs(main.HeightMm - envelope.HeightMm) <= tolerance)
            {
                return false;
            }

            return coverage < 0.85 || widthGap > 0.12 || depthGap > 0.12;
        }

        private static double RelativeGap(double a, double b)
        {
            return Math.Abs(a - b) / Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1.0);
        }

        private static void AddSyntheticContour(IList<RecognizedContour> contours, RecognizedContour contour)
        {
            if (contours != null && contour != null && !contours.Any(x => x.Id == contour.Id))
            {
                contours.Add(contour);
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
            {
                return value;
            }

            return Math.Max(min, Math.Min(max, value));
        }

        private static BuildCandidate CreateFrontCandidate(
            RecognizedContour contour,
            IList<RecognizedContour> all,
            IList<RecognizedContour> planSolids,
            IList<RecognizedContour> sideSolids,
            DrawingProjectionRegion plan,
            DrawingProjectionRegion side,
            DrawingToFamilySettings settings,
            IList<string> warnings)
        {
            RecognizedContour planMatch = FindWidthMatch(contour.WidthMm, planSolids, settings);
            RecognizedContour sideMatch = FindHeightMatch(contour.HeightMm, sideSolids, settings);
            double depthMm = planMatch != null ? planMatch.HeightMm : side == null ? (plan == null ? 0 : plan.HeightMm) : side.WidthMm;
            var candidate = new BuildCandidate
            {
                PrimaryContour = contour,
                MatchedSideContour = sideMatch,
                Direction = BuildDirection.ExtrudeY_FromFront,
                WidthMm = contour.WidthMm,
                DepthMm = depthMm,
                HeightMm = contour.HeightMm,
                Confidence = planMatch != null || sideMatch != null ? 0.75 : 0.5,
                CanBuild = contour.IsValidForRevit && depthMm > 0
            };

            AddImmediateVoids(candidate, contour, all);
            if (planMatch == null)
            {
                string warning = "Front contour " + ShortId(contour) + ": depth uses overall Plan/Side size because no matching plan contour was found.";
                candidate.Warnings.Add(warning);
                if (warnings != null)
                {
                    warnings.Add(warning);
                }
            }

            if (!candidate.CanBuild)
            {
                candidate.SkipReason = "Cannot determine front extrusion depth.";
            }

            return candidate;
        }

        private static BuildCandidate CreateSideCandidate(
            RecognizedContour contour,
            IList<RecognizedContour> all,
            IList<RecognizedContour> planSolids,
            IList<RecognizedContour> frontSolids,
            DrawingProjectionRegion plan,
            DrawingProjectionRegion front,
            DrawingToFamilySettings settings,
            IList<string> warnings)
        {
            RecognizedContour planMatch = FindHeightMatch(contour.WidthMm, planSolids, settings);
            RecognizedContour frontMatch = FindHeightMatch(contour.HeightMm, frontSolids, settings);
            double widthMm = planMatch != null ? planMatch.WidthMm : front == null ? (plan == null ? 0 : plan.WidthMm) : front.WidthMm;
            var candidate = new BuildCandidate
            {
                PrimaryContour = contour,
                MatchedFrontContour = frontMatch,
                Direction = BuildDirection.ExtrudeX_FromSide,
                WidthMm = widthMm,
                DepthMm = contour.WidthMm,
                HeightMm = contour.HeightMm,
                Confidence = planMatch != null || frontMatch != null ? 0.75 : 0.5,
                CanBuild = contour.IsValidForRevit && widthMm > 0
            };

            AddImmediateVoids(candidate, contour, all);
            if (planMatch == null && frontMatch == null)
            {
                string warning = "Side contour " + ShortId(contour) + ": width uses overall Plan/Front size because no direct match was found.";
                candidate.Warnings.Add(warning);
                if (warnings != null)
                {
                    warnings.Add(warning);
                }
            }

            if (!candidate.CanBuild)
            {
                candidate.SkipReason = "Cannot determine side extrusion width.";
            }

            return candidate;
        }

        private static IList<RecognizedContour> SolidContours(IList<RecognizedContour> contours, ProjectionType projection)
        {
            return contours
                .Where(x => x.SourceProjection == projection && x.Type == ContourType.SolidProfile && x.IsValidForRevit)
                .OrderByDescending(x => x.AreaMm2)
                .ToList();
        }

        private static void AddImmediateVoids(BuildCandidate candidate, RecognizedContour solid, IList<RecognizedContour> all)
        {
            foreach (RecognizedContour contour in all)
            {
                if (contour.SourceProjection == solid.SourceProjection
                    && contour.Type == ContourType.VoidProfile
                    && contour.ParentContourId == solid.Id
                    && contour.IsValidForRevit)
                {
                    candidate.VoidContours.Add(contour);
                }
            }
        }

        private static RecognizedContour FindWidthMatch(double widthMm, IList<RecognizedContour> candidates, DrawingToFamilySettings settings)
        {
            return FindMatch(widthMm, candidates, x => x.WidthMm, settings);
        }

        private static RecognizedContour FindHeightMatch(double valueMm, IList<RecognizedContour> candidates, DrawingToFamilySettings settings)
        {
            return FindMatch(valueMm, candidates, x => x.HeightMm, settings);
        }

        private static RecognizedContour FindMatch(
            double valueMm,
            IList<RecognizedContour> candidates,
            Func<RecognizedContour, double> selector,
            DrawingToFamilySettings settings)
        {
            if (valueMm <= 0 || candidates == null || candidates.Count == 0)
            {
                return null;
            }

            double absoluteTolerance = Math.Max(settings.ClosureToleranceMm, valueMm * 0.08);
            return candidates
                .Where(x => Math.Abs(selector(x) - valueMm) <= absoluteTolerance)
                .OrderBy(x => Math.Abs(selector(x) - valueMm))
                .ThenByDescending(x => x.AreaMm2)
                .FirstOrDefault();
        }

        private static bool IsRepresentedBy(RecognizedContour contour, IList<RecognizedContour> candidates, DrawingToFamilySettings settings)
        {
            if (contour == null)
            {
                return false;
            }

            return FindWidthMatch(contour.WidthMm, candidates, settings) != null
                || FindHeightMatch(contour.HeightMm, candidates, settings) != null;
        }

        private static double MatchConfidence(double a, double b, DrawingToFamilySettings settings)
        {
            double delta = Math.Abs(a - b);
            if (delta <= settings.ClosureToleranceMm)
            {
                return 1.0;
            }

            double relative = delta / Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1.0);
            if (relative <= 0.08)
            {
                return 0.75;
            }

            return 0.5;
        }

        private static string ShortId(RecognizedContour contour)
        {
            return contour == null ? "-" : contour.Id.ToString("N").Substring(0, 8);
        }

        private class ProjectionExtents
        {
            private double _minU = double.MaxValue;
            private double _maxU = double.MinValue;
            private double _minV = double.MaxValue;
            private double _maxV = double.MinValue;
            private readonly IList<DwgCurveEntity> _sourceEntities = new List<DwgCurveEntity>();

            public bool IsValid { get; private set; }
            public double MinU { get { return _minU; } }
            public double MaxU { get { return _maxU; } }
            public double MinV { get { return _minV; } }
            public double MaxV { get { return _maxV; } }
            public IEnumerable<DwgCurveEntity> SourceEntities { get { return _sourceEntities.Distinct(); } }

            public double WidthMm
            {
                get { return IsValid ? UnitUtilsExtensions.FeetToMm(Math.Abs(_maxU - _minU)) : 0; }
            }

            public double HeightMm
            {
                get { return IsValid ? UnitUtilsExtensions.FeetToMm(Math.Abs(_maxV - _minV)) : 0; }
            }

            public void Add(double u, double v, DwgCurveEntity entity)
            {
                _minU = Math.Min(_minU, u);
                _maxU = Math.Max(_maxU, u);
                _minV = Math.Min(_minV, v);
                _maxV = Math.Max(_maxV, v);
                if (entity != null && !_sourceEntities.Any(x => x.Id == entity.Id))
                {
                    _sourceEntities.Add(entity);
                }
                IsValid = true;
            }
        }

        private class AxisSample
        {
            public AxisSample(double minU, double maxU, double minV, double maxV, DwgCurveEntity entity)
            {
                MinU = minU;
                MaxU = maxU;
                MinV = minV;
                MaxV = maxV;
                Entity = entity;
            }

            public double MinU { get; private set; }
            public double MaxU { get; private set; }
            public double MinV { get; private set; }
            public double MaxV { get; private set; }
            public DwgCurveEntity Entity { get; private set; }
        }
    }
}
