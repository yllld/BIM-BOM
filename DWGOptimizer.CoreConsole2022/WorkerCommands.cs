using System;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using DWGOptimizer.AutoCAD2022;
using DWGOptimizer.Contracts;

[assembly: CommandClass(typeof(DWGOptimizer.CoreConsole2022.WorkerCommands))]

namespace DWGOptimizer.CoreConsole2022
{
    public sealed class WorkerCommands
    {
        [CommandMethod("DWGREVITREADYWORKER", CommandFlags.Session)]
        public void Worker()
        {
            string jobPath = Environment.GetEnvironmentVariable("DWG_OPTIMIZER_JOB");
            if (string.IsNullOrWhiteSpace(jobPath) || !File.Exists(jobPath))
                throw new InvalidOperationException("DWG_OPTIMIZER_JOB не задан.");
            BatchJob job = JsonFile.Read<BatchJob>(jobPath);
            Database database = HostApplicationServices.WorkingDatabase;
            try
            {
                WriteStatus(job, "Running", "Анализ геометрии DWG.", null);
                AnalysisReport analysis = new DwgAnalyzer().Analyze(database, job.SourcePath);
                WriteStatus(job, "Running", "Оптимизация рабочей копии DWG.", null);
                OptimizationReport result = new DwgOptimizerService().Optimize(database, job.SourcePath, analysis, job.Request);
                WriteStatus(job, result.Success ? "Completed" : "Failed", string.Join("; ", result.Errors), result.OutputPath);
            }
            catch (System.Exception ex)
            {
                WriteStatus(job, "Failed", ex.ToString(), null);
                throw;
            }
        }

        private static void WriteStatus(BatchJob job, string state, string message, string output)
        {
            JsonFile.Write(job.StatusPath, new BatchJobStatus
            {
                JobId = job.Id, State = state, Message = message, OutputPath = output,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
    }
}
