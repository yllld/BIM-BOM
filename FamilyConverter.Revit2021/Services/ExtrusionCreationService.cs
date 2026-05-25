using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.Models;
using FamilyConverter.Revit2021.Utils;

namespace FamilyConverter.Revit2021.Services
{
    public class ExtrusionCreationService
    {
        private readonly SubcategoryService _subcategoryService;

        public ExtrusionCreationService(SubcategoryService subcategoryService)
        {
            _subcategoryService = subcategoryService;
        }

        public Element Create(Document document, GeometryObjectInfo source, PrismaticCandidate candidate, ConversionOptions options, bool outerOnly)
        {
            if (document == null || !document.IsFamilyDocument)
            {
                throw new System.InvalidOperationException("Extrusion можно создавать только внутри документа семейства.");
            }

            if (candidate == null || candidate.SketchPlane == null || candidate.ProfileLoops.Count == 0)
            {
                throw new System.InvalidOperationException("Не найден безопасный профиль для Extrusion.");
            }

            SketchPlane sketchPlane = SketchPlane.Create(document, candidate.SketchPlane);
            CurveArrArray profile = CurveLoopUtils.ToCurveArrArray(candidate.ProfileLoops, outerOnly);
            Extrusion extrusion = document.FamilyCreate.NewExtrusion(true, profile, sketchPlane, candidate.DepthFeet);

            Category subcategory = _subcategoryService.GetOrCreate(document, source.LayerName, options.CreateSubcategoriesByLayer);
            _subcategoryService.AssignSubcategory(extrusion, subcategory);
            return extrusion;
        }
    }
}
