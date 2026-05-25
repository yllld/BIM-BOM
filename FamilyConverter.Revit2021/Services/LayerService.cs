using System;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.Services
{
    public class LayerService
    {
        public string GetLayerName(Document document, GeometryObject geometryObject)
        {
            if (document == null || geometryObject == null || geometryObject.GraphicsStyleId == ElementId.InvalidElementId)
            {
                return "Unknown";
            }

            try
            {
                GraphicsStyle style = document.GetElement(geometryObject.GraphicsStyleId) as GraphicsStyle;
                if (style != null && style.GraphicsStyleCategory != null && !string.IsNullOrWhiteSpace(style.GraphicsStyleCategory.Name))
                {
                    return style.GraphicsStyleCategory.Name;
                }
            }
            catch
            {
                return "Unknown";
            }

            return "Unknown";
        }

        public string SanitizeForSubcategory(string layerName)
        {
            string source = string.IsNullOrWhiteSpace(layerName) || string.Equals(layerName, "Unknown", StringComparison.OrdinalIgnoreCase)
                ? "Converted"
                : layerName;

            var builder = new StringBuilder();
            foreach (char c in source)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ')
                {
                    builder.Append(c);
                }
                else
                {
                    builder.Append('_');
                }
            }

            string clean = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(clean))
            {
                clean = "Converted";
            }

            string prefixed = clean.StartsWith("DWG_", StringComparison.OrdinalIgnoreCase) ? clean : "DWG_" + clean;
            return prefixed.Length <= 80 ? prefixed : prefixed.Substring(0, 80);
        }

        public string JoinTopLayers(System.Collections.Generic.IEnumerable<string> layers, int count)
        {
            var top = layers
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(x => x)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key)
                .Take(count)
                .Select(x => x.Key)
                .ToList();

            return top.Count == 0 ? "Unknown" : string.Join(", ", top);
        }
    }
}
