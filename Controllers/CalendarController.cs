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
        public async Task<IActionResult> SyncCalendar(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                _logger.LogInformation("Starting Calendar sync for user {UserId}", userId);

                var events = await _eventService.ListEventsAsync(user, 100);

                int newEvents = 0;
                int updatedEvents = 0;

                foreach (var evt in events)
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

                    // Store attendees as JSON
                    if (evt.Attendees != null && evt.Attendees.Any())
                    {
                        var attendeeEmails = evt.Attendees.Select(a => a.Email).ToList();
                        eventCache.Attendees = JsonSerializer.Serialize(attendeeEmails);
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
                }

                user.LastCalendarSync = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Calendar sync completed for user {UserId}. New: {New}, Updated: {Updated}",
                    userId, newEvents, updatedEvents);

                return Ok(new
                {
                    success = true,
                    message = "Calendar sync completed",
                    newEvents,
                    updatedEvents,
                    totalProcessed = events.Count,
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