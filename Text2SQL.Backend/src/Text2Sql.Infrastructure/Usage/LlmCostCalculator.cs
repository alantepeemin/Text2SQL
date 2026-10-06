using Microsoft.Extensions.Configuration;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Infrastructure.Usage
{
    /// <summary>
    /// Config tabanlı maliyet tahmini:
    /// "Usage": { "Pricing": { "openai/gpt-4.1-mini": { "InputPer1M": 0.40, "OutputPer1M": 1.60 } } }
    /// Model bulunamazsa Usage:Pricing:default kullanılır; o da yoksa 0 döner
    /// (ölçüm token bazında yine doğru kalır, yalnızca para tahmini boş olur).
    /// </summary>
    public sealed class LlmCostCalculator : ILlmCostCalculator
    {
        private readonly IConfiguration _config;

        public LlmCostCalculator(IConfiguration config) => _config = config;

        public decimal Estimate(string? model, int promptTokens, int completionTokens)
        {
            var section = model != null
                ? _config.GetSection($"Usage:Pricing:{model}")
                : null;

            if (section == null || !section.Exists())
                section = _config.GetSection("Usage:Pricing:default");

            if (!section.Exists()) return 0m;

            var inputPer1M  = section.GetValue<decimal>("InputPer1M");
            var outputPer1M = section.GetValue<decimal>("OutputPer1M");

            return (promptTokens / 1_000_000m) * inputPer1M
                 + (completionTokens / 1_000_000m) * outputPer1M;
        }
    }
}
