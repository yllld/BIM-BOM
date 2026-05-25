using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.Services;

namespace FamilyConverter.Revit2021.Utils
{
    public static class GeometryUtils
    {
        public static bool IsIdentity(Transform transform)
        {
            if (transform == null)
            {
                return true;
            }

            Transform identity = Transform.Identity;
            return transform.Origin.DistanceTo(identity.Origin) < 1e-9
                && transform.BasisX.DistanceTo(identity.BasisX) < 1e-9
                && transform.BasisY.DistanceTo(identity.BasisY) < 1e-9
                && transform.BasisZ.DistanceTo(identity.BasisZ) < 1e-9;
        }

        public static Solid TryCreateTransformedSolid(Solid solid, Transform transform, IList<string> warnings)
        {
            if (solid == null)
            {
                return null;
            }

            if (IsIdentity(transform))
            {
                return solid;
            }

            try
            {
                return SolidUtils.CreateTransformed(solid, transform);
            }
            catch (Exception ex)
            {
                if (warnings != null)
                {
                    warnings.Add("Не удалось применить transform к Solid: " + ex.Message);
                }

                return solid;
            }
        }

        public static Curve TryCreateTransformedCurve(Curve curve, Transform transform, IList<string> warnings)
        {
            if (curve == null)
            {
                return null;
            }

            if (IsIdentity(transform))
            {
                return curve;
            }

            try
            {
                return curve.CreateTransformed(transform);
            }
            catch (Exception ex)
            {
                if (warnings != null)
                {
                    warnings.Add("Не удалось применить transform к Curve: " + ex.Message);
                }

                return curve;
            }
        }

        public static BoundingBoxXYZ GetSolidBoundingBox(Solid solid)
        {
            if (solid == null)
            {
                return null;
            }

            try
            {
                BoundingBoxXYZ bbox = solid.GetBoundingBox();
                return TransformBoundingBox(bbox, bbox == null ? Transform.Identity : bbox.Transform);
            }
            catch
            {
                return null;
            }
        }

        public static BoundingBoxXYZ GetMeshBoundingBox(Mesh mesh, Transform transform)
        {
            if (mesh == null)
            {
                return null;
            }

            BoundingBoxAccumulator accumulator = new BoundingBoxAccumulator();
            try
            {
                for (int i = 0; i < mesh.NumTriangles; i++)
                {
                    MeshTriangle triangle = mesh.get_Triangle(i);
                    for (int j = 0; j < 3; j++)
                    {
                        XYZ point = triangle.get_Vertex(j);
                        accumulator.Add(transform == null ? point : transform.OfPoint(point));
                    }
                }
            }
            catch
            {
                return null;
            }

            return accumulator.ToBoundingBox();
        }

        public static BoundingBoxXYZ GetCurveBoundingBox(Curve curve)
        {
            if (curve == null)
            {
                return null;
            }

            BoundingBoxAccumulator accumulator = new BoundingBoxAccumulator();
            try
            {
                IList<XYZ> points = curve.Tessellate();
                foreach (XYZ point in points)
                {
                    accumulator.Add(point);
                }
            }
            catch
            {
                try
                {
                    accumulator.Add(curve.GetEndPoint(0));
                    accumulator.Add(curve.GetEndPoint(1));
                }
                catch
                {
                    return null;
                }
            }

            return accumulator.ToBoundingBox();
        }

        public static BoundingBoxXYZ TransformBoundingBox(BoundingBoxXYZ source, Transform transform)
        {
            if (source == null)
            {
                return null;
            }

            BoundingBoxAccumulator accumulator = new BoundingBoxAccumulator();
            XYZ min = source.Min;
            XYZ max = source.Max;
            XYZ[] corners =
            {
                new XYZ(min.X, min.Y, min.Z),
                new XYZ(max.X, min.Y, min.Z),
                new XYZ(min.X, max.Y, min.Z),
                new XYZ(min.X, min.Y, max.Z),
                new XYZ(max.X, max.Y, min.Z),
                new XYZ(max.X, min.Y, max.Z),
                new XYZ(min.X, max.Y, max.Z),
                new XYZ(max.X, max.Y, max.Z)
            };

            foreach (XYZ corner in corners)
            {
                accumulator.Add(transform == null ? corner : transform.OfPoint(corner));
            }

            return accumulator.ToBoundingBox();
        }

