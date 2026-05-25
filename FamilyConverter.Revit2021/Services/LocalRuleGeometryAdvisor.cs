using System.Threading;
using System.Threading.Tasks;
using FamilyConverter.Revit2021.Models;

namespace FamilyConverter.Revit2021.Services
{
    public class LocalRuleGeometryAdvisor : IAiGeometryAdvisor
    {
        public Task<AiGeometryResponse> AnalyzeAsync(AiGeometryRequest request, CancellationToken cancellationToken)
        {
            var response = new AiGeometryResponse
            {
                classification = request.local_classification,
                confidence = request.local_confidence,
                provider = "local-rules",
                model = "none"
            };

            if (request.local_confidence >= 0.85)
            {
                response.recommended_method = "extrusion";
                response.reason = "Локальная классификация достаточно уверенная.";
                response.fallback_method = "freeform";
            }
            else if (request.local_confidence >= 0.50)
            {
                response.recommended_method = "freeform";
                response.reason = "Локальная классификация сомнительная; безопаснее использовать FreeFormElement.";
                response.fallback_method = "skip";
            }
            else
            {
                response.recommended_method = "skip";
                response.reason = "Локальная классификация недостаточно надежная.";
                response.fallback_method = "skip";
            }

            return Task.FromResult(response);
        }
    }
}
