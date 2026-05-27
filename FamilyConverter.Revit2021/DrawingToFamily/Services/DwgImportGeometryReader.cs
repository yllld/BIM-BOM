using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class DwgImportGeometryReader
    {
        public IList<DwgCurveEntity> Read(Document document, ImportInstance importInstance, double smallObjectMm)
        {
            var result = new List<DwgCurveEntity>();
            if (document == null || importInstance == null)
            {
                return result;
            }

            GeometryElement geometry = importInstance.get_Geometry(new Options
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            });

            Traverse(document, geometry, Transform.Identity, smallObjectMm, result);
            return result;
        }

        private void Traverse(
            Document document,
            GeometryElement geometry,
            Transform transform,
            double smallObjectMm,
            IList<DwgCurveEntity> result)
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
                    Transform next = transform == null ? instance.Transform : transform.Multiply(instance.Transform);
                    try
                    {
                        Traverse(document, instance.GetSymbolGeometry(), next, smallObjectMm, result);
                    }
                    catch
                    {
                        try
                        {
                            Traverse(document, instance.GetInstanceGeometry(), next, smallObjectMm, result);
                        }
                        catch
                        {
                            // Revit can expose nested CAD geometry that is not readable in family context.
                        }
                    }

                    continue;
                }

                PolyLine polyLine = obj as PolyLine;
                if (polyLine != null)
                {
                    DwgCurveEntity entity = CreateFromPolyLine(document, polyLine, obj, transform, smallObjectMm);
                    if (entity != null)
                    {
                        result.Add(entity);
                    }
                    continue;
                }

                Curve curve = obj as Curve;
                if (curve != null)
                {
                    DwgCurveEntity entity = CreateFromCurve(document, curve, obj, transform, smallObjectMm);
                    if (entity != null)
                    {
                        result.Add(entity);
                    }
                }
            }
        }

        private DwgCurveEntity CreateFromCurve(Document document, Curve curve, GeometryObject raw, Transform transform, double smallObjectMm)
        {
            var warnings = new List<string>();
            Curve transformed = curve;
            try
            {
                if (transform != null)
                {
                    transformed = curve.CreateTransformed(transform);
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Curve transform failed: " + ex.Message);
            }

            IList<XYZ> points = SafeTessellate(transformed, warnings);
            if (points.Count == 0)
            {
                return null;
            }

            double lengthMm = SafeLengthMm(transformed, points);
            var entity = CreateBaseEntity(document, raw, transform, GetEntityTypeName(transformed), points, lengthMm, smallObjectMm);
            entity.Curve = transformed;
            entity.IsClosedCandidate = IsClosed(points, UnitUtilsExtensions.MmToFeet(2.0));
            foreach (string warning in warnings)
            {
                entity.Warnings.Add(warning);
            }

            if (entity.Warnings.Count > 0)
            {
                entity.Warning = string.Join("; ", entity.Warnings);
            }

            return entity;
        }

        private DwgCurveEntity CreateFromPolyLine(Document document, PolyLine polyLine, GeometryObject raw, Transform transform, double smallObjectMm)
        {
            IList<XYZ> coordinates;
            try
            {
                coordinates = polyLine.GetCoordinates();
            }
            catch
            {
                return null;
            }

            var points = new List<XYZ>();
            foreach (XYZ coordinate in coordinates)
            {
                points.Add(transform == null ? coordinate : transform.OfPoint(coordinate));
            }

            if (points.Count == 0)
            {
                return null;
            }

            double lengthFeet = 0;
            for (int i = 1; i < points.Count; i++)
            {
                lengthFeet += points[i - 1].DistanceTo(points[i]);
            }

            var entity = CreateBaseEntity(document, raw, transform, "PolyLine", points, UnitUtilsExtensions.FeetToMm(lengthFeet), smallObjectMm);
            entity.IsClosedCandidate = IsClosed(points, UnitUtilsExtensions.MmToFeet(2.0));
            return entity;
        }

        private DwgCurveEntity CreateBaseEntity(
            Document document,
            GeometryObject raw,
            Transform transform,
            string entityType,
            IList<XYZ> points,
            double lengthMm,
            double smallObjectMm)
        {
            Color layerColor;
            string colorHex = GetStyleColor(document, raw, out layerColor);
            var entity = new DwgCurveEntity
            {
                EntityType = entityType,
                TotalTransform = transform,
                LengthMm = lengthMm,
                GraphicsStyleId = raw == null ? ElementId.InvalidElementId : raw.GraphicsStyleId,
                LayerName = GetLayerName(document, raw),
                LayerColor = layerColor,
                LayerColorHex = colorHex,
                IsSmallObject = lengthMm < smallObjectMm
            };

            BoundingBoxXYZ box = null;
            foreach (XYZ point in points)
            {
                XYZ flat = GeometryToleranceUtils.Flatten(point);
                entity.Points.Add(flat);
                entity.PointsMm.Add(new XYZ(
                    UnitUtilsExtensions.FeetToMm(flat.X),
                    UnitUtilsExtensions.FeetToMm(flat.Y),
                    UnitUtilsExtensions.FeetToMm(flat.Z)));
                box = BoundingBoxUtils.AddPoint(box, flat);
            }

            entity.BoundingBox = box;
            ApplyOrientationFlags(entity);
            return entity;
        }

        private static string GetEntityTypeName(Curve curve)
        {
            if (curve == null)
            {
                return "Curve";
            }

            if (curve is Line)
            {
                return "Line";
            }
            if (curve is Arc)
            {
                return "Arc";
            }
            if (curve is Ellipse)
            {
                return "Ellipse";
            }
            if (curve is NurbSpline)
            {
                return "NurbSpline";
            }

            return curve.GetType().Name;
        }

        private static IList<XYZ> SafeTessellate(Curve curve, IList<string> warnings)
        {
            try
            {
                return curve.Tessellate();
            }
            catch (Exception ex)
            {
                if (warnings != null)
                {
                    warnings.Add("Curve tessellation failed: " + ex.Message);
                }

                var points = new List<XYZ>();
                try
                {
                    points.Add(curve.GetEndPoint(0));
                    points.Add(curve.GetEndPoint(1));
                }
                catch
                {
                    // No reliable points are available for this curve.
                }

                return points;
            }
        }

        private static double SafeLengthMm(Curve curve, IList<XYZ> points)
        {
            try
            {
                return UnitUtilsExtensions.FeetToMm(curve.Length);
            }
            catch
            {
                double lengthFeet = 0;
                for (int i = 1; i < points.Count; i++)
                {
                    lengthFeet += points[i - 1].DistanceTo(points[i]);
                }

                return UnitUtilsExtensions.FeetToMm(lengthFeet);
            }
        }

        private static bool IsClosed(IList<XYZ> points, double toleranceFeet)
        {
            return points != null
                && points.Count > 2
                && points[0].DistanceTo(points[points.Count - 1]) <= toleranceFeet;
        }

        private static void ApplyOrientationFlags(DwgCurveEntity entity)
        {
            if (entity == null || entity.Points.Count < 2)
            {
                return;
            }

            double toleranceFeet = UnitUtilsExtensions.MmToFeet(1.0);
            XYZ first = entity.Points[0];
            XYZ last = entity.Points[entity.Points.Count - 1];
            entity.IsHorizontal = GeometryToleranceUtils.IsHorizontal(first, last, toleranceFeet);
            entity.IsVertical = GeometryToleranceUtils.IsVertical(first, last, toleranceFeet);
        }

        private static string GetLayerName(Document document, GeometryObject raw)
        {
            if (document == null || raw == null || raw.GraphicsStyleId == ElementId.InvalidElementId)
            {
                return "Unknown";
            }

            try
            {
                GraphicsStyle style = document.GetElement(raw.GraphicsStyleId) as GraphicsStyle;
                if (style != null && style.GraphicsStyleCategory != null)
                {
                    return style.GraphicsStyleCategory.Name;
                }
            }
            catch
            {
                return "Unknown";
            }

            return "Unknown";
        }

        private static string GetStyleColor(Document document, GeometryObject raw, out Color color)
        {
            color = null;
            if (document == null || raw == null || raw.GraphicsStyleId == ElementId.InvalidElementId)
            {
                return "-";
            }

            try
            {
                GraphicsStyle style = document.GetElement(raw.GraphicsStyleId) as GraphicsStyle;
                if (style != null && style.GraphicsStyleCategory != null)
                {
                    color = style.GraphicsStyleCategory.LineColor;
                    return color == null ? "-" : string.Format("#{0:X2}{1:X2}{2:X2}", color.Red, color.Green, color.Blue);
                }
            }
            catch
            {
                return "-";
            }

            return "-";
        }
    }
}
