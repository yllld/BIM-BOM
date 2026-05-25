using System.Collections.Generic;
using Newtonsoft.Json;

namespace FamilyConverter.Revit2021.Models
{
    public class AiGeometryRequest
    {
        public AiGeometryRequest()
        {
            face_summary = new Dictionary<string, object>();
            edge_summary = new Dictionary<string, object>();
            candidate_methods = new List<string>();
            warnings = new List<string>();
        }

        public string object_id { get; set; }
        public string layer_name { get; set; }
        public string bbox_mm { get; set; }
        public double volume_mm3 { get; set; }
        public Dictionary<string, object> face_summary { get; private set; }
        public Dictionary<string, object> edge_summary { get; private set; }
        public string local_classification { get; set; }
        public double local_confidence { get; set; }
        public IList<string> candidate_methods { get; private set; }
        public IList<string> warnings { get; private set; }

        [JsonIgnore]
        public GeometryObjectInfo Source { get; set; }
    }
}