        public static string BoundingBoxToMmString(BoundingBoxXYZ bbox, UnitService units)
        {
            if (bbox == null)
            {
                return string.Empty;
            }

            return string.Format(
                "min({0:0.###},{1:0.###},{2:0.###}); max({3:0.###},{4:0.###},{5:0.###})",
                units.FeetToMm(bbox.Min.X),
                units.FeetToMm(bbox.Min.Y),
                units.FeetToMm(bbox.Min.Z),
                units.FeetToMm(bbox.Max.X),
                units.FeetToMm(bbox.Max.Y),
                units.FeetToMm(bbox.Max.Z));
        }

        public static double GetElementSolidVolume(Element element)
        {
            if (element == null)
            {
                return 0;
            }

            double volume = 0;
            try
            {
                var options = new Options
                {
                    ComputeReferences = false,
                    IncludeNonVisibleObjects = false,
                    DetailLevel = ViewDetailLevel.Fine
                };
                GeometryElement geometry = element.get_Geometry(options);
                CollectVolume(geometry, ref volume);
            }
            catch
            {
                return 0;
            }

            return volume;
        }

        public static double BoundingBoxDeviationMm(BoundingBoxXYZ source, BoundingBoxXYZ created, UnitService units)
        {
            if (source == null || created == null)
            {
                return 0;
            }

            source = TransformBoundingBox(source, source.Transform);
            created = TransformBoundingBox(created, created.Transform);

            double deviationFeet = 0;
            deviationFeet = Math.Max(deviationFeet, Math.Abs(source.Min.X - created.Min.X));
            deviationFeet = Math.Max(deviationFeet, Math.Abs(source.Min.Y - created.Min.Y));
            deviationFeet = Math.Max(deviationFeet, Math.Abs(source.Min.Z - created.Min.Z));
            deviationFeet = Math.Max(deviationFeet, Math.Abs(source.Max.X - created.Max.X));
            deviationFeet = Math.Max(deviationFeet, Math.Abs(source.Max.Y - created.Max.Y));
            deviationFeet = Math.Max(deviationFeet, Math.Abs(source.Max.Z - created.Max.Z));
            return units.FeetToMm(deviationFeet);
        }

        public static double VolumeDeviationPercent(double sourceVolumeFeet3, double createdVolumeFeet3)
        {
            if (sourceVolumeFeet3 <= 1e-12 || createdVolumeFeet3 <= 1e-12)
            {
                return 0;
            }

            return Math.Abs(sourceVolumeFeet3 - createdVolumeFeet3) / sourceVolumeFeet3 * 100.0;
        }

        private static void CollectVolume(GeometryElement geometry, ref double volume)
        {
            if (geometry == null)
            {
                return;
            }

            foreach (GeometryObject obj in geometry)
            {
                Solid solid = obj as Solid;
                if (solid != null && solid.Volume > 0)
                {
                    volume += solid.Volume;
                    continue;
                }

                GeometryInstance instance = obj as GeometryInstance;
                if (instance != null)
                {
                    CollectVolume(instance.GetInstanceGeometry(), ref volume);
                }
            }
        }

        private class BoundingBoxAccumulator
        {
            private bool _hasPoint;
            private double _minX;
            private double _minY;
            private double _minZ;
            private double _maxX;
            private double _maxY;
            private double _maxZ;

            public void Add(XYZ point)
            {
                if (point == null)
                {
                    return;
                }

                if (!_hasPoint)
                {
                    _minX = _maxX = point.X;
                    _minY = _maxY = point.Y;
                    _minZ = _maxZ = point.Z;
                    _hasPoint = true;
                    return;
                }

                _minX = Math.Min(_minX, point.X);
                _minY = Math.Min(_minY, point.Y);
                _minZ = Math.Min(_minZ, point.Z);
                _maxX = Math.Max(_maxX, point.X);
                _maxY = Math.Max(_maxY, point.Y);
                _maxZ = Math.Max(_maxZ, point.Z);
            }

            public BoundingBoxXYZ ToBoundingBox()
            {
                if (!_hasPoint)
                {
                    return null;
                }

                return new BoundingBoxXYZ
                {
                    Min = new XYZ(_minX, _minY, _minZ),
                    Max = new XYZ(_maxX, _maxY, _maxZ)
                };
            }
        }
    }
}
