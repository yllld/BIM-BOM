using System;
using System.IO;

namespace FamilyConverter.Revit2021.Models
{
    public class ConversionOptions
    {
        public bool CreateNativeExtrusions { get; set; }
        public bool TryExtrusionBeforeFreeForm { get; set; }
        public bool UseFreeFormFallback { get; set; }
        public bool DeleteSourceDwgOnSuccess { get; set; }
        public bool CreateSubcategoriesByLayer { get; set; }
        public bool CreateJsonReport { get; set; }
        public bool CreateCsvReport { get; set; }
        public bool UseAiAdvisor { get; set; }
        public string AiConfigPath { get; set; }
        public double MinSolidVolumeMm3 { get; set; }
        public double BoundingBoxToleranceMm { get; set; }
        public double VolumeTolerancePercent { get; set; }
        public double LoopClosureToleranceMm { get; set; }
        public double MinExtrusionConfidence { get; set; }

        public static ConversionOptions CreateDefaults()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new ConversionOptions
            {
                CreateNativeExtrusions = true,
                TryExtrusionBeforeFreeForm = true,
                UseFreeFormFallback = true,
                DeleteSourceDwgOnSuccess = false,
                CreateSubcategoriesByLayer = true,
                CreateJsonReport = true,
                CreateCsvReport = true,
                UseAiAdvisor = false,
                AiConfigPath = Path.Combine(appData, "ENECA_MEP", "FamilyConverter", "ai_config.json"),
                MinSolidVolumeMm3 = 1.0,
                BoundingBoxToleranceMm = 2.0,
                VolumeTolerancePercent = 2.0,
                LoopClosureToleranceMm = 0.5,
                MinExtrusionConfidence = 0.85
            };
        }
    }
}
