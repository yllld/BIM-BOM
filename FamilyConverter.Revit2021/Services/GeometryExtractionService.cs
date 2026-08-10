using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.Models;
using FamilyConverter.Revit2021.Utils;

namespace FamilyConverter.Revit2021.Services
{
    public class GeometryExtractionService
    {
        private readonly LayerService _layerService;
        private readonly UnitService _unitService;

        public GeometryExtractionService(LayerService layerService, UnitService unitService)
        {
            _layerService = layerService;
            _unitService = unitService;
        }

        public IList<GeometryObjectInfo> Extract(Document document, ImportInstance importInstance, ConversionOptions options)
        {
            var result = new List<GeometryObjectInfo>();
            if (document == null || importInstance == null)
            {
                return result;
            }

            var geometryOptions = new Options
            {
                ComputeReferences = !options.SuperTurboMode,
                IncludeNonVisibleObjects = false,
                DetailLevel = options.SuperTurboMode ? ViewDetailLevel.Coarse : ViewDetailLevel.Fine
            };

            GeometryElement geometry = importInstance.get_Geometry(geometryOptions);
            if (geometry == null)
            {
                return result;
            }

            int index = 0;

            // Transform policy:
            // The top ImportInstance geometry normally exposes GeometryInstance.Transform.
            // We start with identity and compose nested GeometryInstance transforms only once.
            TraverseGeometry(document, importInstance.Id, geometry, Transform.Identity, options, result, ref index);
            return result;
        }

        private void TraverseGeometry(
            Document document,
            ElementId sourceId,
            GeometryElement geometry,
            Transform currentTransform,
            ConversionOptions options,
            IList<GeometryObjectInfo> result,
            ref int index)
        {
            if (geometry == null)
            {
                return;
            }

            foreach (GeometryObject obj in geometry)
            {
                if (obj == null)
                {
                    continue;
                }

                GeometryInstance instance = obj as GeometryInstance;
                if (instance != null)
                {
                    TraverseGeometryInstance(document, sourceId, instance, currentTransform, options, result, ref index);
                    continue;
                }

                Solid solid = obj as Solid;
                if (solid != null)
                {
                    AddSolid(document, sourceId, solid, obj, currentTransform, options, result, ref index);
                    continue;
                }

                Mesh mesh = obj as Mesh;
                if (mesh != null)
                {
                    if (options.CollectUnsupportedGeometry)
                    {
                        AddMesh(document, sourceId, mesh, obj, currentTransform, result, ref index);
                    }
                    continue;
                }

                Curve curve = obj as Curve;
                if (curve != null)
                {
                    if (options.CollectUnsupportedGeometry)
                    {
                        AddCurve(document, sourceId, curve, obj, currentTransform, result, ref index);
                    }
                    continue;
                }

                PolyLine polyLine = obj as PolyLine;
                if (polyLine != null)
                {
                    if (options.CollectUnsupportedGeometry)
                    {
                        AddPolyLine(document, sourceId, polyLine, obj, currentTransform, result, ref index);
                    }
                    continue;
                }

                if (options.CollectUnsupportedGeometry)
                {
                    AddUnknown(document, sourceId, obj, currentTransform, result, ref index);
                }
            }
        }

        private void TraverseGeometryInstance(
            Document document,
            ElementId sourceId,
            GeometryInstance instance,
            Transform currentTransform,
            ConversionOptions options,
            IList<GeometryObjectInfo> result,
            ref int index)
        {
            Transform nextTransform = currentTransform == null
                ? instance.Transform
                : currentTransform.Multiply(instance.Transform);

            try
            {
                GeometryElement symbolGeometry = instance.GetSymbolGeometry();
                TraverseGeometry(document, sourceId, symbolGeometry, nextTransform, options, result, ref index);
                return;
            }
            catch
            {
                // Fallback: instance geometry is already transformed by Revit. Keep the current transform.
            }

            try
            {
                GeometryElement instanceGeometry = instance.GetInstanceGeometry();
                TraverseGeometry(document, sourceId, instanceGeometry, currentTransform, options, result, ref index);
            }
            catch
            {
                AddUnknown(document, sourceId, instance, currentTransform, result, ref index);
            }
        }

