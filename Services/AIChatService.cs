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

        private async Task<List<Models.CalendarEventCache>> SearchCalendarEventsAsync(int userId, string query)
        {
            try
            {
                // Use GPT to analyze the query with schema awareness
                var searchParams = await AnalyzeCalendarQueryAsync(query);

                if (!searchParams.IsCalendarQuery)
                {
                    return new List<Models.CalendarEventCache>();
                }

                var now = DateTime.UtcNow;
                var today = now.Date;

                var eventsQuery = _context.CalendarEventCaches
                    .Where(e => e.UserId == userId);

                // Apply time filtering based on GPT's analysis
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
                            // No time filter - show all events
                            break;
                        case "upcoming":
                        default:
                            eventsQuery = eventsQuery.Where(e => e.StartTime >= now);
                            break;
                    }

                    // Apply search terms to specified fields
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

                // Apply ordering
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

                _logger.LogInformation("Found {Count} calendar events using AI-generated query parameters", events.Count);

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
                        "You are a database query analyzer. Given a user's natural language question and database schema, generate query parameters.\n\n" +
                        "DATABASE SCHEMA - CalendarEventCache:\n" +
                        "- EventId (string): Google Calendar event ID\n" +
                        "- Summary (string): Event title/name\n" +
                        "- Description (string): Event description\n" +
                        "- Location (string): Event location\n" +
                        "- StartTime (DateTime): Event start time\n" +
                        "- EndTime (DateTime): Event end time\n" +
                        "- Attendees (string): JSON array of attendee emails\n" +
                        "- IsAllDay (bool): Whether event is all-day\n\n" +
                        "INSTRUCTIONS:\n" +
                        "1. Determine if this is a calendar/meeting query\n" +
                        "2. Extract time filters (today, this_week, upcoming, past, last_week, yesterday, specific_date, etc.)\n" +
                        "3. Identify which fields to search in and what terms to search for\n" +
                        "4. Specify ordering and limits\n\n" +
                        "Respond with ONLY valid JSON in this format:\n" +
                        "{\n" +
                        "  \"isCalendarQuery\": true/false,\n" +
                        "  \"filters\": {\n" +
                        "    \"timeFilter\": \"upcoming\" | \"today\" | \"tomorrow\" | \"yesterday\" | \"this_week\" | \"last_week\" | \"next_week\" | \"past\" | \"all\",\n" +
                        "    \"searchFields\": [\"Summary\", \"Description\", \"Location\", \"Attendees\"],\n" +
                        "    \"searchTerms\": [\"term1\", \"term2\"] or null,\n" +
                        "    \"specificDate\": \"YYYY-MM-DD\" or null,\n" +
                        "    \"startTimeAfter\": \"YYYY-MM-DD\" or null,\n" +
                        "    \"startTimeBefore\": \"YYYY-MM-DD\" or null\n" +
                        "  },\n" +
                        "  \"orderBy\": \"StartTime\" | \"EndTime\",\n" +
                        "  \"orderDirection\": \"asc\" | \"desc\",\n" +
                        "  \"limit\": 10\n" +
                        "}\n\n" +
                        "EXAMPLES:\n" +
                        "Q: 'What meetings do I have coming up?'\n" +
                        "A: {\"isCalendarQuery\": true, \"filters\": {\"timeFilter\": \"upcoming\", \"searchFields\": null, \"searchTerms\": null}, \"orderBy\": \"StartTime\", \"orderDirection\": \"asc\", \"limit\": 10}\n\n" +
                        "Q: 'Do I have anything today?'\n" +
                        "A: {\"isCalendarQuery\": true, \"filters\": {\"timeFilter\": \"today\", \"searchFields\": null, \"searchTerms\": null}, \"orderBy\": \"StartTime\", \"orderDirection\": \"asc\", \"limit\": 20}\n\n" +
                        "Q: 'Show me meetings with John about budget'\n" +
                        "A: {\"isCalendarQuery\": true, \"filters\": {\"timeFilter\": \"upcoming\", \"searchFields\": [\"Summary\", \"Description\", \"Attendees\"], \"searchTerms\": [\"john\", \"budget\"]}, \"orderBy\": \"StartTime\", \"orderDirection\": \"asc\", \"limit\": 10}\n\n" +
                        "Q: 'Did I meet with Sara last week?'\n" +
                        "A: {\"isCalendarQuery\": true, \"filters\": {\"timeFilter\": \"last_week\", \"searchFields\": [\"Summary\", \"Attendees\"], \"searchTerms\": [\"sara\"]}, \"orderBy\": \"StartTime\", \"orderDirection\": \"desc\", \"limit\": 10}\n\n" +
                        "Q: 'When was my last meeting with the client?'\n" +
                        "A: {\"isCalendarQuery\": true, \"filters\": {\"timeFilter\": \"past\", \"searchFields\": [\"Summary\", \"Description\"], \"searchTerms\": [\"client\"]}, \"orderBy\": \"StartTime\", \"orderDirection\": \"desc\", \"limit\": 5}\n\n" +
                        "Q: 'What emails did I get?'\n" +
                        "A: {\"isCalendarQuery\": false}"),
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
                    _logger.LogInformation("Calendar query analysis: {Response}", response);

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