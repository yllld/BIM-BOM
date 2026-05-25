using System;
using System.IO;

namespace FamilyConverter.Revit2021.Services
{
    public class LoggingService
    {
        private readonly string _logFilePath;

        public LoggingService()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string folder = Path.Combine(appData, "ENECA_MEP", "FamilyConverter", "logs");
                Directory.CreateDirectory(folder);
                _logFilePath = Path.Combine(folder, "FamilyConverter_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            }
            catch
            {
                _logFilePath = null;
            }
        }

        public void Info(string message)
        {
            Write("INFO", message, null);
        }

        public void Warning(string message)
        {
            Write("WARN", message, null);
        }

        public void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        private void Write(string level, string message, Exception exception)
        {
            if (string.IsNullOrWhiteSpace(_logFilePath))
            {
                return;
            }

            try
            {
                string safeMessage = RedactSecrets(message ?? string.Empty);
                string line = string.Format("{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}", DateTime.Now, level, safeMessage);
                if (exception != null)
                {
                    line += Environment.NewLine + RedactSecrets(exception.ToString());
                }

                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Logging is diagnostic only; it must never break conversion.
            }
        }

        private static string RedactSecrets(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return text.Replace("\"apiKey\"", "\"apiKey_redacted\"");
        }
    }
}
