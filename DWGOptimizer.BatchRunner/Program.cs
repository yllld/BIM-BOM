using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
                        "_.SETVAR",
                        "SECURELOAD",
                        "0",
                        "_.NETLOAD",
                        "\"" + manifest.PluginPath.Replace("\\", "/") + "\"",
                        "DWGREVITREADYWORKER",
                        "_.QUIT",
                        "_Y"
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
                        WorkingDirectory = Path.GetDirectoryName(manifest.AutoCadCoreConsolePath),
                        StandardOutputEncoding = Encoding.Unicode,
                        StandardErrorEncoding = Encoding.Unicode
                    };
                    start.EnvironmentVariables["DWG_OPTIMIZER_JOB"] = jobPath;
                    int exitCode;
                    bool cancelled = false;
                    var output = new StringBuilder();
                    var error = new StringBuilder();
                    using (var process = new Process { StartInfo = start })
                    {
                        process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                        process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (error) error.AppendLine(e.Data); };
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        while (!process.WaitForExit(500))
                        {
                            if (!File.Exists(cancelPath)) continue;
                            cancelled = true;
                            try { process.Kill(); } catch { }
                            break;
                        }
                        process.WaitForExit();
                        exitCode = process.ExitCode;
                        File.WriteAllText(logPath, output + Environment.NewLine + error, new UTF8Encoding(false));
                    }
                    if (cancelled)
                    {
                        WriteStatus(job, "Cancelled", "Обработка текущего файла отменена пользователем.", null, exitCode);
                    }
                    else if (!string.Equals(sourceHash, Sha256(job.SourcePath), StringComparison.OrdinalIgnoreCase))
                    {
                        WriteStatus(job, "Failed", "Контрольная сумма исходного DWG изменилась.", null, exitCode);
                        failures++;
                    }
                    else if (exitCode != 0)
                    {
                        BatchJobStatus workerStatus = TryReadStatus(job.StatusPath);
                        string detail = workerStatus != null && workerStatus.State == "Failed" && !string.IsNullOrWhiteSpace(workerStatus.Message)
                            ? workerStatus.Message
                            : "Core Console завершился с кодом " + exitCode + ". См. " + logPath;
                        WriteStatus(job, "Failed", detail, workerStatus == null ? null : workerStatus.OutputPath, exitCode);
                        failures++;
                    }
                    else
                    {
                        BatchJobStatus status = TryReadStatus(job.StatusPath);
                        if (status == null || (status.State != "Completed" && status.State != "Failed"))
                        {
                            WriteStatus(job, "Failed", "Worker-команда не завершилась. Проверьте загрузку модуля в логе: " + logPath, null, exitCode);
                            failures++;
                        }
                        else if (status.State == "Failed") failures++;
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

        private static BatchJobStatus TryReadStatus(string path)
        {
            try { return File.Exists(path) ? JsonFile.Read<BatchJobStatus>(path) : null; }
            catch { return null; }
        }

        private static string Sha256(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan))
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
        }
    }
}
