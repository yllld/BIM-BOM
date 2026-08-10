namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class ContourManualOverride
    {
        public string Signature { get; set; }
        public ProjectionType Projection { get; set; }
        public string LayerName { get; set; }
        public ContourType AutoType { get; set; }
        public ContourType OverrideType { get; set; }
        public bool IsIncluded { get; set; }
        public string Reason { get; set; }
    }
}
