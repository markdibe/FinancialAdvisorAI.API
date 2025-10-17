using OpenAI.Managers;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;

// Alias for clarity
using OpenAIChatMessage = OpenAI.ObjectModels.RequestModels.ChatMessage;

namespace FinancialAdvisorAI.API.Services
{
    public class AiChatService
    {
        private readonly OpenAI.Managers.OpenAIService _openAIClient;
        private readonly IConfiguration _configuration;

        public AiChatService(IConfiguration configuration)
        {
            _configuration = configuration;
            var apiKey = _configuration["OpenAI:ApiKey"];
            _openAIClient = new OpenAI.Managers.OpenAIService(new OpenAI.OpenAiOptions()
            {
                ApiKey = apiKey
            });
        }

        public async Task<string> GetChatCompletionAsync(List<OpenAIChatMessage> messages)
        {
            try
            {
                var completionResult = await _openAIClient.ChatCompletion.CreateCompletion(
                    new ChatCompletionCreateRequest
                    {
                        Messages = messages,
                        Model = OpenAI.ObjectModels.Models.Gpt_4,
                        MaxTokens = 1000,
                        Temperature = 0.7f
                    });

                if (completionResult.Successful)
                {
                    return completionResult.Choices.First().Message.Content;
                }
                else
                {
                    throw new Exception($"OpenAI API Error: {completionResult.Error?.Message}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error calling OpenAI: {ex.Message}", ex);
            }
        }

        public async Task<string> GetSimpleResponseAsync(string userMessage, List<OpenAIChatMessage>? conversationHistory = null)
        {
            var messages = new List<OpenAIChatMessage>
            {
                OpenAIChatMessage.FromSystem("You are a helpful AI assistant for a financial advisor. " +
                    "You help manage client relationships, schedule meetings, and provide information about clients. " +
                    "Be professional, concise, and helpful.")
            };

            // Add conversation history if provided
            if (conversationHistory != null && conversationHistory.Any())
            {
                messages.AddRange(conversationHistory);
            }

            // Add current user message
            messages.Add(OpenAIChatMessage.FromUser(userMessage));

            return await GetChatCompletionAsync(messages);
        }
    }
}