using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FamilyConverter.Revit2021.DrawingToFamily.Models;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class DrawingToFamilyReportWriter
    {
        public string Write(
            UIApplication uiapp,
            ImportInstance importInstance,
            DrawingToFamilyPreview preview,
            DrawingToFamilySettings settings,
            IList<DrawingProjectionRegion> projections,
            IList<RecognizedContour> contours,
            IList<BuildCandidate> candidates,
            DrawingToFamilyResult result)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, ProductInfo.AppDataRootFolder, ProductInfo.AppDataProductFolder, "logs");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "DrawingToFamily_Report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");

            StringBuilder builder = new StringBuilder();
            Document document = uiapp == null || uiapp.ActiveUIDocument == null ? null : uiapp.ActiveUIDocument.Document;
            builder.AppendLine("DWG Converter - 2D Drawing to Family Report");
            builder.AppendLine("Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine("Revit: " + (uiapp == null ? "-" : uiapp.Application.VersionName));
            builder.AppendLine("Document: " + (document == null ? "-" : document.PathName));
            builder.AppendLine("Family: " + (document == null || document.OwnerFamily == null ? "-" : document.OwnerFamily.Name));
            builder.AppendLine("ImportInstance: " + (importInstance == null ? "-" : importInstance.Id.IntegerValue.ToString()));
            builder.AppendLine();

            builder.AppendLine("Read objects: " + result.ReadObjectCount);
            builder.AppendLine("Layers: " + result.LayerCount);
            builder.AppendLine("Objects inside Plan View: " + result.PlanObjectCount);
            builder.AppendLine("Objects inside Front View: " + result.FrontObjectCount);
            builder.AppendLine("Objects inside Side View: " + result.SideObjectCount);
            builder.AppendLine("DWG bounds: " + (preview == null ? "-" : preview.BoundingBoxText));
            builder.AppendLine();

            builder.AppendLine("Layers:");
            foreach (DwgLayerInfo layer in settings.Layers)
            {
                builder.AppendLine(string.Format(
                    "- {0}; objects={1}; color={2}; role={3}; included={4}",
                    layer.LayerName,
                    layer.ObjectCount,
                    layer.ColorLabel,
                    layer.UserRole,
                    layer.IsIncluded));
            }
            builder.AppendLine();

            builder.AppendLine("Projection regions:");
            foreach (DrawingProjectionRegion region in projections)
            {
                builder.AppendLine(string.Format(
                    "- {0}; valid={1}; objects={2}; size={3:0.#} x {4:0.#} mm; warning={5}",
                    region.Type,
                    region.IsValid,
                    region.EntityCount,
                    region.WidthMm,
                    region.HeightMm,
                    string.IsNullOrWhiteSpace(region.WarningMessage) ? "-" : region.WarningMessage));
            }
            builder.AppendLine();

            builder.AppendLine("Final dimensions:");
            builder.AppendLine("Width: " + result.WidthMm.ToString("0.#") + " mm");
            builder.AppendLine("Depth: " + result.DepthMm.ToString("0.#") + " mm");
            builder.AppendLine("Height: " + result.HeightMm.ToString("0.#") + " mm");
            builder.AppendLine();

            builder.AppendLine("Contours:");
            builder.AppendLine("Found: " + result.ContoursFound);
            builder.AppendLine("Solid profiles: " + result.SolidContours);
            builder.AppendLine("Void profiles: " + result.VoidContours);
            builder.AppendLine("Open/reference curves: " + result.OpenContours);
            builder.AppendLine("Invalid contours: " + result.InvalidContours);
            foreach (RecognizedContour contour in contours ?? new List<RecognizedContour>())
            {
                builder.AppendLine(string.Format(
                    "- {0}; projection={1}; layer={2}; nesting={3}; size={4:0.#} x {5:0.#} mm; area={6:0.#} mm2; built={7}; reason={8}; result={9}",
                    ShortId(contour.Id),
                    contour.SourceProjection,
                    contour.SourceLayer,
                    contour.NestingLevel,
                    contour.WidthMm,
                    contour.HeightMm,
                    contour.AreaMm2,
                    contour.IsBuilt,
                    string.IsNullOrWhiteSpace(contour.ReasonIfInvalid) ? "-" : contour.ReasonIfInvalid,
                    string.IsNullOrWhiteSpace(contour.BuildResult) ? "-" : contour.BuildResult));
            }
            builder.AppendLine();

            builder.AppendLine("Build candidates:");
            foreach (BuildCandidate candidate in candidates ?? new List<BuildCandidate>())
            {
                builder.AppendLine(string.Format(
                    "- {0}; direction={1}; contour={2}; confidence={3:0.##}; size W/D/H={4:0.#}/{5:0.#}/{6:0.#} mm; voids={7}; canBuild={8}; built={9}; result={10}",
                    ShortId(candidate.Id),
                    candidate.Direction,
                    candidate.PrimaryContour == null ? "-" : ShortId(candidate.PrimaryContour.Id),
                    candidate.Confidence,
                    candidate.WidthMm,
                    candidate.DepthMm,
                    candidate.HeightMm,
                    candidate.VoidContours.Count,
                    candidate.CanBuild,
                    candidate.IsBuilt,
                    string.IsNullOrWhiteSpace(candidate.BuildResult) ? candidate.SkipReason ?? "-" : candidate.BuildResult));
                foreach (string warning in candidate.Warnings)
                {
                    builder.AppendLine("  warning: " + warning);
                }
            }
            builder.AppendLine();

            builder.AppendLine("Native feature stack:");
            builder.AppendLine("Features: " + result.NativeFeatureCount);
            builder.AppendLine("Box features: " + result.BoxFeatureCount);
            builder.AppendLine("Cylinder/opening features: " + result.CylinderFeatureCount);
            foreach (NativeGeometryFeature feature in result.NativeFeatures ?? new List<NativeGeometryFeature>())
            {
                builder.AppendLine(string.Format(
                    "- {0}; type={1}; axis={2}; box=({3:0.#},{4:0.#},{5:0.#})..({6:0.#},{7:0.#},{8:0.#}) mm; diameter={9:0.#}; confidence={10:0.##}; canBuild={11}; built={12}; method={13}; result={14}",
                    ShortId(feature.Id),
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
                    feature.IsBuilt,
                    string.IsNullOrWhiteSpace(feature.BuildMethod) ? "-" : feature.BuildMethod,
                    string.IsNullOrWhiteSpace(feature.BuildResult) ? feature.SkipReason ?? "-" : feature.BuildResult));
                foreach (string warning in feature.Warnings)
                {
                    builder.AppendLine("  warning: " + warning);
                }
            }
            builder.AppendLine();

            builder.AppendLine("Geometry:");
            builder.AppendLine("Solid extrusion created: " + result.SolidExtrusionsCreated);
            builder.AppendLine("FreeForm elements created: " + result.FreeFormElementsCreated);
            builder.AppendLine("Void profiles used: " + result.VoidProfilesUsed);
            builder.AppendLine("Reference/model lines created: " + result.ReferenceLinesCreated);
            builder.AppendLine("Failed build candidates: " + result.FailedBuildCandidates);
            builder.AppendLine("Created ElementIds: " + string.Join(", ", result.CreatedElementIds));
            builder.AppendLine("FALLBACK used: " + result.FallbackUsed);
            builder.AppendLine();

            builder.AppendLine("Build coverage:");
            builder.AppendLine("Read objects: " + result.ReadObjectCount);
            builder.AppendLine("Included in processing: " + result.ProcessedObjectCount);
            builder.AppendLine("Used by build candidates: " + result.UsedObjectCount);
            builder.AppendLine("Saved as reference: " + result.ReferenceObjectCount);
            builder.AppendLine("Skipped: " + result.SkippedObjects);
            builder.AppendLine();

            builder.AppendLine("MVP limitations:");
            builder.AppendLine("- Text dimensions, MTEXT, OCR, ML and external AI APIs are not used.");
            builder.AppendLine("- DWG is read only through Revit ImportInstance geometry, not through a full CAD SDK.");
            builder.AppendLine("- Splines/NURBS are approximated by tessellated reference/profile segments where possible.");
            builder.AppendLine("- Projection regions are selected by the user and are not guessed automatically.");
            builder.AppendLine("- Bad, open or self-intersecting contours can be saved as reference lines instead of solid geometry.");
            builder.AppendLine();

            builder.AppendLine("Warnings:");
            foreach (string warning in result.Warnings)
            {
                builder.AppendLine("- " + warning);
            }
            builder.AppendLine();

            builder.AppendLine("Errors:");
            foreach (string error in result.Errors)
            {
                builder.AppendLine("- " + error);
            }
            builder.AppendLine();
            builder.AppendLine("Final status: " + result.Status);

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static string ShortId(Guid id)
        {
            return id.ToString("N").Substring(0, 8);
        }
    }
}
