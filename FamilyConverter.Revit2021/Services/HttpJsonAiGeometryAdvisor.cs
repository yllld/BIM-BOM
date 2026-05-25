using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FamilyConverter.Revit2021.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FamilyConverter.Revit2021.Services
{
    public class HttpJsonAiGeometryAdvisor : IAiGeometryAdvisor
    {
        private const string SystemPrompt =
            "Ты советник по классификации CAD/BIM геометрии. Не генерируй точные координаты, размеры, профили, Revit API код или команды построения. Верни только JSON с выбором метода преобразования: extrusion, freeform или skip. Точная геометрия будет построена локальным C# кодом через Revit API.";

        private readonly AiConfig _config;

        public HttpJsonAiGeometryAdvisor(AiConfig config)
        {
            _config = config;
        }

        public async Task<AiGeometryResponse> AnalyzeAsync(AiGeometryRequest request, CancellationToken cancellationToken)
        {
            if (_config == null)
            {
                throw new InvalidOperationException("AI-конфиг не загружен.");
            }

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(_config.TimeoutSec <= 0 ? 60 : _config.TimeoutSec);
                ApplyHeaders(client);

                string payload = BuildPayload(request);
                using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
                {
                    HttpResponseMessage response = await client.PostAsync(_config.BaseUrl, content, cancellationToken).ConfigureAwait(false);
                    string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    AiGeometryResponse parsed = ParseResponse(responseText);
                    parsed.provider = _config.Provider;
                    parsed.model = _config.Model;
                    return parsed;
                }
            }
        }

        private void ApplyHeaders(HttpClient client)
        {
            if (_config.CustomHeaders != null)
            {
                foreach (var pair in _config.CustomHeaders)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        client.DefaultRequestHeaders.TryAddWithoutValidation(pair.Key, pair.Value);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(_config.ApiKey) && !string.IsNullOrWhiteSpace(_config.AuthHeaderName))
            {
                string value = string.IsNullOrWhiteSpace(_config.AuthScheme)
                    ? _config.ApiKey
                    : _config.AuthScheme + " " + _config.ApiKey;
                client.DefaultRequestHeaders.TryAddWithoutValidation(_config.AuthHeaderName, value);
            }
        }

        private string BuildPayload(AiGeometryRequest request)
        {
            if (string.Equals(_config.Provider, "openai-compatible-chat-completions", StringComparison.OrdinalIgnoreCase))
            {
                var systemMessage = new JObject();
                systemMessage["role"] = "system";
                systemMessage["content"] = SystemPrompt;

                var userMessage = new JObject();
                userMessage["role"] = "user";
                userMessage["content"] = JsonConvert.SerializeObject(request, Formatting.None);

                var messages = new JArray();
                messages.Add(systemMessage);
                messages.Add(userMessage);

                var payload = new JObject();
                payload["model"] = _config.Model;
                payload["temperature"] = _config.Temperature;
                payload["messages"] = messages;
                return payload.ToString(Formatting.None);
            }

            var generic = new JObject();
            generic["system_prompt"] = SystemPrompt;
            generic["geometry_passport"] = JObject.FromObject(request);
            return generic.ToString(Formatting.None);
        }

        private AiGeometryResponse ParseResponse(string responseText)
        {
            JObject root = JObject.Parse(responseText);
            JToken contentToken = root.SelectToken("choices[0].message.content");
            string json = contentToken == null ? responseText : contentToken.ToString();
            json = StripCodeFence(json);
            AiGeometryResponse parsed = JsonConvert.DeserializeObject<AiGeometryResponse>(json);
            if (parsed == null)
            {
                throw new InvalidOperationException("AI вернул пустой JSON.");
            }

            return parsed;
        }

        private static string StripCodeFence(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            string trimmed = text.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewLine = trimmed.IndexOf('\n');
                int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewLine >= 0 && lastFence > firstNewLine)
                {
                    return trimmed.Substring(firstNewLine + 1, lastFence - firstNewLine - 1).Trim();
                }
            }

            return trimmed;
        }
    }
}
