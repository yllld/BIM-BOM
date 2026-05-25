using System;
using System.IO;
using FamilyConverter.Revit2021.Models;
using Newtonsoft.Json;

namespace FamilyConverter.Revit2021.Services
{
    public class AiConfigService
    {
        public bool TryLoad(string path, out AiConfig config, out string message)
        {
            config = null;
            message = null;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                message = "AI-конфиг не найден. Плагин продолжит работу в локальном режиме.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                config = JsonConvert.DeserializeObject<AiConfig>(json);
                Normalize(config);

                if (config == null)
                {
                    message = "AI-конфиг пустой или поврежден.";
                    return false;
                }

                if (!config.Enabled)
                {
                    message = "AI-конфиг найден, но enabled=false. Плагин продолжит работу локально.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(config.Provider) || string.IsNullOrWhiteSpace(config.BaseUrl))
                {
                    message = "AI-конфиг должен содержать provider и baseUrl.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                message = "AI-конфиг не удалось прочитать: " + ex.Message;
                return false;
            }
        }

        public bool ValidateConfigFile(string path, out string message)
        {
            AiConfig config;
            return TryLoad(path, out config, out message);
        }

        private static void Normalize(AiConfig config)
        {
            if (config == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(config.Provider))
            {
                config.Provider = "generic-json-post";
            }
            if (string.IsNullOrWhiteSpace(config.AuthHeaderName))
            {
                config.AuthHeaderName = "Authorization";
            }
            if (string.IsNullOrWhiteSpace(config.AuthScheme))
            {
                config.AuthScheme = "Bearer";
            }
            if (config.TimeoutSec <= 0)
            {
                config.TimeoutSec = 60;
            }
            if (string.IsNullOrWhiteSpace(config.SendMode))
            {
                config.SendMode = "geometry_passport_only";
            }
        }
    }
}
