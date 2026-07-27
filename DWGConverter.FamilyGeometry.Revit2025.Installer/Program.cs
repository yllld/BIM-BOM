using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace DWGConverter.FamilyGeometry.Revit2025.Installer
{
    internal static class Program
    {
        private const string ProductName = "DWG Converter - Family Geometry";
        private const string DllFileName = "FamilyConverter.Revit2025.dll";
        private const string AddinFileName = "FamilyConverter.addin";
        private const string RevitVersion = "2025";

        [STAThread]
        private static int Main(string[] args)
        {
            bool quiet = args.Any(x => string.Equals(x, "/quiet", StringComparison.OrdinalIgnoreCase));

            try
            {
                string addinsDirectory = GetAddinsDirectory(args);
                string dllPath = Path.Combine(addinsDirectory, DllFileName);
                string addinPath = Path.Combine(addinsDirectory, AddinFileName);

                if (!quiet && !ConfirmInstall(addinsDirectory))
                {
                    return 1;
                }

                Directory.CreateDirectory(addinsDirectory);
                WriteEmbeddedDll(dllPath);
                File.WriteAllText(addinPath, BuildAddinManifest(dllPath));

                if (!quiet)
                {
                    MessageBox.Show(
                        ProductName + " installed for Revit " + RevitVersion + "." + Environment.NewLine + Environment.NewLine +
                        "Restart Revit to load the add-in.",
                        ProductName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (!quiet)
                {
                    MessageBox.Show(
                        "Installation failed:" + Environment.NewLine + ex.Message,
                        ProductName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                return 2;
            }
        }

        private static bool ConfirmInstall(string addinsDirectory)
        {
            DialogResult result = MessageBox.Show(
                "Install " + ProductName + " for Revit " + RevitVersion + "?" + Environment.NewLine + Environment.NewLine +
                "Target folder:" + Environment.NewLine + addinsDirectory,
                ProductName,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question);

            return result == DialogResult.OK;
        }

        private static string GetAddinsDirectory(string[] args)
        {
            string targetArgument = args.FirstOrDefault(
                x => x.StartsWith("/target=", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(targetArgument))
            {
                string customTarget = targetArgument.Substring("/target=".Length).Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(customTarget))
                {
                    throw new ArgumentException("Custom installation target is empty.");
                }

                return Path.GetFullPath(customTarget);
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Autodesk", "Revit", "Addins", RevitVersion);
        }

        private static void WriteEmbeddedDll(string dllPath)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resourceName = assembly
                .GetManifestResourceNames()
                .FirstOrDefault(x => x.EndsWith("." + DllFileName, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(resourceName))
            {
                throw new InvalidOperationException("Embedded Revit 2025 add-in DLL was not found.");
            }

            using (Stream source = assembly.GetManifestResourceStream(resourceName))
            {
                if (source == null)
                {
                    throw new InvalidOperationException("Embedded add-in DLL could not be opened.");
                }

                using (FileStream target = File.Create(dllPath))
                {
                    source.CopyTo(target);
                }
            }
        }

        private static string BuildAddinManifest(string dllPath)
        {
            string escapedDllPath = EscapeXml(dllPath);
            return
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine +
                "<RevitAddIns>" + Environment.NewLine +
                "  <AddIn Type=\"Application\">" + Environment.NewLine +
                "    <Name>DWG Converter - Family Geometry</Name>" + Environment.NewLine +
                "    <Assembly>" + escapedDllPath + "</Assembly>" + Environment.NewLine +
                "    <AddInId>80B7093F-8B7F-4FC0-B3D2-977FB234B6C4</AddInId>" + Environment.NewLine +
                "    <FullClassName>FamilyConverter.Revit2025.App</FullClassName>" + Environment.NewLine +
                "    <VendorId>DWGC</VendorId>" + Environment.NewLine +
                "    <VendorDescription>DWG Converter</VendorDescription>" + Environment.NewLine +
                "  </AddIn>" + Environment.NewLine +
                "</RevitAddIns>" + Environment.NewLine;
        }

        private static string EscapeXml(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}
