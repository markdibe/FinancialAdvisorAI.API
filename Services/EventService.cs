using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using FinancialAdvisorAI.API.Models;

// Alias to avoid confusion
using GoogleCalendarService = Google.Apis.Calendar.v3.CalendarService;

namespace FinancialAdvisorAI.API.Services
{
    public class EventService
    {
        private readonly GoogleAuthService _googleAuthService;
        private readonly ILogger<EventService> _logger;

        public EventService(
            GoogleAuthService googleAuthService,
            ILogger<EventService> logger)
        {
            _googleAuthService = googleAuthService;
            _logger = logger;
        }

        public async Task<GoogleCalendarService> GetCalendarServiceAsync(User user)
        {
            var credential = await _googleAuthService.GetUserCredentialAsync(user);

            var service = new GoogleCalendarService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Financial Advisor AI"
            });

            return service;
        }

        public async Task<List<Event>> ListEventsAsync(User user, int maxResults = 100)
        {
            try
            {
                var service = await GetCalendarServiceAsync(user);
                var request = service.Events.List("primary");

                request.TimeMinDateTimeOffset = DateTime.UtcNow.AddMonths(-6);
                request.TimeMaxDateTimeOffset = DateTime.UtcNow.AddMonths(6);
                request.MaxResults = maxResults;
                request.SingleEvents = true;
                request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

                var events = await request.ExecuteAsync();
                return events.Items?.ToList() ?? new List<Event>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing calendar events for user {UserId}", user.Id);
                throw;
            }
        }

        public string GetEventTitle(Event evt)
        {
            return evt.Summary ?? "(No Title)";
        }

        public DateTime? GetEventStartTime(Event evt)
        {
            if (evt.Start?.DateTime != null)
                return evt.Start.DateTime.Value;

            if (evt.Start?.Date != null && DateTime.TryParse(evt.Start.Date, out var date))
                return date;

            return null;
        }

        public DateTime? GetEventEndTime(Event evt)
        {
            if (evt.End?.DateTime != null)
                return evt.End.DateTime.Value;

            if (evt.End?.Date != null && DateTime.TryParse(evt.End.Date, out var date))
                return date;

            return null;
        }
    }
}