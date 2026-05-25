using Newtonsoft.Json;

namespace FamilyConverter.Revit2021.Utils
{
    public static class JsonUtils
    {
        public static string ToIndentedJson(object value)
        {
            return JsonConvert.SerializeObject(value, Formatting.Indented);
        }

        public static T FromJson<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
