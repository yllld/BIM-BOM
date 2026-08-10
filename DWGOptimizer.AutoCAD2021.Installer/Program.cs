using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace DWGOptimizer.AutoCAD2021.Installer
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool quiet = args.Any(x => string.Equals(x, "/quiet", StringComparison.OrdinalIgnoreCase));
            bool uninstall = args.Any(x => string.Equals(x, "/uninstall", StringComparison.OrdinalIgnoreCase));
            string bundle = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk", "ApplicationPlugins", "DWGOptimizer2021.bundle");
            try
            {
                if (uninstall)
                {
                    if (Directory.Exists(bundle)) Directory.Delete(bundle, true);
                    if (!quiet) MessageBox.Show("DWG Revit Optimizer для AutoCAD 2021 удалён.", "BIM BOM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                string acad = @"C:\Program Files\Autodesk\AutoCAD 2021\acad.exe";
                if (!File.Exists(acad)) throw new InvalidOperationException("AutoCAD 2021 не найден. Установщик предназначен только для версии 2021.");
                string contents = Path.Combine(bundle, "Contents", "2021-v0.1.2");
                Directory.CreateDirectory(contents);
                Extract("Payload.DWGOptimizer.AutoCAD2021.dll", Path.Combine(contents, "DWGOptimizer.AutoCAD2021.dll"));
                Extract("Payload.DWGOptimizer.Contracts.dll", Path.Combine(contents, "DWGOptimizer.Contracts.dll"));
                Extract("Payload.DWGOptimizer.Engine.AutoCAD2021.dll", Path.Combine(contents, "DWGOptimizer.Engine.AutoCAD2021.dll"));
                Extract("Payload.DWGOptimizer.CoreConsole2021.dll", Path.Combine(contents, "DWGOptimizer.CoreConsole2021.dll"));
                Extract("Payload.DWGOptimizer.BatchRunner.exe", Path.Combine(contents, "DWGOptimizer.BatchRunner.exe"));
                File.WriteAllText(Path.Combine(bundle, "PackageContents.xml"), PackageXml(), new UTF8Encoding(false));
                if (!quiet) MessageBox.Show("DWG Revit Optimizer установлен для AutoCAD 2021.\nПерезапустите AutoCAD.", "BIM BOM", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet) MessageBox.Show("Установка не выполнена:\n" + ex.Message + "\n\nЗакройте AutoCAD и повторите.", "BIM BOM", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void Extract(string resourceName, string destination)
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (source == null) throw new InvalidOperationException("Повреждён установщик: " + resourceName);
                using (FileStream target = File.Create(destination)) source.CopyTo(target);
            }
        }

        private static string PackageXml()
        {
            return @"<?xml version=""1.0"" encoding=""utf-8""?>
<ApplicationPackage SchemaVersion=""1.0"" AutodeskProduct=""AutoCAD"" ProductType=""Application"" Name=""DWG Revit Optimizer 2021"" AppVersion=""0.1.2"" ProductCode=""{DAA1D02A-D7BC-4E77-94DC-B97099F838B7}"" UpgradeCode=""{726D06BD-E68E-493E-A81B-015C401519AF}"" Author=""BIM BOM"">
  <CompanyDetails Name=""BIM BOM"" />
  <RuntimeRequirements OS=""Win64"" Platform=""AutoCAD*"" SeriesMin=""R24.0"" SeriesMax=""R24.0"" />
  <Components>
    <RuntimeRequirements OS=""Win64"" Platform=""AutoCAD*"" SeriesMin=""R24.0"" SeriesMax=""R24.0"" />
    <ComponentEntry AppName=""DWG Revit Optimizer 2021"" ModuleName=""./Contents/2021-v0.1.2/DWGOptimizer.AutoCAD2021.dll"" AppDescription=""Подготовка 3D DWG для Revit"" LoadOnAutoCADStartup=""True"">
      <Commands GroupName=""DWGREVITOPTIMIZER2021"">
        <Command Global=""DWGREVITREADY"" Local=""DWGREVITREADY"" />
        <Command Global=""DWGREVITREADYBATCH"" Local=""DWGREVITREADYBATCH"" />
        <Command Global=""DWGREVITREADYREPORTS"" Local=""DWGREVITREADYREPORTS"" />
      </Commands>
    </ComponentEntry>
  </Components>
</ApplicationPackage>";
        }
    }
}
