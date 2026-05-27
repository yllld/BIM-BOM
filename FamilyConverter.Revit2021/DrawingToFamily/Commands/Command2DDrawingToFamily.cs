using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Services;
using FamilyConverter.Revit2021.DrawingToFamily.UI;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;
using FamilyConverter.Revit2021.Services;

namespace FamilyConverter.Revit2021.DrawingToFamily.Commands
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class Command2DDrawingToFamily : IExternalCommand
    {
        private enum StagedBuildDecision
        {
            Cancel,
            ReportOnly,
            BuildFirst,
            BuildAll
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            var technicalLog = new DrawingToFamilyLogger();

            try
            {
                technicalLog.Stage("Start");
                if (uidoc == null || uidoc.Document == null)
                {
                    TaskDialog.Show(ProductInfo.Name, "Активный документ Revit не найден.");
                    return Result.Cancelled;
                }

                Document document = uidoc.Document;
                if (!document.IsFamilyDocument)
                {
                    TaskDialog.Show(ProductInfo.Name, "Команда работает только в редакторе семейств Revit.");
                    return Result.Cancelled;
                }

                ImportInstance importInstance = GetImportInstance(uidoc);
                if (importInstance == null)
                {
                    return Result.Cancelled;
                }

                var defaultSettings = new DrawingToFamilySettings();
                var reader = new DwgImportGeometryReader();
                technicalLog.Stage("Read ImportInstance geometry");
                IList<DwgCurveEntity> entities = reader.Read(document, importInstance, defaultSettings.MinimumElementSizeMm);
                technicalLog.Info("Read curve entities: " + entities.Count);
                if (entities.Count == 0)
                {
                    TaskDialog.Show(ProductInfo.Name, "В выбранном DWG не найдены линии, дуги, кривые или полилинии.");
                    return Result.Cancelled;
                }

                var layerAnalyzer = new DwgLayerAnalyzer();
                IList<DwgLayerInfo> layers = layerAnalyzer.Analyze(entities);
                if (layers.Count == 0)
                {
                    TaskDialog.Show(ProductInfo.Name, "Не удалось определить слои DWG. Проверьте импортированный файл.");
                    return Result.Cancelled;
                }

                technicalLog.Stage("Layer analysis complete");
                technicalLog.Data("Layer count", layers.Count);
                foreach (DwgLayerInfo layer in layers)
                {
                    technicalLog.Info(string.Format(
                        "Layer: name={0}; objects={1}; role={2}; included={3}; color={4}; totalLengthMm={5:0.#}",
                        layer.LayerName,
                        layer.ObjectCount,
                        layer.UserRole,
                        layer.IsIncluded,
                        layer.LayerColorHex,
                        layer.TotalLengthMm));
                }

                ApplyLayerRoles(entities, layers);
                var preview = BuildPreview(importInstance, entities, layers);
                var picker = new ProjectionRegionPicker(uidoc, entities);
                DrawingToFamilySettings settings = ShowSettingsAndPickProjectionRegions(uiapp, picker, preview, new DrawingToFamilySettings(), technicalLog);
                if (settings == null)
                {
                    return Result.Cancelled;
                }

                technicalLog.Stage("Apply settings after WPF");
                technicalLog.Data("ClosureToleranceMm", settings.ClosureToleranceMm);
                technicalLog.Data("MinimumElementSizeMm", settings.MinimumElementSizeMm);
                ApplyLayerRoles(entities, settings.Layers);
                foreach (DwgCurveEntity entity in entities)
                {
                    entity.IsSmallObject = entity.LengthMm < settings.MinimumElementSizeMm;
                }

                IList<DrawingProjectionRegion> selectedProjections = GetSelectedProjections(settings);
                technicalLog.Data("Selected projection count", selectedProjections.Count);
                foreach (DrawingProjectionRegion region in selectedProjections)
                {
                    picker.RefreshRegionEntities(region, settings);
                    technicalLog.Info(string.Format("{0}: entities={1}; size={2:0.#} x {3:0.#} mm", region.Type, region.EntityCount, region.WidthMm, region.HeightMm));
                }

                var result = new DrawingToFamilyResult
                {
                    ReadObjectCount = entities.Count,
                    LayerCount = layers.Count,
                    PlanObjectCount = settings.PlanRegion == null ? 0 : settings.PlanRegion.EntityCount,
                    FrontObjectCount = settings.FrontRegion == null ? 0 : settings.FrontRegion.EntityCount,
                    SideObjectCount = settings.SideRegion == null ? 0 : settings.SideRegion.EntityCount,
                    LogPath = technicalLog.LogPath
                };

                if (settings.PlanRegion == null || !settings.PlanRegion.IsValid)
                {
                    TaskDialog.Show(ProductInfo.Name, "Не выбран вид сверху.");
                    return Result.Cancelled;
                }
                if (settings.FrontRegion == null || !settings.FrontRegion.IsValid)
                {
                    TaskDialog.Show(ProductInfo.Name, "Не выбран вид спереди.");
                    return Result.Cancelled;
                }

                technicalLog.Stage("Contour recognition - strict preview");
                DrawingToFamilySettings strictSettings = CloneSettingsWithTolerance(settings, 0.1);
                IList<RecognizedContour> strictContours = RecognizeContours(selectedProjections, strictSettings, result.Warnings);
                technicalLog.Stage("Contour recognition - strict preview complete");
                if (!ConfirmContourStep(strictContours, strictSettings))
                {
                    return Result.Cancelled;
                }

                bool hasOpenPlanCurves = strictContours.Any(x => x.SourceProjection == ProjectionType.Plan && (x.Type == ContourType.OpenCurve || x.Type == ContourType.ReferenceCurve));
                if (hasOpenPlanCurves && settings.ClosureToleranceMm > 0.1)
                {
                    TaskDialogResult closeChoice = AskAutoCloseOpenContours(strictContours, settings);
                    if (closeChoice == TaskDialogResult.Cancel)
                    {
                        return Result.Cancelled;
                    }

                    if (closeChoice == TaskDialogResult.CommandLink2)
                    {
                        settings = strictSettings;
                        selectedProjections = GetSelectedProjections(settings);
                        result.Warnings.Add("User declined automatic closing of open plan lines. Strict 0.1 mm closure tolerance is used.");
                    }
                    else
                    {
                        result.Warnings.Add("User allowed automatic closing of open plan lines within tolerance " + settings.ClosureToleranceMm.ToString("0.###") + " mm.");
                    }
                }

                technicalLog.Stage("Contour recognition - final");
                IList<RecognizedContour> contours = RecognizeContours(selectedProjections, settings, result.Warnings);
                technicalLog.Stage("Contour recognition - final complete");
                FillContourStats(result, contours);
                foreach (RecognizedContour contour in contours)
                {
                    technicalLog.Info(string.Format(
                        "Contour {0}: projection={1}; layer={2}; type={3}; nesting={4}; curves={5}; area={6:0.#}mm2; valid={7}; reason={8}",
                        contour.Id,
                        contour.SourceProjection,
                        contour.SourceLayer,
                        contour.Type,
                        contour.NestingLevel,
                        contour.Curves.Count,
                        contour.AreaMm2,
                        contour.IsValidForRevit,
                        contour.ReasonIfInvalid ?? "-"));
                }

                technicalLog.Stage("Show dimension confirmation");
                if (!ConfirmDimensionStep(settings, result))
                {
                    return Result.Cancelled;
                }
                technicalLog.Stage("Dimension confirmation accepted");

                technicalLog.Stage("Show shape confirmation");
                if (!ConfirmShapeStep(contours, settings))
                {
                    return Result.Cancelled;
                }
                technicalLog.Stage("Shape confirmation accepted");

                technicalLog.Stage("Projection fusion");
                var fusion = new ProjectionFusionService();
                IList<BuildCandidate> candidates = fusion.CreateCandidates(
                    contours,
                    settings.PlanRegion,
                    settings.FrontRegion,
                    settings.SideRegion,
                    settings,
                result.Warnings);
                result.BuildCandidateCount = candidates.Count;
                FillContourStats(result, contours);
                foreach (RecognizedContour footprint in contours.Where(x => string.Equals(x.SourceLayer, "Plan footprint", StringComparison.OrdinalIgnoreCase)))
                {
                    technicalLog.Info(string.Format(
                        "Synthetic Plan footprint: id={0}; size={1:0.#} x {2:0.#} mm; area={3:0.#}mm2; sourceEntities={4}",
                        footprint.Id,
                        footprint.WidthMm,
                        footprint.HeightMm,
                        footprint.AreaMm2,
                        footprint.SourceEntities.Count));
                }
                result.ProcessedObjectCount = selectedProjections.SelectMany(x => x.Entities).Select(x => x.Id).Distinct().Count();
                result.UsedObjectCount = candidates
                    .Where(x => x.PrimaryContour != null)
                    .SelectMany(x => x.PrimaryContour.SourceEntities)
                    .Select(x => x.Id)
                    .Distinct()
                    .Count();
                result.SkippedObjects = Math.Max(0, result.ProcessedObjectCount - result.UsedObjectCount);
                technicalLog.Info("Build candidates: " + candidates.Count);
                foreach (BuildCandidate candidate in candidates)
                {
                    technicalLog.Info(string.Format(
                        "Candidate {0}: direction={1}; contour={2}; canBuild={3}; confidence={4:0.##}; W/D/H={5:0.#}/{6:0.#}/{7:0.#}mm; voids={8}; skip={9}",
                        candidate.Id,
                        candidate.Direction,
                        candidate.PrimaryContour == null ? "-" : candidate.PrimaryContour.Id.ToString(),
                        candidate.CanBuild,
                        candidate.Confidence,
                        candidate.WidthMm,
                        candidate.DepthMm,
                        candidate.HeightMm,
                        candidate.VoidContours.Count,
                        candidate.SkipReason ?? "-"));
                }

                if (candidates.Count == 0)
                {
                    result.Warnings.Add("Не найдено build-кандидатов. Будет использован FALLBACK только если reference geometry тоже не создастся.");
                }

                technicalLog.Stage("Native feature extraction");
                var featureExtractor = new NativeFeatureExtractionService();
                IList<NativeGeometryFeature> nativeFeatures = featureExtractor.Extract(
                    contours,
                    candidates,
                    settings.PlanRegion,
                    settings.FrontRegion,
                    settings.SideRegion,
                    settings,
                    result.Warnings);
                FillFeatureStats(result, nativeFeatures);
                foreach (NativeGeometryFeature feature in nativeFeatures)
                {
                    result.NativeFeatures.Add(feature);
                    technicalLog.Info(string.Format(
                        "Native feature {0}: type={1}; axis={2}; box=({3:0.#},{4:0.#},{5:0.#})..({6:0.#},{7:0.#},{8:0.#}); diameter={9:0.#}; confidence={10:0.##}; canBuild={11}; source={12}",
                        feature.Id,
                        feature.FeatureType,
                        feature.Axis,
                        feature.XMinMm,
                        feature.YMinMm,
                        feature.ZMinMm,
                        feature.XMaxMm,
                        feature.YMaxMm,
                        feature.ZMaxMm,
                        feature.DiameterMm,
                        feature.Confidence,
                        feature.CanBuild,
                        feature.SourceDescription));
                    foreach (string warning in feature.Warnings)
                    {
                        technicalLog.Warning("Native feature " + feature.Id + ": " + warning);
                    }
                }

                technicalLog.Stage("Show native feature stack");
                if (!ConfirmNativeFeatureStackStep(nativeFeatures))
                {
                    return Result.Cancelled;
                }

                technicalLog.Stage("Show final build decision");
                StagedBuildDecision decision = AskFinalBuildDecision(candidates, nativeFeatures, settings);
                technicalLog.Data("Final build decision", decision);
                if (decision == StagedBuildDecision.Cancel)
                {
                    return Result.Cancelled;
                }

                if (decision == StagedBuildDecision.ReportOnly)
                {
                    settings.BuildGeometry = false;
                    result.Warnings.Add("User selected analysis/report only. No Revit geometry was created.");
                }
                else if (decision == StagedBuildDecision.BuildFirst)
                {
                    settings.BuildGeometry = true;
                    settings.MaxBuildCandidates = 1;
                    result.Warnings.Add("User selected staged test build: only the largest Plan contour will be created.");
                }
                else
                {
                    settings.BuildGeometry = true;
                    settings.MaxBuildCandidates = 0;
                }

                technicalLog.Stage("Geometry build");
                var builder = new FamilyGeometryBuilder(new SubcategoryService(), technicalLog);
                if (nativeFeatures.Count > 0)
                {
                    builder.BuildNativeFeatures(document, nativeFeatures, settings, result);
                }
                else
                {
                    result.Warnings.Add("Native feature stack is empty. Legacy Plan-only builder is used as fallback.");
                    builder.Build(document, candidates, settings.PlanRegion, settings.FrontRegion, settings.SideRegion, settings, result);
                }
                technicalLog.Info(string.Format(
                    "Build result: solids={0}; references={1}; failed={2}; fallback={3}",
                    result.SolidExtrusionsCreated,
                    result.ReferenceLinesCreated,
                    result.FailedBuildCandidates,
                    result.FallbackUsed));
                foreach (string warning in result.Warnings)
                {
                    technicalLog.Warning(warning);
                }
                foreach (string error in result.Errors)
                {
                    technicalLog.Error(error, null);
                }

                technicalLog.Stage("Report");
                var reportWriter = new DrawingToFamilyReportWriter();
                result.ReportPath = reportWriter.Write(uiapp, importInstance, preview, settings, selectedProjections, contours, candidates, result);
                technicalLog.Info("Report: " + result.ReportPath);

                TaskDialog.Show(ProductInfo.Name + " - 2D Drawing to Family", BuildSummary(result));
                return result.Errors.Count > 0 && result.CreatedGeometryCount == 0 && result.ReferenceLinesCreated == 0 ? Result.Failed : Result.Succeeded;
            }
            catch (Exception ex)
            {
                technicalLog.Error("Unhandled command error.", ex);
                message = ex.Message;
                TaskDialog.Show(ProductInfo.Name, "Команда завершилась с ошибкой:\n" + ex.Message + "\n\nЛог:\n" + technicalLog.LogPath);
                return Result.Failed;
            }
        }

        private static IList<RecognizedContour> RecognizeContours(
            IList<DrawingProjectionRegion> selectedProjections,
            DrawingToFamilySettings settings,
            IList<string> warnings)
        {
            var contours = new List<RecognizedContour>();
            var contourRecognition = new ContourRecognitionService();
            foreach (DrawingProjectionRegion region in selectedProjections ?? new List<DrawingProjectionRegion>())
            {
                if (region.Type == ProjectionType.Isometric)
                {
                    continue;
                }

                contours.AddRange(contourRecognition.Recognize(region, settings, warnings));
            }

            return contours;
        }

        private static DrawingToFamilySettings ShowSettingsAndPickProjectionRegions(
            UIApplication uiapp,
            ProjectionRegionPicker picker,
            DrawingToFamilyPreview preview,
            DrawingToFamilySettings settings,
            DrawingToFamilyLogger technicalLog)
        {
            DrawingToFamilySettings currentSettings = settings ?? new DrawingToFamilySettings();
            while (true)
            {
                technicalLog.Stage("Create WPF settings window");
                var window = new DrawingToFamilyWindow(preview, picker, technicalLog, currentSettings);
                new System.Windows.Interop.WindowInteropHelper(window).Owner = uiapp.MainWindowHandle;
                technicalLog.Stage("Show WPF settings window");
                window.ShowDialog();
                technicalLog.Stage("WPF settings window closed");
                technicalLog.Data("WPF requested action", window.RequestedAction);
                currentSettings = window.Settings;

                if (window.RequestedAction == DrawingToFamilyWindowAction.Build)
                {
                    return currentSettings;
                }

                if (window.RequestedAction == DrawingToFamilyWindowAction.Cancel
                    || window.RequestedAction == DrawingToFamilyWindowAction.None)
                {
                    return null;
                }

                ProjectionType projectionType;
                if (!TryGetRequestedProjectionType(window.RequestedAction, out projectionType))
                {
                    return null;
                }

                PickProjectionRegionInCommand(picker, currentSettings, projectionType, technicalLog);
            }
        }

        private static bool TryGetRequestedProjectionType(DrawingToFamilyWindowAction action, out ProjectionType projectionType)
        {
            projectionType = ProjectionType.Unknown;
            if (action == DrawingToFamilyWindowAction.PickPlan)
            {
                projectionType = ProjectionType.Plan;
                return true;
            }
            if (action == DrawingToFamilyWindowAction.PickFront)
            {
                projectionType = ProjectionType.Front;
                return true;
            }
            if (action == DrawingToFamilyWindowAction.PickSide)
            {
                projectionType = ProjectionType.Side;
                return true;
            }
            if (action == DrawingToFamilyWindowAction.PickIsometric)
            {
                projectionType = ProjectionType.Isometric;
                return true;
            }

            return false;
        }

        private static void PickProjectionRegionInCommand(
            ProjectionRegionPicker picker,
            DrawingToFamilySettings settings,
            ProjectionType type,
            DrawingToFamilyLogger technicalLog)
        {
            try
            {
                technicalLog.Stage("Command PickPoint start: " + type);
                DrawingProjectionRegion region = picker.Pick(type, settings);
                technicalLog.Stage("Command PickPoint end: " + type);
                if (region == null)
                {
                    technicalLog.Warning("Projection pick returned null: " + type);
                    return;
                }

                if (type == ProjectionType.Plan)
                {
                    settings.PlanRegion = region;
                }
                else if (type == ProjectionType.Front)
                {
                    settings.FrontRegion = region;
                }
                else if (type == ProjectionType.Side)
                {
                    settings.SideRegion = region;
                }
                else if (type == ProjectionType.Isometric)
                {
                    settings.IsometricRegion = region;
                    settings.UseIsometricReference = true;
                }

                technicalLog.Info(string.Format(
                    "Projection picked in command: type={0}; valid={1}; entities={2}; size={3:0.#} x {4:0.#} mm",
                    region.Type,
                    region.IsValid,
                    region.EntityCount,
                    region.WidthMm,
                    region.HeightMm));
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                technicalLog.Warning("Projection pick cancelled: " + type);
            }
            catch (Exception ex)
            {
                technicalLog.Error("Projection pick failed: " + type, ex);
                TaskDialog.Show(ProductInfo.Name, "Не удалось выбрать область проекции:\n" + ex.Message);
            }
        }

        private static DrawingToFamilySettings CloneSettingsWithTolerance(DrawingToFamilySettings source, double closureToleranceMm)
        {
            var clone = new DrawingToFamilySettings
            {
                PlanRegion = source.PlanRegion,
                FrontRegion = source.FrontRegion,
                SideRegion = source.SideRegion,
                IsometricRegion = source.IsometricRegion,
                UseIsometricReference = source.UseIsometricReference,
                ClosureToleranceMm = closureToleranceMm,
                MinimumElementSizeMm = source.MinimumElementSizeMm,
                BuildGeometry = source.BuildGeometry,
                AllowFreeFormFallback = source.AllowFreeFormFallback,
                MaxBuildCandidates = source.MaxBuildCandidates,
                PlanRegionId = source.PlanRegionId,
                FrontRegionId = source.FrontRegionId,
                SideRegionId = source.SideRegionId,
                IsometricRegionId = source.IsometricRegionId
            };
            clone.Layers.Clear();
            foreach (DwgLayerInfo layer in source.Layers)
            {
                clone.Layers.Add(layer);
            }

            return clone;
        }

        private static bool ConfirmContourStep(IList<RecognizedContour> contours, DrawingToFamilySettings settings)
        {
            int planSolid = contours.Count(x => x.SourceProjection == ProjectionType.Plan && x.Type == ContourType.SolidProfile);
            int planVoid = contours.Count(x => x.SourceProjection == ProjectionType.Plan && x.Type == ContourType.VoidProfile);
            int planOpen = contours.Count(x => x.SourceProjection == ProjectionType.Plan && (x.Type == ContourType.OpenCurve || x.Type == ContourType.ReferenceCurve));
            int planInvalid = contours.Count(x => x.SourceProjection == ProjectionType.Plan && x.Type == ContourType.Invalid);

            var dialog = new TaskDialog(ProductInfo.Name + " - Step 1")
            {
                MainInstruction = "1. Замкнутые контуры для построения",
                MainContent =
                    "Plan View:\n" +
                    "Solid-контуры: " + planSolid + "\n" +
                    "Void/отверстия: " + planVoid + "\n" +
                    "Открытые линии: " + planOpen + "\n" +
                    "Невалидные контуры: " + planInvalid + "\n\n" +
                    "На этом шаге геометрия Revit еще не создается. Проверяем только линии DWG.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Продолжить анализ");
            return dialog.Show() == TaskDialogResult.CommandLink1;
        }

        private static TaskDialogResult AskAutoCloseOpenContours(IList<RecognizedContour> strictContours, DrawingToFamilySettings settings)
        {
            int open = strictContours.Count(x => x.SourceProjection == ProjectionType.Plan && (x.Type == ContourType.OpenCurve || x.Type == ContourType.ReferenceCurve));
            var dialog = new TaskDialog(ProductInfo.Name + " - Step 1A")
            {
                MainInstruction = "Есть разомкнутые линии в Plan View",
                MainContent =
                    "Найдено открытых линий: " + open + "\n\n" +
                    "Замкнуть линии автоматически, если разрыв меньше " + settings.ClosureToleranceMm.ToString("0.###") + " мм?\n" +
                    "Да: контуры будут переанализированы с текущим допуском.\n" +
                    "Нет: будут использоваться только строго замкнутые контуры.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Да, замкнуть автоматически");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Нет, строить только строго замкнутые");
            return dialog.Show();
        }

        private static bool ConfirmDimensionStep(DrawingToFamilySettings settings, DrawingToFamilyResult result)
        {
            double planWidth = settings.PlanRegion == null ? 0 : settings.PlanRegion.WidthMm;
            double planDepth = settings.PlanRegion == null ? 0 : settings.PlanRegion.HeightMm;
            double frontWidth = settings.FrontRegion == null ? 0 : settings.FrontRegion.WidthMm;
            double frontHeight = settings.FrontRegion == null ? 0 : settings.FrontRegion.HeightMm;
            double sideDepth = settings.SideRegion == null ? 0 : settings.SideRegion.WidthMm;
            double sideHeight = settings.SideRegion == null ? 0 : settings.SideRegion.HeightMm;

            var dialog = new TaskDialog(ProductInfo.Name + " - Step 2")
            {
                MainInstruction = "2. Начало и конец выдавливания",
                MainContent =
                    "Строим от вида сверху.\n" +
                    "Начало выдавливания: Z = 0 мм.\n" +
                    "Конец выдавливания: Z = высота из Front View.\n\n" +
                    "Plan View: ширина " + planWidth.ToString("0.#") + " мм, глубина " + planDepth.ToString("0.#") + " мм.\n" +
                    "Front View: ширина " + frontWidth.ToString("0.#") + " мм, высота " + frontHeight.ToString("0.#") + " мм.\n" +
                    "Side View: глубина " + sideDepth.ToString("0.#") + " мм, высота " + sideHeight.ToString("0.#") + " мм.\n\n" +
                    "Front/Side используются только для размеров и сверки, не как самостоятельные тела.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Размеры понятны, продолжить");
            return dialog.Show() == TaskDialogResult.CommandLink1;
        }

        private static bool ConfirmShapeStep(IList<RecognizedContour> contours, DrawingToFamilySettings settings)
        {
            var dialog = new TaskDialog(ProductInfo.Name + " - Step 3")
            {
                MainInstruction = "3. Форма по линиям DWG",
                MainContent = BuildShapeSummary(contours, settings),
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Формы понятны, продолжить");
            return dialog.Show() == TaskDialogResult.CommandLink1;
        }

        private static bool ConfirmNativeFeatureStackStep(IList<NativeGeometryFeature> features)
        {
            var dialog = new TaskDialog(ProductInfo.Name + " - Step 3B")
            {
                MainInstruction = "3B. Стек объектов перед построением",
                MainContent = BuildNativeFeatureSummary(features),
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Стек объектов понятен, перейти к построению");
            return dialog.Show() == TaskDialogResult.CommandLink1;
        }

        private static string BuildNativeFeatureSummary(IList<NativeGeometryFeature> features)
        {
            IList<NativeGeometryFeature> list = features ?? new List<NativeGeometryFeature>();
            var builder = new StringBuilder();
            if (list.Count == 0)
            {
                builder.AppendLine("Native-объекты не найдены. Будет использован старый безопасный fallback по Plan View.");
                return builder.ToString();
            }

            builder.AppendLine("Сначала плагин обвел DWG и собрал не геометрию, а признаки модели:");
            builder.AppendLine();
            int index = 1;
            foreach (NativeGeometryFeature feature in list)
            {
                builder.AppendLine(string.Format(
                    "{0}. {1}: {2:0.#} x {3:0.#} x {4:0.#} мм; axis={5}; confidence={6:0.##}",
                    index,
                    feature.FeatureType,
                    feature.WidthMm,
                    feature.DepthMm,
                    feature.HeightMm,
                    feature.Axis,
                    feature.Confidence));
                if (feature.FeatureType == NativeFeatureType.Cylinder)
                {
                    builder.AppendLine("   диаметр: " + feature.DiameterMm.ToString("0.#") + " мм");
                }
                if (!feature.CanBuild)
                {
                    builder.AppendLine("   не строится: " + feature.SkipReason);
                }
                foreach (string warning in feature.Warnings)
                {
                    builder.AppendLine("   warning: " + warning);
                }

                index++;
            }

            builder.AppendLine();
            builder.AppendLine("Построение будет выполнено нативными Revit-профилями: прямоугольные выдавливания и цилиндры по X/Y/Z в зависимости от проекции, где найден круг. Остальные проекции используются как источники размеров, Plan View остается диктующим.");
            return builder.ToString();
        }

        private static string BuildShapeSummary(IList<RecognizedContour> contours, DrawingToFamilySettings settings)
        {
            IList<RecognizedContour> plan = contours
                .Where(x => x.SourceProjection == ProjectionType.Plan)
                .ToList();
            int solids = plan.Count(x => x.Type == ContourType.SolidProfile);
            int holes = plan.Count(x => x.Type == ContourType.VoidProfile);
            int rectangles = plan.Count(IsRectangleLike);
            int squares = plan.Count(x => IsRectangleLike(x) && IsSquareLike(x, settings));
            int circles = plan.Count(IsCircleLike);
            int open = plan.Count(x => x.Type == ContourType.OpenCurve || x.Type == ContourType.ReferenceCurve);

            var builder = new StringBuilder();
            builder.AppendLine("Plan View является источником построения.");
            builder.AppendLine();
            builder.AppendLine("Solid-контуры: " + solids);
            builder.AppendLine("Отверстия/внутренние контуры: " + holes);
            builder.AppendLine("Прямоугольники: " + rectangles);
            builder.AppendLine("Квадраты: " + squares);
            builder.AppendLine("Круги/почти круги: " + circles);
            builder.AppendLine("Открытые линии: " + open);
            builder.AppendLine();
            builder.AppendLine("Дальше плагин собирает native feature stack: основной footprint из Plan View, высоты/глубины из Front/Side и круговые признаки из той проекции, где они читаются надежнее.");
            return builder.ToString();
        }

        private static StagedBuildDecision AskFinalBuildDecision(IList<BuildCandidate> candidates, IList<NativeGeometryFeature> features, DrawingToFamilySettings settings)
        {
            int buildable = candidates.Count(x => x != null && x.CanBuild);
            int nativeBuildable = (features ?? new List<NativeGeometryFeature>()).Count(x => x != null && x.CanBuild);
            int boxes = (features ?? new List<NativeGeometryFeature>()).Count(x => x != null
                && (x.FeatureType == NativeFeatureType.BaseFrame
                    || x.FeatureType == NativeFeatureType.MainContainer
                    || x.FeatureType == NativeFeatureType.Box
                    || x.FeatureType == NativeFeatureType.SurfaceDetail
                    || x.FeatureType == NativeFeatureType.IsoDetail));
            int cylinders = (features ?? new List<NativeGeometryFeature>()).Count(x => x != null && x.FeatureType == NativeFeatureType.Cylinder);
            int isoDetails = (features ?? new List<NativeGeometryFeature>()).Count(x => x != null && x.FeatureType == NativeFeatureType.IsoDetail);
            double largestArea = candidates
                .Where(x => x != null && x.PrimaryContour != null)
                .Select(x => x.PrimaryContour.AreaMm2)
                .DefaultIfEmpty(0)
                .Max();

            var dialog = new TaskDialog(ProductInfo.Name + " - Step 4")
            {
                MainInstruction = "4. Метод построения и запуск",
                MainContent =
                    "Build-кандидатов контуров: " + buildable + "\n" +
                    "Native-объектов: " + nativeBuildable + " (boxes: " + boxes + ", cylinders: " + cylinders + ", ISO-details: " + isoDetails + ")\n" +
                    "Основной плановый footprint/контур: " + largestArea.ToString("0.#") + " мм2\n\n" +
                    "Порядок: от больших тел к меньшим.\n" +
                    "Метод: native feature stack. Plan задает footprint; Front/Side задают высоты, глубины и признаки формы. Опциональный 3D/ISO вид добавляет только приблизительные detail-boxes, не меняя основной габарит.\n" +
                    "Если Revit вернет управляемую ошибку, будет попытка bbox FreeForm fallback.\n\n" +
                    "Если Revit снова падает фатально, выберите на следующем тесте 'только 1 самое крупное тело' - так мы изолируем точку падения.",
                CommonButtons = TaskDialogCommonButtons.Cancel
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Только отчет, без построения");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Построить только 1 самое крупное тело");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Построить все тела поэтапно");
            TaskDialogResult result = dialog.Show();
            if (result == TaskDialogResult.CommandLink1)
            {
                return StagedBuildDecision.ReportOnly;
            }
            if (result == TaskDialogResult.CommandLink2)
            {
                return StagedBuildDecision.BuildFirst;
            }
            if (result == TaskDialogResult.CommandLink3)
            {
                return StagedBuildDecision.BuildAll;
            }

            return StagedBuildDecision.Cancel;
        }

        private static bool IsRectangleLike(RecognizedContour contour)
        {
            return contour != null
                && contour.Type == ContourType.SolidProfile
                && contour.Curves.Count == 4;
        }

        private static bool IsSquareLike(RecognizedContour contour, DrawingToFamilySettings settings)
        {
            if (contour == null)
            {
                return false;
            }

            double tolerance = Math.Max(settings.ClosureToleranceMm, Math.Max(contour.WidthMm, contour.HeightMm) * 0.05);
            return Math.Abs(contour.WidthMm - contour.HeightMm) <= tolerance;
        }

        private static bool IsCircleLike(RecognizedContour contour)
        {
            if (contour == null || contour.Type != ContourType.SolidProfile)
            {
                return false;
            }

            bool fromCurvedEntity = contour.SourceEntities.Any(x =>
                string.Equals(x.EntityType, "Arc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.EntityType, "Ellipse", StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.EntityType, "Circle", StringComparison.OrdinalIgnoreCase));
            bool manySegmentsAndRoundBox = contour.Curves.Count >= 8
                && Math.Abs(contour.WidthMm - contour.HeightMm) <= Math.Max(2.0, Math.Max(contour.WidthMm, contour.HeightMm) * 0.08);
            return fromCurvedEntity || manySegmentsAndRoundBox;
        }

        private static ImportInstance GetImportInstance(UIDocument uidoc)
        {
            ICollection<ElementId> selected = uidoc.Selection.GetElementIds();
            if (selected != null && selected.Count == 1)
            {
                Element element = uidoc.Document.GetElement(selected.First());
                ImportInstance import = element as ImportInstance;
                if (import != null)
                {
                    return import;
                }

                TaskDialog.Show(ProductInfo.Name, "Выбранный элемент не является импортированным DWG. Выберите один ImportInstance.");
                return null;
            }

            if (selected != null && selected.Count > 1)
            {
                TaskDialog.Show(ProductInfo.Name, "Выберите только один импортированный DWG.");
                return null;
            }

            try
            {
                Reference picked = uidoc.Selection.PickObject(ObjectType.Element, new ImportInstanceSelectionFilter(), "Выберите импортированный DWG");
                return uidoc.Document.GetElement(picked.ElementId) as ImportInstance;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        private static DrawingToFamilyPreview BuildPreview(
            ImportInstance importInstance,
            IList<DwgCurveEntity> entities,
            IList<DwgLayerInfo> layers)
        {
            BoundingBoxXYZ box = null;
            foreach (DwgCurveEntity entity in entities)
            {
                box = BoundingBoxUtils.Union(box, entity.BoundingBox);
            }

            var preview = new DrawingToFamilyPreview
            {
                ImportName = importInstance.Name + " (" + importInstance.Id.IntegerValue + ")",
                ObjectCount = entities.Count,
                LayerCount = layers.Count,
                BoundingBox = box,
                BoundingBoxText = BoundingBoxUtils.ToMmString(box)
            };

            foreach (DwgCurveEntity entity in entities)
            {
                preview.Entities.Add(entity);
            }
            foreach (DwgLayerInfo layer in layers)
            {
                preview.Layers.Add(layer);
            }

            return preview;
        }

        private static void ApplyLayerRoles(IList<DwgCurveEntity> entities, IList<DwgLayerInfo> layers)
        {
            var map = layers.ToDictionary(x => x.LayerName ?? "Unknown", x => x, StringComparer.OrdinalIgnoreCase);
            foreach (DwgCurveEntity entity in entities)
            {
                DwgLayerInfo layer;
                if (map.TryGetValue(entity.LayerName ?? "Unknown", out layer))
                {
                    entity.RecognitionRole = layer.EffectiveRole;
                    entity.IsIgnored = layer.EffectiveRole == RecognitionRole.Ignored;
                }
                else
                {
                    entity.RecognitionRole = RecognitionRole.Unknown;
                    entity.IsIgnored = false;
                }
            }
        }

        private static IList<DrawingProjectionRegion> GetSelectedProjections(DrawingToFamilySettings settings)
        {
            var result = new List<DrawingProjectionRegion>();
            if (settings.PlanRegion != null && settings.PlanRegion.IsValid)
            {
                result.Add(settings.PlanRegion);
            }
            if (settings.FrontRegion != null && settings.FrontRegion.IsValid)
            {
                result.Add(settings.FrontRegion);
            }
            if (settings.SideRegion != null && settings.SideRegion.IsValid)
            {
                result.Add(settings.SideRegion);
            }
            if (settings.UseIsometricReference
                && settings.IsometricRegion != null
                && settings.IsometricRegion.IsValid)
            {
                result.Add(settings.IsometricRegion);
            }

            return result;
        }

        private static void FillContourStats(DrawingToFamilyResult result, IList<RecognizedContour> contours)
        {
            result.ContoursFound = contours.Count;
            result.SolidContours = contours.Count(x => x.Type == ContourType.SolidProfile);
            result.VoidContours = contours.Count(x => x.Type == ContourType.VoidProfile);
            result.OpenContours = contours.Count(x => x.Type == ContourType.OpenCurve || x.Type == ContourType.ReferenceCurve);
            result.InvalidContours = contours.Count(x => x.Type == ContourType.Invalid);
            result.OuterContours = result.SolidContours;
            result.InnerContours = result.VoidContours;
            result.HoleContours = result.VoidContours;
            result.SkippedContours = result.InvalidContours;
        }

        private static void FillFeatureStats(DrawingToFamilyResult result, IList<NativeGeometryFeature> features)
        {
            IList<NativeGeometryFeature> list = features ?? new List<NativeGeometryFeature>();
            result.NativeFeatureCount = list.Count;
            result.BoxFeatureCount = list.Count(x => x.FeatureType == NativeFeatureType.BaseFrame
                || x.FeatureType == NativeFeatureType.MainContainer
                || x.FeatureType == NativeFeatureType.Box
                || x.FeatureType == NativeFeatureType.SurfaceDetail
                || x.FeatureType == NativeFeatureType.IsoDetail);
            result.CylinderFeatureCount = list.Count(x => x.FeatureType == NativeFeatureType.Cylinder
                || x.FeatureType == NativeFeatureType.VoidCylinder);
        }

        private static string BuildSummary(DrawingToFamilyResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Итог: " + result.Status);
            builder.AppendLine();
            builder.AppendLine("Считано линий/кривых: " + result.ReadObjectCount);
            builder.AppendLine("Слоёв: " + result.LayerCount);
            builder.AppendLine("Объектов в Plan/Front/Side: " + result.PlanObjectCount + " / " + result.FrontObjectCount + " / " + result.SideObjectCount);
            builder.AppendLine("Контуров найдено: " + result.ContoursFound);
            builder.AppendLine("Solid / void / open: " + result.SolidContours + " / " + result.VoidContours + " / " + result.OpenContours);
            builder.AppendLine("Build candidates: " + result.BuildCandidateCount);
            builder.AppendLine("Native features: " + result.NativeFeatureCount + " (boxes " + result.BoxFeatureCount + ", cylinders/openings " + result.CylinderFeatureCount + ")");
            builder.AppendLine("Solid extrusions: " + result.SolidExtrusionsCreated);
            builder.AppendLine("FreeForms: " + result.FreeFormElementsCreated);
            builder.AppendLine("Reference lines: " + result.ReferenceLinesCreated);
            builder.AppendLine("Пропущено кандидатов: " + result.FailedBuildCandidates);
            builder.AppendLine("FALLBACK: " + (result.FallbackUsed ? "да" : "нет"));
            builder.AppendLine();
            builder.AppendLine("Отчет: " + result.ReportPath);
            builder.AppendLine("Лог: " + result.LogPath);

            if (result.Warnings.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("Предупреждения: " + result.Warnings.Count);
                builder.AppendLine(result.Warnings[0]);
            }

            return builder.ToString();
        }

        private class ImportInstanceSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is ImportInstance;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }
}