        private void AddSolid(
            Document document,
            ElementId sourceId,
            Solid solid,
            GeometryObject rawObject,
            Transform transform,
            ConversionOptions options,
            IList<GeometryObjectInfo> result,
            ref int index)
        {
            if (solid.Faces.Size == 0)
            {
                return;
            }

            var warnings = new List<string>();
            Solid transformedSolid = GeometryUtils.TryCreateTransformedSolid(solid, transform, warnings);
            if (transformedSolid == null || transformedSolid.Faces.Size == 0)
            {
                return;
            }

            double volumeFeet3 = Math.Max(0, transformedSolid.Volume);
            double minVolume = _unitService.CubicMmToCubicFeet(Math.Max(0, options.MinSolidVolumeMm3));
            if (volumeFeet3 <= minVolume)
            {
                return;
            }

            BoundingBoxXYZ boundingBox = GeometryUtils.GetSolidBoundingBox(transformedSolid);
            if (ShouldSkipByBoundingBox(boundingBox, options))
            {
                return;
            }

            var info = new GeometryObjectInfo
            {
                SourceImportInstanceId = sourceId,
                GeometryIndex = ++index,
                GeometryType = "Solid",
                Transform = transform,
                Solid = transformedSolid,
                RawObject = rawObject,
                BoundingBox = boundingBox,
                VolumeFeet3 = volumeFeet3,
                VolumeMm3 = _unitService.CubicFeetToCubicMm(volumeFeet3),
                FaceCount = transformedSolid.Faces.Size,
                EdgeCount = transformedSolid.Edges.Size,
                LayerName = options.ReadLayerNames ? _layerService.GetLayerName(document, rawObject) : "Turbo"
            };

            foreach (string warning in warnings)
            {
                info.Warnings.Add(warning);
            }

            result.Add(info);
        }

        private bool ShouldSkipByBoundingBox(BoundingBoxXYZ boundingBox, ConversionOptions options)
        {
            if (boundingBox == null || options.MinSolidMaxDimensionMm <= 0)
            {
                return false;
            }

            double dx = Math.Abs(boundingBox.Max.X - boundingBox.Min.X);
            double dy = Math.Abs(boundingBox.Max.Y - boundingBox.Min.Y);
            double dz = Math.Abs(boundingBox.Max.Z - boundingBox.Min.Z);
            double maxDimensionMm = _unitService.FeetToMm(Math.Max(dx, Math.Max(dy, dz)));
            return maxDimensionMm < options.MinSolidMaxDimensionMm;
        }

        private void AddMesh(
            Document document,
            ElementId sourceId,
            Mesh mesh,
            GeometryObject rawObject,
            Transform transform,
            IList<GeometryObjectInfo> result,
            ref int index)
        {
            result.Add(new GeometryObjectInfo
            {
                SourceImportInstanceId = sourceId,
                GeometryIndex = ++index,
                GeometryType = "Mesh",
                Transform = transform,
                Mesh = mesh,
                RawObject = rawObject,
                BoundingBox = GeometryUtils.GetMeshBoundingBox(mesh, transform),
                FaceCount = mesh.NumTriangles,
                EdgeCount = 0,
                LayerName = _layerService.GetLayerName(document, rawObject)
            });
        }

        private void AddCurve(
            Document document,
            ElementId sourceId,
            Curve curve,
            GeometryObject rawObject,
            Transform transform,
            IList<GeometryObjectInfo> result,
            ref int index)
        {
            var warnings = new List<string>();
            Curve transformedCurve = GeometryUtils.TryCreateTransformedCurve(curve, transform, warnings);
            var info = new GeometryObjectInfo
            {
                SourceImportInstanceId = sourceId,
                GeometryIndex = ++index,
                GeometryType = "Curve",
                Transform = transform,
                Curve = transformedCurve,
                RawObject = rawObject,
                BoundingBox = GeometryUtils.GetCurveBoundingBox(transformedCurve),
                FaceCount = 0,
                EdgeCount = 1,
                LayerName = _layerService.GetLayerName(document, rawObject)
            };

            foreach (string warning in warnings)
            {
                info.Warnings.Add(warning);
            }

            result.Add(info);
        }

        private void AddPolyLine(
            Document document,
            ElementId sourceId,
            PolyLine polyLine,
            GeometryObject rawObject,
            Transform transform,
            IList<GeometryObjectInfo> result,
            ref int index)
        {
            int coordinateCount = 0;
            try
            {
                coordinateCount = polyLine.GetCoordinates().Count;
            }
            catch
            {
                coordinateCount = 0;
            }

            result.Add(new GeometryObjectInfo
            {
                SourceImportInstanceId = sourceId,
                GeometryIndex = ++index,
                GeometryType = "PolyLine",
                Transform = transform,
                PolyLine = polyLine,
                RawObject = rawObject,
                BoundingBox = GeometryUtils.GetPolyLineBoundingBox(polyLine, transform),
                FaceCount = 0,
                EdgeCount = Math.Max(0, coordinateCount - 1),
                LayerName = _layerService.GetLayerName(document, rawObject)
            });
        }

        private void AddUnknown(
            Document document,
            ElementId sourceId,
            GeometryObject rawObject,
            Transform transform,
            IList<GeometryObjectInfo> result,
            ref int index)
        {
            result.Add(new GeometryObjectInfo
            {
                SourceImportInstanceId = sourceId,
                GeometryIndex = ++index,
                GeometryType = rawObject == null ? "Unknown" : rawObject.GetType().Name,
                Transform = transform,
                RawObject = rawObject,
                LayerName = rawObject == null ? "Unknown" : _layerService.GetLayerName(document, rawObject)
            });
        }
    }
}
