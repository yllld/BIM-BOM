using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class NativeFeatureExtractionService
    {
        private const double MaxFrameHeightMm = 500.0;
        private const double MinimumNativeBuildSizeMm = 10.0;
        private const double MinimumPrimaryCircleRelativeSize = 0.04;
        private const int MaxSurfaceDetailFeatures = 48;
        private const int MaxIsometricDetailFeatures = 16;

        public IList<NativeGeometryFeature> Extract(
            IList<RecognizedContour> contours,
            IList<BuildCandidate> candidates,
            DrawingProjectionRegion plan,
            DrawingProjectionRegion front,
            DrawingProjectionRegion side,
            DrawingToFamilySettings settings,
            IList<string> warnings)
        {
            var features = new List<NativeGeometryFeature>();
            IList<RecognizedContour> all = contours ?? new List<RecognizedContour>();
            RecognizedContour planFootprint = SelectMainPlanFootprint(all, candidates);
            if (planFootprint == null || planFootprint.BoundingBox == null)
            {
                AddWarning(warnings, "Feature extraction: не найден главный footprint вида сверху. Native-стек не создан.");
                return features;
            }

            double mainWidthMm = Math.Max(planFootprint.WidthMm, 0);
            double mainDepthMm = Math.Max(planFootprint.HeightMm, 0);
            double totalHeightMm = DetermineOverallHeightMm(all, candidates, front, side);
            if (totalHeightMm <= settings.MinimumElementSizeMm)
            {
                AddWarning(warnings, "Feature extraction: не удалось надежно определить высоту. Native-стек не создан.");
                return features;
            }

            if (settings.MinimumElementSizeMm < MinimumNativeBuildSizeMm)
            {
                AddWarning(warnings, string.Format(
                    "Native feature extraction: пользовательский минимальный размер {0:0.#} мм используется для чтения линий, но тела меньше {1:0.#} мм не строятся.",
                    settings.MinimumElementSizeMm,
                    MinimumNativeBuildSizeMm));
            }

            ProjectionMetrics planMetrics = ProjectionMetrics.FromPlanFootprint(planFootprint);
            planMetrics.SetTarget(mainWidthMm, mainDepthMm, 0);
            ProjectionMetrics frontMetrics = ProjectionMetrics.FromMainContourOrAll(all, ProjectionType.Front);
            if (!frontMetrics.IsValid)
            {
                frontMetrics = ProjectionMetrics.FromRegion(front, ProjectionType.Front);
            }
            frontMetrics.SetTarget(mainWidthMm, 0, totalHeightMm);

            ProjectionMetrics sideMetrics = ProjectionMetrics.FromMainContourOrAll(all, ProjectionType.Side);
            if (!sideMetrics.IsValid)
            {
                sideMetrics = ProjectionMetrics.FromRegion(side, ProjectionType.Side);
            }
            sideMetrics.SetTarget(0, mainDepthMm, totalHeightMm);
            double frameHeightMm = DetectFrameHeightMm(all, frontMetrics, sideMetrics, totalHeightMm, settings, warnings);

            if (frameHeightMm > settings.MinimumElementSizeMm && frameHeightMm < totalHeightMm)
            {
                features.Add(CreateBoxFeature(
                    NativeFeatureType.BaseFrame,
                    "Lower band",
                    "Plan footprint + detected lower band in Front/Side",
                    0,
                    mainWidthMm,
                    0,
                    mainDepthMm,
                    0,
                    frameHeightMm,
                    planFootprint,
                    0.85));
            }

            features.Add(CreateBoxFeature(
                NativeFeatureType.MainContainer,
                "Main body",
                "Plan footprint + detected overall height",
                0,
                mainWidthMm,
                0,
                mainDepthMm,
                frameHeightMm > settings.MinimumElementSizeMm ? frameHeightMm : 0,
                totalHeightMm,
                planFootprint,
                0.9));

            foreach (NativeGeometryFeature cylinder in DetectCylinders(all, planFootprint, planMetrics, frontMetrics, sideMetrics, mainWidthMm, mainDepthMm, totalHeightMm, settings, warnings))
            {
                features.Add(cylinder);
            }

            foreach (NativeGeometryFeature detail in DetectPlanFrontSurfaceDetails(plan, front, mainWidthMm, mainDepthMm, totalHeightMm, settings, warnings))
            {
                features.Add(detail);
            }

            foreach (NativeGeometryFeature detail in DetectIsometricDetailBoxes(settings.IsometricRegion, front, mainWidthMm, mainDepthMm, totalHeightMm, settings, warnings))
            {
                features.Add(detail);
            }

            int index = 1;
            foreach (NativeGeometryFeature feature in features)
            {
                feature.Name = string.IsNullOrWhiteSpace(feature.Name)
                    ? "Feature " + index.ToString("000")
                    : feature.Name + " " + index.ToString("000");
                ValidateFeature(feature, settings);
                index++;
            }

            return features;
        }

        private static RecognizedContour SelectMainPlanFootprint(IList<RecognizedContour> contours, IList<BuildCandidate> candidates)
        {
            RecognizedContour synthetic = contours
                .Where(x => x.SourceProjection == ProjectionType.Plan
                    && x.Type == ContourType.SolidProfile
                    && x.IsValidForRevit
                    && string.Equals(x.SourceLayer, "Plan footprint", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.AreaMm2)
                .FirstOrDefault();
            if (synthetic != null)
            {
                return synthetic;
            }

            IList<RecognizedContour> planSolids = contours
                .Where(x => x.SourceProjection == ProjectionType.Plan
                    && x.Type == ContourType.SolidProfile
                    && x.IsValidForRevit
                    && !string.Equals(x.SourceLayer, "Plan footprint", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.AreaMm2)
                .ToList();

            RecognizedContour rectangular = planSolids
                .Where(IsRectangleLike)
                .OrderByDescending(x => x.AreaMm2)
                .FirstOrDefault();
            if (rectangular != null)
            {
                return rectangular;
            }

            if (planSolids.Count > 0)
            {
                return planSolids[0];
            }

            return candidates == null
                ? null
                : candidates
                    .Where(x => x.PrimaryContour != null && x.PrimaryContour.SourceProjection == ProjectionType.Plan)
                    .OrderByDescending(x => x.PrimaryContour.AreaMm2)
                    .Select(x => x.PrimaryContour)
                    .FirstOrDefault();
        }

        private static double DetermineOverallHeightMm(
            IList<RecognizedContour> contours,
            IList<BuildCandidate> candidates,
            DrawingProjectionRegion front,
            DrawingProjectionRegion side)
        {
            double candidateHeight = candidates == null
                ? 0
                : candidates.Where(x => x != null).Select(x => x.HeightMm).DefaultIfEmpty(0).Max();
            if (candidateHeight > 0)
            {
                return candidateHeight;
            }

            ProjectionMetrics frontMetrics = ProjectionMetrics.FromMainContourOrAll(contours, ProjectionType.Front);
            if (frontMetrics.IsValid)
            {
                return frontMetrics.HeightMm;
            }

            ProjectionMetrics sideMetrics = ProjectionMetrics.FromMainContourOrAll(contours, ProjectionType.Side);
            if (sideMetrics.IsValid)
            {
                return sideMetrics.HeightMm;
            }

            if (front != null && front.HeightMm > 0)
            {
                return front.HeightMm;
            }

            return side == null ? 0 : side.HeightMm;
        }

        private static double DetectFrameHeightMm(
            IList<RecognizedContour> contours,
            ProjectionMetrics frontMetrics,
            ProjectionMetrics sideMetrics,
            double totalHeightMm,
            DrawingToFamilySettings settings,
            IList<string> warnings)
        {
            double maxBandHeight = Math.Min(MaxFrameHeightMm, Math.Max(80.0, totalHeightMm * 0.2));
            double frontBand = DetectBottomBandHeight(contours, ProjectionType.Front, frontMetrics, maxBandHeight, settings);
            double sideBand = DetectBottomBandHeight(contours, ProjectionType.Side, sideMetrics, maxBandHeight, settings);

            if (frontBand > 0 && sideBand > 0)
            {
                double delta = Math.Abs(frontBand - sideBand);
                if (delta > Math.Max(settings.ClosureToleranceMm, Math.Min(frontBand, sideBand) * 0.2))
                {
                    AddWarning(warnings, string.Format("Lower band: Front/Side band heights differ ({0:0.#}/{1:0.#} mm). Using Front View value.", frontBand, sideBand));
                }

                return frontBand;
            }

            if (frontBand > 0)
            {
                return frontBand;
            }

            if (sideBand > 0)
            {
                return sideBand;
            }

            return 0;
        }

        private static double DetectBottomBandHeight(
            IList<RecognizedContour> contours,
            ProjectionType projection,
            ProjectionMetrics metrics,
            double maxBandHeight,
            DrawingToFamilySettings settings)
        {
            if (!metrics.IsValid)
            {
                return 0;
            }

            var candidates = contours
                .Where(x => x.SourceProjection == projection && x.BoundingBox != null && x.IsValidForRevit)
                .Select(x => new ContourBox(x, metrics))
                .Where(x => x.HeightMm >= settings.MinimumElementSizeMm
                    && x.HeightMm <= maxBandHeight
                    && x.WidthMm >= metrics.WidthMm * 0.45
                    && x.MinZMm <= Math.Max(settings.ClosureToleranceMm, maxBandHeight * 0.2))
                .OrderBy(x => x.HeightMm)
                .ToList();

            ContourBox band = candidates.FirstOrDefault();
            if (band != null)
            {
                return band.HeightMm;
            }

            double lineLevelHeight = DetectBottomBandFromHorizontalLevels(contours, projection, metrics, maxBandHeight, settings);
            return lineLevelHeight;
        }

        private static double DetectBottomBandFromHorizontalLevels(
            IList<RecognizedContour> contours,
            ProjectionType projection,
            ProjectionMetrics metrics,
            double maxBandHeight,
            DrawingToFamilySettings settings)
        {
            double zTolerance = Math.Max(settings.ClosureToleranceMm, 3.0);
            var levels = new List<double>();
            foreach (ContourBox box in contours
                .Where(x => x.SourceProjection == projection && x.BoundingBox != null && x.IsValidForRevit)
                .Select(x => new ContourBox(x, metrics)))
            {
                if (box.WidthMm < metrics.WidthMm * 0.45)
                {
                    continue;
                }

                if (box.HeightMm > Math.Max(settings.ClosureToleranceMm, 5.0))
                {
                    continue;
                }

                double level = box.CenterZMm;
                if (level < -zTolerance || level > maxBandHeight + zTolerance)
                {
                    continue;
                }

                if (!levels.Any(x => Math.Abs(x - level) <= zTolerance))
                {
                    levels.Add(level);
                }
            }

            levels.Sort();
            if (levels.Count < 2)
            {
                return 0;
            }

            double height = levels[1] - levels[0];
            return height >= settings.MinimumElementSizeMm && height <= maxBandHeight ? height : 0;
        }

        private static IEnumerable<NativeGeometryFeature> DetectCylinders(
            IList<RecognizedContour> contours,
            RecognizedContour planFootprint,
            ProjectionMetrics planMetrics,
            ProjectionMetrics frontMetrics,
            ProjectionMetrics sideMetrics,
            double mainWidthMm,
            double mainDepthMm,
            double totalHeightMm,
            DrawingToFamilySettings settings,
            IList<string> warnings)
        {
            IList<ContourBox> frontBoxes = BoxesForProjection(contours, ProjectionType.Front, frontMetrics, settings);
            IList<ContourBox> planBoxes = BoxesForProjection(contours, ProjectionType.Plan, planMetrics, settings)
                .Where(x => x.Contour.Id != planFootprint.Id)
                .ToList();
            IList<ContourBox> sideBoxes = BoxesForProjection(contours, ProjectionType.Side, sideMetrics, settings);

            foreach (ContourBox circle in PrimaryCircleBoxes(sideBoxes, mainDepthMm, totalHeightMm, settings))
            {
                double diameterMm = (circle.WidthMm + circle.HeightMm) * 0.5;
                double centerY = circle.CenterXMm;
                double centerZ = circle.CenterZMm;
                if (!IsInsideMainBody(centerY, centerZ, mainDepthMm, totalHeightMm, diameterMm, settings))
                {
                    continue;
                }

                ContourBox xRangeBox = FindCylinderXRange(circle, frontBoxes, planBoxes, diameterMm, centerY, centerZ, mainWidthMm, settings);
                var feature = new NativeGeometryFeature
                {
                    FeatureType = NativeFeatureType.VoidCylinder,
                    Name = "Side opening",
                    SourceDescription = xRangeBox == null ? "Side View circle without confirmed Plan/Front match" : "Side View circular face feature",
                    Axis = BuildDirection.ExtrudeX_FromSide,
                    XMinMm = 0,
                    XMaxMm = mainWidthMm,
                    YMinMm = centerY - diameterMm * 0.5,
                    YMaxMm = centerY + diameterMm * 0.5,
                    ZMinMm = centerZ - diameterMm * 0.5,
                    ZMaxMm = centerZ + diameterMm * 0.5,
                    DiameterMm = diameterMm,
                    Confidence = xRangeBox == null ? 0.45 : xRangeBox.SourceProjection == ProjectionType.Front ? 0.85 : 0.65,
                    CanBuild = true,
                    BuildMethod = "Face circular opening candidate along X"
                };
                feature.SourceContourIds.Add(circle.Contour.Id);
                if (xRangeBox != null)
                {
                    feature.SourceContourIds.Add(xRangeBox.Contour.Id);
                }
                feature.Warnings.Add(xRangeBox == null
                    ? "Side circle length was not confirmed in Front/Plan; a full-width void extrusion will be attempted."
                    : "Side circle will be built as a void extrusion along X.");
                if (xRangeBox != null && xRangeBox.SourceProjection == ProjectionType.Plan)
                {
                    feature.Warnings.Add("Side circle had only a Plan-range hint; the void extrusion may need manual review.");
                }

                yield return feature;
            }

            foreach (ContourBox circle in PrimaryCircleBoxes(frontBoxes, mainWidthMm, totalHeightMm, settings))
            {
                double diameterMm = (circle.WidthMm + circle.HeightMm) * 0.5;
                double centerX = circle.CenterXMm;
                double centerZ = circle.CenterZMm;
                if (!IsInsideMainBody(centerX, centerZ, mainWidthMm, totalHeightMm, diameterMm, settings))
                {
                    continue;
                }

                ContourBox yRangeBox = FindCylinderYRange(circle, planBoxes, sideBoxes, diameterMm, centerX, centerZ, mainDepthMm, settings);
                var feature = new NativeGeometryFeature
                {
                    FeatureType = NativeFeatureType.VoidCylinder,
                    Name = "Front opening",
                    SourceDescription = yRangeBox == null ? "Front View circle without confirmed Plan/Side match" : "Front View circular face feature",
                    Axis = BuildDirection.ExtrudeY_FromFront,
                    XMinMm = centerX - diameterMm * 0.5,
                    XMaxMm = centerX + diameterMm * 0.5,
                    YMinMm = 0,
                    YMaxMm = mainDepthMm,
                    ZMinMm = centerZ - diameterMm * 0.5,
                    ZMaxMm = centerZ + diameterMm * 0.5,
                    DiameterMm = diameterMm,
                    Confidence = yRangeBox == null ? 0.45 : 0.75,
                    CanBuild = true,
                    BuildMethod = "Face circular opening candidate along Y"
                };
                feature.SourceContourIds.Add(circle.Contour.Id);
                if (yRangeBox != null)
                {
                    feature.SourceContourIds.Add(yRangeBox.Contour.Id);
                }
                feature.Warnings.Add(yRangeBox == null
                    ? "Front circle depth was not confirmed in Plan/Side; a full-depth void extrusion will be attempted."
                    : "Front circle will be built as a void extrusion along Y.");

                yield return feature;
            }

            foreach (ContourBox circle in PrimaryCircleBoxes(planBoxes, mainWidthMm, mainDepthMm, settings))
            {
                double diameterMm = (circle.WidthMm + circle.HeightMm) * 0.5;
                double centerX = circle.CenterXMm;
                double centerY = circle.CenterYMm;
                if (!IsInsideMainBody(centerX, centerY, mainWidthMm, mainDepthMm, diameterMm, settings))
                {
                    continue;
                }

                ContourBox zRangeBox = FindCylinderZRange(circle, frontBoxes, sideBoxes, diameterMm, centerX, centerY, totalHeightMm, settings);
                var feature = new NativeGeometryFeature
                {
                    FeatureType = NativeFeatureType.VoidCylinder,
                    Name = "Plan opening",
                    SourceDescription = zRangeBox == null ? "Plan View circle without confirmed height" : "Plan View circular opening candidate",
                    Axis = BuildDirection.ExtrudeZ_FromPlan,
                    XMinMm = centerX - diameterMm * 0.5,
                    XMaxMm = centerX + diameterMm * 0.5,
                    YMinMm = centerY - diameterMm * 0.5,
                    YMaxMm = centerY + diameterMm * 0.5,
                    ZMinMm = 0,
                    ZMaxMm = zRangeBox == null ? totalHeightMm : Math.Min(totalHeightMm, Math.Max(0, zRangeBox.MaxZMm)),
                    DiameterMm = diameterMm,
                    Confidence = zRangeBox == null ? 0.45 : 0.75,
                    CanBuild = true,
                    BuildMethod = "Plan circular opening candidate along Z"
                };
                feature.SourceContourIds.Add(circle.Contour.Id);
                if (zRangeBox != null)
                {
                    feature.SourceContourIds.Add(zRangeBox.Contour.Id);
                }
                feature.Warnings.Add(zRangeBox == null
                    ? "Plan circle height was not confirmed in Front/Side; a full-height void extrusion will be attempted."
                    : "Plan circle will be built as a void extrusion along Z.");

                yield return feature;
            }
        }

        private static IList<ContourBox> CircleBoxes(IList<ContourBox> boxes, double maxA, double maxB, DrawingToFamilySettings settings)
        {
            double minimum = NativeMinimumSizeMm(settings);
            double maxDiameter = Math.Max(minimum, Math.Min(maxA, maxB) * 0.8);
            return (boxes ?? new List<ContourBox>())
                .Where(x => x.Contour.IsClosed
                    && IsCircleLike(x)
                    && x.WidthMm >= minimum
                    && x.HeightMm >= minimum
                    && Math.Max(x.WidthMm, x.HeightMm) <= maxDiameter)
                .OrderByDescending(x => x.WidthMm)
                .ToList();
        }

        private static IList<ContourBox> PrimaryCircleBoxes(IList<ContourBox> boxes, double maxA, double maxB, DrawingToFamilySettings settings)
        {
            double reference = Math.Max(1.0, Math.Min(maxA, maxB));
            double minimumDiameter = Math.Max(
                NativeMinimumSizeMm(settings),
                reference * MinimumPrimaryCircleRelativeSize);
            double toleranceMm = settings == null ? 2.0 : Math.Max(2.0, settings.ClosureToleranceMm * 3.0);
            var selected = new List<ContourBox>();

            foreach (ContourBox circle in CircleBoxes(boxes, maxA, maxB, settings)
                .Where(x => Math.Max(x.WidthMm, x.HeightMm) >= minimumDiameter)
                .OrderByDescending(x => x.WidthMm * x.HeightMm))
            {
                bool duplicate = selected.Any(x => SameCircleCenter(x, circle, toleranceMm)
                    && Math.Abs(x.WidthMm - circle.WidthMm) <= Math.Max(toleranceMm, Math.Max(x.WidthMm, circle.WidthMm) * 0.35));
                if (!duplicate)
                {
                    selected.Add(circle);
                }
            }

            return selected;
        }

        private static bool IsInsideMainBody(double horizontalMm, double verticalMm, double maxHorizontalMm, double maxVerticalMm, double diameterMm, DrawingToFamilySettings settings)
        {
            double tolerance = Math.Max(settings == null ? 2.0 : settings.ClosureToleranceMm * 3.0, Math.Max(diameterMm * 0.5, NativeMinimumSizeMm(settings)));
            return horizontalMm >= -tolerance
                && horizontalMm <= maxHorizontalMm + tolerance
                && verticalMm >= -tolerance
                && verticalMm <= maxVerticalMm + tolerance;
        }

        private static double NativeMinimumSizeMm(DrawingToFamilySettings settings)
        {
            return Math.Max(MinimumNativeBuildSizeMm, settings == null ? MinimumNativeBuildSizeMm : settings.MinimumElementSizeMm);
        }

        private static bool SameCircleCenter(ContourBox first, ContourBox second, double toleranceMm)
        {
            if (first == null || second == null || first.SourceProjection != second.SourceProjection)
            {
                return false;
            }

            double dx = first.CenterXMm - second.CenterXMm;
            double dy = first.SourceProjection == ProjectionType.Plan
                ? first.CenterYMm - second.CenterYMm
                : first.CenterZMm - second.CenterZMm;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            double diameter = Math.Max(Math.Max(first.WidthMm, first.HeightMm), Math.Max(second.WidthMm, second.HeightMm));
            return distance <= Math.Max(toleranceMm, diameter * 0.18);
        }

        private static IList<ContourBox> BoxesForProjection(
            IList<RecognizedContour> contours,
            ProjectionType projection,
            ProjectionMetrics metrics,
            DrawingToFamilySettings settings)
        {
            double toleranceMm = settings == null ? 2.0 : Math.Max(2.0, settings.ClosureToleranceMm * 2.0);
            return (contours ?? new List<RecognizedContour>())
                .Where(x => x.SourceProjection == projection
                    && x.BoundingBox != null
                    && x.IsValidForRevit)
                .Select(x => new ContourBox(x, metrics))
                .Where(x => IsInsideProjectionWindow(x, metrics, toleranceMm))
                .ToList();
        }

        private static bool IsInsideProjectionWindow(ContourBox box, ProjectionMetrics metrics, double toleranceMm)
        {
            if (box == null || metrics == null || !metrics.IsValid)
            {
                return false;
            }

            double horizontalLimit = Math.Max(metrics.ModelWidthMm, metrics.ModelDepthMm);
            double verticalLimit = box.SourceProjection == ProjectionType.Plan
                ? metrics.ModelDepthMm
                : metrics.ModelHeightMm;

            if (horizontalLimit <= 0 || verticalLimit <= 0)
            {
                return true;
            }

            bool horizontalInside = box.MinXMm >= -toleranceMm
                && box.MaxXMm <= horizontalLimit + toleranceMm;
            bool verticalInside = box.SourceProjection == ProjectionType.Plan
                ? box.MinYMm >= -toleranceMm && box.MaxYMm <= verticalLimit + toleranceMm
                : box.MinZMm >= -toleranceMm && box.MaxZMm <= verticalLimit + toleranceMm;

            return horizontalInside && verticalInside;
        }

        private static ContourBox FindCylinderXRange(
            ContourBox circle,
            IList<ContourBox> frontBoxes,
            IList<ContourBox> planBoxes,
            double diameterMm,
            double centerY,
            double centerZ,
            double mainWidthMm,
            DrawingToFamilySettings settings)
        {
            double minimum = NativeMinimumSizeMm(settings);
            double zTolerance = Math.Max(settings.ClosureToleranceMm * 3.0, diameterMm * 0.45);
            ContourBox front = frontBoxes
                .Where(x => x.WidthMm > minimum
                    && x.WidthMm < mainWidthMm * 0.55
                    && x.HeightMm >= diameterMm * 0.35
                    && x.HeightMm <= diameterMm * 1.8
                    && Math.Abs(x.CenterZMm - centerZ) <= zTolerance)
                .OrderBy(x => Math.Abs(x.CenterZMm - centerZ))
                .ThenByDescending(x => x.AreaMm2)
                .FirstOrDefault();
            if (front != null)
            {
                return front;
            }

            double yTolerance = Math.Max(settings.ClosureToleranceMm * 3.0, diameterMm * 0.6);
            return planBoxes
                .Where(x => x.WidthMm > minimum
                    && x.WidthMm < mainWidthMm * 0.55
                    && Math.Abs(x.CenterYMm - centerY) <= yTolerance)
                .OrderBy(x => Math.Abs(x.CenterYMm - centerY))
                .ThenByDescending(x => x.AreaMm2)
                .FirstOrDefault();
        }

        private static ContourBox FindCylinderYRange(
            ContourBox circle,
            IList<ContourBox> planBoxes,
            IList<ContourBox> sideBoxes,
            double diameterMm,
            double centerX,
            double centerZ,
            double mainDepthMm,
            DrawingToFamilySettings settings)
        {
            double minimum = NativeMinimumSizeMm(settings);
            double xTolerance = Math.Max(settings.ClosureToleranceMm * 3.0, diameterMm * 0.6);
            ContourBox plan = planBoxes
                .Where(x => Math.Abs(x.CenterXMm - centerX) <= xTolerance
                    && x.WidthMm >= diameterMm * 0.35
                    && x.WidthMm <= diameterMm * 1.8
                    && Math.Abs(x.MaxYMm - x.MinYMm) > minimum
                    && Math.Abs(x.MaxYMm - x.MinYMm) <= Math.Max(mainDepthMm, minimum) * 1.1)
                .OrderBy(x => Math.Abs(x.CenterXMm - centerX))
                .ThenByDescending(x => Math.Abs(x.MaxYMm - x.MinYMm))
                .FirstOrDefault();
            if (plan != null)
            {
                return plan;
            }

            double zTolerance = Math.Max(settings.ClosureToleranceMm * 3.0, diameterMm * 0.45);
            return sideBoxes
                .Where(x => Math.Abs(x.CenterZMm - centerZ) <= zTolerance
                    && x.HeightMm >= diameterMm * 0.35
                    && x.HeightMm <= diameterMm * 1.8
                    && x.WidthMm > minimum
                    && x.WidthMm <= Math.Max(mainDepthMm, minimum) * 1.1)
                .OrderBy(x => Math.Abs(x.CenterZMm - centerZ))
                .ThenByDescending(x => x.WidthMm)
                .FirstOrDefault();
        }

        private static ContourBox FindCylinderZRange(
            ContourBox circle,
            IList<ContourBox> frontBoxes,
            IList<ContourBox> sideBoxes,
            double diameterMm,
            double centerX,
            double centerY,
            double totalHeightMm,
            DrawingToFamilySettings settings)
        {
            double minimum = NativeMinimumSizeMm(settings);
            double xTolerance = Math.Max(settings.ClosureToleranceMm * 3.0, diameterMm * 0.6);
            ContourBox front = frontBoxes
                .Where(x => Math.Abs(x.CenterXMm - centerX) <= xTolerance
                    && x.WidthMm >= diameterMm * 0.35
                    && x.WidthMm <= diameterMm * 1.8
                    && x.HeightMm > minimum
                    && x.HeightMm <= Math.Max(totalHeightMm, minimum) * 1.1)
                .OrderBy(x => Math.Abs(x.CenterXMm - centerX))
                .ThenByDescending(x => x.HeightMm)
                .FirstOrDefault();
            if (front != null)
            {
                return front;
            }

            double yTolerance = Math.Max(settings.ClosureToleranceMm * 3.0, diameterMm * 0.6);
            return sideBoxes
                .Where(x => Math.Abs(x.CenterXMm - centerY) <= yTolerance
                    && x.WidthMm >= diameterMm * 0.35
                    && x.WidthMm <= diameterMm * 1.8
                    && x.HeightMm > minimum
                    && x.HeightMm <= Math.Max(totalHeightMm, minimum) * 1.1)
                .OrderBy(x => Math.Abs(x.CenterXMm - centerY))
                .ThenByDescending(x => x.HeightMm)
                .FirstOrDefault();
        }

        private static IEnumerable<NativeGeometryFeature> DetectPlanFrontSurfaceDetails(
            DrawingProjectionRegion planRegion,
            DrawingProjectionRegion frontRegion,
            double mainWidthMm,
            double mainDepthMm,
            double totalHeightMm,
            DrawingToFamilySettings settings,
            IList<string> warnings)
        {
            var details = new List<NativeGeometryFeature>();
            if (planRegion == null
                || !planRegion.IsValid
                || frontRegion == null
                || !frontRegion.IsValid
                || mainWidthMm <= 0
                || mainDepthMm <= 0
                || totalHeightMm <= 0)
            {
                return details;
            }

            double minimum = NativeMinimumSizeMm(settings);
            double thicknessMm = Clamp(Math.Min(Math.Min(mainWidthMm, mainDepthMm), totalHeightMm) * 0.012, minimum, 25.0);
            double protrusionMm = Clamp(mainDepthMm * 0.04, minimum, 45.0);
            double minPlanSegmentMm = Math.Max(120.0, Math.Min(mainWidthMm, mainDepthMm) * 0.18);
            double minFrontSegmentMm = Math.Max(150.0, mainWidthMm * 0.18);
            double matchToleranceMm = Math.Max(settings == null ? 12.0 : settings.ClosureToleranceMm * 8.0, thicknessMm * 2.0);

            IList<PlanReferenceSegment> planSegments = BuildPlanReferenceSegments(planRegion, mainWidthMm, mainDepthMm, minPlanSegmentMm, settings);
            IList<DetailReferenceSegment> frontSegments = BuildFrontReferenceSegments(frontRegion, mainWidthMm, totalHeightMm, minFrontSegmentMm, settings);
            if (planSegments.Count == 0)
            {
                AddWarning(warnings, "Surface details: в Plan View не найдены достаточно длинные линии для построения ребер/стоек поверх основного тела.");
                return details;
            }

            int unmatchedFront = 0;
            foreach (DetailReferenceSegment front in frontSegments
                .OrderByDescending(x => x.Horizontal ? x.MaxX - x.MinX : x.MaxZ - x.MinZ))
            {
                if (details.Count >= MaxSurfaceDetailFeatures)
                {
                    break;
                }

                if (!HasPlanSupportForFrontReference(front, planSegments, matchToleranceMm))
                {
                    unmatchedFront++;
                    continue;
                }

                NativeGeometryFeature feature;
                if (front.Horizontal)
                {
                    double xMin = Clamp(front.MinX, 0, mainWidthMm);
                    double xMax = Clamp(front.MaxX, 0, mainWidthMm);
                    double zMin = Clamp(front.CenterZ - thicknessMm * 0.5, 0, totalHeightMm);
                    double zMax = Clamp(front.CenterZ + thicknessMm * 0.5, 0, totalHeightMm);
                    feature = CreateBoxFeature(
                        NativeFeatureType.SurfaceDetail,
                        "Front rail",
                        "Plan-confirmed Front View horizontal line",
                        xMin,
                        xMax,
                        -protrusionMm,
                        0,
                        zMin,
                        zMax,
                        null,
                        0.72);
                }
                else
                {
                    double xMin = Clamp(front.CenterX - thicknessMm * 0.5, 0, mainWidthMm);
                    double xMax = Clamp(front.CenterX + thicknessMm * 0.5, 0, mainWidthMm);
                    double zMin = Clamp(front.MinZ, 0, totalHeightMm);
                    double zMax = Clamp(front.MaxZ, 0, totalHeightMm);
                    feature = CreateBoxFeature(
                        NativeFeatureType.SurfaceDetail,
                        "Front post",
                        "Plan-confirmed Front View vertical line",
                        xMin,
                        xMax,
                        -protrusionMm,
                        0,
                        zMin,
                        zMax,
                        null,
                        0.7);
                }

                feature.BuildMethod = "Surface detail box from Plan + Front matched lines";
                if (!IsDuplicateSurfaceDetail(details, feature, matchToleranceMm))
                {
                    details.Add(feature);
                }
            }

            int topLimit = Math.Max(6, MaxSurfaceDetailFeatures / 3);
            foreach (PlanReferenceSegment plan in planSegments
                .Where(x => x.Horizontal)
                .OrderByDescending(x => x.LengthMm))
            {
                if (details.Count >= MaxSurfaceDetailFeatures || topLimit <= 0)
                {
                    break;
                }

                NativeGeometryFeature top = CreateBoxFeature(
                    NativeFeatureType.SurfaceDetail,
                    "Plan top rib",
                    "Long Plan View line raised as top surface rib",
                    Clamp(plan.MinX, 0, mainWidthMm),
                    Clamp(plan.MaxX, 0, mainWidthMm),
                    Clamp(plan.CenterY - thicknessMm * 0.5, 0, mainDepthMm),
                    Clamp(plan.CenterY + thicknessMm * 0.5, 0, mainDepthMm),
                    totalHeightMm,
                    totalHeightMm + thicknessMm,
                    null,
                    0.58);
                top.BuildMethod = "Top surface rib from long Plan View line";
                if (!IsDuplicateSurfaceDetail(details, top, matchToleranceMm))
                {
                    details.Add(top);
                    topLimit--;
                }
            }

            if (details.Count > 0)
            {
                AddWarning(warnings, "Surface details: построено " + details.Count + " тонких ребер/стоек по длинным линиям Plan View с проверкой Front View. Они не меняют основные габариты.");
            }
            else
            {
                AddWarning(warnings, "Surface details: подходящие Plan/Front линии не найдены, поэтому поверхностные детали не построены.");
            }

            if (unmatchedFront > 0)
            {
                AddWarning(warnings, "Surface details: " + unmatchedFront + " Front View line(s) ignored because Plan View did not confirm their X position/range.");
            }

            return details;
        }

        private static IList<PlanReferenceSegment> BuildPlanReferenceSegments(
            DrawingProjectionRegion planRegion,
            double mainWidthMm,
            double mainDepthMm,
            double minSegmentMm,
            DrawingToFamilySettings settings)
        {
            var result = new List<PlanReferenceSegment>();
            if (planRegion == null || !planRegion.IsValid || planRegion.Entities == null)
            {
                return result;
            }

            double axisToleranceMm = Math.Max(settings == null ? 2.0 : settings.ClosureToleranceMm * 4.0, 8.0);
            foreach (DwgCurveEntity entity in planRegion.Entities)
            {
                if (entity == null
                    || entity.Points == null
                    || entity.Points.Count < 2
                    || entity.IsIgnored
                    || entity.RecognitionRole != RecognitionRole.MainGeometry)
                {
                    continue;
                }

                for (int i = 1; i < entity.Points.Count; i++)
                {
                    double x1;
                    double y1;
                    double x2;
                    double y2;
                    if (!TryMapRegionPointToModel(planRegion, entity.Points[i - 1], mainWidthMm, mainDepthMm, 0.05, out x1, out y1)
                        || !TryMapRegionPointToModel(planRegion, entity.Points[i], mainWidthMm, mainDepthMm, 0.05, out x2, out y2))
                    {
                        continue;
                    }

                    double dx = Math.Abs(x2 - x1);
                    double dy = Math.Abs(y2 - y1);
                    if (dx >= minSegmentMm && dy <= Math.Max(axisToleranceMm, dx * 0.04))
                    {
                        result.Add(new PlanReferenceSegment(Math.Min(x1, x2), Math.Max(x1, x2), (y1 + y2) * 0.5, (y1 + y2) * 0.5, true));
                    }
                    else if (dy >= minSegmentMm && dx <= Math.Max(axisToleranceMm, dy * 0.04))
                    {
                        result.Add(new PlanReferenceSegment((x1 + x2) * 0.5, (x1 + x2) * 0.5, Math.Min(y1, y2), Math.Max(y1, y2), false));
                    }
                }
            }

            return result;
        }

        private static bool HasPlanSupportForFrontReference(DetailReferenceSegment front, IList<PlanReferenceSegment> planSegments, double toleranceMm)
        {
            if (front == null || planSegments == null || planSegments.Count == 0)
            {
                return false;
            }

            if (front.Horizontal)
            {
                foreach (PlanReferenceSegment plan in planSegments.Where(x => x.Horizontal))
                {
                    double overlap = OverlapLength(front.MinX, front.MaxX, plan.MinX, plan.MaxX);
                    double required = Math.Min(front.MaxX - front.MinX, plan.MaxX - plan.MinX) * 0.4;
                    if (overlap >= Math.Max(toleranceMm, required))
                    {
                        return true;
                    }
                }

                return false;
            }

            foreach (PlanReferenceSegment plan in planSegments)
            {
                if (plan.Horizontal)
                {
                    if (front.CenterX >= plan.MinX - toleranceMm && front.CenterX <= plan.MaxX + toleranceMm)
                    {
                        return true;
                    }
                }
                else if (Math.Abs(front.CenterX - plan.CenterX) <= toleranceMm)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDuplicateSurfaceDetail(IList<NativeGeometryFeature> details, NativeGeometryFeature feature, double toleranceMm)
        {
            if (details == null || feature == null)
            {
                return false;
            }

            bool horizontal = feature.WidthMm >= feature.HeightMm;
            double centerX = (feature.XMinMm + feature.XMaxMm) * 0.5;
            double centerY = (feature.YMinMm + feature.YMaxMm) * 0.5;
            double centerZ = (feature.ZMinMm + feature.ZMaxMm) * 0.5;
            foreach (NativeGeometryFeature existing in details)
            {
                if (existing == null || existing.FeatureType != NativeFeatureType.SurfaceDetail)
                {
                    continue;
                }

                bool existingHorizontal = existing.WidthMm >= existing.HeightMm;
                if (existingHorizontal != horizontal)
                {
                    continue;
                }

                double existingCenterX = (existing.XMinMm + existing.XMaxMm) * 0.5;
                double existingCenterY = (existing.YMinMm + existing.YMaxMm) * 0.5;
                double existingCenterZ = (existing.ZMinMm + existing.ZMaxMm) * 0.5;
                if (Math.Abs(existingCenterX - centerX) <= toleranceMm
                    && Math.Abs(existingCenterY - centerY) <= toleranceMm
                    && Math.Abs(existingCenterZ - centerZ) <= toleranceMm)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<NativeGeometryFeature> DetectIsometricDetailBoxes(
            DrawingProjectionRegion isometricRegion,
            DrawingProjectionRegion frontRegion,
            double mainWidthMm,
            double mainDepthMm,
            double totalHeightMm,
            DrawingToFamilySettings settings,
            IList<string> warnings)
        {
            var details = new List<NativeGeometryFeature>();
            if (settings == null
                || !settings.UseIsometricReference
                || isometricRegion == null
                || !isometricRegion.IsValid
                || isometricRegion.Entities == null
                || isometricRegion.Entities.Count == 0
                || mainWidthMm <= 0
                || mainDepthMm <= 0
                || totalHeightMm <= 0)
            {
                return details;
            }

            AddWarning(warnings, "3D/ISO reference mode is approximate: ISO lines create safe thin detail boxes on the main Plan-based body and do not change the main dimensions.");

            double minimum = NativeMinimumSizeMm(settings);
            double thicknessMm = Clamp(Math.Min(mainWidthMm, totalHeightMm) * 0.012, minimum, Math.Max(minimum, 35.0));
            double protrusionMm = Clamp(mainDepthMm * 0.035, minimum, Math.Max(minimum, 50.0));
            double minSegmentMm = Math.Max(minimum * 3.0, Math.Min(mainWidthMm, totalHeightMm) * 0.08);
            double orientationToleranceMm = Math.Max(settings.ClosureToleranceMm * 6.0, thicknessMm * 2.0);
            double duplicateToleranceMm = Math.Max(settings.ClosureToleranceMm * 4.0, thicknessMm * 1.75);
            double matchToleranceMm = Math.Max(settings.ClosureToleranceMm * 8.0, Math.Max(thicknessMm * 2.5, Math.Min(mainWidthMm, totalHeightMm) * 0.035));
            IList<DetailReferenceSegment> frontReferences = BuildFrontReferenceSegments(frontRegion, mainWidthMm, totalHeightMm, minSegmentMm, settings);
            if (frontReferences.Count == 0)
            {
                AddWarning(warnings, "3D/ISO reference mode: не найдены длинные линии Front View для проверки ISO-деталей. ISO-детали не будут построены.");
                return details;
            }

            int unmatched = 0;

            foreach (DwgCurveEntity entity in isometricRegion.Entities)
            {
                if (!IsIsometricDetailEntity(entity, settings))
                {
                    continue;
                }

                for (int i = 1; i < entity.Points.Count; i++)
                {
                    double x1;
                    double z1;
                    double x2;
                    double z2;
                    if (!TryMapIsoPointToModel(isometricRegion, entity.Points[i - 1], mainWidthMm, totalHeightMm, out x1, out z1)
                        || !TryMapIsoPointToModel(isometricRegion, entity.Points[i], mainWidthMm, totalHeightMm, out x2, out z2))
                    {
                        continue;
                    }

                    double dx = Math.Abs(x2 - x1);
                    double dz = Math.Abs(z2 - z1);
                    if (dx < minSegmentMm && dz < minSegmentMm)
                    {
                        continue;
                    }

                    NativeGeometryFeature feature = null;
                    if (dx >= minSegmentMm
                        && (dx >= dz || dz <= Math.Max(orientationToleranceMm, dx * 0.75)))
                    {
                        double centerZ = Clamp((z1 + z2) * 0.5, 0, totalHeightMm);
                        feature = CreateBoxFeature(
                            NativeFeatureType.IsoDetail,
                            "ISO horizontal detail",
                            "Optional 3D/ISO reference line",
                            Clamp(Math.Min(x1, x2), 0, mainWidthMm),
                            Clamp(Math.Max(x1, x2), 0, mainWidthMm),
                            -protrusionMm,
                            0,
                            Clamp(centerZ - thicknessMm * 0.5, 0, totalHeightMm),
                            Clamp(centerZ + thicknessMm * 0.5, 0, totalHeightMm),
                            null,
                            0.35);
                    }
                    else if (dz >= minSegmentMm)
                    {
                        double centerX = Clamp((x1 + x2) * 0.5, 0, mainWidthMm);
                        feature = CreateBoxFeature(
                            NativeFeatureType.IsoDetail,
                            "ISO vertical detail",
                            "Optional 3D/ISO reference line",
                            Clamp(centerX - thicknessMm * 0.5, 0, mainWidthMm),
                            Clamp(centerX + thicknessMm * 0.5, 0, mainWidthMm),
                            -protrusionMm,
                            0,
                            Clamp(Math.Min(z1, z2), 0, totalHeightMm),
                            Clamp(Math.Max(z1, z2), 0, totalHeightMm),
                            null,
                            0.35);
                    }

                    if (feature == null)
                    {
                        continue;
                    }

                    feature.BuildMethod = "Thin detail box from optional 3D/ISO reference";
                    if (feature.WidthMm < minimum || feature.DepthMm < minimum || feature.HeightMm < minimum)
                    {
                        continue;
                    }

                    if (!HasMatchingFrontReference(feature, frontReferences, matchToleranceMm))
                    {
                        unmatched++;
                        continue;
                    }

                    feature.SourceDescription = "Optional 3D/ISO reference line matched by Front View";
                    feature.Confidence = 0.65;

                    if (!IsDuplicateIsoDetail(details, feature, duplicateToleranceMm))
                    {
                        details.Add(feature);
                    }

                    if (details.Count >= MaxIsometricDetailFeatures)
                    {
                        AddWarning(warnings, "3D/ISO reference mode reached the MVP safety limit of " + MaxIsometricDetailFeatures + " detail boxes. Remaining ISO lines were ignored.");
                        return details;
                    }
                }
            }

            if (unmatched > 0)
            {
                AddWarning(warnings, "3D/ISO reference mode ignored " + unmatched + " ISO detail candidate(s) because no matching Front View line was found.");
            }

            if (details.Count == 0)
            {
                AddWarning(warnings, "3D/ISO reference mode did not find safe horizontal/vertical detail lines to build.");
            }

            return details;
        }

        private static bool IsIsometricDetailEntity(DwgCurveEntity entity, DrawingToFamilySettings settings)
        {
            if (entity == null || entity.Points == null || entity.Points.Count < 2 || entity.IsIgnored)
            {
                return false;
            }

            if (entity.LengthMm < NativeMinimumSizeMm(settings))
            {
                return false;
            }

            return entity.RecognitionRole == RecognitionRole.MainGeometry
                || entity.RecognitionRole == RecognitionRole.Unknown;
        }

        private static IList<DetailReferenceSegment> BuildFrontReferenceSegments(
            DrawingProjectionRegion frontRegion,
            double mainWidthMm,
            double totalHeightMm,
            double minSegmentMm,
            DrawingToFamilySettings settings)
        {
            var result = new List<DetailReferenceSegment>();
            if (frontRegion == null || !frontRegion.IsValid || frontRegion.Entities == null)
            {
                return result;
            }

            double axisToleranceMm = Math.Max(settings == null ? 2.0 : settings.ClosureToleranceMm * 4.0, 8.0);
            foreach (DwgCurveEntity entity in frontRegion.Entities)
            {
                if (entity == null
                    || entity.Points == null
                    || entity.Points.Count < 2
                    || entity.IsIgnored
                    || entity.RecognitionRole != RecognitionRole.MainGeometry)
                {
                    continue;
                }

                for (int i = 1; i < entity.Points.Count; i++)
                {
                    double x1;
                    double z1;
                    double x2;
                    double z2;
                    if (!TryMapRegionPointToModel(frontRegion, entity.Points[i - 1], mainWidthMm, totalHeightMm, 0.05, out x1, out z1)
                        || !TryMapRegionPointToModel(frontRegion, entity.Points[i], mainWidthMm, totalHeightMm, 0.05, out x2, out z2))
                    {
                        continue;
                    }

                    double dx = Math.Abs(x2 - x1);
                    double dz = Math.Abs(z2 - z1);
                    if (dx >= minSegmentMm && dz <= Math.Max(axisToleranceMm, dx * 0.04))
                    {
                        result.Add(new DetailReferenceSegment(Math.Min(x1, x2), Math.Max(x1, x2), (z1 + z2) * 0.5, (z1 + z2) * 0.5, true));
                    }
                    else if (dz >= minSegmentMm && dx <= Math.Max(axisToleranceMm, dz * 0.04))
                    {
                        result.Add(new DetailReferenceSegment((x1 + x2) * 0.5, (x1 + x2) * 0.5, Math.Min(z1, z2), Math.Max(z1, z2), false));
                    }
                }
            }

            return result;
        }

        private static bool HasMatchingFrontReference(NativeGeometryFeature feature, IList<DetailReferenceSegment> references, double toleranceMm)
        {
            if (feature == null || references == null || references.Count == 0)
            {
                return false;
            }

            bool horizontal = feature.WidthMm >= feature.HeightMm;
            double minX = Math.Min(feature.XMinMm, feature.XMaxMm);
            double maxX = Math.Max(feature.XMinMm, feature.XMaxMm);
            double minZ = Math.Min(feature.ZMinMm, feature.ZMaxMm);
            double maxZ = Math.Max(feature.ZMinMm, feature.ZMaxMm);
            double centerX = (minX + maxX) * 0.5;
            double centerZ = (minZ + maxZ) * 0.5;

            foreach (DetailReferenceSegment reference in references)
            {
                if (reference.Horizontal != horizontal)
                {
                    continue;
                }

                if (horizontal)
                {
                    double overlap = OverlapLength(minX, maxX, reference.MinX, reference.MaxX);
                    double required = Math.Min(maxX - minX, reference.MaxX - reference.MinX) * 0.55;
                    if (overlap >= required && Math.Abs(reference.CenterZ - centerZ) <= toleranceMm)
                    {
                        return true;
                    }
                }
                else
                {
                    double overlap = OverlapLength(minZ, maxZ, reference.MinZ, reference.MaxZ);
                    double required = Math.Min(maxZ - minZ, reference.MaxZ - reference.MinZ) * 0.55;
                    if (overlap >= required && Math.Abs(reference.CenterX - centerX) <= toleranceMm)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryMapIsoPointToModel(
            DrawingProjectionRegion region,
            XYZ point,
            double mainWidthMm,
            double totalHeightMm,
            out double xMm,
            out double zMm)
        {
            return TryMapRegionPointToModel(region, point, mainWidthMm, totalHeightMm, 0.2, out xMm, out zMm);
        }

        private static bool TryMapRegionPointToModel(
            DrawingProjectionRegion region,
            XYZ point,
            double mainWidthMm,
            double totalHeightMm,
            double looseFraction,
            out double xMm,
            out double zMm)
        {
            xMm = 0;
            zMm = 0;
            if (region == null || point == null || region.Origin == null || region.LocalXAxis == null || region.LocalYAxis == null)
            {
                return false;
            }

            double widthFeet = UnitUtilsExtensions.MmToFeet(Math.Max(region.WidthMm, 0));
            double heightFeet = UnitUtilsExtensions.MmToFeet(Math.Max(region.HeightMm, 0));
            if (widthFeet <= 1e-9 || heightFeet <= 1e-9)
            {
                return false;
            }

            XYZ flat = GeometryToleranceUtils.Flatten(point);
            XYZ delta = flat - region.Origin;
            double uFeet = GeometryToleranceUtils.Dot2D(delta, region.LocalXAxis) - region.LocalMinU;
            double vFeet = GeometryToleranceUtils.Dot2D(delta, region.LocalYAxis) - region.LocalMinV;
            double loose = Math.Max(widthFeet, heightFeet) * Math.Max(0, looseFraction);
            if (uFeet < -loose || uFeet > widthFeet + loose || vFeet < -loose || vFeet > heightFeet + loose)
            {
                return false;
            }

            double uMm = UnitUtilsExtensions.FeetToMm(Clamp(uFeet, 0, widthFeet));
            double vMm = UnitUtilsExtensions.FeetToMm(Clamp(vFeet, 0, heightFeet));
            xMm = region.WidthMm <= 1e-9 ? 0 : Clamp(uMm / region.WidthMm * mainWidthMm, 0, mainWidthMm);
            zMm = region.HeightMm <= 1e-9 ? 0 : Clamp(vMm / region.HeightMm * totalHeightMm, 0, totalHeightMm);
            return true;
        }

        private static double OverlapLength(double minA, double maxA, double minB, double maxB)
        {
            return Math.Max(0, Math.Min(maxA, maxB) - Math.Max(minA, minB));
        }

        private static bool IsDuplicateIsoDetail(IList<NativeGeometryFeature> details, NativeGeometryFeature feature, double toleranceMm)
        {
            if (details == null || feature == null)
            {
                return false;
            }

            bool horizontal = feature.WidthMm >= feature.HeightMm;
            double centerX = (feature.XMinMm + feature.XMaxMm) * 0.5;
            double centerZ = (feature.ZMinMm + feature.ZMaxMm) * 0.5;
            foreach (NativeGeometryFeature existing in details)
            {
                if (existing == null || existing.FeatureType != NativeFeatureType.IsoDetail)
                {
                    continue;
                }

                bool existingHorizontal = existing.WidthMm >= existing.HeightMm;
                if (existingHorizontal != horizontal)
                {
                    continue;
                }

                double existingCenterX = (existing.XMinMm + existing.XMaxMm) * 0.5;
                double existingCenterZ = (existing.ZMinMm + existing.ZMaxMm) * 0.5;
                if (Math.Abs(existingCenterX - centerX) <= toleranceMm
                    && Math.Abs(existingCenterZ - centerZ) <= toleranceMm)
                {
                    return true;
                }
            }

            return false;
        }

        private static NativeGeometryFeature CreateBoxFeature(
            NativeFeatureType type,
            string name,
            string source,
            double xMinMm,
            double xMaxMm,
            double yMinMm,
            double yMaxMm,
            double zMinMm,
            double zMaxMm,
            RecognizedContour contour,
            double confidence)
        {
            var feature = new NativeGeometryFeature
            {
                FeatureType = type,
                Name = name,
                SourceDescription = source,
                Axis = BuildDirection.ExtrudeZ_FromPlan,
                XMinMm = xMinMm,
                XMaxMm = xMaxMm,
                YMinMm = yMinMm,
                YMaxMm = yMaxMm,
                ZMinMm = zMinMm,
                ZMaxMm = zMaxMm,
                Confidence = confidence,
                BuildMethod = "Box extrusion from Plan footprint"
            };
            if (contour != null)
            {
                feature.SourceContourIds.Add(contour.Id);
            }

            return feature;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
            {
                double temp = min;
                min = max;
                max = temp;
            }

            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        private static void ValidateFeature(NativeGeometryFeature feature, DrawingToFamilySettings settings)
        {
            double minimum = NativeMinimumSizeMm(settings);
            if (feature.FeatureType == NativeFeatureType.Cylinder
                || feature.FeatureType == NativeFeatureType.VoidCylinder)
            {
                if (CylinderLengthMm(feature) < minimum || feature.DiameterMm < minimum)
                {
                    feature.CanBuild = false;
                    feature.SkipReason = feature.FeatureType == NativeFeatureType.VoidCylinder
                        ? "Opening candidate is smaller than the native build minimum size."
                        : "Cylinder is smaller than the native build minimum size.";
                }
                return;
            }

            if (feature.WidthMm < minimum || feature.DepthMm < minimum || feature.HeightMm < minimum)
            {
                feature.CanBuild = false;
                feature.SkipReason = "Box feature is smaller than the minimum element size.";
            }
        }

        private static double CylinderLengthMm(NativeGeometryFeature feature)
        {
            if (feature == null)
            {
                return 0;
            }

            if (feature.Axis == BuildDirection.ExtrudeY_FromFront)
            {
                return feature.DepthMm;
            }

            if (feature.Axis == BuildDirection.ExtrudeZ_FromPlan)
            {
                return feature.HeightMm;
            }

            return feature.WidthMm;
        }

        private static bool IsRectangleLike(RecognizedContour contour)
        {
            return contour != null
                && contour.BoundingBox != null
                && contour.WidthMm > 0
                && contour.HeightMm > 0
                && (contour.Curves.Count == 4 || contour.AreaMm2 >= contour.WidthMm * contour.HeightMm * 0.75);
        }

        private static bool IsCircleLike(ContourBox box)
        {
            if (box == null || box.WidthMm <= 0 || box.HeightMm <= 0)
            {
                return false;
            }

            double roundness = Math.Abs(box.WidthMm - box.HeightMm) / Math.Max(box.WidthMm, box.HeightMm);
            bool curvedSource = box.Contour.SourceEntities.Any(x =>
                string.Equals(x.EntityType, "Arc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.EntityType, "Ellipse", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.EntityType, "Circle", StringComparison.OrdinalIgnoreCase));
            return roundness <= 0.12 && (curvedSource || box.Contour.Curves.Count >= 8 || box.Contour.AreaMm2 > 0);
        }

        private static void AddWarning(IList<string> warnings, string message)
        {
            if (warnings != null && !string.IsNullOrWhiteSpace(message))
            {
                warnings.Add(message);
            }
        }

        private class ProjectionMetrics
        {
            public bool IsValid { get; private set; }
            public double MinXMm { get; private set; }
            public double MaxXMm { get; private set; }
            public double MinYMm { get; private set; }
            public double MaxYMm { get; private set; }
            public double MinZMm { get; private set; }
            public double MaxZMm { get; private set; }
            public double WidthMm { get { return Math.Abs(MaxXMm - MinXMm); } }
            public double DepthMm { get { return Math.Abs(MaxYMm - MinYMm); } }
            public double HeightMm { get { return Math.Abs(MaxZMm - MinZMm); } }
            public double TargetWidthMm { get; private set; }
            public double TargetDepthMm { get; private set; }
            public double TargetHeightMm { get; private set; }
            public double ModelWidthMm { get { return TargetWidthMm > 0 ? TargetWidthMm : WidthMm; } }
            public double ModelDepthMm { get { return TargetDepthMm > 0 ? TargetDepthMm : DepthMm; } }
            public double ModelHeightMm { get { return TargetHeightMm > 0 ? TargetHeightMm : HeightMm; } }
            public double ScaleXMm { get { return TargetWidthMm > 0 && WidthMm > 1e-9 ? TargetWidthMm / WidthMm : 1.0; } }
            public double ScaleYMm { get { return TargetDepthMm > 0 && DepthMm > 1e-9 ? TargetDepthMm / DepthMm : 1.0; } }
            public double ScaleZMm { get { return TargetHeightMm > 0 && HeightMm > 1e-9 ? TargetHeightMm / HeightMm : 1.0; } }

            public void SetTarget(double widthMm, double depthMm, double heightMm)
            {
                TargetWidthMm = Math.Max(0, widthMm);
                TargetDepthMm = Math.Max(0, depthMm);
                TargetHeightMm = Math.Max(0, heightMm);
            }

            public static ProjectionMetrics FromContours(IList<RecognizedContour> contours, ProjectionType projection)
            {
                var metrics = new ProjectionMetrics();
                foreach (RecognizedContour contour in contours ?? new List<RecognizedContour>())
                {
                    if (contour.SourceProjection != projection || contour.BoundingBox == null || !contour.IsValidForRevit)
                    {
                        continue;
                    }

                    metrics.Add(contour.BoundingBox);
                }

                return metrics;
            }

            public static ProjectionMetrics FromMainContourOrAll(IList<RecognizedContour> contours, ProjectionType projection)
            {
                RecognizedContour main = (contours ?? new List<RecognizedContour>())
                    .Where(x => x.SourceProjection == projection
                        && x.BoundingBox != null
                        && x.IsClosed
                        && x.IsValidForRevit
                        && (x.Type == ContourType.SolidProfile || x.Type == ContourType.Unknown))
                    .OrderByDescending(x => x.AreaMm2)
                    .FirstOrDefault();
                if (main != null)
                {
                    var metrics = new ProjectionMetrics();
                    metrics.Add(main.BoundingBox);
                    return metrics;
                }

                return FromContours(contours, projection);
            }

            public static ProjectionMetrics FromPlanFootprint(RecognizedContour contour)
            {
                var metrics = new ProjectionMetrics();
                if (contour != null && contour.BoundingBox != null)
                {
                    metrics.Add(contour.BoundingBox);
                }

                return metrics;
            }

            public static ProjectionMetrics FromRegion(DrawingProjectionRegion region, ProjectionType projection)
            {
                var metrics = new ProjectionMetrics();
                if (region == null || !region.IsValid || region.WidthMm <= 0 || region.HeightMm <= 0)
                {
                    return metrics;
                }

                metrics.IsValid = true;
                metrics.MinXMm = 0;
                metrics.MinYMm = 0;
                metrics.MinZMm = 0;

                if (projection == ProjectionType.Side)
                {
                    metrics.MaxXMm = region.WidthMm;
                    metrics.MaxYMm = region.WidthMm;
                    metrics.MaxZMm = region.HeightMm;
                    return metrics;
                }

                if (projection == ProjectionType.Front)
                {
                    metrics.MaxXMm = region.WidthMm;
                    metrics.MaxYMm = 0;
                    metrics.MaxZMm = region.HeightMm;
                    return metrics;
                }

                metrics.MaxXMm = region.WidthMm;
                metrics.MaxYMm = region.HeightMm;
                metrics.MaxZMm = 0;
                return metrics;
            }

            private void Add(BoundingBoxXYZ box)
            {
                double minX = UnitUtilsExtensions.FeetToMm(box.Min.X);
                double maxX = UnitUtilsExtensions.FeetToMm(box.Max.X);
                double minY = UnitUtilsExtensions.FeetToMm(box.Min.Y);
                double maxY = UnitUtilsExtensions.FeetToMm(box.Max.Y);
                double minZ = UnitUtilsExtensions.FeetToMm(box.Min.Z);
                double maxZ = UnitUtilsExtensions.FeetToMm(box.Max.Z);

                if (!IsValid)
                {
                    MinXMm = minX;
                    MaxXMm = maxX;
                    MinYMm = minY;
                    MaxYMm = maxY;
                    MinZMm = minZ;
                    MaxZMm = maxZ;
                    IsValid = true;
                    return;
                }

                MinXMm = Math.Min(MinXMm, minX);
                MaxXMm = Math.Max(MaxXMm, maxX);
                MinYMm = Math.Min(MinYMm, minY);
                MaxYMm = Math.Max(MaxYMm, maxY);
                MinZMm = Math.Min(MinZMm, minZ);
                MaxZMm = Math.Max(MaxZMm, maxZ);
            }
        }

        private class ContourBox
        {
            public ContourBox(RecognizedContour contour, ProjectionMetrics metrics)
            {
                Contour = contour;
                SourceProjection = contour.SourceProjection;
                double rawMinX = UnitUtilsExtensions.FeetToMm(contour.BoundingBox.Min.X);
                double rawMaxX = UnitUtilsExtensions.FeetToMm(contour.BoundingBox.Max.X);
                double rawMinY = UnitUtilsExtensions.FeetToMm(contour.BoundingBox.Min.Y);
                double rawMaxY = UnitUtilsExtensions.FeetToMm(contour.BoundingBox.Max.Y);
                double rawMinZ = UnitUtilsExtensions.FeetToMm(contour.BoundingBox.Min.Z);
                double rawMaxZ = UnitUtilsExtensions.FeetToMm(contour.BoundingBox.Max.Z);

                if (SourceProjection == ProjectionType.Side)
                {
                    MinXMm = (rawMinY - metrics.MinYMm) * metrics.ScaleYMm;
                    MaxXMm = (rawMaxY - metrics.MinYMm) * metrics.ScaleYMm;
                    MinYMm = MinXMm;
                    MaxYMm = MaxXMm;
                    MinZMm = (rawMinZ - metrics.MinZMm) * metrics.ScaleZMm;
                    MaxZMm = (rawMaxZ - metrics.MinZMm) * metrics.ScaleZMm;
                }
                else if (SourceProjection == ProjectionType.Front)
                {
                    MinXMm = (rawMinX - metrics.MinXMm) * metrics.ScaleXMm;
                    MaxXMm = (rawMaxX - metrics.MinXMm) * metrics.ScaleXMm;
                    MinYMm = 0;
                    MaxYMm = 0;
                    MinZMm = (rawMinZ - metrics.MinZMm) * metrics.ScaleZMm;
                    MaxZMm = (rawMaxZ - metrics.MinZMm) * metrics.ScaleZMm;
                }
                else
                {
                    MinXMm = (rawMinX - metrics.MinXMm) * metrics.ScaleXMm;
                    MaxXMm = (rawMaxX - metrics.MinXMm) * metrics.ScaleXMm;
                    MinYMm = (rawMinY - metrics.MinYMm) * metrics.ScaleYMm;
                    MaxYMm = (rawMaxY - metrics.MinYMm) * metrics.ScaleYMm;
                    MinZMm = rawMinZ - metrics.MinZMm;
                    MaxZMm = rawMaxZ - metrics.MinZMm;
                }
            }

            public RecognizedContour Contour { get; private set; }
            public ProjectionType SourceProjection { get; private set; }
            public double MinXMm { get; private set; }
            public double MaxXMm { get; private set; }
            public double MinYMm { get; private set; }
            public double MaxYMm { get; private set; }
            public double MinZMm { get; private set; }
            public double MaxZMm { get; private set; }
            public double WidthMm { get { return Math.Abs(MaxXMm - MinXMm); } }
            public double DepthMm { get { return Math.Abs(MaxYMm - MinYMm); } }
            public double HeightMm
            {
                get
                {
                    return SourceProjection == ProjectionType.Plan
                        ? DepthMm
                        : Math.Abs(MaxZMm - MinZMm);
                }
            }
            public double AreaMm2 { get { return WidthMm * HeightMm; } }
            public double CenterXMm { get { return (MinXMm + MaxXMm) * 0.5; } }
            public double CenterYMm { get { return (MinYMm + MaxYMm) * 0.5; } }
            public double CenterZMm { get { return (MinZMm + MaxZMm) * 0.5; } }
        }

        private class DetailReferenceSegment
        {
            public DetailReferenceSegment(double minX, double maxX, double minZ, double maxZ, bool horizontal)
            {
                MinX = minX;
                MaxX = maxX;
                MinZ = minZ;
                MaxZ = maxZ;
                Horizontal = horizontal;
            }

            public double MinX { get; private set; }
            public double MaxX { get; private set; }
            public double MinZ { get; private set; }
            public double MaxZ { get; private set; }
            public bool Horizontal { get; private set; }
            public double CenterX { get { return (MinX + MaxX) * 0.5; } }
            public double CenterZ { get { return (MinZ + MaxZ) * 0.5; } }
        }

        private class PlanReferenceSegment
        {
            public PlanReferenceSegment(double minX, double maxX, double minY, double maxY, bool horizontal)
            {
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
                Horizontal = horizontal;
            }

            public double MinX { get; private set; }
            public double MaxX { get; private set; }
            public double MinY { get; private set; }
            public double MaxY { get; private set; }
            public bool Horizontal { get; private set; }
            public double CenterX { get { return (MinX + MaxX) * 0.5; } }
            public double CenterY { get { return (MinY + MaxY) * 0.5; } }
            public double LengthMm
            {
                get
                {
                    double dx = MaxX - MinX;
                    double dy = MaxY - MinY;
                    return Math.Sqrt(dx * dx + dy * dy);
                }
            }
        }
    }
}
