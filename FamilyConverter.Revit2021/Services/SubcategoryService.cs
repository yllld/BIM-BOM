using System;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.Services
{
    public class SubcategoryService
    {
        private readonly LayerService _layerService;

        public SubcategoryService()
        {
            _layerService = new LayerService();
        }

        public Category GetOrCreate(Document document, string layerName, bool createByLayer)
        {
            if (document == null || !document.IsFamilyDocument || !createByLayer)
            {
                return null;
            }

            try
            {
                Category parent = document.OwnerFamily == null ? null : document.OwnerFamily.FamilyCategory;
                if (parent == null)
                {
                    return null;
                }

                string name = _layerService.SanitizeForSubcategory(layerName);
                foreach (Category subcategory in parent.SubCategories)
                {
                    if (string.Equals(subcategory.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return subcategory;
                    }
                }

                return document.Settings.Categories.NewSubcategory(parent, name);
            }
            catch
            {
                return null;
            }
        }

        public void AssignSubcategory(Element element, Category subcategory)
        {
            if (element == null || subcategory == null)
            {
                return;
            }

            try
            {
                GenericForm form = element as GenericForm;
                if (form != null)
                {
                    form.Subcategory = subcategory;
                }
            }
            catch
            {
                // Some family categories/forms do not accept subcategory assignment.
            }
        }
    }
}
