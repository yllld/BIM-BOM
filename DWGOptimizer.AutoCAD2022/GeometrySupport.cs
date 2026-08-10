using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.AutoCAD2022
{
    internal static class GeometrySupport
    {
        public static bool IsAnnotation(Entity entity)
        {
            return entity is DBText
                || entity is MText
                || entity is Dimension
                || entity is Leader
                || entity is MLeader
                || entity is Table
                || entity is Hatch;
        }

        public static bool IsExternalGraphic(Entity entity)
        {
            return entity is RasterImage || entity is UnderlayReference;
        }

        public static bool IsUseful3d(Entity entity)
        {
            return entity is Solid3d
                || entity is Autodesk.AutoCAD.DatabaseServices.Surface
                || entity is SubDMesh
                || entity is Body
                || entity is Region
                || entity is BlockReference;
        }

        public static bool IsFinite(Point3d point)
        {
            return IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
        }

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public static BoundsInfo ToBounds(Extents3d extents, Matrix3d transform)
        {
            Point3d min = extents.MinPoint;
            Point3d max = extents.MaxPoint;
            var points = new[]
            {
                new Point3d(min.X, min.Y, min.Z), new Point3d(max.X, min.Y, min.Z),
                new Point3d(min.X, max.Y, min.Z), new Point3d(max.X, max.Y, min.Z),
                new Point3d(min.X, min.Y, max.Z), new Point3d(max.X, min.Y, max.Z),
                new Point3d(min.X, max.Y, max.Z), new Point3d(max.X, max.Y, max.Z)
            };

            var bounds = new BoundsAccumulator();
            foreach (Point3d point in points)
            {
                bounds.Add(point.TransformBy(transform));
            }

            return bounds.ToInfo();
        }

        public static double Diagonal(BoundsInfo bounds)
        {
            if (bounds == null || !bounds.IsValid)
            {
                return 0;
            }

            double x = bounds.MaxX - bounds.MinX;
            double y = bounds.MaxY - bounds.MinY;
            double z = bounds.MaxZ - bounds.MinZ;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        public static void Merge(BoundsInfo target, BoundsInfo source)
        {
            if (target == null || source == null || !source.IsValid)
            {
                return;
            }

            if (!target.IsValid)
            {
                target.MinX = source.MinX;
                target.MinY = source.MinY;
                target.MinZ = source.MinZ;
                target.MaxX = source.MaxX;
                target.MaxY = source.MaxY;
                target.MaxZ = source.MaxZ;
                target.IsValid = true;
            }
            else
            {
                target.MinX = Math.Min(target.MinX, source.MinX);
                target.MinY = Math.Min(target.MinY, source.MinY);
                target.MinZ = Math.Min(target.MinZ, source.MinZ);
                target.MaxX = Math.Max(target.MaxX, source.MaxX);
                target.MaxY = Math.Max(target.MaxY, source.MaxY);
                target.MaxZ = Math.Max(target.MaxZ, source.MaxZ);
            }

            double cx = (target.MinX + target.MaxX) * 0.5;
            double cy = (target.MinY + target.MaxY) * 0.5;
            double cz = (target.MinZ + target.MaxZ) * 0.5;
            target.DistanceFromOrigin = Math.Sqrt(cx * cx + cy * cy + cz * cz);
        }

        public static double MillimetersToDrawingUnits(double millimeters, UnitsValue units)
        {
            switch (units)
            {
                case UnitsValue.Inches: return millimeters / 25.4;
                case UnitsValue.Feet: return millimeters / 304.8;
                case UnitsValue.Miles: return millimeters / 1609344.0;
                case UnitsValue.Millimeters: return millimeters;
                case UnitsValue.Centimeters: return millimeters / 10.0;
                case UnitsValue.Meters: return millimeters / 1000.0;
                case UnitsValue.Kilometers: return millimeters / 1000000.0;
                case UnitsValue.Mils: return millimeters / 0.0254;
                case UnitsValue.Yards: return millimeters / 914.4;
                default: return millimeters;
            }
        }

        private sealed class BoundsAccumulator
        {
            private bool _hasValue;
            private double _minX;
            private double _minY;
            private double _minZ;
            private double _maxX;
            private double _maxY;
            private double _maxZ;

            public void Add(Point3d point)
            {
                if (!IsFinite(point))
                {
                    return;
                }

                if (!_hasValue)
                {
                    _minX = _maxX = point.X;
                    _minY = _maxY = point.Y;
                    _minZ = _maxZ = point.Z;
                    _hasValue = true;
                    return;
                }

                _minX = Math.Min(_minX, point.X);
                _minY = Math.Min(_minY, point.Y);
                _minZ = Math.Min(_minZ, point.Z);
                _maxX = Math.Max(_maxX, point.X);
                _maxY = Math.Max(_maxY, point.Y);
                _maxZ = Math.Max(_maxZ, point.Z);
            }

            public BoundsInfo ToInfo()
            {
                var result = new BoundsInfo { IsValid = _hasValue };
                if (!_hasValue)
                {
                    return result;
                }

                result.MinX = _minX;
                result.MinY = _minY;
                result.MinZ = _minZ;
                result.MaxX = _maxX;
                result.MaxY = _maxY;
                result.MaxZ = _maxZ;
                double cx = (_minX + _maxX) * 0.5;
                double cy = (_minY + _maxY) * 0.5;
                double cz = (_minZ + _maxZ) * 0.5;
                result.DistanceFromOrigin = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                return result;
            }
        }
    }
}
