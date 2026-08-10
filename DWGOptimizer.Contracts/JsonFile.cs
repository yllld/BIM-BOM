using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DWGOptimizer.Contracts
{
    public static class JsonFile
    {
        public static void Write<T>(string path, T value)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true
            });

            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                string json = Encoding.UTF8.GetString(stream.ToArray());
                File.WriteAllText(path, PrettyPrint(json), new UTF8Encoding(false));
            }
        }

        public static T Read<T>(string path)
        {
            var serializer = new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings
            {
                UseSimpleDictionaryFormat = true
            });

            using (FileStream stream = File.OpenRead(path))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        private static string PrettyPrint(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return json;
            }

            var result = new StringBuilder();
            bool quoted = false;
            bool escaped = false;
            int indent = 0;
            foreach (char character in json)
            {
                if (character == '"' && !escaped)
                {
                    quoted = !quoted;
                }

                if (!quoted && (character == '{' || character == '['))
                {
                    result.Append(character).AppendLine();
                    indent++;
                    result.Append(new string(' ', indent * 2));
                }
                else if (!quoted && (character == '}' || character == ']'))
                {
                    result.AppendLine();
                    indent = indent > 0 ? indent - 1 : 0;
                    result.Append(new string(' ', indent * 2)).Append(character);
                }
                else if (!quoted && character == ',')
                {
                    result.Append(character).AppendLine().Append(new string(' ', indent * 2));
                }
                else if (!quoted && character == ':')
                {
                    result.Append(": ");
                }
                else
                {
                    result.Append(character);
                }

                escaped = character == '\\' && !escaped;
                if (character != '\\')
                {
                    escaped = false;
                }
            }

            return result.ToString();
        }
    }
}
