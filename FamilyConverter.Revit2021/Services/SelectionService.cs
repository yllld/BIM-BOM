using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace FamilyConverter.Revit2021.Services
{
    public class SelectionService
    {
        public bool TryGetSingleImportInstance(UIDocument uidoc, out ImportInstance importInstance, out string message)
        {
            importInstance = null;
            message = null;

            if (uidoc == null || uidoc.Document == null)
            {
                message = "Активный документ Revit не найден.";
                return false;
            }

            Document doc = uidoc.Document;
            if (!doc.IsFamilyDocument)
            {
                message = "Команда работает только внутри редактора семейств Revit.";
                return false;
            }

            ICollection<ElementId> selectedIds = uidoc.Selection.GetElementIds();
            if (selectedIds == null || selectedIds.Count == 0)
            {
                message = "Сначала выберите импортированный DWG в семействе.";
                return false;
            }

            if (selectedIds.Count > 1)
            {
                message = "Выберите только один импортированный DWG-элемент.";
                return false;
            }

            Element element = doc.GetElement(selectedIds.First());
            importInstance = element as ImportInstance;
            if (importInstance == null)
            {
                message = "Выберите один импортированный DWG-элемент в семействе. DWG должен быть заранее импортирован и расположен пользователем.";
                return false;
            }

            return true;
        }
    }
}
