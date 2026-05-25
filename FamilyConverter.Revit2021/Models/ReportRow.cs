namespace FamilyConverter.Revit2021.Models
{
    public class ReportRow
    {
        public string object_id { get; set; }
        public string layer { get; set; }
        public string source_geometry_type { get; set; }
        public string bbox_mm { get; set; }
        public double volume_mm3 { get; set; }
        public int face_count { get; set; }
        public int edge_count { get; set; }
        public string local_classification { get; set; }
        public double local_confidence { get; set; }
        public bool ai_used { get; set; }
        public string ai_provider { get; set; }
        public string ai_model { get; set; }
        public string ai_recommendation { get; set; }
        public double ai_confidence { get; set; }
        public string final_method { get; set; }
        public string result_status { get; set; }
        public string created_element_id { get; set; }
        public string message { get; set; }
        public string exception { get; set; }
        public bool fallback_used { get; set; }
        public string extrusion_failed_reason { get; set; }
        public double validation_bbox_deviation_mm { get; set; }
        public double validation_volume_deviation_percent { get; set; }
    }
}
