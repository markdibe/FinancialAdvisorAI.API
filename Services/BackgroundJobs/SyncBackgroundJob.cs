using Hangfire;
using Microsoft.EntityFrameworkCore;
using FinancialAdvisorAI.API.Repositories;
using FinancialAdvisorAI.API.Models;

namespace FinancialAdvisorAI.API.Services.BackgroundJobs
{
    public class SyncBackgroundJob
    {
        private readonly AppDbContext _context;
        private readonly EmailService _emailService;
        private readonly EventService _eventService;
        private readonly HubSpotService _hubspotService;
        private readonly VectorSyncService _vectorSyncService;
        private readonly ILogger<SyncBackgroundJob> _logger;
        private readonly IConfiguration _configuration;

        public SyncBackgroundJob(
            AppDbContext context,
            EmailService emailService,
            EventService eventService,
            HubSpotService hubspotService,
            VectorSyncService vectorSyncService,
            IConfiguration configuration,
            ILogger<SyncBackgroundJob> logger)
        {
            _context = context;
            _emailService = emailService;
            _eventService = eventService;
            _hubspotService = hubspotService;
            _vectorSyncService = vectorSyncService;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Main incremental sync job - runs for all users
        /// </summary>
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 600 })]
        public async Task IncrementalSyncAllUsersAsync()
        {
            _logger.LogInformation("Starting incremental sync for all users");

            var users = await _context.Users
                .Where(u => !string.IsNullOrEmpty(u.GoogleAccessToken))
                .ToListAsync();

            _logger.LogInformation("Found {Count} users to sync", users.Count);

            foreach (var user in users)
            {
                try
                {
                    // Enqueue individual user sync (prevents one user from blocking others)
                    BackgroundJob.Enqueue(() => IncrementalSyncUserAsync(user.Id));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enqueueing sync for user {UserId}", user.Id);
                }
            }
        }

        /// <summary>
        /// Incremental sync for a single user
        /// </summary>
        [AutomaticRetry(Attempts = 3)]
        public async Task IncrementalSyncUserAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                return;
            }

            _logger.LogInformation("Starting incremental sync for user {UserId} ({Email})",
                userId, user.Email);

            try
            {
                // 1. Sync Gmail
                await SyncGmailAsync(userId, incremental: true);

                // Small delay between syncs
                await Task.Delay(2000);

                // 2. Sync Calendar
                await SyncCalendarAsync(userId, incremental: true);

                await Task.Delay(2000);

                // 3. Sync HubSpot (if connected)
                if (!string.IsNullOrEmpty(user.HubspotAccessToken))
                {
                    await SyncHubSpotAsync(userId);
                }

                await Task.Delay(2000);

                // 4. Sync Vectors (only new items)
                await SyncVectorsAsync(userId);

                _logger.LogInformation("Completed incremental sync for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in incremental sync for user {UserId}", userId);
                throw; // Re-throw to trigger Hangfire retry
            }
        }

        /// <summary>
        /// Full sync for a single user (all data)
        /// </summary>
        [AutomaticRetry(Attempts = 2)]
        public async Task FullSyncUserAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found", userId);
                return;
            }

            _logger.LogInformation("Starting FULL sync for user {UserId} ({Email})",
                userId, user.Email);

            var syncLog = new SyncLog
            {
                UserId = userId,
                SyncType = "Full",
                Status = "Running",
                StartedAt = DateTime.UtcNow
            };
            _context.SyncLogs.Add(syncLog);
            await _context.SaveChangesAsync();

            try
            {
                // 1. Full Gmail sync
                await SyncGmailAsync(userId, incremental: false);
                await Task.Delay(5000);

                // 2. Full Calendar sync
                await SyncCalendarAsync(userId, incremental: false);
                await Task.Delay(5000);

                // 3. Full HubSpot sync
                if (!string.IsNullOrEmpty(user.HubspotAccessToken))
                {
                    await SyncHubSpotAsync(userId);
                }
                await Task.Delay(5000);

                // 4. Full Vector sync
                await SyncVectorsAsync(userId);

                // Mark as success
                syncLog.Status = "Success";
                syncLog.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Completed FULL sync for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in full sync for user {UserId}", userId);

                syncLog.Status = "Failed";
                syncLog.ErrorMessage = ex.Message;
                syncLog.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                throw;
            }
        }

        /// <summary>
        /// Full sync for all users (runs daily)
        /// </summary>
        public async Task FullSyncAllUsersAsync()
        {
            _logger.LogInformation("Starting daily full sync for all users");

            var users = await _context.Users
                .Where(u => !string.IsNullOrEmpty(u.GoogleAccessToken))
                .ToListAsync();

            _logger.LogInformation("Found {Count} users for full sync", users.Count);

            foreach (var user in users)
            {
                try
                {
                    // Enqueue full sync with delay to avoid overwhelming APIs
                    BackgroundJob.Schedule(() => FullSyncUserAsync(user.Id), TimeSpan.FromMinutes(5 * user.Id));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enqueueing full sync for user {UserId}", user.Id);
                }
            }
        }

        /// <summary>
        /// Sync Gmail emails
        /// </summary>
        private async Task SyncGmailAsync(int userId, bool incremental)
        {
            var context = CreateDbContextScope();

            var syncLog = new SyncLog
            {
                UserId = userId,
                SyncType = "Gmail",
                Status = "Running",
                StartedAt = DateTime.UtcNow
            };
            context.SyncLogs.Add(syncLog);
            await context.SaveChangesAsync();

            try
            {
                var user = await context.Users.FindAsync(userId);
                if (user == null) throw new Exception("User not found");

                _logger.LogInformation("Syncing Gmail for user {UserId} (Incremental: {Inc})",
                    userId, incremental);

                // Determine since date
                DateTime? since = incremental
                    ? user.LastGmailSync?.AddMinutes(-5) // 5 min overlap for safety
                    : null;

                var progress = new Progress<SyncProgress>(p =>
                {
                    _logger.LogInformation("Gmail sync progress: {Status}", p.Status);
                });

                // ✅ Use new incremental sync method
                var (newEmails, updatedEmails, totalProcessed) =
                    await _emailService.SyncAllMessagesIncrementallyAsync(
                        user,
                        query: null,
                        since: since,
                        progress: progress);

                user.LastGmailSync = DateTime.UtcNow;
                await context.SaveChangesAsync();

                // Update sync log
                syncLog.Status = "Success";
                syncLog.ItemsProcessed = totalProcessed;
                syncLog.ItemsAdded = newEmails;
                syncLog.ItemsUpdated = updatedEmails;
                syncLog.CompletedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();

                _logger.LogInformation("Gmail sync completed: {New} new, {Updated} updated, {Total} total",
                    newEmails, updatedEmails, totalProcessed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gmail sync failed for user {UserId}", userId);

                syncLog.Status = "Failed";
                syncLog.ErrorMessage = ex.Message;
                syncLog.CompletedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();

                throw;
            }
            finally
            {
                context.Dispose();
            }
        }

        private AppDbContext CreateDbContextScope()
        {
            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            var connectionString = _configuration.GetConnectionString("DefaultConnection");

            optionsBuilder.UseSqlite(connectionString);

            return new AppDbContext(optionsBuilder.Options);

        }

        /// <summary>
        /// Sync Calendar events
        /// </summary>
        private async Task SyncCalendarAsync(int userId, bool incremental)
        {
            var syncLog = new SyncLog
            {
                UserId = userId,
                SyncType = "Calendar",
                Status = "Running",
                StartedAt = DateTime.UtcNow
            };
            _context.SyncLogs.Add(syncLog);
            await _context.SaveChangesAsync();

            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null) throw new Exception("User not found");

                _logger.LogInformation("Syncing Calendar for user {UserId} (Incremental: {Inc})",
                    userId, incremental);

                DateTime minTime = incremental
                    ? (user.LastCalendarSync?.AddDays(-1) ?? DateTime.UtcNow.AddMonths(-6))
                    : DateTime.UtcNow.AddYears(-1);

                DateTime maxTime = incremental
                    ? DateTime.UtcNow.AddYears(1)
                    : DateTime.UtcNow.AddYears(2);

                var allEvents = new List<Google.Apis.Calendar.v3.Data.Event>();
                var service = await _eventService.GetCalendarServiceAsync(user);
                string? pageToken = null;

                do
                {
                    var request = service.Events.List("primary");
                    request.TimeMinDateTimeOffset = minTime;
                    request.TimeMaxDateTimeOffset = maxTime;
                    request.MaxResults = 250;
                    request.SingleEvents = true;
                    request.OrderBy = Google.Apis.Calendar.v3.EventsResource.ListRequest.OrderByEnum.StartTime;
                    request.PageToken = pageToken;

                    var events = await request.ExecuteAsync();
                    if (events.Items != null && events.Items.Any())
                    {
                        allEvents.AddRange(events.Items);
                    }

                    pageToken = events.NextPageToken;

                } while (!string.IsNullOrEmpty(pageToken));

                int newEvents = 0;
                int updatedEvents = 0;

                foreach (var evt in allEvents)
                {
                    var eventId = evt.Id;
                    var existingEvent = await _context.CalendarEventCaches
                        .FirstOrDefaultAsync(e => e.UserId == userId && e.EventId == eventId);

                    var eventCache = existingEvent ?? new CalendarEventCache { UserId = userId };

                    eventCache.EventId = eventId;
                    eventCache.Summary = _eventService.GetEventTitle(evt);
                    eventCache.Description = evt.Description;
                    eventCache.Location = evt.Location;
                    eventCache.StartTime = _eventService.GetEventStartTime(evt);
                    eventCache.EndTime = _eventService.GetEventEndTime(evt);
                    eventCache.IsAllDay = evt.Start?.Date != null;

                    if (evt.Attendees != null && evt.Attendees.Any())
                    {
                        var attendeeEmails = evt.Attendees.Select(a => a.Email).ToList();
                        eventCache.Attendees = System.Text.Json.JsonSerializer.Serialize(attendeeEmails);
                    }

                    eventCache.UpdatedAt = DateTime.UtcNow;

                    if (existingEvent == null)
                    {
                        _context.CalendarEventCaches.Add(eventCache);
                        newEvents++;
                    }
                    else
                    {
                        updatedEvents++;
                    }

                    if ((newEvents + updatedEvents) % 50 == 0)
                    {
                        await _context.SaveChangesAsync();
                    }
                }

                user.LastCalendarSync = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                syncLog.Status = "Success";
                syncLog.ItemsProcessed = allEvents.Count;
                syncLog.ItemsAdded = newEvents;
                syncLog.ItemsUpdated = updatedEvents;
                syncLog.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Calendar sync completed: {New} new, {Updated} updated",
                    newEvents, updatedEvents);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Calendar sync failed for user {UserId}", userId);

                syncLog.Status = "Failed";
                syncLog.ErrorMessage = ex.Message;
                syncLog.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                throw;
            }
        }

        /// <summary>
        /// Sync HubSpot data
        /// </summary>
        private async Task SyncHubSpotAsync(int userId)
        {
            var syncLog = new SyncLog
            {
                UserId = userId,
                SyncType = "HubSpot",
                Status = "Running",
                StartedAt = DateTime.UtcNow
            };
            _context.SyncLogs.Add(syncLog);
            await _context.SaveChangesAsync();

            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null) throw new Exception("User not found");

                _logger.LogInformation("Syncing HubSpot for user {UserId}", userId);

                var contactCountBefore = await _context.HubSpotContacts.CountAsync(c => c.UserId == userId);
                var companyCountBefore = await _context.HubSpotCompanies.CountAsync(c => c.UserId == userId);
                var dealCountBefore = await _context.HubSpotDeals.CountAsync(d => d.UserId == userId);

                await _hubspotService.SyncContactsAsync(user);
                await _hubspotService.SyncCompaniesAsync(user);
                await _hubspotService.SyncDealsAsync(user);

                var contactCountAfter = await _context.HubSpotContacts.CountAsync(c => c.UserId == userId);
                var companyCountAfter = await _context.HubSpotCompanies.CountAsync(c => c.UserId == userId);
                var dealCountAfter = await _context.HubSpotDeals.CountAsync(d => d.UserId == userId);

                var totalAdded = (contactCountAfter - contactCountBefore) +
                                 (companyCountAfter - companyCountBefore) +
                                 (dealCountAfter - dealCountBefore);

                syncLog.Status = "Success";
                syncLog.ItemsProcessed = contactCountAfter + companyCountAfter + dealCountAfter;
                syncLog.ItemsAdded = totalAdded;
                syncLog.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("HubSpot sync completed: {Contacts} contacts, {Companies} companies, {Deals} deals",
                    contactCountAfter, companyCountAfter, dealCountAfter);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HubSpot sync failed for user {UserId}", userId);

                syncLog.Status = "Failed";
                syncLog.ErrorMessage = ex.Message;
                syncLog.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Don't re-throw - HubSpot failure shouldn't stop other syncs
            }
        }

        /// <summary>
        /// Sync vectors to Qdrant (only new items)
        /// </summary>
        private async Task SyncVectorsAsync(int userId)
        {
            var syncLog = new SyncLog
            {
                UserId = userId,
                SyncType = "Vector",
                Status = "Running",
                StartedAt = DateTime.UtcNow
            };
            _context.SyncLogs.Add(syncLog);
            await _context.SaveChangesAsync();

            try
            {
                _logger.LogInformation("Syncing vectors for user {UserId}", userId);

                // This will only sync new items (VectorSyncService checks what's already in Qdrant)
                await _vectorSyncService.SyncAllDataForUserAsync(userId);

                syncLog.Status = "Success";
                syncLog.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Vector sync completed for user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Vector sync failed for user {UserId}", userId);

                syncLog.Status = "Failed";
                syncLog.ErrorMessage = ex.Message;
                syncLog.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Don't re-throw - vector failure shouldn't stop other syncs
            }
        }

    }
}