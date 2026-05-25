using System;
using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using FamilyConverter.Revit2021.Utils;

namespace FamilyConverter.Revit2021
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class OpenReportsFolderCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIDocument uidoc = commandData.Application.ActiveUIDocument;
                Document document = uidoc == null ? null : uidoc.Document;
                string reportDirectory = FileNameUtils.GetReportDirectory(document);

                Process.Start("explorer.exe", "\"" + reportDirectory + "\"");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show(ProductInfo.Name, "Не удалось открыть папку отчетов:\n" + ex.Message);
                return Result.Failed;
            }
        }
    }
}
