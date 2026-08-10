using System;
using System.IO;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.AutoCAD2022
{
    internal static class OutputPathService
    {
        public static string GetOutputPath(string sourcePath, OptimizationProfile profile)
        {
            string sourceDirectory = !string.IsNullOrWhiteSpace(sourcePath) && Path.IsPathRooted(sourcePath)
                ? Path.GetDirectoryName(sourcePath)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string outputDirectory = Path.Combine(sourceDirectory, "RevitReady");
            Directory.CreateDirectory(outputDirectory);

            string baseName = string.IsNullOrWhiteSpace(sourcePath)
                ? "UnsavedDrawing"
                : Path.GetFileNameWithoutExtension(sourcePath);
            string stem = baseName + "_RevitReady_" + profile;
            string candidate = Path.Combine(outputDirectory, stem + ".dwg");
            int suffix = 2;
            while (File.Exists(candidate)
                || File.Exists(Path.ChangeExtension(candidate, ".revitprep.json"))
                || File.Exists(Path.ChangeExtension(candidate, ".html")))
            {
                candidate = Path.Combine(outputDirectory, stem + "_" + suffix + ".dwg");
                suffix++;
            }

            return candidate;
        }

        public static string GetReportsDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string directory = Path.Combine(appData, "DWG_Optimizer", "Reports");
            Directory.CreateDirectory(directory);
            return directory;
        }

        public static string GetQueuesDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string directory = Path.Combine(appData, "DWG_Optimizer", "Queues");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
