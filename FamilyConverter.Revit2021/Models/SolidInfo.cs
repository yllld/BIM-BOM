namespace FamilyConverter.Revit2021.Models
{
    public class SolidInfo
    {
        public int FaceCount { get; set; }
        public int EdgeCount { get; set; }
        public double VolumeMm3 { get; set; }
        public int PlanarFaceCount { get; set; }
        public int CurvedFaceCount { get; set; }
    }
}
