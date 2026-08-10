using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.Models;

namespace FamilyConverter.Revit2021.Services
{
    public class FreeFormCreationService
    {
        private readonly SubcategoryService _subcategoryService;

        public FreeFormCreationService(SubcategoryService subcategoryService)
        {
            _subcategoryService = subcategoryService;
        }

        public Element Create(Document document, GeometryObjectInfo source, ConversionOptions options)
        {
            if (document == null || !document.IsFamilyDocument)
            {
                throw new System.InvalidOperationException("FreeFormElement можно создавать только внутри документа семейства.");
            }

            if (source == null || source.Solid == null)
            {
                throw new System.InvalidOperationException("Solid для FreeFormElement не найден.");
            }

            return Create(document, source.Solid, source, options);
        }

        public Element Create(
            Document document,
            Solid solid,
            GeometryObjectInfo source,
            ConversionOptions options)
        {
            if (document == null || !document.IsFamilyDocument)
            {
                throw new System.InvalidOperationException("FreeFormElement можно создавать только внутри документа семейства.");
            }

            if (solid == null || solid.Faces.Size == 0)
            {
                throw new System.InvalidOperationException("Solid для FreeFormElement не найден или не содержит граней.");
            }

            FreeFormElement element = FreeFormElement.Create(document, solid);
            Category subcategory = _subcategoryService.GetOrCreate(document, source.LayerName, options.CreateSubcategoriesByLayer);
            _subcategoryService.AssignSubcategory(element, subcategory);
            return element;
        }
    }
}
