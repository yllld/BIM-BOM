using System;
using System.Diagnostics;
using System.IO;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.BatchRunner
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length != 1 || !File.Exists(args[0])) return 2;
            BatchManifest manifest = JsonFile.Read<BatchManifest>(args[0]);
            if (!File.Exists(manifest.AutoCadCoreConsolePath) || !File.Exists(manifest.PluginPath)) return 3;
            string queueDirectory = Path.GetDirectoryName(Path.GetFullPath(args[0]));
            string cancelPath = args[0] + ".cancel";
            int failures = 0;

            foreach (BatchJob job in manifest.Jobs)
            {
                if (File.Exists(cancelPath))
                {
                    WriteStatus(job, "Cancelled", "Очередь отменена пользователем.", null, 0);
                    continue;
                }
                try
                {
                    string sourceHash = Sha256(job.SourcePath);
                    string jobPath = Path.Combine(queueDirectory, job.Id + ".job.json");
                    string scriptPath = Path.Combine(queueDirectory, job.Id + ".scr");
                    string logPath = Path.Combine(queueDirectory, job.Id + ".log");
                    string isolatePath = Path.Combine(queueDirectory, "isolate");
                    Directory.CreateDirectory(isolatePath);
                    JsonFile.Write(jobPath, job);
                    File.WriteAllLines(scriptPath, new[]
                    {
                        "_.NETLOAD",
                        "\"" + manifest.PluginPath.Replace("\\", "/") + "\"",
                        "DWGREVITREADYWORKER",
                        "_.QUIT",
                        "_N"
                    });
                    WriteStatus(job, "Running", "Обработка в AutoCAD Core Console.", null, 0);
                    var start = new ProcessStartInfo
                    {
                        FileName = manifest.AutoCadCoreConsolePath,
                        Arguments = "/i \"" + job.SourcePath + "\" /s \"" + scriptPath + "\" /l en-US /isolate DWGOptimizer \"" + isolatePath + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Path.GetDirectoryName(manifest.AutoCadCoreConsolePath)
                    };
                    start.EnvironmentVariables["DWG_OPTIMIZER_JOB"] = jobPath;
                    int exitCode;
                    using (Process process = Process.Start(start))
                    {
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        exitCode = process.ExitCode;
                        File.WriteAllText(logPath, output + Environment.NewLine + error);
                    }
                    if (!string.Equals(sourceHash, Sha256(job.SourcePath), StringComparison.OrdinalIgnoreCase))
                    {
                        WriteStatus(job, "Failed", "Контрольная сумма исходного DWG изменилась.", null, exitCode);
                        failures++;
                    }
                    else if (exitCode != 0)
                    {
                        WriteStatus(job, "Failed", "Core Console завершился с кодом " + exitCode + ". См. " + logPath, null, exitCode);
                        failures++;
                    }
                }
                catch (Exception ex)
                {
                    WriteStatus(job, "Failed", ex.ToString(), null, -1);
                    failures++;
                }
            }
            return failures == 0 ? 0 : 1;
        }

        private static void WriteStatus(BatchJob job, string state, string message, string output, int exitCode)
        {
            JsonFile.Write(job.StatusPath, new BatchJobStatus
            {
                JobId = job.Id, State = state, Message = message, OutputPath = output,
                ExitCode = exitCode, UpdatedAtUtc = DateTime.UtcNow
            });
        }

        private static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
