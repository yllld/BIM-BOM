using System;
using System.IO;
using FamilyConverter.Revit2021;

namespace FamilyConverter.Revit2021.Utils
{
    public static class FileNameUtils
    {
        public static string GetReportDirectory(Autodesk.Revit.DB.Document document)
        {
            try
            {
                if (document != null && !string.IsNullOrWhiteSpace(document.PathName))
                {
                    string familyDirectory = Path.GetDirectoryName(document.PathName);
                    string reportDirectory = Path.Combine(familyDirectory, "DWG_Conversion_Reports");
                    Directory.CreateDirectory(reportDirectory);
                    return reportDirectory;
                }
            }
            catch
            {
                // Fall through to temp directory.
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), ProductInfo.AppDataRootFolder + "_" + ProductInfo.AppDataProductFolder);
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        public static string Timestamp()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }
    }
}
