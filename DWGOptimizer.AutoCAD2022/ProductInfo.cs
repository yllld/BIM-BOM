namespace DWGOptimizer.AutoCAD2022
{
    internal static class ProductInfo
    {
        public const string Name = "DWG Revit Optimizer";
        public const string Version = "0.1.2";
#if AUTOCAD2021
        public const string AutoCadYear = "2021";
        public const string CoreConsolePath = @"C:\Program Files\Autodesk\AutoCAD 2021\accoreconsole.exe";
        public const string CoreWorkerAssembly = "DWGOptimizer.CoreConsole2021.dll";
        public const string UiAssemblyName = "DWGOptimizer.AutoCAD2021";
#else
        public const string AutoCadYear = "2022";
        public const string CoreConsolePath = @"C:\Program Files\Autodesk\AutoCAD 2022\accoreconsole.exe";
        public const string CoreWorkerAssembly = "DWGOptimizer.CoreConsole2022.dll";
        public const string UiAssemblyName = "DWGOptimizer.AutoCAD2022";
#endif
        public const string CommandAnalyze = "DWGREVITREADY";
        public const string CommandBatch = "DWGREVITREADYBATCH";
        public const string CommandReports = "DWGREVITREADYREPORTS";
    }
}
