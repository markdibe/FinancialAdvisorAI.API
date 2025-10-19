using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinancialAdvisorAI.API.Services;
using FinancialAdvisorAI.API.Repositories;
using FinancialAdvisorAI.API.Models;
using System.Text.Json;

namespace FinancialAdvisorAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CalendarController : ControllerBase
    {
        private readonly EventService _eventService;
        private readonly AppDbContext _context;
        private readonly ILogger<CalendarController> _logger;

        public CalendarController(
            EventService eventService,
            AppDbContext context,
            ILogger<CalendarController> logger)
        {
            _eventService = eventService;
            _context = context;
            _logger = logger;
        }

        [HttpPost("sync/{userId}")]
        public async Task<IActionResult> SyncCalendar(
     int userId,
     [FromQuery] bool fullSync = false)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                _logger.LogInformation("Starting Calendar sync for user {UserId}", userId);

                // Determine date range
                DateTime minTime;
                DateTime maxTime;

                if (fullSync)
                {
                    // Full sync: past year to 2 years future
                    minTime = DateTime.UtcNow.AddYears(-1);
                    maxTime = DateTime.UtcNow.AddYears(2);
                    _logger.LogInformation("Full sync: {Min} to {Max}", minTime, maxTime);
                }
                else
                {
                    // Incremental: last sync to 1 year future
                    minTime = user.LastCalendarSync?.AddDays(-1) ?? DateTime.UtcNow.AddMonths(-6);
                    maxTime = DateTime.UtcNow.AddYears(1);
                    _logger.LogInformation("Incremental sync: {Min} to {Max}", minTime, maxTime);
                }

                var allEvents = new List<Google.Apis.Calendar.v3.Data.Event>();
                var service = await _eventService.GetCalendarServiceAsync(user);
                string? pageToken = null;
                var pageCount = 0;

                // Paginate through all events
                do
                {
                    pageCount++;
                    var request = service.Events.List("primary");
                    request.TimeMinDateTimeOffset = minTime;
                    request.TimeMaxDateTimeOffset = maxTime;
                    request.MaxResults = 250; // Max allowed
                    request.SingleEvents = true;
                    request.OrderBy = Google.Apis.Calendar.v3.EventsResource.ListRequest.OrderByEnum.StartTime;
                    request.PageToken = pageToken;

                    var events = await request.ExecuteAsync();

                    if (events.Items != null && events.Items.Any())
                    {
                        allEvents.AddRange(events.Items);
                        _logger.LogInformation("Page {Page}: Found {Count} events",
                            pageCount, events.Items.Count);
                    }

                    pageToken = events.NextPageToken;

                } while (!string.IsNullOrEmpty(pageToken));

                _logger.LogInformation("Fetched {Count} events across {Pages} pages",
                    allEvents.Count, pageCount);

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

                    // Save in batches
                    if ((newEvents + updatedEvents) % 50 == 0)
                    {
                        await _context.SaveChangesAsync();
                    }
                }

                user.LastCalendarSync = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Calendar sync completed. New: {New}, Updated: {Updated}",
                    newEvents, updatedEvents);

                return Ok(new
                {
                    success = true,
                    message = "Calendar sync completed",
                    newEvents,
                    updatedEvents,
                    totalProcessed = allEvents.Count,
                    lastSync = user.LastCalendarSync
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing calendar for user {UserId}", userId);
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("events/{userId}")]
        public async Task<IActionResult> GetEvents(int userId, [FromQuery] int limit = 20)
        {
            try
            {
                var now = DateTime.UtcNow;
                var events = await _context.CalendarEventCaches
                    .Where(e => e.UserId == userId && e.StartTime >= now)
                    .OrderBy(e => e.StartTime)
                    .Take(limit)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    events,
                    count = events.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting events for user {UserId}", userId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("sync-status/{userId}")]
        public async Task<IActionResult> GetSyncStatus(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                var eventCount = await _context.CalendarEventCaches.CountAsync(e => e.UserId == userId);
                var upcomingCount = await _context.CalendarEventCaches
                    .CountAsync(e => e.UserId == userId && e.StartTime >= DateTime.UtcNow);

                return Ok(new
                {
                    success = true,
                    lastSync = user.LastCalendarSync,
                    eventCount,
                    upcomingCount,
                    hasSynced = user.LastCalendarSync.HasValue
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting calendar sync status for user {UserId}", userId);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}