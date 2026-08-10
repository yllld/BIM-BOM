using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;
using FamilyConverter.Revit2021.Services;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class FamilyGeometryBuilder
    {
        private const int MaxCurvesPerSafeExtrusion = 80;
        private const double SafeToleranceFeet = 1.0e-6;
        // Revit 2021 can terminate on dirty DWG curve profiles before managed exceptions are raised.
        // Keep exact-profile creation disabled until we can isolate unsafe contours outside NewExtrusion/NewModelCurve.
        private static readonly bool DetailedCandidateGeometryEnabled = true;
        private static readonly bool SimpleFreeFormGeometryEnabled = false;
        private static readonly bool PlanOnlySafeExtrusionEnabled = true;
        private readonly SubcategoryService _subcategoryService;
        private readonly DrawingToFamilyLogger _logger;
        private bool _suppressRevitWarnings;

        public FamilyGeometryBuilder(SubcategoryService subcategoryService)
            : this(subcategoryService, null)
        {
        }

        public FamilyGeometryBuilder(SubcategoryService subcategoryService, DrawingToFamilyLogger logger)
        {
            _subcategoryService = subcategoryService;
            _logger = logger;
        }

        public void Build(
            Document document,
            IList<BuildCandidate> candidates,
            DrawingProjectionRegion plan,
            DrawingProjectionRegion front,
            DrawingProjectionRegion side,
            DrawingToFamilySettings settings,
            DrawingToFamilyResult result)
        {
            LogStage("FamilyGeometryBuilder.Build start");
            if (document == null || !document.IsFamilyDocument)
            {
                result.Errors.Add("Команда работает только в редакторе семейств Revit.");
                return;
            }

            IList<BuildCandidate> buildCandidates = candidates ?? new List<BuildCandidate>();
            _suppressRevitWarnings = ShouldSuppressRevitWarnings(settings);
            result.BuildCandidateCount = buildCandidates.Count;
            UpdateGlobalDimensions(plan, front, side, result);
            LogData("Build candidate count", buildCandidates.Count);
            LogRegion("Build Plan region", plan);
            LogRegion("Build Front region", front);
            LogRegion("Build Side region", side);

            if (settings == null || !settings.BuildGeometry)
            {
                LogStage("FamilyGeometryBuilder BuildGeometry disabled");
                result.Warnings.Add("BuildGeometry is disabled. Analysis/report was created without Revit model creation.");
                WarnAboutSuspiciousSize(result, result.WidthMm, "ширина");
                WarnAboutSuspiciousSize(result, result.DepthMm, "глубина");
                WarnAboutSuspiciousSize(result, result.HeightMm, "высота");
                LogStage("FamilyGeometryBuilder.Build end");
                return;
            }

            if (!DetailedCandidateGeometryEnabled)
            {
                LogStage("FamilyGeometryBuilder crash-safe branch");
                result.Warnings.Add("Crash-safe staged Plan-only mode is active: Revit geometry is built only from Plan View bbox profiles. Front/Side views are used only for height/depth checks. Exact CAD profiles are not sent to Revit model creation.");
                foreach (BuildCandidate candidate in buildCandidates)
                {
                    if (candidate != null)
                    {
                        candidate.BuildResult = "Crash-safe staged Plan-only mode: candidate recorded.";
                        candidate.IsBuilt = false;
                    }
                }

                if (PlanOnlySafeExtrusionEnabled)
                {
                    BuildPlanOnlySafeExtrusions(document, buildCandidates, plan, settings, result);
                }
                else if (SimpleFreeFormGeometryEnabled)
                {
                    BuildSimpleFreeForms(document, buildCandidates, result);
                }

                if (result.CreatedGeometryCount == 0 && settings.MaxBuildCandidates == 0)
                {
                    CreateFallbackSafeExtrusion(document, plan, front, settings, result);
                }
                else if (result.CreatedGeometryCount == 0)
                {
                    result.Warnings.Add("Global fallback was not used because staged build limit is active.");
                }

                WarnAboutSuspiciousSize(result, result.WidthMm, "ширина");
                WarnAboutSuspiciousSize(result, result.DepthMm, "глубина");
                WarnAboutSuspiciousSize(result, result.HeightMm, "высота");
                LogStage("FamilyGeometryBuilder.Build end");
                return;
            }

            using (var group = new TransactionGroup(document, "2D Drawing to Family"))
            {
                group.Start();

                if (!DetailedCandidateGeometryEnabled)
                {
                    result.Warnings.Add("Crash-safe Plan-only mode is active: Revit geometry is built only from Plan View bbox profiles. Front/Side views are used only for height/depth checks. Exact CAD profiles and FreeForms are not sent to Revit model creation.");
                    foreach (BuildCandidate candidate in buildCandidates)
                    {
                        if (candidate != null)
                        {
                            candidate.BuildResult = "Crash-safe Plan-only mode: candidate recorded.";
                            candidate.IsBuilt = false;
                        }
                    }

                    if (PlanOnlySafeExtrusionEnabled)
                    {
                        BuildPlanOnlySafeExtrusions(document, buildCandidates, plan, settings, result);
                    }
                    else if (SimpleFreeFormGeometryEnabled)
                    {
                        BuildSimpleFreeForms(document, buildCandidates, result);
                    }

                    if (result.CreatedGeometryCount == 0)
                    {
                        CreateFallbackSafeExtrusion(document, plan, front, settings, result);
                    }

                    group.Assimilate();
                    WarnAboutSuspiciousSize(result, result.WidthMm, "ширина");
                    WarnAboutSuspiciousSize(result, result.DepthMm, "глубина");
                    WarnAboutSuspiciousSize(result, result.HeightMm, "высота");
                    return;
                }

                int index = 1;
                foreach (BuildCandidate candidate in buildCandidates)
                {
                    if (candidate == null || !candidate.CanBuild)
                    {
                        result.FailedBuildCandidates++;
                        result.SkippedContours++;
                        if (candidate != null && !string.IsNullOrWhiteSpace(candidate.SkipReason))
                        {
                            result.Warnings.Add(candidate.SkipReason);
                        }
                        continue;
                    }

                    try
                    {
                        bool built = BuildCandidate(document, candidate, index, settings, result);
                        if (built)
                        {
                            candidate.IsBuilt = true;
                        }
                        else
                        {
                            result.FailedBuildCandidates++;
                        }
                    }
                    catch (Exception ex)
                    {
                        candidate.BuildResult = ex.Message;
                        result.Errors.Add("Candidate " + ShortId(candidate) + " failed: " + ex.Message);
                        result.FailedBuildCandidates++;
                    }

                    index++;
                }

                if (buildCandidates.Count > 0 && result.CreatedGeometryCount == 0 && result.ReferenceLinesCreated == 0)
                {
                    using (var transaction = new Transaction(document, "2D2F FALLBACK Bounding Box"))
                    {
                        try
                        {
                            transaction.Start();
                            ConfigureFailureHandling(transaction);
                            Element fallback = CreateFallbackBox(document, plan, front, settings, result);
                            if (fallback != null)
                            {
                                transaction.Commit();
                                result.FallbackUsed = true;
                                result.CreatedGeometryCount++;
                                result.SolidExtrusionsCreated++;
                                result.CreatedElementIds.Add(fallback.Id.IntegerValue);
                            }
                            else
                            {
                                transaction.RollBack();
                            }
                        }
                        catch (Exception ex)
                        {
                            if (transaction.HasStarted())
                            {
                                transaction.RollBack();
                            }

                            result.Errors.Add("FALLBACK bounding box failed: " + ex.Message);
                        }
                    }
                }

                group.Assimilate();
            }

            WarnAboutSuspiciousSize(result, result.WidthMm, "ширина");
            WarnAboutSuspiciousSize(result, result.DepthMm, "глубина");
            WarnAboutSuspiciousSize(result, result.HeightMm, "высота");
        }

        public void BuildNativeFeatures(
            Document document,
            IList<NativeGeometryFeature> features,
            DrawingToFamilySettings settings,
            DrawingToFamilyResult result)
        {
            LogStage("FamilyGeometryBuilder.BuildNativeFeatures start");
            _suppressRevitWarnings = ShouldSuppressRevitWarnings(settings);
            if (document == null || !document.IsFamilyDocument)
            {
                result.Errors.Add("Команда работает только в редакторе семейств Revit.");
                return;
            }

            IList<NativeGeometryFeature> nativeFeatures = features ?? new List<NativeGeometryFeature>();
            if (result.NativeFeatureCount == 0)
            {
                result.NativeFeatureCount = nativeFeatures.Count;
                result.BoxFeatureCount = nativeFeatures.Count(IsBoxFeature);
                result.CylinderFeatureCount = nativeFeatures.Count(x => x.FeatureType == NativeFeatureType.Cylinder
                    || x.FeatureType == NativeFeatureType.VoidCylinder);
            }
            if (result.WidthMm <= 0 || result.DepthMm <= 0 || result.HeightMm <= 0)
            {
                UpdateGlobalDimensions(nativeFeatures, result);
            }
            LogData("Native feature count", nativeFeatures.Count);

            if (settings == null || !settings.BuildGeometry)
            {
                result.Warnings.Add("BuildGeometry is disabled. Native feature stack was analyzed without Revit model creation.");
                LogStage("FamilyGeometryBuilder.BuildNativeFeatures end - report only");
                return;
            }

            int maxFeatures = settings == null ? 0 : Math.Max(0, settings.MaxBuildCandidates);
            IEnumerable<NativeGeometryFeature> orderedFeatures = nativeFeatures
                .Where(x => x != null)
                .OrderBy(x => FeatureBuildOrder(x))
                .ThenByDescending(x => x.VolumeScore);
            if (maxFeatures > 0)
            {
                orderedFeatures = nativeFeatures
                    .Where(x => x != null)
                    .OrderByDescending(x => x.VolumeScore)
                    .Take(maxFeatures);
            }

            int index = 1;
            int reportOnlyOpenings = nativeFeatures.Count(x => x != null
                && x.FeatureType == NativeFeatureType.VoidCylinder
                && !x.CanBuild);
            if (reportOnlyOpenings > 0)
            {
                result.Warnings.Add(reportOnlyOpenings + " circular opening candidate(s) were detected from Plan/Front/Side and kept report-only. They are not built as solid rods.");
            }

            foreach (NativeGeometryFeature feature in orderedFeatures)
            {
                if (!feature.CanBuild)
                {
                    feature.BuildResult = string.IsNullOrWhiteSpace(feature.SkipReason)
                        ? "Skipped before Revit build."
                        : feature.SkipReason;
                    if (feature.FeatureType == NativeFeatureType.VoidCylinder)
                    {
                        result.ReferenceObjectCount++;
                    }
                    else
                    {
                        result.FailedBuildCandidates++;
                    }
                    result.SkippedContours++;
                    if (!string.IsNullOrWhiteSpace(feature.SkipReason)
                        && feature.FeatureType != NativeFeatureType.VoidCylinder)
                    {
                        result.Warnings.Add(feature.Name + ": " + feature.SkipReason);
                    }
                    index++;
                    continue;
                }

                using (var transaction = new Transaction(document, "2D2F Native Feature " + index.ToString("000")))
                {
                    try
                    {
                        LogInfo(string.Format(
                            "Native feature {0}: type={1}; box=({2:0.#},{3:0.#},{4:0.#})..({5:0.#},{6:0.#},{7:0.#}); diameter={8:0.#}; method={9}",
                            index,
                            feature.FeatureType,
                            feature.XMinMm,
                            feature.YMinMm,
                            feature.ZMinMm,
                            feature.XMaxMm,
                            feature.YMaxMm,
                            feature.ZMaxMm,
                            feature.DiameterMm,
                            feature.BuildMethod));
                        transaction.Start();
                        ConfigureFailureHandling(transaction);
                        Element element = BuildNativeFeature(document, feature, index, result);
                        if (element != null)
                        {
                            transaction.Commit();
                            feature.IsBuilt = true;
                            if (string.IsNullOrWhiteSpace(feature.BuildResult))
                            {
                                feature.BuildResult = "Native Revit extrusion created.";
                            }

                            result.CreatedGeometryCount++;
                            if (feature.FeatureType == NativeFeatureType.VoidCylinder)
                            {
                                result.VoidProfilesUsed++;
                            }
                            else
                            {
                                result.SolidExtrusionsCreated++;
                            }
                            result.CreatedElementIds.Add(element.Id.IntegerValue);
                        }
                        else
                        {
                            transaction.RollBack();
                            result.FailedBuildCandidates++;
                            result.SkippedContours++;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (transaction.HasStarted())
                        {
                            transaction.RollBack();
                        }

                        feature.BuildResult = "Native feature failed: " + ex.Message;
                        result.Warnings.Add(feature.Name + " failed: " + ex.Message);
                        LogError("Native feature " + index.ToString("000") + " failed.", ex);
                        result.FailedBuildCandidates++;
                    }
                }

                index++;
            }

            if (result.CreatedGeometryCount == 0)
            {
                result.Warnings.Add("Native feature stack did not create Revit geometry. Legacy fallback can be used on the next run if needed.");
            }

            WarnAboutSuspiciousSize(result, result.WidthMm, "ширина");
            WarnAboutSuspiciousSize(result, result.DepthMm, "глубина");
            WarnAboutSuspiciousSize(result, result.HeightMm, "высота");
            LogStage("FamilyGeometryBuilder.BuildNativeFeatures end");
        }

        public void BuildReferenceContours(
            Document document,
            IList<RecognizedContour> contours,
            DrawingToFamilySettings settings,
            DrawingToFamilyResult result)
        {
            LogStage("FamilyGeometryBuilder.BuildReferenceContours start");
            _suppressRevitWarnings = ShouldSuppressRevitWarnings(settings);
            if (document == null || !document.IsFamilyDocument)
            {
                result.Errors.Add("Reference/model lines can be created only inside a Revit family document.");
                return;
            }

            if (settings != null && !settings.BuildGeometry)
            {
                LogStage("FamilyGeometryBuilder.BuildReferenceContours skipped - report only");
                return;
            }

            int index = 1;
            foreach (RecognizedContour contour in contours ?? new List<RecognizedContour>())
            {
                if (contour == null
                    || contour.IsBuilt
                    || contour.Curves == null
                    || contour.Curves.Count == 0)
                {
                    index++;
                    continue;
                }

                bool shouldBuildReference =
                    contour.Type == ContourType.OpenCurve
                    || contour.Type == ContourType.ReferenceCurve
                    || contour.Type == ContourType.Invalid
                    || contour.Type == ContourType.VoidProfile
                    || contour.SourceProjection != ProjectionType.Plan;
                if (!shouldBuildReference)
                {
                    index++;
                    continue;
                }

                var candidate = new BuildCandidate
                {
                    PrimaryContour = contour,
                    Direction = BuildDirection.ReferenceOnly,
                    CanBuild = true,
                    Confidence = 0.25
                };
                CreateReferenceLines(document, candidate, 50000 + index, result);
                index++;
            }

            LogStage("FamilyGeometryBuilder.BuildReferenceContours end");
        }

        private Element BuildNativeFeature(Document document, NativeGeometryFeature feature, int index, DrawingToFamilyResult result)
        {
            if (feature.FeatureType == NativeFeatureType.Cylinder)
            {
                return BuildCylinderFeature(document, feature, index, result);
            }

            if (feature.FeatureType == NativeFeatureType.VoidCylinder)
            {
                return BuildVoidCylinderFeature(document, feature, index, result);
            }

            if (IsBoxFeature(feature))
            {
                return BuildBoxFeature(document, feature, index, result);
            }

            feature.BuildResult = "Native feature skipped: unsupported feature type.";
            result.Warnings.Add(feature.Name + ": unsupported feature type " + feature.FeatureType + ".");
            return null;
        }

        private Element BuildBoxFeature(Document document, NativeGeometryFeature feature, int index, DrawingToFamilyResult result)
        {
            double xMin = UnitUtilsExtensions.MmToFeet(Math.Min(feature.XMinMm, feature.XMaxMm));
            double xMax = UnitUtilsExtensions.MmToFeet(Math.Max(feature.XMinMm, feature.XMaxMm));
            double yMin = UnitUtilsExtensions.MmToFeet(Math.Min(feature.YMinMm, feature.YMaxMm));
            double yMax = UnitUtilsExtensions.MmToFeet(Math.Max(feature.YMinMm, feature.YMaxMm));
            double zMin = UnitUtilsExtensions.MmToFeet(Math.Min(feature.ZMinMm, feature.ZMaxMm));
            double zMax = UnitUtilsExtensions.MmToFeet(Math.Max(feature.ZMinMm, feature.ZMaxMm));
            double height = zMax - zMin;
            if (!IsSafeNativeSize(feature.WidthMm, "width", feature, result)
                || !IsSafeNativeSize(feature.DepthMm, "depth", feature, result)
                || !IsSafeNativeSize(feature.HeightMm, "height", feature, result)
                || height <= UnitUtilsExtensions.MmToFeet(0.1))
            {
                return null;
            }

            SketchPlane sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, zMin)));
            CurveArrArray profile = CreateRectangleProfileAtZ(xMin, xMax, yMin, yMax, zMin);
            Extrusion extrusion = document.FamilyCreate.NewExtrusion(true, profile, sketchPlane, height);
            PostProcessNativeElement(document, extrusion, feature, index);
            feature.BuildResult = "Native box extrusion created.";
            return extrusion;
        }

        private Element BuildCylinderFeature(Document document, NativeGeometryFeature feature, int index, DrawingToFamilyResult result)
        {
            return BuildCylinderFeature(document, feature, index, result, true);
        }

        private Element BuildVoidCylinderFeature(Document document, NativeGeometryFeature feature, int index, DrawingToFamilyResult result)
        {
            return BuildCylinderFeature(document, feature, index, result, false);
        }

        private Element BuildCylinderFeature(Document document, NativeGeometryFeature feature, int index, DrawingToFamilyResult result, bool isSolid)
        {
            double radius = UnitUtilsExtensions.MmToFeet(feature.DiameterMm * 0.5);
            if (!IsSafeNativeSize(CylinderLengthMm(feature), "length", feature, result)
                || !IsSafeNativeSize(feature.DiameterMm, "diameter", feature, result)
                || radius <= UnitUtilsExtensions.MmToFeet(0.1))
            {
                return null;
            }

            SketchPlane sketchPlane;
            CurveArrArray profile;
            double length;
            if (feature.Axis == BuildDirection.ExtrudeY_FromFront)
            {
                double yMin = UnitUtilsExtensions.MmToFeet(Math.Min(feature.YMinMm, feature.YMaxMm));
                double yMax = UnitUtilsExtensions.MmToFeet(Math.Max(feature.YMinMm, feature.YMaxMm));
                double xCenter = UnitUtilsExtensions.MmToFeet((feature.XMinMm + feature.XMaxMm) * 0.5);
                double zCenter = UnitUtilsExtensions.MmToFeet((feature.ZMinMm + feature.ZMaxMm) * 0.5);
                length = yMax - yMin;
                sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisY, new XYZ(0, yMin, 0)));
                profile = CreateCircleProfileAtY(yMin, xCenter, zCenter, radius, 32);
            }
            else if (feature.Axis == BuildDirection.ExtrudeZ_FromPlan)
            {
                double zMin = UnitUtilsExtensions.MmToFeet(Math.Min(feature.ZMinMm, feature.ZMaxMm));
                double zMax = UnitUtilsExtensions.MmToFeet(Math.Max(feature.ZMinMm, feature.ZMaxMm));
                double xCenter = UnitUtilsExtensions.MmToFeet((feature.XMinMm + feature.XMaxMm) * 0.5);
                double yCenter = UnitUtilsExtensions.MmToFeet((feature.YMinMm + feature.YMaxMm) * 0.5);
                length = zMax - zMin;
                sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, zMin)));
                profile = CreateCircleProfileAtZ(zMin, xCenter, yCenter, radius, 32);
            }
            else
            {
                double xMin = UnitUtilsExtensions.MmToFeet(Math.Min(feature.XMinMm, feature.XMaxMm));
                double xMax = UnitUtilsExtensions.MmToFeet(Math.Max(feature.XMinMm, feature.XMaxMm));
                double yCenter = UnitUtilsExtensions.MmToFeet((feature.YMinMm + feature.YMaxMm) * 0.5);
                double zCenter = UnitUtilsExtensions.MmToFeet((feature.ZMinMm + feature.ZMaxMm) * 0.5);
                length = xMax - xMin;
                sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisX, new XYZ(xMin, 0, 0)));
                profile = CreateCircleProfileAtX(xMin, yCenter, zCenter, radius, 32);
            }

            if (length <= UnitUtilsExtensions.MmToFeet(0.1))
            {
                feature.BuildResult = "Native cylinder skipped: extrusion length is too small.";
                return null;
            }

            Extrusion extrusion = document.FamilyCreate.NewExtrusion(isSolid, profile, sketchPlane, length);
            PostProcessNativeElement(document, extrusion, feature, index);
            feature.BuildResult = isSolid ? "Native cylinder extrusion created." : "Native void cylinder extrusion created.";
            return extrusion;
        }

        private void PostProcessNativeElement(Document document, Element element, NativeGeometryFeature feature, int index)
        {
            if (element == null || feature == null)
            {
                return;
            }

            string subcategoryName = "2D2F_" + SanitizeShort(feature.FeatureType.ToString());
            Category subcategory = _subcategoryService.GetOrCreate(document, subcategoryName, true);
            _subcategoryService.AssignSubcategory(element, subcategory);
            SetElementComment(element, string.Format("2D2F_{0}_{1:000}_{2}", feature.FeatureType, index, SanitizeShort(feature.Name)));
        }

        private void BuildPlanOnlySafeExtrusions(Document document, IList<BuildCandidate> candidates, DrawingProjectionRegion plan, DrawingToFamilySettings settings, DrawingToFamilyResult result)
        {
            LogStage("BuildPlanOnlySafeExtrusions start");
            int index = 1;
            int builtOrAttempted = 0;
            int maxCandidates = settings == null ? 0 : Math.Max(0, settings.MaxBuildCandidates);
            foreach (BuildCandidate candidate in candidates
                .Where(x => x != null)
                .OrderByDescending(x => x.PrimaryContour == null ? 0 : x.PrimaryContour.AreaMm2))
            {
                if (candidate == null
                    || candidate.PrimaryContour == null
                    || candidate.PrimaryContour.SourceProjection != ProjectionType.Plan
                    || candidate.PrimaryContour.Type != ContourType.SolidProfile
                    || candidate.Direction != BuildDirection.ExtrudeZ_FromPlan
                    || !candidate.CanBuild)
                {
                    result.FailedBuildCandidates++;
                    result.SkippedContours++;
                    index++;
                    continue;
                }

                if (maxCandidates > 0 && builtOrAttempted >= maxCandidates)
                {
                    candidate.BuildResult = "Skipped by staged build limit.";
                    result.SkippedContours++;
                    index++;
                    continue;
                }

                builtOrAttempted++;
                LogInfo(string.Format(
                    "Build candidate {0} start: id={1}; area={2:0.#}mm2; W/D/H={3:0.#}/{4:0.#}/{5:0.#}mm",
                    index,
                    ShortId(candidate),
                    candidate.PrimaryContour == null ? 0 : candidate.PrimaryContour.AreaMm2,
                    candidate.WidthMm,
                    candidate.DepthMm,
                    candidate.HeightMm));
                using (var transaction = new Transaction(document, "2D2F Plan Box Extrusion " + index.ToString("000")))
                {
                    try
                    {
                        LogStage("Candidate " + index.ToString("000") + " transaction start");
                        transaction.Start();
                        ConfigureFailureHandling(transaction);
                        LogStage("Candidate " + index.ToString("000") + " transaction started");
                        Element element = TryCreatePlanOnlySafeExtrusion(document, candidate, plan, index, result);
                        bool createdFreeForm = false;
                        if (element == null && settings != null && settings.AllowFreeFormFallback)
                        {
                            LogStage("Candidate " + index.ToString("000") + " FreeForm fallback start");
                            element = TryCreateSimpleFreeForm(document, candidate, plan, result);
                            createdFreeForm = element != null;
                            LogStage("Candidate " + index.ToString("000") + " FreeForm fallback end");
                        }

                        if (element != null)
                        {
                            LogStage("Candidate " + index.ToString("000") + " transaction commit start");
                            transaction.Commit();
                            LogStage("Candidate " + index.ToString("000") + " transaction committed");
                            candidate.IsBuilt = true;
                            candidate.BuildResult = createdFreeForm ? "Plan bbox FreeForm fallback created." : "Plan bbox extrusion created.";
                            candidate.PrimaryContour.IsBuilt = true;
                            candidate.PrimaryContour.BuildResult = candidate.BuildResult;
                            result.CreatedGeometryCount++;
                            if (createdFreeForm)
                            {
                                result.FreeFormElementsCreated++;
                            }
                            else
                            {
                                result.SolidExtrusionsCreated++;
                            }
                            result.CreatedElementIds.Add(element.Id.IntegerValue);
                        }
                        else
                        {
                            LogStage("Candidate " + index.ToString("000") + " transaction rollback");
                            transaction.RollBack();
                            result.FailedBuildCandidates++;
                            result.SkippedContours++;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (transaction.HasStarted())
                        {
                            transaction.RollBack();
                        }

                        candidate.BuildResult = "Plan bbox extrusion failed: " + ex.Message;
                        LogError("Candidate " + index.ToString("000") + " failed.", ex);
                        result.Warnings.Add("Plan bbox extrusion failed for candidate " + ShortId(candidate) + ": " + ex.Message);
                        result.FailedBuildCandidates++;
                    }
                }

                index++;
            }
            LogStage("BuildPlanOnlySafeExtrusions end");
        }

        private Element TryCreatePlanOnlySafeExtrusion(Document document, BuildCandidate candidate, DrawingProjectionRegion plan, int index, DrawingToFamilyResult result)
        {
            try
            {
                LogStage("Candidate " + index.ToString("000") + " create local plan box");
                BoundingBoxXYZ box = CreateMinimumPlanBox(candidate.PrimaryContour.BoundingBox, plan, candidate, result);
                if (box == null)
                {
                    candidate.BuildResult = "Plan bbox extrusion skipped: empty plan contour bbox.";
                    return null;
                }

                double widthMm = BoundingBoxUtils.WidthMm(box);
                double depthMm = BoundingBoxUtils.HeightMm(box);
                if (!IsSafeBuildSize(widthMm, "width", candidate, result)
                    || !IsSafeBuildSize(depthMm, "depth", candidate, result)
                    || !IsSafeBuildSize(candidate.HeightMm, "height", candidate, result))
                {
                    return null;
                }

                double heightFeet = UnitUtilsExtensions.MmToFeet(candidate.HeightMm);
                if (heightFeet <= UnitUtilsExtensions.MmToFeet(0.1))
                {
                    candidate.BuildResult = "Plan bbox extrusion skipped: height is zero.";
                    return null;
                }

                LogInfo(string.Format(
                    "Candidate {0}: local profile bbox min=({1:0.###},{2:0.###}) max=({3:0.###},{4:0.###}) ft; height={5:0.###} ft",
                    index.ToString("000"),
                    box.Min.X,
                    box.Min.Y,
                    box.Max.X,
                    box.Max.Y,
                    heightFeet));
                LogStage("Candidate " + index.ToString("000") + " SketchPlane.Create");
                SketchPlane sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
                LogStage("Candidate " + index.ToString("000") + " CreatePlanProfile");
                CurveArrArray profile = TryCreateExactPlanProfile(candidate.PrimaryContour, result) ?? CreateRectangleProfile(box);
                LogStage("Candidate " + index.ToString("000") + " NewExtrusion start");
                Extrusion extrusion = document.FamilyCreate.NewExtrusion(true, profile, sketchPlane, heightFeet);
                LogStage("Candidate " + index.ToString("000") + " NewExtrusion end");
                string layerName = candidate.PrimaryContour.SourceLayer ?? "Mixed";
                LogStage("Candidate " + index.ToString("000") + " subcategory start");
                Category subcategory = _subcategoryService.GetOrCreate(document, "2D2F_" + layerName, true);
                ApplyLayerColor(subcategory, candidate.PrimaryContour);
                _subcategoryService.AssignSubcategory(extrusion, subcategory);
                SetElementComment(extrusion, BuildElementName(candidate, index) + "_PLAN_BOX");
                LogStage("Candidate " + index.ToString("000") + " postprocess end");
                return extrusion;
            }
            catch (Exception ex)
            {
                candidate.BuildResult = "Plan bbox extrusion failed: " + ex.Message;
                LogError("Candidate " + index.ToString("000") + " plan bbox extrusion managed failure.", ex);
                result.Warnings.Add("Candidate " + ShortId(candidate) + " plan bbox extrusion failed: " + ex.Message);
                return null;
            }
        }

        private static bool IsSafeBuildSize(double valueMm, string label, BuildCandidate candidate, DrawingToFamilyResult result)
        {
            if (valueMm >= 0.1 && valueMm <= 100000.0)
            {
                return true;
            }

            string warning = "Candidate " + ShortId(candidate) + " skipped: suspicious " + label + " = " + valueMm.ToString("0.#") + " mm. Check DWG import units.";
            candidate.BuildResult = warning;
            if (result != null)
            {
                result.Warnings.Add(warning);
            }

            return false;
        }

        private static CurveArrArray TryCreateExactPlanProfile(RecognizedContour contour, DrawingToFamilyResult result)
        {
            try
            {
                if (contour == null
                    || contour.SourceProjection != ProjectionType.Plan
                    || !contour.IsClosed
                    || !contour.IsValidForRevit
                    || contour.Curves == null
                    || contour.Curves.Count < 3
                    || contour.Curves.Count > 32)
                {
                    return null;
                }

                var loop = new CurveArray();
                foreach (Curve curve in contour.Curves)
                {
                    Line line = curve as Line;
                    if (line == null)
                    {
                        return null;
                    }

                    XYZ start = CurveLoopUtils.GetEndPoint(line, 0);
                    XYZ end = CurveLoopUtils.GetEndPoint(line, 1);
                    if (start == null || end == null)
                    {
                        return null;
                    }

                    XYZ safeStart = new XYZ(start.X, start.Y, 0);
                    XYZ safeEnd = new XYZ(end.X, end.Y, 0);
                    if (safeStart.DistanceTo(safeEnd) <= UnitUtilsExtensions.MmToFeet(0.5))
                    {
                        return null;
                    }

                    loop.Append(Line.CreateBound(safeStart, safeEnd));
                }

                var profile = new CurveArrArray();
                profile.Append(loop);
                return profile;
            }
            catch (Exception ex)
            {
                if (result != null)
                {
                    result.Warnings.Add("Exact Plan profile was not used: " + ex.Message);
                }

                return null;
            }
        }

        private static BoundingBoxXYZ CreateMinimumPlanBox(BoundingBoxXYZ source, DrawingProjectionRegion plan, BuildCandidate candidate, DrawingToFamilyResult result)
        {
            if (source == null)
            {
                return null;
            }

            double minSize = UnitUtilsExtensions.MmToFeet(1.0);
            double minX = source.Min.X;
            double minY = source.Min.Y;
            double maxX = source.Max.X;
            double maxY = source.Max.Y;

            if (!AreFinite(minX, minY, maxX, maxY))
            {
                if (candidate != null)
                {
                    candidate.BuildResult = "Plan bbox extrusion skipped: non-finite local coordinates.";
                }
                if (result != null)
                {
                    result.Warnings.Add("Candidate " + ShortId(candidate) + " skipped: non-finite local coordinates.");
                }
                return null;
            }

            if (!IsSafeLocalCoordinate(minX)
                || !IsSafeLocalCoordinate(minY)
                || !IsSafeLocalCoordinate(maxX)
                || !IsSafeLocalCoordinate(maxY))
            {
                if (candidate != null)
                {
                    candidate.BuildResult = "Plan bbox extrusion skipped: local coordinates are too far from family origin.";
                }
                if (result != null)
                {
                    result.Warnings.Add("Candidate " + ShortId(candidate) + " skipped: local coordinates are too far from family origin after Plan View rebasing. Check DWG import units.");
                }
                return null;
            }

            maxX = Math.Max(maxX, minX + minSize);
            maxY = Math.Max(maxY, minY + minSize);
            return new BoundingBoxXYZ
            {
                Min = new XYZ(minX, minY, 0),
                Max = new XYZ(maxX, maxY, 0)
            };
        }

        private static bool AreFinite(params double[] values)
        {
            foreach (double value in values)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSafeLocalCoordinate(double valueFeet)
        {
            return Math.Abs(valueFeet) <= UnitUtilsExtensions.MmToFeet(200000.0);
        }

        private void CreateFallbackSafeExtrusion(
            Document document,
            DrawingProjectionRegion plan,
            DrawingProjectionRegion front,
            DrawingToFamilySettings settings,
            DrawingToFamilyResult result)
        {
            using (var transaction = new Transaction(document, "2D2F FALLBACK Plan Box Extrusion"))
            {
                try
                {
                    transaction.Start();
                    ConfigureFailureHandling(transaction);
                    Element fallback = CreateFallbackBox(document, plan, front, settings, result);
                    if (fallback != null)
                    {
                        transaction.Commit();
                        result.FallbackUsed = true;
                        result.CreatedGeometryCount++;
                        result.SolidExtrusionsCreated++;
                        result.CreatedElementIds.Add(fallback.Id.IntegerValue);
                    }
                    else
                    {
                        transaction.RollBack();
                    }
                }
                catch (Exception ex)
                {
                    if (transaction.HasStarted())
                    {
                        transaction.RollBack();
                    }

                    result.Errors.Add("FALLBACK plan bbox extrusion failed: " + ex.Message);
                }
            }
        }

        private void BuildSimpleFreeForms(Document document, IList<BuildCandidate> candidates, DrawingToFamilyResult result)
        {
            int index = 1;
            foreach (BuildCandidate candidate in candidates)
            {
                if (candidate == null
                    || candidate.PrimaryContour == null
                    || candidate.PrimaryContour.Type != ContourType.SolidProfile
                    || !candidate.CanBuild)
                {
                    result.FailedBuildCandidates++;
                    result.SkippedContours++;
                    index++;
                    continue;
                }

                using (var transaction = new Transaction(document, "2D2F Simple FreeForm " + index.ToString("000")))
                {
                    try
                    {
                        transaction.Start();
                        ConfigureFailureHandling(transaction);
                        FreeFormElement element = TryCreateSimpleFreeForm(document, candidate, null, result);
                        if (element != null)
                        {
                            transaction.Commit();
                            candidate.IsBuilt = true;
                            candidate.BuildResult = "Simple bbox FreeForm created.";
                            candidate.PrimaryContour.IsBuilt = true;
                            candidate.PrimaryContour.BuildResult = candidate.BuildResult;
                            result.CreatedGeometryCount++;
                            result.FreeFormElementsCreated++;
                            result.CreatedElementIds.Add(element.Id.IntegerValue);
                        }
                        else
                        {
                            transaction.RollBack();
                            result.FailedBuildCandidates++;
                            result.SkippedContours++;
                        }
                    }
                    catch (Exception ex)
                    {
                        if (transaction.HasStarted())
                        {
                            transaction.RollBack();
                        }

                        candidate.BuildResult = "Simple FreeForm failed: " + ex.Message;
                        result.Warnings.Add("Simple FreeForm failed for candidate " + ShortId(candidate) + ": " + ex.Message);
                        result.FailedBuildCandidates++;
                    }
                }

                index++;
            }
        }

        private FreeFormElement TryCreateSimpleFreeForm(Document document, BuildCandidate candidate, DrawingProjectionRegion plan, DrawingToFamilyResult result)
        {
            try
            {
                Solid solid = CreateCandidateBoxSolid(candidate, plan, result);
                if (solid == null || solid.Volume <= 1.0e-9)
                {
                    candidate.BuildResult = "Simple FreeForm skipped: empty bbox solid.";
                    return null;
                }

                return FreeFormElement.Create(document, solid);
            }
            catch (Exception ex)
            {
                candidate.BuildResult = "Simple FreeForm failed: " + ex.Message;
                result.Warnings.Add("Candidate " + ShortId(candidate) + " simple bbox FreeForm failed: " + ex.Message);
                return null;
            }
        }

        private static Solid CreateCandidateBoxSolid(BuildCandidate candidate, DrawingProjectionRegion plan, DrawingToFamilyResult result)
        {
            BoundingBoxXYZ box = candidate.PrimaryContour == null ? null : candidate.PrimaryContour.BoundingBox;
            if (box == null)
            {
                return null;
            }

            double minSize = UnitUtilsExtensions.MmToFeet(1.0);
            double depth = GetExtrusionDepthFeet(candidate);
            if (depth <= minSize)
            {
                return null;
            }

            BoundingBoxXYZ localBox = candidate.Direction == BuildDirection.ExtrudeZ_FromPlan
                ? CreateMinimumPlanBox(box, plan, candidate, result)
                : box;
            if (localBox == null)
            {
                return null;
            }
            CurveLoop loop = CreateBoxLoop(localBox, candidate.Direction);
            if (loop == null)
            {
                return null;
            }

            return GeometryCreationUtilities.CreateExtrusionGeometry(
                new List<CurveLoop> { loop },
                GetDirectionVector(candidate.Direction),
                depth);
        }

        private static CurveLoop CreateBoxLoop(BoundingBoxXYZ box, BuildDirection direction)
        {
            double minSize = UnitUtilsExtensions.MmToFeet(1.0);
            if (direction == BuildDirection.ExtrudeY_FromFront)
            {
                return CreateLoop(
                    new XYZ(box.Min.X, 0, box.Min.Z),
                    new XYZ(Math.Max(box.Max.X, box.Min.X + minSize), 0, box.Min.Z),
                    new XYZ(Math.Max(box.Max.X, box.Min.X + minSize), 0, Math.Max(box.Max.Z, box.Min.Z + minSize)),
                    new XYZ(box.Min.X, 0, Math.Max(box.Max.Z, box.Min.Z + minSize)));
            }

            if (direction == BuildDirection.ExtrudeX_FromSide)
            {
                return CreateLoop(
                    new XYZ(0, box.Min.Y, box.Min.Z),
                    new XYZ(0, Math.Max(box.Max.Y, box.Min.Y + minSize), box.Min.Z),
                    new XYZ(0, Math.Max(box.Max.Y, box.Min.Y + minSize), Math.Max(box.Max.Z, box.Min.Z + minSize)),
                    new XYZ(0, box.Min.Y, Math.Max(box.Max.Z, box.Min.Z + minSize)));
            }

            return CreateLoop(
                new XYZ(box.Min.X, box.Min.Y, 0),
                new XYZ(Math.Max(box.Max.X, box.Min.X + minSize), box.Min.Y, 0),
                new XYZ(Math.Max(box.Max.X, box.Min.X + minSize), Math.Max(box.Max.Y, box.Min.Y + minSize), 0),
                new XYZ(box.Min.X, Math.Max(box.Max.Y, box.Min.Y + minSize), 0));
        }

        private static CurveLoop CreateLoop(XYZ a, XYZ b, XYZ c, XYZ d)
        {
            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(a, b));
            loop.Append(Line.CreateBound(b, c));
            loop.Append(Line.CreateBound(c, d));
            loop.Append(Line.CreateBound(d, a));
            return loop;
        }

        private static XYZ GetDirectionVector(BuildDirection direction)
        {
            switch (direction)
            {
                case BuildDirection.ExtrudeY_FromFront:
                    return XYZ.BasisY;
                case BuildDirection.ExtrudeX_FromSide:
                    return XYZ.BasisX;
                default:
                    return XYZ.BasisZ;
            }
        }

        private void CreateFallbackFreeForm(
            Document document,
            DrawingProjectionRegion plan,
            DrawingProjectionRegion front,
            DrawingToFamilySettings settings,
            DrawingToFamilyResult result)
        {
            using (var transaction = new Transaction(document, "2D2F FALLBACK FreeForm Box"))
            {
                try
                {
                    transaction.Start();
                    ConfigureFailureHandling(transaction);
                    Solid solid = CreateFallbackBoxSolid(plan, front);
                    FreeFormElement fallback = solid == null ? null : FreeFormElement.Create(document, solid);
                    if (fallback != null)
                    {
                        transaction.Commit();
                        result.FallbackUsed = true;
                        result.CreatedGeometryCount++;
                        result.FreeFormElementsCreated++;
                        result.CreatedElementIds.Add(fallback.Id.IntegerValue);
                        result.Warnings.Add("FALLBACK bbox FreeForm was used because no simple candidate FreeForms were created.");
                    }
                    else
                    {
                        transaction.RollBack();
                        result.Errors.Add("FALLBACK bbox FreeForm was not created.");
                    }
                }
                catch (Exception ex)
                {
                    if (transaction.HasStarted())
                    {
                        transaction.RollBack();
                    }

                    result.Errors.Add("FALLBACK bbox FreeForm failed: " + ex.Message);
                }
            }
        }

        private static Solid CreateFallbackBoxSolid(DrawingProjectionRegion plan, DrawingProjectionRegion front)
        {
            double width = UnitUtilsExtensions.MmToFeet(Math.Max(100.0, plan == null ? 1000.0 : plan.WidthMm));
            double depth = UnitUtilsExtensions.MmToFeet(Math.Max(100.0, plan == null ? 1000.0 : plan.HeightMm));
            double height = UnitUtilsExtensions.MmToFeet(Math.Max(100.0, front == null ? 1000.0 : front.HeightMm));

            CurveLoop loop = CreateLoop(
                XYZ.Zero,
                new XYZ(width, 0, 0),
                new XYZ(width, depth, 0),
                new XYZ(0, depth, 0));

            return GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { loop }, XYZ.BasisZ, height);
        }

        private bool BuildCandidate(Document document, BuildCandidate candidate, int index, DrawingToFamilySettings settings, DrawingToFamilyResult result)
        {
            if (candidate.Direction == BuildDirection.ReferenceOnly)
            {
                return CreateReferenceLines(document, candidate, index, result) > 0;
            }

            if (candidate.Direction == BuildDirection.Skip)
            {
                candidate.BuildResult = candidate.SkipReason ?? "Skipped.";
                return false;
            }

            Element extrusion = TryCreateExtrusion(document, candidate, index, result);
            if (extrusion != null)
            {
                CompleteCandidateElement(candidate, extrusion, "Extrusion created.", false, true, result);
                return true;
            }

            bool allowFreeForm = settings == null || settings.AllowFreeFormFallback;
            if (allowFreeForm)
            {
                Element exactFreeForm = TryCreateExactFreeForm(document, candidate, index, result);
                if (exactFreeForm != null)
                {
                    CompleteCandidateElement(candidate, exactFreeForm, "Exact-profile FreeForm fallback created.", true, true, result);
                    return true;
                }

                FreeFormElement boxFreeForm = TryCreateSimpleFreeFormInTransaction(document, candidate, null, index, result);
                if (boxFreeForm != null)
                {
                    CompleteCandidateElement(candidate, boxFreeForm, "BBox FreeForm fallback created.", true, false, result);
                    result.FallbackUsed = true;
                    return true;
                }
            }

            int referenceCount = CreateReferenceLines(document, candidate, index, result);
            candidate.BuildResult = referenceCount > 0 ? "Saved as reference lines." : "Skipped: Revit rejected profile.";
            return referenceCount > 0;
        }

        private void CompleteCandidateElement(
            BuildCandidate candidate,
            Element element,
            string buildResult,
            bool freeForm,
            bool usesVoidLoops,
            DrawingToFamilyResult result)
        {
            candidate.BuildResult = buildResult;
            candidate.IsBuilt = true;
            if (candidate.PrimaryContour != null)
            {
                candidate.PrimaryContour.IsBuilt = true;
                candidate.PrimaryContour.BuildResult = buildResult;
            }

            foreach (RecognizedContour voidContour in candidate.VoidContours)
            {
                if (usesVoidLoops && voidContour != null && voidContour.IsValidForRevit)
                {
                    voidContour.IsBuilt = true;
                    voidContour.BuildResult = "Used as inner loop for " + buildResult;
                    result.VoidProfilesUsed++;
                }
            }

            result.CreatedGeometryCount++;
            if (freeForm)
            {
                result.FreeFormElementsCreated++;
                result.FallbackUsed = true;
            }
            else
            {
                result.SolidExtrusionsCreated++;
            }
            if (element != null)
            {
                result.CreatedElementIds.Add(element.Id.IntegerValue);
            }
        }

        private Element TryCreateExtrusion(
            Document document,
            BuildCandidate candidate,
            int index,
            DrawingToFamilyResult result)
        {
            try
            {
                string validationReason;
                CurveArrArray profile;
                if (!TryCreateSafeProfile(candidate, out profile, out validationReason))
                {
                    candidate.Warnings.Add(validationReason);
                    result.Warnings.Add("Candidate " + ShortId(candidate) + " skipped before Revit extrusion: " + validationReason);
                    return null;
                }

                double depthFeet = GetExtrusionDepthFeet(candidate);
                if (depthFeet <= UnitUtilsExtensions.MmToFeet(0.1))
                {
                    candidate.SkipReason = "Extrusion depth is zero.";
                    return null;
                }

                return CreateInTransaction(document, "2D2F Exact Extrusion " + index.ToString("000"), () =>
                {
                    SketchPlane sketchPlane = CreateSketchPlane(document, candidate.Direction);
                    Extrusion extrusion = document.FamilyCreate.NewExtrusion(true, profile, sketchPlane, depthFeet);
                    string layerName = candidate.PrimaryContour.SourceLayer ?? "Mixed";
                    Category subcategory = _subcategoryService.GetOrCreate(document, "2D2F_" + layerName, true);
                    ApplyLayerColor(subcategory, candidate.PrimaryContour);
                    _subcategoryService.AssignSubcategory(extrusion, subcategory);
                    SetElementComment(extrusion, BuildElementName(candidate, index));
                    return extrusion;
                });
            }
            catch (Exception ex)
            {
                candidate.Warnings.Add(ex.Message);
                result.Warnings.Add("Candidate " + ShortId(candidate) + " extrusion failed: " + ex.Message);
                return null;
            }
        }

        private FreeFormElement TryCreateExactFreeForm(Document document, BuildCandidate candidate, int index, DrawingToFamilyResult result)
        {
            try
            {
                string validationReason;
                IList<CurveLoop> loops;
                if (!TryCreateSafeCurveLoops(candidate, out loops, out validationReason))
                {
                    candidate.Warnings.Add(validationReason);
                    result.Warnings.Add("Candidate " + ShortId(candidate) + " exact FreeForm skipped: " + validationReason);
                    return null;
                }

                double depthFeet = GetExtrusionDepthFeet(candidate);
                XYZ direction = GetDirectionVector(candidate.Direction);
                if (depthFeet <= UnitUtilsExtensions.MmToFeet(0.1) || direction == null)
                {
                    candidate.SkipReason = "FreeForm extrusion depth is zero.";
                    return null;
                }

                return (FreeFormElement)CreateInTransaction(document, "2D2F Exact FreeForm " + index.ToString("000"), () =>
                {
                    Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(loops, direction, depthFeet);
                    FreeFormElement element = FreeFormElement.Create(document, solid);
                    string layerName = candidate.PrimaryContour.SourceLayer ?? "Mixed";
                    Category subcategory = _subcategoryService.GetOrCreate(document, "2D2F_" + layerName, true);
                    ApplyLayerColor(subcategory, candidate.PrimaryContour);
                    _subcategoryService.AssignSubcategory(element, subcategory);
                    SetElementComment(element, BuildElementName(candidate, index) + "_FREEFORM");
                    return element;
                });
            }
            catch (Exception ex)
            {
                candidate.Warnings.Add(ex.Message);
                result.Warnings.Add("Candidate " + ShortId(candidate) + " exact FreeForm failed: " + ex.Message);
                return null;
            }
        }

        private FreeFormElement TryCreateSimpleFreeFormInTransaction(
            Document document,
            BuildCandidate candidate,
            DrawingProjectionRegion plan,
            int index,
            DrawingToFamilyResult result)
        {
            try
            {
                return (FreeFormElement)CreateInTransaction(document, "2D2F BBox FreeForm " + index.ToString("000"), () => TryCreateSimpleFreeForm(document, candidate, plan, result));
            }
            catch (Exception ex)
            {
                candidate.Warnings.Add(ex.Message);
                result.Warnings.Add("Candidate " + ShortId(candidate) + " bbox FreeForm transaction failed: " + ex.Message);
                return null;
            }
        }

        private int CreateReferenceLines(Document document, BuildCandidate candidate, int index, DrawingToFamilyResult result)
        {
            if (candidate.PrimaryContour == null || candidate.PrimaryContour.Curves.Count == 0)
            {
                result.SkippedContours++;
                return 0;
            }

            int created = 0;
            using (var transaction = new Transaction(document, "2D2F Reference Lines " + index.ToString("000")))
            {
                try
                {
                    transaction.Start();
                    ConfigureFailureHandling(transaction);
                    SketchPlane sketchPlane = CreateSketchPlane(document, DirectionForProjection(candidate.PrimaryContour.SourceProjection));
                    Category subcategory = _subcategoryService.GetOrCreate(document, "2D2F_Reference", true);
                    foreach (Curve curve in candidate.PrimaryContour.Curves)
                    {
                        try
                        {
                            if (CurveLengthFeet(curve) <= UnitUtilsExtensions.MmToFeet(0.1))
                            {
                                continue;
                            }

                            Curve safeCurve;
                            if (!TryCreateSafeReferenceCurve(curve, candidate.PrimaryContour.SourceProjection, out safeCurve))
                            {
                                continue;
                            }

                            ModelCurve modelCurve = document.FamilyCreate.NewModelCurve(safeCurve, sketchPlane);
                            _subcategoryService.AssignSubcategory(modelCurve, subcategory);
                            SetElementComment(modelCurve, BuildElementName(candidate, index) + "_REF");
                            result.CreatedElementIds.Add(modelCurve.Id.IntegerValue);
                            created++;
                        }
                        catch (Exception ex)
                        {
                            result.Warnings.Add("Reference line failed for candidate " + ShortId(candidate) + ": " + ex.Message);
                        }
                    }

                    if (created > 0)
                    {
                        transaction.Commit();
                    }
                    else
                    {
                        transaction.RollBack();
                    }
                }
                catch (Exception ex)
                {
                    if (transaction.HasStarted())
                    {
                        transaction.RollBack();
                    }

                    result.Warnings.Add("Reference line transaction failed for candidate " + ShortId(candidate) + ": " + ex.Message);
                }
            }

            if (created > 0)
            {
                result.ReferenceLinesCreated += created;
                result.ReferenceObjectCount += created;
                candidate.PrimaryContour.BuildResult = "Reference lines created: " + created;
                candidate.PrimaryContour.IsBuilt = true;
            }
            else
            {
                result.SkippedContours++;
            }

            return created;
        }

        private Element CreateFallbackBox(
            Document document,
            DrawingProjectionRegion plan,
            DrawingProjectionRegion front,
            DrawingToFamilySettings settings,
            DrawingToFamilyResult result)
        {
            try
            {
                double widthMm = Math.Max(100.0, plan == null ? 1000.0 : plan.WidthMm);
                double depthMm = Math.Max(100.0, plan == null ? 1000.0 : plan.HeightMm);
                double heightMm = Math.Max(100.0, front == null ? 1000.0 : front.HeightMm);
                if (widthMm > 100000.0 || depthMm > 100000.0 || heightMm > 100000.0)
                {
                    result.Errors.Add("FALLBACK bounding box skipped: suspicious size. Check DWG import units.");
                    return null;
                }

                double widthFeet = UnitUtilsExtensions.MmToFeet(widthMm);
                double depthFeet = UnitUtilsExtensions.MmToFeet(depthMm);
                double heightFeet = UnitUtilsExtensions.MmToFeet(heightMm);

                var box = new BoundingBoxXYZ
                {
                    Min = XYZ.Zero,
                    Max = new XYZ(widthFeet, depthFeet, 0)
                };

                SketchPlane sketchPlane = SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
                CurveArrArray profile = CreateRectangleProfile(box);
                Extrusion extrusion = document.FamilyCreate.NewExtrusion(true, profile, sketchPlane, heightFeet);
                result.Warnings.Add("FALLBACK bounding box was used because no valid contour/reference geometry was built.");
                return extrusion;
            }
            catch (Exception ex)
            {
                result.Errors.Add("FALLBACK bounding box was not created: " + ex.Message);
                return null;
            }
        }

        private static SketchPlane CreateSketchPlane(Document document, BuildDirection direction)
        {
            switch (direction)
            {
                case BuildDirection.ExtrudeY_FromFront:
                    return SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisY, XYZ.Zero));
                case BuildDirection.ExtrudeX_FromSide:
                    return SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisX, XYZ.Zero));
                default:
                    return SketchPlane.Create(document, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
            }
        }

        private static BuildDirection DirectionForProjection(ProjectionType projection)
        {
            switch (projection)
            {
                case ProjectionType.Front:
                    return BuildDirection.ExtrudeY_FromFront;
                case ProjectionType.Side:
                    return BuildDirection.ExtrudeX_FromSide;
                default:
                    return BuildDirection.ExtrudeZ_FromPlan;
            }
        }

        private static double GetExtrusionDepthFeet(BuildCandidate candidate)
        {
            switch (candidate.Direction)
            {
                case BuildDirection.ExtrudeY_FromFront:
                    return UnitUtilsExtensions.MmToFeet(candidate.DepthMm);
                case BuildDirection.ExtrudeX_FromSide:
                    return UnitUtilsExtensions.MmToFeet(candidate.WidthMm);
                default:
                    return UnitUtilsExtensions.MmToFeet(candidate.HeightMm);
            }
        }

        private static bool TryCreateSafeProfile(BuildCandidate candidate, out CurveArrArray profile, out string reason)
        {
            profile = null;
            reason = null;

            IList<IList<XYZ>> pointLoops;
            if (!TryCreateSafeProfilePointLoops(candidate, out pointLoops, out reason))
            {
                return false;
            }

            profile = ToCurveArrArray(pointLoops);
            return true;
        }

        private static bool TryCreateSafeCurveLoops(BuildCandidate candidate, out IList<CurveLoop> loops, out string reason)
        {
            loops = null;
            reason = null;

            IList<IList<XYZ>> pointLoops;
            if (!TryCreateSafeProfilePointLoops(candidate, out pointLoops, out reason))
            {
                return false;
            }

            loops = ToCurveLoops(pointLoops);
            return loops.Count > 0;
        }

        private static bool TryCreateSafeProfilePointLoops(BuildCandidate candidate, out IList<IList<XYZ>> pointLoops, out string reason)
        {
            pointLoops = new List<IList<XYZ>>();
            reason = null;

            if (candidate == null || candidate.PrimaryContour == null)
            {
                reason = "Candidate has no primary contour.";
                return false;
            }

            double depthFeet = GetExtrusionDepthFeet(candidate);
            if (depthFeet <= UnitUtilsExtensions.MmToFeet(0.1))
            {
                reason = "Extrusion depth is too small.";
                return false;
            }

            IList<XYZ> outerPoints;
            if (!TryGetSafeContourPoints(candidate, candidate.PrimaryContour, true, out outerPoints, out reason))
            {
                return false;
            }

            pointLoops.Add(outerPoints);
            foreach (RecognizedContour voidContour in candidate.VoidContours)
            {
                IList<XYZ> voidPoints;
                string voidReason;
                if (TryGetSafeContourPoints(candidate, voidContour, false, out voidPoints, out voidReason))
                {
                    pointLoops.Add(voidPoints);
                }
                else if (!string.IsNullOrWhiteSpace(voidReason))
                {
                    candidate.Warnings.Add("Void contour skipped from exact profile: " + voidReason);
                }
            }

            return pointLoops.Count > 0;
        }

        private static bool TryGetSafeContourPoints(
            BuildCandidate candidate,
            RecognizedContour contour,
            bool requireSolid,
            out IList<XYZ> points,
            out string reason)
        {
            points = new List<XYZ>();
            reason = null;

            if (contour == null)
            {
                reason = "Contour is missing.";
                return false;
            }

            bool acceptedType = requireSolid
                ? contour.Type == ContourType.SolidProfile
                : contour.Type == ContourType.VoidProfile;
            if (!acceptedType || !contour.IsClosed || !contour.IsValidForRevit)
            {
                reason = requireSolid
                    ? "Only closed valid SolidProfile contours are allowed as extrusion outer loops."
                    : "Only closed valid VoidProfile contours are allowed as extrusion inner loops.";
                return false;
            }

            if (contour.Curves == null || contour.Curves.Count < 3)
            {
                reason = "Contour has fewer than 3 curves.";
                return false;
            }

            points = GetOrderedLoopPoints(contour.Curves);
            if (points.Count < 3)
            {
                reason = "Cannot extract ordered loop points.";
                return false;
            }

            if (!IsLoopPlanarForDirection(points, candidate.Direction))
            {
                reason = "Profile points are not on the expected sketch plane.";
                return false;
            }

            if (!IsLoopClosed(points, SafeToleranceFeet))
            {
                reason = "Profile loop is not exactly closed after cleanup.";
                return false;
            }

            if (HasDuplicateOrTinyEdges(points, UnitUtilsExtensions.MmToFeet(0.5)))
            {
                reason = "Profile has duplicate or tiny edges.";
                return false;
            }

            if (Math.Abs(AreaFeet2(points, contour.SourceProjection)) < 1.0e-10)
            {
                reason = "Profile area is too small.";
                return false;
            }

            if (HasSelfIntersections(points, contour.SourceProjection))
            {
                reason = "Profile appears self-intersecting.";
                return false;
            }

            return true;
        }

        private static CurveArrArray ToCurveArrArray(IList<IList<XYZ>> pointLoops)
        {
            var profile = new CurveArrArray();
            foreach (IList<XYZ> points in pointLoops ?? new List<IList<XYZ>>())
            {
                CurveArray loop = CreateCurveArray(points);
                if (loop.Size > 0)
                {
                    profile.Append(loop);
                }
            }

            return profile;
        }

        private static IList<CurveLoop> ToCurveLoops(IList<IList<XYZ>> pointLoops)
        {
            var loops = new List<CurveLoop>();
            foreach (IList<XYZ> points in pointLoops ?? new List<IList<XYZ>>())
            {
                var loop = new CurveLoop();
                for (int i = 0; i < points.Count; i++)
                {
                    XYZ start = points[i];
                    XYZ end = points[(i + 1) % points.Count];
                    if (start.DistanceTo(end) > UnitUtilsExtensions.MmToFeet(0.5))
                    {
                        loop.Append(Line.CreateBound(start, end));
                    }
                }

                loops.Add(loop);
            }

            return loops;
        }

        private static CurveArray CreateCurveArray(IList<XYZ> points)
        {
            var loop = new CurveArray();
            if (points == null)
            {
                return loop;
            }

            for (int i = 0; i < points.Count; i++)
            {
                XYZ start = points[i];
                XYZ end = points[(i + 1) % points.Count];
                if (start.DistanceTo(end) > UnitUtilsExtensions.MmToFeet(0.5))
                {
                    loop.Append(Line.CreateBound(start, end));
                }
            }

            return loop;
        }

        private static bool IsBoxFeature(NativeGeometryFeature feature)
        {
            return feature != null
                && (feature.FeatureType == NativeFeatureType.BaseFrame
                    || feature.FeatureType == NativeFeatureType.MainContainer
                    || feature.FeatureType == NativeFeatureType.Box
                    || feature.FeatureType == NativeFeatureType.SurfaceDetail
                    || feature.FeatureType == NativeFeatureType.IsoDetail);
        }

        private static int FeatureBuildOrder(NativeGeometryFeature feature)
        {
            if (feature == null)
            {
                return 99;
            }

            if (feature.FeatureType == NativeFeatureType.BaseFrame)
            {
                return 0;
            }

            if (feature.FeatureType == NativeFeatureType.MainContainer)
            {
                return 1;
            }

            if (feature.FeatureType == NativeFeatureType.Box)
            {
                return 2;
            }

            if (feature.FeatureType == NativeFeatureType.SurfaceDetail)
            {
                return 3;
            }

            if (feature.FeatureType == NativeFeatureType.IsoDetail)
            {
                return 4;
            }

            if (feature.FeatureType == NativeFeatureType.Cylinder
                || feature.FeatureType == NativeFeatureType.VoidCylinder)
            {
                return 5;
            }

            return 99;
        }

        private static bool IsSafeNativeSize(double valueMm, string label, NativeGeometryFeature feature, DrawingToFamilyResult result)
        {
            if (valueMm >= 0.1 && valueMm <= 100000.0)
            {
                return true;
            }

            string warning = "Native feature " + (feature == null ? "-" : feature.Name) + " skipped: suspicious " + label + " = " + valueMm.ToString("0.#") + " mm. Check DWG import units.";
            if (feature != null)
            {
                feature.BuildResult = warning;
                feature.SkipReason = warning;
            }
            if (result != null)
            {
                result.Warnings.Add(warning);
            }

            return false;
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

        private static void UpdateGlobalDimensions(IList<NativeGeometryFeature> features, DrawingToFamilyResult result)
        {
            if (features == null || features.Count == 0 || result == null)
            {
                return;
            }

            IList<NativeGeometryFeature> measured = features
                .Where(x => x != null
                    && x.CanBuild
                    && (x.FeatureType == NativeFeatureType.BaseFrame
                        || x.FeatureType == NativeFeatureType.MainContainer
                        || x.FeatureType == NativeFeatureType.Box))
                .ToList();
            if (measured.Count == 0)
            {
                measured = features.Where(x => x != null && x.CanBuild).ToList();
            }
            if (measured.Count == 0)
            {
                measured = features.Where(x => x != null).ToList();
            }
            if (measured.Count == 0)
            {
                return;
            }

            double minX = measured.Min(x => Math.Min(x.XMinMm, x.XMaxMm));
            double maxX = measured.Max(x => Math.Max(x.XMinMm, x.XMaxMm));
            double minY = measured.Min(x => Math.Min(x.YMinMm, x.YMaxMm));
            double maxY = measured.Max(x => Math.Max(x.YMinMm, x.YMaxMm));
            double minZ = measured.Min(x => Math.Min(x.ZMinMm, x.ZMaxMm));
            double maxZ = measured.Max(x => Math.Max(x.ZMinMm, x.ZMaxMm));
            result.WidthMm = Math.Abs(maxX - minX);
            result.DepthMm = Math.Abs(maxY - minY);
            result.HeightMm = Math.Abs(maxZ - minZ);
        }

        private static CurveArrArray CreateRectangleProfileAtZ(double xMin, double xMax, double yMin, double yMax, double z)
        {
            XYZ a = new XYZ(xMin, yMin, z);
            XYZ b = new XYZ(xMax, yMin, z);
            XYZ c = new XYZ(xMax, yMax, z);
            XYZ d = new XYZ(xMin, yMax, z);
            var profile = new CurveArrArray();
            var loop = new CurveArray();
            loop.Append(Line.CreateBound(a, b));
            loop.Append(Line.CreateBound(b, c));
            loop.Append(Line.CreateBound(c, d));
            loop.Append(Line.CreateBound(d, a));
            profile.Append(loop);
            return profile;
        }

        private static CurveArrArray CreateCircleProfileAtX(double x, double yCenter, double zCenter, double radius, int segments)
        {
            int count = Math.Max(12, segments);
            var profile = new CurveArrArray();
            var loop = new CurveArray();
            var points = new List<XYZ>();
            for (int i = 0; i < count; i++)
            {
                double angle = Math.PI * 2.0 * i / count;
                points.Add(new XYZ(
                    x,
                    yCenter + Math.Cos(angle) * radius,
                    zCenter + Math.Sin(angle) * radius));
            }

            for (int i = 0; i < count; i++)
            {
                loop.Append(Line.CreateBound(points[i], points[(i + 1) % count]));
            }

            profile.Append(loop);
            return profile;
        }

        private static CurveArrArray CreateCircleProfileAtY(double y, double xCenter, double zCenter, double radius, int segments)
        {
            int count = Math.Max(12, segments);
            var profile = new CurveArrArray();
            var loop = new CurveArray();
            var points = new List<XYZ>();
            for (int i = 0; i < count; i++)
            {
                double angle = Math.PI * 2.0 * i / count;
                points.Add(new XYZ(
                    xCenter + Math.Cos(angle) * radius,
                    y,
                    zCenter + Math.Sin(angle) * radius));
            }

            for (int i = 0; i < count; i++)
            {
                loop.Append(Line.CreateBound(points[i], points[(i + 1) % count]));
            }

            profile.Append(loop);
            return profile;
        }

        private static CurveArrArray CreateCircleProfileAtZ(double z, double xCenter, double yCenter, double radius, int segments)
        {
            int count = Math.Max(12, segments);
            var profile = new CurveArrArray();
            var loop = new CurveArray();
            var points = new List<XYZ>();
            for (int i = 0; i < count; i++)
            {
                double angle = Math.PI * 2.0 * i / count;
                points.Add(new XYZ(
                    xCenter + Math.Cos(angle) * radius,
                    yCenter + Math.Sin(angle) * radius,
                    z));
            }

            for (int i = 0; i < count; i++)
            {
                loop.Append(Line.CreateBound(points[i], points[(i + 1) % count]));
            }

            profile.Append(loop);
            return profile;
        }

        private static CurveArrArray CreateRectangleProfile(BoundingBoxXYZ box)
        {
            XYZ a = new XYZ(box.Min.X, box.Min.Y, 0);
            XYZ b = new XYZ(box.Max.X, box.Min.Y, 0);
            XYZ c = new XYZ(box.Max.X, box.Max.Y, 0);
            XYZ d = new XYZ(box.Min.X, box.Max.Y, 0);
            var profile = new CurveArrArray();
            var loop = new CurveArray();
            loop.Append(Line.CreateBound(a, b));
            loop.Append(Line.CreateBound(b, c));
            loop.Append(Line.CreateBound(c, d));
            loop.Append(Line.CreateBound(d, a));
            profile.Append(loop);
            return profile;
        }

        private static CurveArray ToCurveArray(IList<Curve> curves)
        {
            var array = new CurveArray();
            foreach (Curve curve in curves)
            {
                array.Append(curve);
            }

            return array;
        }

        private static IList<XYZ> GetOrderedLoopPoints(IList<Curve> curves)
        {
            var points = new List<XYZ>();
            foreach (Curve curve in curves)
            {
                XYZ start = CurveLoopUtils.GetEndPoint(curve, 0);
                XYZ end = CurveLoopUtils.GetEndPoint(curve, 1);
                if (start == null || end == null)
                {
                    return new List<XYZ>();
                }

                if (points.Count == 0)
                {
                    points.Add(start);
                }
                else if (points[points.Count - 1].DistanceTo(start) > UnitUtilsExtensions.MmToFeet(1.0))
                {
                    return new List<XYZ>();
                }

                if (points[points.Count - 1].DistanceTo(end) > UnitUtilsExtensions.MmToFeet(0.5))
                {
                    points.Add(end);
                }
            }

            if (points.Count < 4 || points[0].DistanceTo(points[points.Count - 1]) > UnitUtilsExtensions.MmToFeet(1.0))
            {
                return new List<XYZ>();
            }

            if (points[0].DistanceTo(points[points.Count - 1]) <= UnitUtilsExtensions.MmToFeet(1.0))
            {
                points.RemoveAt(points.Count - 1);
            }

            return points;
        }

        private static bool IsLoopPlanarForDirection(IList<XYZ> points, BuildDirection direction)
        {
            foreach (XYZ point in points)
            {
                switch (direction)
                {
                    case BuildDirection.ExtrudeY_FromFront:
                        if (Math.Abs(point.Y) > SafeToleranceFeet)
                        {
                            return false;
                        }
                        break;
                    case BuildDirection.ExtrudeX_FromSide:
                        if (Math.Abs(point.X) > SafeToleranceFeet)
                        {
                            return false;
                        }
                        break;
                    default:
                        if (Math.Abs(point.Z) > SafeToleranceFeet)
                        {
                            return false;
                        }
                        break;
                }
            }

            return true;
        }

        private static bool IsLoopClosed(IList<XYZ> points, double toleranceFeet)
        {
            return points != null && points.Count >= 3;
        }

        private static bool HasDuplicateOrTinyEdges(IList<XYZ> points, double minFeet)
        {
            for (int i = 0; i < points.Count; i++)
            {
                XYZ start = points[i];
                XYZ end = points[(i + 1) % points.Count];
                if (start.DistanceTo(end) <= minFeet)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSelfIntersections(IList<XYZ> points, ProjectionType projection)
        {
            for (int i = 0; i < points.Count; i++)
            {
                int iNext = (i + 1) % points.Count;
                for (int j = i + 1; j < points.Count; j++)
                {
                    int jNext = (j + 1) % points.Count;
                    if (i == j || iNext == j || jNext == i)
                    {
                        continue;
                    }

                    if (i == 0 && jNext == 0)
                    {
                        continue;
                    }

                    if (SegmentsIntersect(points[i], points[iNext], points[j], points[jNext], projection))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool SegmentsIntersect(XYZ a, XYZ b, XYZ c, XYZ d, ProjectionType projection)
        {
            double ax;
            double ay;
            double bx;
            double by;
            double cx;
            double cy;
            double dx;
            double dy;
            ToProjection2D(a, projection, out ax, out ay);
            ToProjection2D(b, projection, out bx, out by);
            ToProjection2D(c, projection, out cx, out cy);
            ToProjection2D(d, projection, out dx, out dy);

            double o1 = Orientation(ax, ay, bx, by, cx, cy);
            double o2 = Orientation(ax, ay, bx, by, dx, dy);
            double o3 = Orientation(cx, cy, dx, dy, ax, ay);
            double o4 = Orientation(cx, cy, dx, dy, bx, by);
            return o1 * o2 < 0 && o3 * o4 < 0;
        }

        private static double Orientation(double ax, double ay, double bx, double by, double cx, double cy)
        {
            return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        }

        private static double AreaFeet2(IList<XYZ> points, ProjectionType projection)
        {
            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i];
                XYZ b = points[(i + 1) % points.Count];
                double ax;
                double ay;
                double bx;
                double by;
                ToProjection2D(a, projection, out ax, out ay);
                ToProjection2D(b, projection, out bx, out by);
                area += ax * by - bx * ay;
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

        private static bool TryCreateSafeReferenceCurve(Curve curve, ProjectionType projection, out Curve safeCurve)
        {
            safeCurve = null;
            XYZ start = CurveLoopUtils.GetEndPoint(curve, 0);
            XYZ end = CurveLoopUtils.GetEndPoint(curve, 1);
            if (start == null || end == null || start.DistanceTo(end) <= UnitUtilsExtensions.MmToFeet(0.5))
            {
                return false;
            }

            switch (projection)
            {
                case ProjectionType.Front:
                    safeCurve = Line.CreateBound(new XYZ(start.X, 0, start.Z), new XYZ(end.X, 0, end.Z));
                    return true;
                case ProjectionType.Side:
                    safeCurve = Line.CreateBound(new XYZ(0, start.Y, start.Z), new XYZ(0, end.Y, end.Z));
                    return true;
                default:
                    safeCurve = Line.CreateBound(new XYZ(start.X, start.Y, 0), new XYZ(end.X, end.Y, 0));
                    return true;
            }
        }

        private static double CurveLengthFeet(Curve curve)
        {
            try
            {
                return curve.Length;
            }
            catch
            {
                XYZ start = CurveLoopUtils.GetEndPoint(curve, 0);
                XYZ end = CurveLoopUtils.GetEndPoint(curve, 1);
                return start == null || end == null ? 0 : start.DistanceTo(end);
            }
        }

        private static void UpdateGlobalDimensions(
            DrawingProjectionRegion plan,
            DrawingProjectionRegion front,
            DrawingProjectionRegion side,
            DrawingToFamilyResult result)
        {
            result.WidthMm = plan != null ? plan.WidthMm : front == null ? 0 : front.WidthMm;
            result.DepthMm = plan != null ? plan.HeightMm : side == null ? 0 : side.WidthMm;
            result.HeightMm = front != null ? front.HeightMm : side == null ? 0 : side.HeightMm;
        }

        private static void ApplyLayerColor(Category subcategory, RecognizedContour contour)
        {
            if (subcategory == null || contour == null || contour.SourceEntities.Count == 0)
            {
                return;
            }

            try
            {
                Color color = contour.SourceEntities.First().LayerColor;
                if (color != null)
                {
                    subcategory.LineColor = color;
                }
            }
            catch
            {
                // Color assignment is cosmetic; geometry creation must not depend on it.
            }
        }

        private static void SetElementComment(Element element, string text)
        {
            if (element == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                Parameter parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (parameter != null && !parameter.IsReadOnly)
                {
                    parameter.Set(text);
                }
            }
            catch
            {
                // Some family forms do not expose comments.
            }
        }

        private static string BuildElementName(BuildCandidate candidate, int index)
        {
            string projection = candidate.PrimaryContour == null ? "Unknown" : candidate.PrimaryContour.SourceProjection.ToString();
            string layer = candidate.PrimaryContour == null || string.IsNullOrWhiteSpace(candidate.PrimaryContour.SourceLayer)
                ? "Layer"
                : candidate.PrimaryContour.SourceLayer;
            return string.Format("2D2F_{0}_{1}_Contour_{2:000}", projection, SanitizeShort(layer), index);
        }

        private static string SanitizeShort(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Layer";
            }

            string clean = new string(value.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
            if (clean.Length == 0)
            {
                clean = "Layer";
            }

            return clean.Length > 32 ? clean.Substring(0, 32) : clean;
        }

        private static void WarnAboutSuspiciousSize(DrawingToFamilyResult result, double valueMm, string label)
        {
            if (valueMm > 0 && (valueMm < 10.0 || valueMm > 100000.0))
            {
                result.Warnings.Add("Подозрительный габарит: " + label + " = " + valueMm.ToString("0.#") + " мм. Проверьте единицы импорта DWG. Отдельная проверка масштаба в MVP не используется.");
            }
        }

        private static bool ShouldSuppressRevitWarnings(DrawingToFamilySettings settings)
        {
            return settings != null && settings.SuppressRevitWarnings;
        }

        private void ConfigureFailureHandling(Transaction transaction)
        {
            if (!_suppressRevitWarnings || transaction == null)
            {
                return;
            }

            try
            {
                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new RevitWarningSuppressor());
                transaction.SetFailureHandlingOptions(options);
            }
            catch
            {
                // Failure options are best-effort; transaction creation should continue.
            }
        }

        private Element CreateInTransaction(Document document, string name, Func<Element> create)
        {
            var transaction = new Transaction(document, name);
            transaction.Start();
            ConfigureFailureHandling(transaction);
            try
            {
                Element element = create();
                if (element == null)
                {
                    transaction.RollBack();
                    return null;
                }

                transaction.Commit();
                return element;
            }
            catch
            {
                try
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }
                }
                catch
                {
                    // Nothing else can be done here.
                }

                throw;
            }
        }

        private class RevitWarningSuppressor : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                if (failuresAccessor == null)
                {
                    return FailureProcessingResult.Continue;
                }

                IList<FailureMessageAccessor> failures = failuresAccessor.GetFailureMessages();
                if (failures == null)
                {
                    return FailureProcessingResult.Continue;
                }

                foreach (FailureMessageAccessor failure in failures)
                {
                    if (failure != null && failure.GetSeverity() == FailureSeverity.Warning)
                    {
                        failuresAccessor.DeleteWarning(failure);
                    }
                }

                return FailureProcessingResult.Continue;
            }
        }

        private void LogStage(string message)
        {
            if (_logger != null)
            {
                _logger.Stage(message);
            }
        }

        private void LogInfo(string message)
        {
            if (_logger != null)
            {
                _logger.Info(message);
            }
        }

        private void LogData(string name, object value)
        {
            if (_logger != null)
            {
                _logger.Data(name, value);
            }
        }

        private void LogError(string message, Exception exception)
        {
            if (_logger != null)
            {
                _logger.Error(message, exception);
            }
        }

        private void LogRegion(string label, DrawingProjectionRegion region)
        {
            if (_logger == null || region == null)
            {
                return;
            }

            _logger.Info(string.Format(
                "{0}: type={1}; valid={2}; entities={3}; size={4:0.#} x {5:0.#} mm; bbox={6}",
                label,
                region.Type,
                region.IsValid,
                region.EntityCount,
                region.WidthMm,
                region.HeightMm,
                BoundingBoxUtils.ToMmString(region.BoundingBox)));
        }

        private static string ShortId(BuildCandidate candidate)
        {
            return candidate == null ? "-" : candidate.Id.ToString("N").Substring(0, 8);
        }
    }
}
