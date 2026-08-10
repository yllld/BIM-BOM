using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using DWGOptimizer.Contracts;

namespace DWGOptimizer.AutoCAD2022
{
    internal static class ReportWriter
    {
        public static void Write(OptimizationReport report)
        {
            string jsonPath = Path.ChangeExtension(report.OutputPath, ".revitprep.json");
            string htmlPath = Path.ChangeExtension(report.OutputPath, ".html");
            report.JsonReportPath = jsonPath;
            report.HtmlReportPath = htmlPath;
            JsonFile.Write(jsonPath, report);
            File.WriteAllText(htmlPath, BuildHtml(report), new UTF8Encoding(false));

            string centralReport = Path.Combine(
                OutputPathService.GetReportsDirectory(),
                Path.GetFileNameWithoutExtension(report.OutputPath) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
            File.Copy(htmlPath, centralReport, true);
        }

        private static string BuildHtml(OptimizationReport report)
        {
            var html = new StringBuilder();
            html.Append("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>DWG Revit Optimizer</title>")
                .Append("<style>body{font-family:Segoe UI,Arial;margin:32px;color:#24212b}h1{color:#7b2cff}table{border-collapse:collapse;width:100%;margin:16px 0}th,td{border:1px solid #ddd;padding:8px;text-align:left}th{background:#f3ecff}.ok{color:#087f23}.warn{color:#a35b00}.bad{color:#b00020}code{background:#f4f4f4;padding:2px 5px}</style></head><body>")
                .Append("<h1>DWG Revit Optimizer</h1>")
                .Append("<p><b>Профиль:</b> ").Append(report.Profile).Append("</p>")
                .Append("<p><b>Исходник:</b> ").Append(E(report.Before == null ? null : report.Before.SourcePath)).Append("</p>")
                .Append("<p><b>Результат:</b> ").Append(E(report.OutputPath)).Append("</p>")
                .Append("<p><b>Статус:</b> <span class=\"")
                .Append(report.Success ? "ok\">Успешно" : "bad\">Ошибка")
                .Append("</span></p>")
                .Append("<h2>До / после</h2><table><tr><th>Показатель</th><th>До</th><th>После</th></tr>");

            AddMetric(html, "Размер, МБ", BytesToMb(report.Before == null ? 0 : report.Before.SourceSizeBytes), BytesToMb(report.OutputSizeBytes));
            AddMetric(html, "Readiness", report.Before == null ? "-" : report.Before.ReadinessScore.ToString(), report.After == null ? "-" : report.After.ReadinessScore.ToString());
            AddMetric(html, "Объекты", Count(report.Before, x => x.TotalEntities), Count(report.After, x => x.TotalEntities));
            AddMetric(html, "3D Solid", Count(report.Before, x => x.Solid3d), Count(report.After, x => x.Solid3d));
            AddMetric(html, "Mesh faces", CountLong(report.Before, x => x.MeshFaces), CountLong(report.After, x => x.MeshFaces));
            AddMetric(html, "Proxy", Count(report.Before, x => x.Proxy), Count(report.After, x => x.Proxy));
            html.Append("</table><h2>Операции</h2><table><tr><th>Операция</th><th>Статус</th><th>Объекты</th><th>Сообщение</th></tr>");
            foreach (OperationResult operation in report.Operations)
            {
                html.Append("<tr><td>").Append(E(operation.Description)).Append("</td><td>")
                    .Append(operation.RolledBack ? "Откат" : operation.Applied ? "Выполнено" : "Пропущено")
                    .Append("</td><td>").Append(operation.AffectedObjects).Append("</td><td>").Append(E(operation.Message)).Append("</td></tr>");
            }

            html.Append("</table>");
            if (report.Before != null && report.Before.Findings.Count > 0)
            {
                html.Append("<h2>Диагностика</h2><ul>");
                foreach (Finding finding in report.Before.Findings)
                {
                    html.Append("<li class=\"").Append(finding.Severity == FindingSeverity.Blocker ? "bad" : finding.Severity == FindingSeverity.Warning ? "warn" : string.Empty)
                        .Append("\"><b>").Append(E(finding.Code)).Append(":</b> ").Append(E(finding.Message)).Append("</li>");
                }
                html.Append("</ul>");
            }

            if (report.Errors.Count > 0)
            {
                html.Append("<h2>Ошибки</h2><ul class=\"bad\">");
                foreach (string error in report.Errors) html.Append("<li>").Append(E(error)).Append("</li>");
                html.Append("</ul>");
            }

            html.Append("</body></html>");
            return html.ToString();
        }

        private static void AddMetric(StringBuilder html, string name, string before, string after)
        {
            html.Append("<tr><td>").Append(E(name)).Append("</td><td>").Append(E(before)).Append("</td><td>").Append(E(after)).Append("</td></tr>");
        }

        private static string Count(AnalysisReport report, Func<GeometryCounts, int> selector)
        {
            return report == null ? "-" : selector(report.Counts).ToString(CultureInfo.InvariantCulture);
        }

        private static string CountLong(AnalysisReport report, Func<GeometryCounts, long> selector)
        {
            return report == null ? "-" : selector(report.Counts).ToString(CultureInfo.InvariantCulture);
        }

        private static string BytesToMb(long bytes) { return (bytes / 1024d / 1024d).ToString("0.00", CultureInfo.InvariantCulture); }
        private static string E(string value) { return WebUtility.HtmlEncode(value ?? "-"); }
    }
}
