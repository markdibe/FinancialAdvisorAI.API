using OpenAI.Managers;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels;

namespace FinancialAdvisorAI.API.Services
{
    public class EmbeddingService
    {
        private readonly OpenAI.Managers.OpenAIService _openAIClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmbeddingService> _logger;

        public EmbeddingService(
            IConfiguration configuration,
            ILogger<EmbeddingService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            var apiKey = _configuration["OpenAI:ApiKey"];
            _openAIClient = new OpenAI.Managers.OpenAIService(new OpenAI.OpenAiOptions()
            {
                ApiKey = apiKey
            });
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            try
            {
                // Truncate text if too long (max 8192 tokens for text-embedding-3-small)
                if (text.Length > 10000)
                {
                    text = text.Substring(0, 10000);
                }

                var embeddingResult = await _openAIClient.Embeddings.CreateEmbedding(
                    new EmbeddingCreateRequest
                    {
                        Input = text,
                        Model = OpenAI.ObjectModels.Models.TextEmbeddingV3Small
                    });

                if (embeddingResult.Successful)
                {
                    var embedding = embeddingResult.Data.First().Embedding;
                    Task.Delay(100).Wait();
                    return embedding.Select(x => (float)x).ToArray();
                }
                else
                {
                    throw new Exception($"OpenAI Embedding Error: {embeddingResult.Error?.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating embedding for text: {Text}", text.Substring(0, Math.Min(100, text.Length)));
                throw;
            }
        }

        public async Task<List<float[]>> GenerateBatchEmbeddingsAsync(List<string> texts)
        {
            var embeddings = new List<float[]>();

            // Process in batches of 20 to avoid rate limits
            var batchSize = 20;
            for (int i = 0; i < texts.Count; i += batchSize)
            {
                var batch = texts.Skip(i).Take(batchSize).ToList();

                foreach (var text in batch)
                {
                    var embedding = await GenerateEmbeddingAsync(text);
                    embeddings.Add(embedding);
                }

                // Small delay to respect rate limits
                if (i + batchSize < texts.Count)
                {
                    await Task.Delay(200);
                }
            }

            return embeddings;
        }
    }
}