using System.Collections.Generic;
using Newtonsoft.Json;

namespace FamilyConverter.Revit2021.Models
{
    public class AiConfig
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        [JsonProperty("provider")]
        public string Provider { get; set; }

        [JsonProperty("baseUrl")]
        public string BaseUrl { get; set; }

        [JsonProperty("apiKey")]
        public string ApiKey { get; set; }

        [JsonProperty("authHeaderName")]
        public string AuthHeaderName { get; set; }

        [JsonProperty("authScheme")]
        public string AuthScheme { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("timeoutSec")]
        public int TimeoutSec { get; set; }

        [JsonProperty("temperature")]
        public double Temperature { get; set; }

        [JsonProperty("customHeaders")]
        public Dictionary<string, string> CustomHeaders { get; set; }

        [JsonProperty("sendMode")]
        public string SendMode { get; set; }
    }
}
