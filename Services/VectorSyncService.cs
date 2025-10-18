using FinancialAdvisorAI.API.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FinancialAdvisorAI.API.Services
{
    public class VectorSyncService
    {
        private readonly AppDbContext _context;
        private readonly EmbeddingService _embeddingService;
        private readonly QdrantService _qdrantService;
        private readonly ILogger<VectorSyncService> _logger;

        public VectorSyncService(
            AppDbContext context,
            EmbeddingService embeddingService,
            QdrantService qdrantService,
            ILogger<VectorSyncService> logger)
        {
            _context = context;
            _embeddingService = embeddingService;
            _qdrantService = qdrantService;
            _logger = logger;
        }

        public async Task SyncAllDataForUserAsync(int userId)
        {
            _logger.LogInformation("Starting vector sync for user {UserId}", userId);

            await SyncEmailsAsync(userId);
            await SyncCalendarEventsAsync(userId);
            await SyncHubSpotContactsAsync(userId);
            await SyncHubSpotCompaniesAsync(userId);
            await SyncHubSpotDealsAsync(userId);

            _logger.LogInformation("Completed vector sync for user {UserId}", userId);
        }

        public async Task SyncEmailsAsync(int userId)
        {
            var emails = await _context.EmailCaches
                .Where(e => e.UserId == userId)
                .ToListAsync();

            _logger.LogInformation("Syncing {Count} emails to vector database", emails.Count);

            var points = new List<(string id, float[] vector, Dictionary<string, object> payload)>();

            foreach (var email in emails)
            {
                try
                {
                    // Create searchable text from email
                    var text = $"Subject: {email.Subject}\n" +
                              $"From: {email.FromEmail}\n" +
                              $"Body: {email.Body ?? email.Snippet}\n" +
                              $"Date: {email.EmailDate}";

                    var embedding = await _embeddingService.GenerateEmbeddingAsync(text);

                    var payload = new Dictionary<string, object>
                    {
                        { "type", "email" },
                        { "user_id", userId },
                        { "email_id", email.Id },
                        { "subject", email.Subject ?? "" },
                        { "from", email.FromEmail ?? "" },
                        { "date", email.EmailDate?.ToString("o") ?? "" },
                        { "content", text }
                    };

                    points.Add(($"email_{userId}_{email.Id}", embedding, payload));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing email {EmailId}", email.Id);
                }
            }

            if (points.Any())
            {
                await _qdrantService.UpsertPointsAsync(points);
                _logger.LogInformation("Synced {Count} email vectors", points.Count);
            }
        }

        public async Task SyncCalendarEventsAsync(int userId)
        {
            var events = await _context.CalendarEventCaches
                .Where(e => e.UserId == userId)
                .ToListAsync();

            _logger.LogInformation("Syncing {Count} calendar events to vector database", events.Count);

            var points = new List<(string id, float[] vector, Dictionary<string, object> payload)>();

            foreach (var evt in events)
            {
                try
                {
                    var text = $"Event: {evt.Summary}\n" +
                              $"Description: {evt.Description}\n" +
                              $"Location: {evt.Location}\n" +
                              $"Start: {evt.StartTime}\n" +
                              $"Attendees: {evt.Attendees}";

                    var embedding = await _embeddingService.GenerateEmbeddingAsync(text);

                    var payload = new Dictionary<string, object>
                    {
                        { "type", "calendar_event" },
                        { "user_id", userId },
                        { "event_id", evt.Id },
                        { "summary", evt.Summary ?? "" },
                        { "start_time", evt.StartTime?.ToString("o") ?? "" },
                        { "content", text }
                    };

                    points.Add(($"calendar_{userId}_{evt.Id}", embedding, payload));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing calendar event {EventId}", evt.Id);
                }
            }

            if (points.Any())
            {
                await _qdrantService.UpsertPointsAsync(points);
                _logger.LogInformation("Synced {Count} calendar event vectors", points.Count);
            }
        }

        public async Task SyncHubSpotContactsAsync(int userId)
        {
            var contacts = await _context.HubSpotContacts
                .Where(c => c.UserId == userId)
                .ToListAsync();

            _logger.LogInformation("Syncing {Count} HubSpot contacts to vector database", contacts.Count);

            var points = new List<(string id, float[] vector, Dictionary<string, object> payload)>();

            foreach (var contact in contacts)
            {
                try
                {
                    var text = $"Contact: {contact.FirstName} {contact.LastName}\n" +
                              $"Email: {contact.Email}\n" +
                              $"Phone: {contact.Phone}\n" +
                              $"Company: {contact.Company}\n" +
                              $"Job Title: {contact.JobTitle}\n" +
                              $"Lifecycle Stage: {contact.LifecycleStage}";

                    var embedding = await _embeddingService.GenerateEmbeddingAsync(text);

                    var payload = new Dictionary<string, object>
                    {
                        { "type", "hubspot_contact" },
                        { "user_id", userId },
                        { "contact_id", contact.Id },
                        { "name", $"{contact.FirstName} {contact.LastName}" },
                        { "email", contact.Email ?? "" },
                        { "company", contact.Company ?? "" },
                        { "content", text }
                    };

                    points.Add(($"hubspot_contact_{userId}_{contact.Id}", embedding, payload));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing HubSpot contact {ContactId}", contact.Id);
                }
            }

            if (points.Any())
            {
                await _qdrantService.UpsertPointsAsync(points);
                _logger.LogInformation("Synced {Count} HubSpot contact vectors", points.Count);
            }
        }

        public async Task SyncHubSpotCompaniesAsync(int userId)
        {
            var companies = await _context.HubSpotCompanies
                .Where(c => c.UserId == userId)
                .ToListAsync();

            _logger.LogInformation("Syncing {Count} HubSpot companies to vector database", companies.Count);

            var points = new List<(string id, float[] vector, Dictionary<string, object> payload)>();

            foreach (var company in companies)
            {
                try
                {
                    var text = $"Company: {company.Name}\n" +
                              $"Domain: {company.Domain}\n" +
                              $"Industry: {company.Industry}\n" +
                              $"Location: {company.City}, {company.State}, {company.Country}\n" +
                              $"Employees: {company.NumberOfEmployees}\n" +
                              $"Revenue: {company.AnnualRevenue}";

                    var embedding = await _embeddingService.GenerateEmbeddingAsync(text);

                    var payload = new Dictionary<string, object>
                    {
                        { "type", "hubspot_company" },
                        { "user_id", userId },
                        { "company_id", company.Id },
                        { "name", company.Name ?? "" },
                        { "industry", company.Industry ?? "" },
                        { "content", text }
                    };

                    points.Add(($"hubspot_company_{userId}_{company.Id}", embedding, payload));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing HubSpot company {CompanyId}", company.Id);
                }
            }

            if (points.Any())
            {
                await _qdrantService.UpsertPointsAsync(points);
                _logger.LogInformation("Synced {Count} HubSpot company vectors", points.Count);
            }
        }

        public async Task SyncHubSpotDealsAsync(int userId)
        {
            var deals = await _context.HubSpotDeals
                .Where(d => d.UserId == userId)
                .ToListAsync();

            _logger.LogInformation("Syncing {Count} HubSpot deals to vector database", deals.Count);

            var points = new List<(string id, float[] vector, Dictionary<string, object> payload)>();

            foreach (var deal in deals)
            {
                try
                {
                    var text = $"Deal: {deal.DealName}\n" +
                              $"Stage: {deal.DealStage}\n" +
                              $"Pipeline: {deal.Pipeline}\n" +
                              $"Amount: {deal.Amount}\n" +
                              $"Close Date: {deal.CloseDate}\n" +
                              $"Priority: {deal.Priority}";

                    var embedding = await _embeddingService.GenerateEmbeddingAsync(text);

                    var payload = new Dictionary<string, object>
                    {
                        { "type", "hubspot_deal" },
                        { "user_id", userId },
                        { "deal_id", deal.Id },
                        { "name", deal.DealName ?? "" },
                        { "stage", deal.DealStage ?? "" },
                        { "amount", deal.Amount?.ToString() ?? "0" },
                        { "content", text }
                    };

                    points.Add(($"hubspot_deal_{userId}_{deal.Id}", embedding, payload));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing HubSpot deal {DealId}", deal.Id);
                }
            }

            if (points.Any())
            {
                await _qdrantService.UpsertPointsAsync(points);
                _logger.LogInformation("Synced {Count} HubSpot deal vectors", points.Count);
            }
        }
    }
}