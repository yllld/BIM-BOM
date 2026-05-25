using System.Collections.Generic;

namespace FamilyConverter.Revit2021.Models
{
    public class AiGeometryResponse
    {
        public AiGeometryResponse()
        {
            risk_flags = new List<string>();
        }

        public string classification { get; set; }
        public string recommended_method { get; set; }
        public double confidence { get; set; }
        public string reason { get; set; }
        public IList<string> risk_flags { get; set; }
        public string fallback_method { get; set; }
        public string provider { get; set; }
        public string model { get; set; }
    }
}
