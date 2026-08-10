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
        public int mesh_source_triangle_count { get; set; }
        public int mesh_created_triangle_count { get; set; }
        public int mesh_skipped_triangle_count { get; set; }
        public int mesh_source_vertex_count { get; set; }
        public int mesh_source_normal_count { get; set; }
        public int mesh_output_normal_count { get; set; }
        public string mesh_source_normal_distribution { get; set; }
        public string mesh_output_normal_distribution { get; set; }
        public string mesh_creation_path { get; set; }
        public string mesh_fallback_reason { get; set; }
        public string mesh_freeform_failure_reason { get; set; }
        public string mesh_direct_mesh_failure_reason { get; set; }
        public int mesh_solid_face_count { get; set; }
        public int mesh_freeform_planar_face_count { get; set; }
        public int mesh_freeform_reference_face_count { get; set; }
        public int mesh_boundary_edge_count { get; set; }
        public int mesh_boundary_loop_count { get; set; }
        public int mesh_non_manifold_edge_count { get; set; }
        public int mesh_orientation_flip_count { get; set; }
        public int mesh_orientation_conflict_count { get; set; }
        public int mesh_planar_cap_count { get; set; }
        public int mesh_non_planar_boundary_loop_count { get; set; }
        public int mesh_open_boundary_chain_count { get; set; }
        public bool mesh_topology_repair_applied { get; set; }
        public string mesh_topology_repair_failure_reason { get; set; }
    }
}
