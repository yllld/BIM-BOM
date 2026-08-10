using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class DwgLayerAnalyzer
    {
        public IList<DwgLayerInfo> Analyze(IList<DwgCurveEntity> entities)
        {
            var result = new List<DwgLayerInfo>();
            if (entities == null)
            {
                return result;
            }

            foreach (IGrouping<string, DwgCurveEntity> group in entities.GroupBy(x => string.IsNullOrWhiteSpace(x.LayerName) ? "Unknown" : x.LayerName))
            {
                int count = group.Count();
                double total = group.Sum(x => x.LengthMm);
                int shortCount = group.Count(x => x.IsSmallObject);
                int orthoCount = group.Count(x => x.IsHorizontal || x.IsVertical);
                BoundingBoxXYZ box = null;
                string colorHex = "-";
                Color color = null;
                foreach (DwgCurveEntity entity in group)
                {
                    box = BoundingBoxUtils.Union(box, entity.BoundingBox);
                    if ((string.IsNullOrWhiteSpace(colorHex) || colorHex == "-")
                        && !string.IsNullOrWhiteSpace(entity.LayerColorHex)
                        && entity.LayerColorHex != "-")
                    {
                        colorHex = entity.LayerColorHex;
                        color = entity.LayerColor;
                    }
                }

                RecognitionRole suggested = GuessRole(group.Key);
                result.Add(new DwgLayerInfo
                {
                    LayerName = group.Key,
                    ObjectCount = count,
                    TotalLengthMm = total,
                    AverageLengthMm = count == 0 ? 0 : total / count,
                    BoundingBox = box,
                    BoundingBoxText = BoundingBoxUtils.ToMmString(box),
                    ShortLinePercent = count == 0 ? 0 : shortCount * 100.0 / count,
                    OrthogonalPercent = count == 0 ? 0 : orthoCount * 100.0 / count,
                    SuggestedRole = LayerRoleOption.FromRole(suggested),
                    UserRole = LayerRoleOption.FromRole(suggested),
                    IsIncluded = suggested == RecognitionRole.MainGeometry,
                    LayerColor = color,
                    LayerColorHex = colorHex
                });
            }

            return result.OrderByDescending(x => x.ObjectCount).ThenBy(x => x.LayerName).ToList();
        }

        private static RecognitionRole GuessRole(string layerName)
        {
            string name = (layerName ?? string.Empty).ToUpperInvariant();
            if (ContainsAny(name, "DIM", "РАЗМЕР", "РАЗМЕРЫ", "ANNOTATION", "ANNOTATIONS", "TEXT", "MTEXT"))
            {
                return RecognitionRole.DimensionLine;
            }

            if (ContainsAny(name, "AXIS", "ОСЬ", "ОСИ", "CENTER", "CENTRE"))
            {
                return RecognitionRole.Axis;
            }

            if (ContainsAny(name, "HATCH", "ШТРИХ", "ШТРИХОВКА"))
            {
                return RecognitionRole.HatchOrAuxiliary;
            }

            if (ContainsAny(name, "HIDDEN", "СКРЫТ", "HIDE"))
            {
                return RecognitionRole.HiddenLine;
            }

            return RecognitionRole.MainGeometry;
        }

        private static bool ContainsAny(string source, params string[] patterns)
        {
            foreach (string pattern in patterns)
            {
                if (source.Contains(pattern))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
