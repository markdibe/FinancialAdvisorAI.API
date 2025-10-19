using FinancialAdvisorAI.API.Repositories;
using Google.Apis.Gmail.v1;
using Microsoft.EntityFrameworkCore;
using OpenAI.Managers;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;
using Qdrant.Client.Grpc;
using System.Text;
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
        private readonly EmbeddingService? _embeddingService;
        private readonly QdrantService? _qdrantService;
        private readonly bool _useRAG;
        private readonly EmailService _emailService;
        private readonly ToolExecutorService _toolExecutor;

        public AiChatService(
            IConfiguration configuration,
            AppDbContext context,
            ILogger<AiChatService> logger,
            EmbeddingService? embeddingService = null,
            QdrantService? qdrantService = null,
            EmailService emailService = null,
            ToolExecutorService toolExecutorService = null
            )
        {
            _configuration = configuration;
            _context = context;
            _logger = logger;
            _embeddingService = embeddingService;
            _qdrantService = qdrantService;

            // Enable RAG only if both services are available
            _useRAG = _embeddingService != null && _qdrantService != null;

            var apiKey = _configuration["OpenAI:ApiKey"];
            _openAIClient = new OpenAI.Managers.OpenAIService(new OpenAI.OpenAiOptions()
            {
                ApiKey = apiKey
            });
            _emailService = emailService;
            _toolExecutor = toolExecutorService;
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

        // NEW: Main entry point - uses RAG if available, otherwise falls back to keyword search
        public async Task<string> GetResponseWithContextAsync(
            int userId,
            string userMessage,
            List<OpenAIChatMessage>? conversationHistory = null)
        {
            if (_useRAG)
            {
                try
                {
                    _logger.LogInformation("Using RAG (semantic search) for query");
                    return await GetResponseWithRAGAsync(userId, userMessage, conversationHistory);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RAG failed, falling back to keyword search");
                    // Fall back to keyword search if RAG fails
                    return await GetResponseWithEmailContextAsync(userId, userMessage, conversationHistory);
                }
            }
            else
            {
                _logger.LogInformation("RAG not available, using keyword search");
                return await GetResponseWithEmailContextAsync(userId, userMessage, conversationHistory);
            }
        }

        // NEW: RAG-based response (semantic search using Qdrant)
        private async Task<string> GetResponseWithRAGAsync(
            int userId,
            string userMessage,
            List<OpenAIChatMessage>? conversationHistory = null)
        {
            // Generate embedding for the user's query
            var queryEmbedding = await _embeddingService!.GenerateEmbeddingAsync(userMessage);

            List<ScoredPoint> searchResults = new List<ScoredPoint>();

            // Search Qdrant for relevant context
            try
            {
                searchResults = await _qdrantService!.SearchAsync(
                queryVector: queryEmbedding,
                limit: 10,
                filter: new Dictionary<string, object> { { "user_id", userId } }
            );
            }
            catch (Exception e)
            {
                throw;
            }

            // Build context from search results
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("RELEVANT INFORMATION FROM YOUR DATA:\n");

            foreach (var result in searchResults)
            {
                var type = result.Payload["type"].StringValue;
                var content = result.Payload["content"].StringValue;
                var score = result.Score;

                contextBuilder.AppendLine($"[{type.ToUpper()} - Relevance: {score:F2}]");
                contextBuilder.AppendLine(content);
                contextBuilder.AppendLine();
            }

            _logger.LogInformation("Found {Count} relevant items using RAG", searchResults.Count);

            // Build messages for GPT
            var messages = new List<OpenAIChatMessage>
            {
                OpenAIChatMessage.FromSystem(
                    "You are a helpful AI assistant for a financial advisor. " +
                    "You help manage client relationships, answer questions about emails, meetings, and clients. " +
                    "Use the provided context to answer questions accurately. " +
                    "If you don't find relevant information in the context, say so honestly. " +
                    "Be professional, concise, and helpful.\n\n" +
                    contextBuilder.ToString())
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

        // KEEP YOUR EXISTING METHOD - Used as fallback
        public async Task<string> GetResponseWithEmailContextAsync(
            int userId,
            string userMessage,
            List<OpenAIChatMessage>? conversationHistory = null)
        {
            // Search for relevant emails AND calendar events
            var relevantEmails = await SearchEmailsAsync(userId, userMessage);
            var relevantEvents = await SearchCalendarEventsAsync(userId, userMessage);

            // Build context from emails and calendar
            var emailContext = BuildEmailContext(relevantEmails);
            var calendarContext = BuildCalendarContext(relevantEvents);

            var messages = new List<OpenAIChatMessage>
            {
                OpenAIChatMessage.FromSystem(
                    "You are a helpful AI assistant for a financial advisor. " +
                    "You help manage client relationships, schedule meetings, and provide information about clients. " +
                    "Be professional, concise, and helpful. " +
                    "When answering questions about emails, refer to them naturally (e.g., 'John emailed you on Oct 15...'). " +
                    "When answering about calendar events, mention dates and times clearly. " +
                    emailContext + calendarContext)
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
                // Simple keyword search
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

                var preview = email.Snippet ?? email.Body ?? "";
                if (preview.Length > 500)
                {
                    preview = preview.Substring(0, 500) + "...";
                }
                context += $"Preview: {preview}\n";
            }

            return context;
        }

        private async Task<List<Models.CalendarEventCache>> SearchCalendarEventsAsync(int userId, string query)
        {
            try
            {
                var searchParams = await AnalyzeCalendarQueryAsync(query);

                if (!searchParams.IsCalendarQuery)
                {
                    return new List<Models.CalendarEventCache>();
                }

                var now = DateTime.UtcNow;
                var today = now.Date;

                var eventsQuery = _context.CalendarEventCaches
                    .Where(e => e.UserId == userId);

                if (searchParams.Filters != null)
                {
                    switch (searchParams.Filters.TimeFilter?.ToLower())
                    {
                        case "today":
                            var tomorrow = today.AddDays(1);
                            eventsQuery = eventsQuery.Where(e => e.StartTime >= today && e.StartTime < tomorrow);
                            break;
                        case "yesterday":
                            var yesterday = today.AddDays(-1);
                            eventsQuery = eventsQuery.Where(e => e.StartTime >= yesterday && e.StartTime < today);
                            break;
                        case "tomorrow":
                            var dayAfterTomorrow = today.AddDays(2);
                            eventsQuery = eventsQuery.Where(e => e.StartTime >= today.AddDays(1) && e.StartTime < dayAfterTomorrow);
                            break;
                        case "this_week":
                            var weekStart = today.AddDays(-(int)now.DayOfWeek);
                            var weekEnd = weekStart.AddDays(7);
                            eventsQuery = eventsQuery.Where(e => e.StartTime >= weekStart && e.StartTime < weekEnd);
                            break;
                        case "last_week":
                            var lastWeekStart = today.AddDays(-(int)now.DayOfWeek - 7);
                            var lastWeekEnd = lastWeekStart.AddDays(7);
                            eventsQuery = eventsQuery.Where(e => e.StartTime >= lastWeekStart && e.StartTime < lastWeekEnd);
                            break;
                        case "next_week":
                            var nextWeekStart = today.AddDays(7 - (int)now.DayOfWeek);
                            var nextWeekEnd = nextWeekStart.AddDays(7);
                            eventsQuery = eventsQuery.Where(e => e.StartTime >= nextWeekStart && e.StartTime < nextWeekEnd);
                            break;
                        case "past":
                            eventsQuery = eventsQuery.Where(e => e.StartTime < now);
                            break;
                        case "all":
                            break;
                        case "upcoming":
                        default:
                            eventsQuery = eventsQuery.Where(e => e.StartTime >= now);
                            break;
                    }

                    if (searchParams.Filters.SearchTerms != null && searchParams.Filters.SearchTerms.Any())
                    {
                        var fields = searchParams.Filters.SearchFields ?? new List<string> { "Summary", "Description", "Location", "Attendees" };

                        eventsQuery = eventsQuery.Where(e =>
                            searchParams.Filters.SearchTerms.Any(term =>
                                (fields.Contains("Summary") && e.Summary != null && e.Summary.ToLower().Contains(term.ToLower())) ||
                                (fields.Contains("Description") && e.Description != null && e.Description.ToLower().Contains(term.ToLower())) ||
                                (fields.Contains("Location") && e.Location != null && e.Location.ToLower().Contains(term.ToLower())) ||
                                (fields.Contains("Attendees") && e.Attendees != null && e.Attendees.ToLower().Contains(term.ToLower()))));
                    }
                }

                if (searchParams.OrderDirection?.ToLower() == "desc")
                {
                    eventsQuery = eventsQuery.OrderByDescending(e => e.StartTime);
                }
                else
                {
                    eventsQuery = eventsQuery.OrderBy(e => e.StartTime);
                }

                var events = await eventsQuery
                    .Take(searchParams.Limit ?? 10)
                    .ToListAsync();

                _logger.LogInformation("Found {Count} calendar events", events.Count);

                return events;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching calendar events for user {UserId}", userId);
                return new List<Models.CalendarEventCache>();
            }
        }

        private async Task<CalendarQueryParams> AnalyzeCalendarQueryAsync(string query)
        {
            try
            {
                var messages = new List<OpenAIChatMessage>
                {
                    OpenAIChatMessage.FromSystem(
                        "You are a database query analyzer. Analyze if this is a calendar query and respond with JSON.\n" +
                        "Format: {\"isCalendarQuery\": true/false, \"filters\": {\"timeFilter\": \"upcoming\", \"searchTerms\": []}}"),
                    OpenAIChatMessage.FromUser(query)
                };

                var completionResult = await _openAIClient.ChatCompletion.CreateCompletion(
                    new ChatCompletionCreateRequest
                    {
                        Messages = messages,
                        Model = OpenAI.ObjectModels.Models.Gpt_4o_mini,
                        MaxTokens = 300,
                        Temperature = 0f
                    });

                if (completionResult.Successful)
                {
                    var response = completionResult.Choices.First().Message.Content.Trim();
                    var result = JsonSerializer.Deserialize<CalendarQueryParams>(response, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return result ?? new CalendarQueryParams { IsCalendarQuery = false };
                }

                return new CalendarQueryParams { IsCalendarQuery = false };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing calendar query");
                return new CalendarQueryParams { IsCalendarQuery = false };
            }
        }

        private string BuildCalendarContext(List<Models.CalendarEventCache> events)
        {
            if (!events.Any())
            {
                return "\n\nNo relevant calendar events found.";
            }

            var context = "\n\nRELEVANT CALENDAR EVENTS:\n";

            foreach (var evt in events)
            {
                context += $"\n---\n";
                context += $"Event: {evt.Summary}\n";
                context += $"Start: {evt.StartTime?.ToString("MMMM d, yyyy h:mm tt")}\n";
                context += $"End: {evt.EndTime?.ToString("MMMM d, yyyy h:mm tt")}\n";

                if (!string.IsNullOrEmpty(evt.Location))
                {
                    context += $"Location: {evt.Location}\n";
                }

                if (!string.IsNullOrEmpty(evt.Description))
                {
                    var desc = evt.Description.Length > 200
                        ? evt.Description.Substring(0, 200) + "..."
                        : evt.Description;
                    context += $"Description: {desc}\n";
                }

                if (!string.IsNullOrEmpty(evt.Attendees))
                {
                    try
                    {
                        var attendees = JsonSerializer.Deserialize<List<string>>(evt.Attendees);
                        if (attendees != null && attendees.Any())
                        {
                            context += $"Attendees: {string.Join(", ", attendees)}\n";
                        }
                    }
                    catch { }
                }
            }

            return context;
        }

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
        public async Task<string> GetResponseWithToolsAsync(
    int userId,
    string userMessage,
    List<OpenAIChatMessage>? conversationHistory = null)
        {
            // STEP 1: Use RAG to get context (same as your existing GetResponseWithContextAsync)
            if (_useRAG)
            {
                try
                {
                    _logger.LogInformation("Using RAG with tool calling capability");

                    // Generate embedding for the user's query
                    var queryEmbedding = await _embeddingService!.GenerateEmbeddingAsync(userMessage);

                    int searchLimit = DetermineSearchLimit(userMessage);


                    // Search Qdrant for relevant context
                    var searchResults = await _qdrantService!.SearchAsync(
                        queryVector: queryEmbedding,
                        limit: searchLimit * 2,
                        filter: new Dictionary<string, object> { { "user_id", userId } }
                    );

                    //var relevantResults = searchResults
                    //    .Where(r => r.Score >= 0.5)
                    //    .ToList();

                    //var finalResults = DiversifyResults(relevantResults, searchLimit);

                    _logger.LogInformation(
                        "Search: fetched {Fetched}, filtered to {Filtered} (score>={MinScore}), final {Final}",
                        searchResults.Count,
                        searchResults.Count,
                        0.5,
                        searchResults.Count
                    );


                    // Build context from search results
                    var contextBuilder = new StringBuilder();
                    contextBuilder.AppendLine("RELEVANT INFORMATION FROM YOUR DATA:\n");

                    foreach (var result in searchResults)
                    {
                        var type = result.Payload["type"].StringValue;
                        var content = result.Payload["content"].StringValue;
                        var score = result.Score;

                        contextBuilder.AppendLine($"[{type.ToUpper()} - Relevance: {score:F2}]");
                        contextBuilder.AppendLine(content);
                        contextBuilder.AppendLine();
                    }

                    _logger.LogInformation("Found {Count} relevant items using RAG", searchResults.Count);

                    // STEP 2: Build messages for GPT with system prompt
                    var messages = new List<OpenAIChatMessage>
            {
                OpenAIChatMessage.FromSystem(
                    "You are a helpful AI assistant for a financial advisor. " +
                    "You help manage client relationships, answer questions about emails, meetings, and clients. " +
                    "You can also perform actions like sending emails when the user asks. " +
                    "Use the provided context to answer questions accurately. " +
                    "When the user asks you to DO something (like send an email), use the available tools. " +
                    "Be professional, concise, and helpful.\n\n" +
                    contextBuilder.ToString())
            };

                    // Add conversation history if provided
                    if (conversationHistory != null && conversationHistory.Any())
                    {
                        messages.AddRange(conversationHistory);
                    }

                    // Add current user message
                    messages.Add(OpenAIChatMessage.FromUser(userMessage));

                    // STEP 3: First call to GPT-4 with tool definitions
                    var completionResult = await _openAIClient.ChatCompletion.CreateCompletion(
                        new ChatCompletionCreateRequest
                        {
                            Messages = messages,
                            Model = OpenAI.ObjectModels.Models.Gpt_4o_mini,
                            MaxTokens = 1000,
                            Temperature = 0.7f,
                            Tools = ToolDefinitionService.GetAllTools()
                        });

                    if (!completionResult.Successful)
                    {
                        throw new Exception($"OpenAI API Error: {completionResult.Error?.Message}");
                    }

                    var choice = completionResult.Choices.First();

                    // STEP 4: Check if AI wants to use a tool
                    if (choice.Message.ToolCalls != null && choice.Message.ToolCalls.Any())
                    {
                        _logger.LogInformation("AI requested {Count} tool calls", choice.Message.ToolCalls.Count);

                        // Add assistant's message with tool calls to conversation
                        messages.Add(choice.Message);

                        // STEP 5: Execute each tool call

                        foreach (var toolCall in choice.Message.ToolCalls)
                        {
                            var functionName = toolCall.FunctionCall.Name;
                            var argumentsJson = toolCall.FunctionCall.Arguments;

                            _logger.LogInformation("Executing tool: {FunctionName} with args: {Args}",
                                functionName, argumentsJson);

                            // Execute the tool
                            var result = await _toolExecutor.ExecuteToolAsync(userId, functionName, argumentsJson);

                            _logger.LogInformation("Tool execution result: {Result}", result);

                            // Add tool result to conversation
                            messages.Add(new OpenAIChatMessage
                            {
                                Role = "tool",
                                Content = result,
                                ToolCallId = toolCall.Id
                            });
                        }

                        // STEP 6: Second call to GPT-4 to generate final response with tool results
                        var finalResult = await _openAIClient.ChatCompletion.CreateCompletion(
                            new ChatCompletionCreateRequest
                            {
                                Messages = messages,
                                Model = OpenAI.ObjectModels.Models.Gpt_4o_mini,
                                MaxTokens = 1000,
                                Temperature = 0.7f
                            });

                        if (finalResult.Successful)
                        {
                            return finalResult.Choices.First().Message.Content;
                        }
                        else
                        {
                            _logger.LogWarning("Final response generation failed: {Error}", finalResult.Error?.Message);
                            return "I completed the action, but had trouble generating a final response.";
                        }
                    }

                    // STEP 7: No tool call needed, return regular response
                    return choice.Message.Content;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in RAG with tools, falling back to keyword search");
                    // Fall back to existing method without tools
                    return await GetResponseWithContextAsync(userId, userMessage, conversationHistory);
                }
            }
            else
            {
                _logger.LogWarning("RAG not available for tool calling, using existing context method");
                return await GetResponseWithContextAsync(userId, userMessage, conversationHistory);
            }
        }


        private int DetermineSearchLimit(string query)
        {
            var lowerQuery = query.ToLower();

            // Keywords indicating comprehensive search
            if (lowerQuery.Contains("all") ||
                lowerQuery.Contains("everything") ||
                lowerQuery.Contains("complete") ||
                lowerQuery.Contains("summarize") ||
                lowerQuery.Contains("list"))
                return 50;

            // Long queries = more context needed
            if (query.Length > 150) return 30;
            if (query.Length > 100) return 20;

            return 15; // Default
        }

        // Helper: Determine minimum score
        private float DetermineMinScore(string query)
        {
            var lowerQuery = query.ToLower();

            // Specific questions need high precision
            if (lowerQuery.StartsWith("who") ||
                lowerQuery.StartsWith("what") ||
                lowerQuery.StartsWith("when"))
                return 0.75f; // Higher threshold for factual questions

            // Broad questions can accept lower scores
            if (lowerQuery.Contains("everything") ||
                lowerQuery.Contains("summarize"))
                return 0.65f;

            return 0.70f; // Default
        }

        // Helper: Diversify results
        private List<ScoredPoint> DiversifyResults(List<ScoredPoint> results, int maxResults)
        {
            if (results.Count <= maxResults)
                return results;

            var diversified = new List<ScoredPoint>();

            // Take top N from each type
            var emails = results.Where(r => r.Payload["type"].StringValue == "email").Take(maxResults / 2);
            var calendar = results.Where(r => r.Payload["type"].StringValue == "calendar_event").Take(maxResults / 4);
            var hubspot = results.Where(r => r.Payload["type"].StringValue.StartsWith("hubspot_")).Take(maxResults / 4);

            diversified.AddRange(emails);
            diversified.AddRange(calendar);
            diversified.AddRange(hubspot);

            // Fill remaining slots with highest scores regardless of type
            var remaining = maxResults - diversified.Count;
            if (remaining > 0)
            {
                var others = results
                    .Except(diversified)
                    .OrderByDescending(r => r.Score)
                    .Take(remaining);
                diversified.AddRange(others);
            }

            return diversified.OrderByDescending(r => r.Score).ToList();
        }
    }



    public class CalendarQueryParams
    {
        public bool IsCalendarQuery { get; set; }
        public QueryFilters? Filters { get; set; }
        public string? OrderBy { get; set; }
        public string? OrderDirection { get; set; }
        public int? Limit { get; set; }
    }

    public class QueryFilters
    {
        public string? TimeFilter { get; set; }
        public List<string>? SearchFields { get; set; }
        public List<string>? SearchTerms { get; set; }
        public string? SpecificDate { get; set; }
        public string? StartTimeAfter { get; set; }
        public string? StartTimeBefore { get; set; }
    }
}
