using System.Threading;
using System.Threading.Tasks;
using FamilyConverter.Revit2021.Models;

namespace FamilyConverter.Revit2021.Services
{
    public interface IAiGeometryAdvisor
    {
        Task<AiGeometryResponse> AnalyzeAsync(AiGeometryRequest request, CancellationToken cancellationToken);
    }
}
