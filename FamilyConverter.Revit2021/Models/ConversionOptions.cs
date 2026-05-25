using System;
using System.IO;
using FamilyConverter.Revit2021;

namespace FamilyConverter.Revit2021.Models
{
    public class ConversionOptions
    {
        public bool CreateNativeExtrusions { get; set; }
        public bool TryExtrusionBeforeFreeForm { get; set; }
        public bool UseFreeFormFallback { get; set; }
        public bool SuperTurboMode { get; set; }
        public bool CollectUnsupportedGeometry { get; set; }
        public bool ReadLayerNames { get; set; }
        public bool ValidateCreatedGeometry { get; set; }
        public bool DeleteSourceDwgOnSuccess { get; set; }
        public bool CreateSubcategoriesByLayer { get; set; }
        public bool CreateJsonReport { get; set; }
        public bool CreateCsvReport { get; set; }
        public bool UseAiAdvisor { get; set; }
        public string AiConfigPath { get; set; }
        public double MinSolidVolumeMm3 { get; set; }
        public double MinSolidMaxDimensionMm { get; set; }
        public double BoundingBoxToleranceMm { get; set; }
        public double VolumeTolerancePercent { get; set; }
        public double LoopClosureToleranceMm { get; set; }
        public double MinExtrusionConfidence { get; set; }

        public ConversionOptions Clone()
        {
            return (ConversionOptions)MemberwiseClone();
        }

        public static ConversionOptions CreateDefaults()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new ConversionOptions
            {
                CreateNativeExtrusions = true,
                TryExtrusionBeforeFreeForm = true,
                UseFreeFormFallback = true,
                SuperTurboMode = false,
                CollectUnsupportedGeometry = true,
                ReadLayerNames = true,
                ValidateCreatedGeometry = true,
                DeleteSourceDwgOnSuccess = false,
                CreateSubcategoriesByLayer = true,
                CreateJsonReport = true,
                CreateCsvReport = true,
                UseAiAdvisor = false,
                AiConfigPath = Path.Combine(appData, ProductInfo.AppDataRootFolder, ProductInfo.AppDataProductFolder, "ai_config.json"),
                MinSolidVolumeMm3 = 1.0,
                MinSolidMaxDimensionMm = 0.0,
                BoundingBoxToleranceMm = 2.0,
                VolumeTolerancePercent = 2.0,
                LoopClosureToleranceMm = 0.5,
                MinExtrusionConfidence = 0.85
            };
        }

        public static ConversionOptions CreateSuperTurboDefaults()
        {
            ConversionOptions options = CreateDefaults();
            options.CreateNativeExtrusions = false;
            options.TryExtrusionBeforeFreeForm = false;
            options.UseFreeFormFallback = true;
            options.SuperTurboMode = true;
            options.CollectUnsupportedGeometry = false;
            options.ReadLayerNames = false;
            options.ValidateCreatedGeometry = false;
            options.DeleteSourceDwgOnSuccess = false;
            options.CreateSubcategoriesByLayer = false;
            options.CreateJsonReport = true;
            options.CreateCsvReport = true;
            options.UseAiAdvisor = false;
            options.MinSolidVolumeMm3 = 50000.0;
            options.MinSolidMaxDimensionMm = 25.0;
            options.BoundingBoxToleranceMm = 50.0;
            options.VolumeTolerancePercent = 25.0;
            options.LoopClosureToleranceMm = 5.0;
            options.MinExtrusionConfidence = 1.0;
            return options;
        }
    }
}
