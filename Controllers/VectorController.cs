using Microsoft.AspNetCore.Mvc;
using FinancialAdvisorAI.API.Services;
using FinancialAdvisorAI.API.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FinancialAdvisorAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VectorController : ControllerBase
    {
        private readonly VectorSyncService _vectorSyncService;
        private readonly AppDbContext _context;
        private readonly ILogger<VectorController> _logger;

        public VectorController(
            VectorSyncService vectorSyncService,
            AppDbContext context,
            ILogger<VectorController> logger)
        {
            _vectorSyncService = vectorSyncService;
            _context = context;
            _logger = logger;
        }

        [HttpPost("sync/{userId}")]
        public async Task<IActionResult> SyncAllData(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                _logger.LogInformation("Starting vector sync for user {UserId}", userId);

                // Run sync in background (for large datasets)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _vectorSyncService.SyncAllDataForUserAsync(userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during vector sync for user {UserId}", userId);
                    }
                });

                return Ok(new
                {
                    success = true,
                    message = "Vector sync started in background"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting vector sync");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("sync/emails/{userId}")]
        public async Task<IActionResult> SyncEmails(int userId)
        {
            try
            {
                await _vectorSyncService.SyncEmailsAsync(userId);
                return Ok(new { success = true, message = "Emails synced to vector database" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing emails");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("sync/calendar/{userId}")]
        public async Task<IActionResult> SyncCalendar(int userId)
        {
            try
            {
                await _vectorSyncService.SyncCalendarEventsAsync(userId);
                return Ok(new { success = true, message = "Calendar events synced to vector database" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing calendar");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("sync/hubspot/{userId}")]
        public async Task<IActionResult> SyncHubSpot(int userId)
        {
            try
            {
                await _vectorSyncService.SyncHubSpotContactsAsync(userId);
                await _vectorSyncService.SyncHubSpotCompaniesAsync(userId);
                await _vectorSyncService.SyncHubSpotDealsAsync(userId);

                return Ok(new { success = true, message = "HubSpot data synced to vector database" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing HubSpot");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("status/{userId}")]
        public async Task<IActionResult> GetSyncStatus(int userId)
        {
            try
            {
                var emailCount = await _context.EmailCaches.CountAsync(e => e.UserId == userId);
                var calendarCount = await _context.CalendarEventCaches.CountAsync(e => e.UserId == userId);
                var contactCount = await _context.HubSpotContacts.CountAsync(c => c.UserId == userId);
                var companyCount = await _context.HubSpotCompanies.CountAsync(c => c.UserId == userId);
                var dealCount = await _context.HubSpotDeals.CountAsync(d => d.UserId == userId);

                return Ok(new
                {
                    success = true,
                    emailCount,
                    calendarCount,
                    contactCount,
                    companyCount,
                    dealCount,
                    totalItems = emailCount + calendarCount + contactCount + companyCount + dealCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sync status");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}