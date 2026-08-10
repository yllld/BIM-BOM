using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Utils;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class ProjectionDetector
    {
        public IList<DrawingProjectionRegion> Detect(IList<DwgCurveEntity> entities)
        {
            var main = (entities ?? new List<DwgCurveEntity>())
                .Where(x => x.RecognitionRole == RecognitionRole.MainGeometry && !x.IsIgnored && x.BoundingBox != null)
                .ToList();

            var result = new List<DrawingProjectionRegion>();
            if (main.Count == 0)
            {
                return result;
            }

            BoundingBoxXYZ global = null;
            foreach (DwgCurveEntity entity in main)
            {
                global = BoundingBoxUtils.Union(global, entity.BoundingBox);
            }

            double globalSizeMm = Math.Max(BoundingBoxUtils.WidthMm(global), BoundingBoxUtils.HeightMm(global));
            double gapFeet = UnitUtilsExtensions.MmToFeet(Math.Max(100.0, globalSizeMm * 0.04));
            var clusters = new List<List<DwgCurveEntity>>();

            foreach (DwgCurveEntity entity in main.OrderBy(x => BoundingBoxUtils.Center(x.BoundingBox).X))
            {
                List<DwgCurveEntity> target = null;
                foreach (List<DwgCurveEntity> cluster in clusters)
                {
                    BoundingBoxXYZ clusterBox = BuildBox(cluster);
                    if (BoundingBoxUtils.IntersectsExpanded(clusterBox, entity.BoundingBox, gapFeet))
                    {
                        target = cluster;
                        break;
                    }
                }

                if (target == null)
                {
                    target = new List<DwgCurveEntity>();
                    clusters.Add(target);
                }

                target.Add(entity);
            }

            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < clusters.Count && !merged; i++)
                {
                    for (int j = i + 1; j < clusters.Count; j++)
                    {
                        if (BoundingBoxUtils.IntersectsExpanded(BuildBox(clusters[i]), BuildBox(clusters[j]), gapFeet))
                        {
                            clusters[i].AddRange(clusters[j]);
                            clusters.RemoveAt(j);
                            merged = true;
                            break;
                        }
                    }
                }
            }
            while (merged);

            foreach (List<DwgCurveEntity> cluster in clusters)
            {
                BoundingBoxXYZ box = BuildBox(cluster);
                var region = new DrawingProjectionRegion
                {
                    BoundingBox = box,
                    WidthMm = BoundingBoxUtils.WidthMm(box),
                    HeightMm = BoundingBoxUtils.HeightMm(box),
                    EntityCount = cluster.Count
                };

                foreach (DwgCurveEntity entity in cluster)
                {
                    region.Entities.Add(entity);
                }

                result.Add(region);
            }

            return result
                .OrderByDescending(x => Math.Max(1, x.WidthMm) * Math.Max(1, x.HeightMm))
                .ThenByDescending(x => x.EntityCount)
                .Take(6)
                .ToList();
        }

        private static BoundingBoxXYZ BuildBox(IEnumerable<DwgCurveEntity> entities)
        {
            BoundingBoxXYZ box = null;
            foreach (DwgCurveEntity entity in entities)
            {
                box = BoundingBoxUtils.Union(box, entity.BoundingBox);
            }

            return box;
        }
    }
}
