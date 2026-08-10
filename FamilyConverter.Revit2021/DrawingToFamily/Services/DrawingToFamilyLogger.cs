using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using FamilyConverter.Revit2021;

namespace FamilyConverter.Revit2021.DrawingToFamily.Services
{
    public class DrawingToFamilyLogger
    {
        private readonly Stopwatch _stopwatch;

        public DrawingToFamilyLogger()
        {
            _stopwatch = Stopwatch.StartNew();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, ProductInfo.AppDataRootFolder, ProductInfo.AppDataProductFolder, "logs");
            Directory.CreateDirectory(folder);
            LogPath = Path.Combine(folder, "DrawingToFamily_Log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
        }

        public string LogPath { get; private set; }

        public void Info(string message)
        {
            Write("INFO", message, null);
        }

        public void Warning(string message)
        {
            Write("WARN", message, null);
        }

        public void Debug(string message)
        {
            Write("DEBUG", message, null);
        }

        public void Data(string name, object value)
        {
            Write("DATA", name + " = " + (value ?? "-"), null);
        }

        public void Error(string message, Exception exception)
        {
            Write("ERROR", message, exception);
        }

        public void Stage(string name)
        {
            Write("STAGE", name + " at " + _stopwatch.ElapsedMilliseconds + " ms", null);
        }

        private void Write(string level, string message, Exception exception)
        {
            try
            {
                string text = string.Format("{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] +{2}ms {3}", DateTime.Now, level, _stopwatch.ElapsedMilliseconds, message);
                if (exception != null)
                {
                    text += Environment.NewLine + exception;
                }

                using (var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                {
                    writer.WriteLine(text);
                    writer.Flush();
                    stream.Flush(true);
                }
            }
            catch
            {
                // Technical logging must never interrupt geometry creation.
            }
        }
    }
}
