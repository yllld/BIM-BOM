using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using DWGOptimizer.AutoCAD2022;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.Tests
{
    internal static class Program
    {
        private static int _failed;

        private static int Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAutoCad;
            Run("units", TestUnits);
            Run("bounds", TestBounds);
            Run("readiness", TestReadiness);
            Run("xref transforms", TestXrefTransforms);
            Run("xref paths", TestXrefPaths);
            Run("json", TestJson);
            Run("hash open drawing", TestHashOpenDrawing);
            Run("unique output names", TestUniqueNames);
            Console.WriteLine(_failed == 0 ? "All unit tests passed." : _failed + " unit test(s) failed.");
            return _failed == 0 ? 0 : 1;
        }

        private static Assembly ResolveAutoCad(object sender, ResolveEventArgs args)
        {
            string file = Path.Combine(@"C:\Program Files\Autodesk\AutoCAD 2022", new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        }

        private static void TestUnits()
        {
            Equal(1.0, GeometrySupport.MillimetersToDrawingUnits(1, UnitsValue.Millimeters), 1e-9);
            Equal(0.001, GeometrySupport.MillimetersToDrawingUnits(1, UnitsValue.Meters), 1e-9);
            Equal(1.0 / 25.4, GeometrySupport.MillimetersToDrawingUnits(1, UnitsValue.Inches), 1e-9);
        }

        private static void TestBounds()
        {
            var target = new BoundsInfo();
            GeometrySupport.Merge(target, new BoundsInfo { IsValid = true, MinX = -2, MinY = 1, MinZ = 3, MaxX = 5, MaxY = 7, MaxZ = 11 });
            if (!target.IsValid || target.MinX != -2 || target.MaxZ != 11) throw new Exception("Bounds merge failed.");
        }

        private static void TestReadiness()
        {
            var report = new AnalysisReport();
            report.Findings.Add(new Finding { Severity = FindingSeverity.Blocker });
            report.Findings.Add(new Finding { Severity = FindingSeverity.Warning });
            report.Counts.MeshFaces = 2000000;
            ReadinessScorer.Score(report);
            if (report.ReadinessScore >= 100 || report.ReadinessScore < 0) throw new Exception("Invalid readiness score.");
            if (report.RecommendedProfile != OptimizationProfile.Aggressive) throw new Exception("Heavy mesh profile rule failed.");
        }

        private static void TestXrefTransforms()
        {
            var extents = new Extents3d(new Point3d(0, 0, 0), new Point3d(2, 4, 6));
            Matrix3d transform = Matrix3d.Displacement(new Vector3d(10, -5, 3));
            BoundsInfo result = GeometrySupport.ToBounds(extents, transform);
            Equal(10, result.MinX, 1e-9);
            Equal(-5, result.MinY, 1e-9);
            Equal(9, result.MaxZ, 1e-9);
        }

        private static void TestXrefPaths()
        {
            string root = Path.Combine(Path.GetTempPath(), "xref-root");
            string source = Path.Combine(root, "model.dwg");
            if (!DwgAnalyzer.IsSamePath("model.dwg", source)) throw new Exception("Relative circular path was not recognized.");
            if (DwgAnalyzer.IsSamePath("other.dwg", source)) throw new Exception("Different XREF paths matched.");
        }

        private static void TestJson()
        {
            string path = Path.Combine(Path.GetTempPath(), "dwgoptimizer-json-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var source = new OptimizationReport { Profile = OptimizationProfile.Balanced, Success = true };
                JsonFile.Write(path, source);
                OptimizationReport value = JsonFile.Read<OptimizationReport>(path);
                if (value.SchemaVersion != 1 || value.Profile != OptimizationProfile.Balanced || !value.Success) throw new Exception("JSON roundtrip failed.");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static void TestUniqueNames()
        {
            string root = Path.Combine(Path.GetTempPath(), "dwgoptimizer-path-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "model.dwg");
                File.WriteAllBytes(source, new byte[] { 1 });
                string first = OutputPathService.GetOutputPath(source, OptimizationProfile.Safe);
                File.WriteAllBytes(first, new byte[] { 1 });
                string second = OutputPathService.GetOutputPath(source, OptimizationProfile.Safe);
                if (first == second || !second.EndsWith("_2.dwg", StringComparison.OrdinalIgnoreCase)) throw new Exception("Collision suffix failed.");
            }
            finally { Directory.Delete(root, true); }
        }

        private static void TestHashOpenDrawing()
        {
            string path = Path.Combine(Path.GetTempPath(), "dwgoptimizer-lock-" + Guid.NewGuid().ToString("N") + ".dwg");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
            try
            {
                using (var activeDrawing = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    string hash = DwgAnalyzer.ComputeSha256(path);
                    if (hash.Length != 64) throw new Exception("SHA-256 was not calculated.");
                }
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static void Run(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { _failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }

        private static void Equal(double expected, double actual, double tolerance)
        {
            if (Math.Abs(expected - actual) > tolerance) throw new Exception("Expected " + expected + ", got " + actual + ".");
        }
    }
}
