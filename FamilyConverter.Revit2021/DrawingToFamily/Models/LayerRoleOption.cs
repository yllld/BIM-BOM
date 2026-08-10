using System.Collections.Generic;

namespace FamilyConverter.Revit2021.DrawingToFamily.Models
{
    public static class LayerRoleOption
    {
        public const string BuildGeometry = "Строить геометрию";
        public const string Annotation = "Аннотации / размеры, не строить";
        public const string AxisAuxiliary = "Оси / вспомогательные";
        public const string Hidden = "Скрытые линии";
        public const string Ignore = "Игнорировать";

        public static IList<string> All()
        {
            return new List<string>
            {
                BuildGeometry,
                Annotation,
                AxisAuxiliary,
                Hidden,
                Ignore
            };
        }

        public static RecognitionRole ToRole(string role)
        {
            if (role == BuildGeometry)
            {
                return RecognitionRole.MainGeometry;
            }
            if (role == Annotation)
            {
                return RecognitionRole.DimensionLine;
            }
            if (role == AxisAuxiliary)
            {
                return RecognitionRole.Axis;
            }
            if (role == Hidden)
            {
                return RecognitionRole.HiddenLine;
            }
            if (role == Ignore)
            {
                return RecognitionRole.Ignored;
            }

            return RecognitionRole.Unknown;
        }

        public static string FromRole(RecognitionRole role)
        {
            switch (role)
            {
                case RecognitionRole.MainGeometry:
                    return BuildGeometry;
                case RecognitionRole.DimensionLine:
                case RecognitionRole.TextOrAnnotation:
                    return Annotation;
                case RecognitionRole.Axis:
                case RecognitionRole.HatchOrAuxiliary:
                case RecognitionRole.ProjectionFrame:
                    return AxisAuxiliary;
                case RecognitionRole.HiddenLine:
                    return Hidden;
                case RecognitionRole.Ignored:
                    return Ignore;
                default:
                    return BuildGeometry;
            }
        }
    }
}
