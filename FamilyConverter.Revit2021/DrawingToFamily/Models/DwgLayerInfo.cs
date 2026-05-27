using Autodesk.Revit.DB;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public class DwgLayerInfo
    {
        public string LayerName { get; set; }
        public int ObjectCount { get; set; }
        public double TotalLengthMm { get; set; }
        public double AverageLengthMm { get; set; }
        public BoundingBoxXYZ BoundingBox { get; set; }
        public string BoundingBoxText { get; set; }
        public double ShortLinePercent { get; set; }
        public double OrthogonalPercent { get; set; }
        public string SuggestedRole { get; set; }
        public string UserRole { get; set; }
        public bool IsIncluded { get; set; }
        public Color LayerColor { get; set; }
        public string LayerColorHex { get; set; }

        public string StyleColor
        {
            get { return LayerColorHex; }
            set { LayerColorHex = value; }
        }

        public string ColorLabel
        {
            get { return string.IsNullOrWhiteSpace(LayerColorHex) || LayerColorHex == "-" ? "нет данных" : LayerColorHex; }
        }

        public RecognitionRole EffectiveRole
        {
            get { return IsIncluded ? LayerRoleOption.ToRole(UserRole) : RecognitionRole.Ignored; }
        }
    }
}
