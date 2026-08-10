using System;
using FamilyConverter.Revit2021.DrawingToFamily.Models;
using FamilyConverter.Revit2021.DrawingToFamily.Services;

namespace FamilyConverter.Revit2021.DrawingToFamily.UI
{
    public class PreviewContourItem
    {
        public PreviewContourItem(RecognizedContour contour)
        {
            Contour = contour;
            AutoType = contour == null ? ContourType.Unknown : contour.Type;
            Role = AutoType;
            IsIncluded = true;
            Signature = ContourSignatureService.Create(contour);
        }

        public RecognizedContour Contour { get; private set; }
        public string Signature { get; private set; }
        public ContourType AutoType { get; private set; }
        public ContourType Role { get; set; }
        public bool IsIncluded { get; set; }
        public bool IsSelected { get; set; }
        public bool IsManual
        {
            get { return Role != AutoType || !IsIncluded; }
        }

        public Guid Id
        {
            get { return Contour == null ? Guid.Empty : Contour.Id; }
        }

        public string ShortId
        {
            get { return Id == Guid.Empty ? "-" : Id.ToString("N").Substring(0, 8); }
        }

        public string LayerName
        {
            get { return Contour == null ? "-" : Contour.SourceLayer ?? "-"; }
        }

        public ProjectionType Projection
        {
            get { return Contour == null ? ProjectionType.Unknown : Contour.SourceProjection; }
        }

        public string Status
        {
            get
            {
                if (!IsIncluded)
                {
                    return "Disabled";
                }

                return Role.ToString();
            }
        }
    }
}
