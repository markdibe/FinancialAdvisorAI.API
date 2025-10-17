using OpenAI.Managers;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;
using Microsoft.EntityFrameworkCore;
using FinancialAdvisorAI.API.Repositories;
using System.Text.Json;

// Alias for clarity
using OpenAIChatMessage = OpenAI.ObjectModels.RequestModels.ChatMessage;

namespace FinancialAdvisorAI.API.Services
{
    public class AiChatService
    {
        private readonly OpenAI.Managers.OpenAIService _openAIClient;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _context;
        private readonly ILogger<AiChatService> _logger;

        public AiChatService(
            IConfiguration configuration,
            AppDbContext context,
            ILogger<AiChatService> logger)
        {
            _configuration = configuration;
            _context = context;
            _logger = logger;

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
                        Model = OpenAI.ObjectModels.Models.Gpt_4o_mini,
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

        public async Task<string> GetResponseWithEmailContextAsync(
            int userId,
            string userMessage,
            List<OpenAIChatMessage>? conversationHistory = null)
        {
            // Search for relevant emails based on user's question
            var relevantEmails = await SearchEmailsAsync(userId, userMessage);

            // Build context from emails
            var emailContext = BuildEmailContext(relevantEmails);

            var messages = new List<OpenAIChatMessage>
            {
                OpenAIChatMessage.FromSystem(
                    "You are a helpful AI assistant for a financial advisor. " +
                    "You help manage client relationships, schedule meetings, and provide information about clients. " +
                    "Be professional, concise, and helpful. " +
                    "When answering questions about emails, refer to them naturally (e.g., 'John emailed you on Oct 15...'). " +
                    emailContext)
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

        private async Task<List<Models.EmailCache>> SearchEmailsAsync(int userId, string query)
        {
            try
            {
                // Simple keyword search - we'll improve this with RAG later
                var keywords = ExtractKeywords(query);

                var emails = await _context.EmailCaches
                    .Where(e => e.UserId == userId)
                    .Where(e =>
                        keywords.Any(k =>
                            (e.Subject != null && e.Subject.ToLower().Contains(k)) ||
                            (e.FromEmail != null && e.FromEmail.ToLower().Contains(k)) ||
                            (e.Body != null && e.Body.ToLower().Contains(k))))
                    .OrderByDescending(e => e.EmailDate)
                    .Take(10)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} relevant emails for query: {Query}", emails.Count, query);
                return emails;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching emails for user {UserId}", userId);
                return new List<Models.EmailCache>();
            }
        }

        private List<string> ExtractKeywords(string query)
        {
            // Remove common words and extract meaningful keywords
            var commonWords = new HashSet<string> {
                "who", "what", "when", "where", "why", "how", "is", "are", "was", "were",
                "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
                "of", "with", "by", "from", "about", "show", "find", "get", "tell",
                "me", "my", "i", "you", "email", "emails", "emailed", "sent", "received"
            };

            var words = query.ToLower()
                .Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !commonWords.Contains(w))
                .ToList();

            return words;
        }

        private string BuildEmailContext(List<Models.EmailCache> emails)
        {
            if (!emails.Any())
            {
                return "\n\nNo relevant emails found in the database.";
            }

            var context = "\n\nRELEVANT EMAILS FROM GMAIL:\n";

            foreach (var email in emails)
            {
                context += $"\n---\n";
                context += $"From: {email.FromEmail}\n";
                context += $"Date: {email.EmailDate?.ToString("MMMM d, yyyy")}\n";
                context += $"Subject: {email.Subject}\n";

                // Include snippet or first 500 chars of body
                var preview = email.Snippet ?? email.Body ?? "";
                if (preview.Length > 500)
                {
                    preview = preview.Substring(0, 500) + "...";
                }
                context += $"Preview: {preview}\n";
            }

            return context;
        }

        // Keep the old method for backward compatibility
        public async Task<string> GetSimpleResponseAsync(string userMessage, List<OpenAIChatMessage>? conversationHistory = null)
        {
            var messages = new List<OpenAIChatMessage>
            {
                OpenAIChatMessage.FromSystem("You are a helpful AI assistant for a financial advisor. " +
                    "You help manage client relationships, schedule meetings, and provide information about clients. " +
                    "Be professional, concise, and helpful.")
            };

            if (conversationHistory != null && conversationHistory.Any())
            {
                messages.AddRange(conversationHistory);
            }

            messages.Add(OpenAIChatMessage.FromUser(userMessage));

            return await GetChatCompletionAsync(messages);
        }
    }
}