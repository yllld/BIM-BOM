using System;
using System.IO;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.Services
{
    public class CadImportService
    {
        public ImportInstance ImportAtOrigin(Document document, View view, string filePath)
        {
            if (document == null)
            {
                throw new ArgumentNullException("document");
            }

            if (view == null)
            {
                throw new ArgumentNullException("view");
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException("Файл САПР не найден.", filePath);
            }

            ElementId importedElementId;
            using (var transaction = new Transaction(document, ProductInfo.Name + ": импорт файла САПР"))
            {
                transaction.Start();
                try
                {
                    importedElementId = Import(document, view, filePath);
                    document.Regenerate();
                    transaction.Commit();
                }
                catch
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        transaction.RollBack();
                    }

                    throw;
                }
            }

            var importInstance = document.GetElement(importedElementId) as ImportInstance;
            if (importInstance == null)
            {
                throw new InvalidOperationException("Revit импортировал файл, но ImportInstance не найден.");
            }

            return importInstance;
        }

        private static ElementId Import(Document document, View view, string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".dwg" || extension == ".dxf")
            {
                var options = new DWGImportOptions
                {
                    Placement = ImportPlacement.Origin,
                    OrientToView = false,
                    ThisViewOnly = false,
                    VisibleLayersOnly = false
                };

                ElementId importedElementId;
                bool imported = document.Import(filePath, options, view, out importedElementId);
                if (!imported || importedElementId == ElementId.InvalidElementId)
                {
                    throw new InvalidOperationException("Revit не смог импортировать выбранный DWG/DXF.");
                }

                return importedElementId;
            }

            if (extension == ".sat")
            {
                var options = new SATImportOptions
                {
                    Placement = ImportPlacement.Origin,
                    OrientToView = false,
                    ThisViewOnly = false
                };

                ElementId importedElementId = document.Import(filePath, options, view);
                if (importedElementId == ElementId.InvalidElementId)
                {
                    throw new InvalidOperationException("Revit не смог импортировать выбранный SAT.");
                }

                return importedElementId;
            }

            throw new NotSupportedException("MVP поддерживает только DWG, DXF и SAT.");
        }
    }
}
