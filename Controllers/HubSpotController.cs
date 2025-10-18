using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FinancialAdvisorAI.API.Services;
using FinancialAdvisorAI.API.Repositories;

namespace FinancialAdvisorAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HubSpotController : ControllerBase
    {
        private readonly HubSpotService _hubspotService;
        private readonly AppDbContext _context;
        private readonly ILogger<HubSpotController> _logger;

        public HubSpotController(
            HubSpotService hubspotService,
            AppDbContext context,
            ILogger<HubSpotController> logger)
        {
            _hubspotService = hubspotService;
            _context = context;
            _logger = logger;
        }

        [HttpGet("connect/{userId}")]
        public IActionResult Connect(int userId)
        {
            try
            {
                var state = Guid.NewGuid().ToString();
                var authUrl = _hubspotService.GetAuthorizationUrl(state);

                return Ok(new { authUrl, state });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating HubSpot connection");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("callback")]
        public async Task<IActionResult> Callback([FromBody] HubSpotCallbackRequest request)
        {
            try
            {
                var user = await _context.Users.FindAsync(request.UserId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                await _hubspotService.ExchangeCodeForTokenAsync(request.Code, user);

                try
                {
                    await _hubspotService.SyncContactsAsync(user);
                    await _hubspotService.SyncCompaniesAsync(user);
                    await _hubspotService.SyncDealsAsync(user);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during initial HubSpot sync");
                }


                return Ok(new
                {
                    success = true,
                    message = "HubSpot connected successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in HubSpot callback");
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpPost("sync/{userId}")]
        public async Task<IActionResult> Sync(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }
                await _hubspotService.SyncContactsAsync(user);
                await _hubspotService.SyncCompaniesAsync(user);
                await _hubspotService.SyncDealsAsync(user);

                return Ok(new
                {
                    success = true,
                    message = "Sync started"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting HubSpot sync");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("status/{userId}")]
        public async Task<IActionResult> GetStatus(int userId)
        {
            try
            {
                var isConnected = await _hubspotService.IsConnectedAsync(userId);

                return Ok(new
                {
                    success = true,
                    connected = isConnected
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking HubSpot status");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("contacts/{userId}")]
        public async Task<IActionResult> GetContacts(int userId, [FromQuery] int limit = 50)
        {
            try
            {
                var contacts = await _context.HubSpotContacts
                    .Where(c => c.UserId == userId)
                    .OrderByDescending(c => c.LastModifiedDate)
                    .Take(limit)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    contacts,
                    count = contacts.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting HubSpot contacts");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("companies/{userId}")]
        public async Task<IActionResult> GetCompanies(int userId, [FromQuery] int limit = 50)
        {
            try
            {
                var companies = await _context.HubSpotCompanies
                    .Where(c => c.UserId == userId)
                    .OrderByDescending(c => c.LastModifiedDate)
                    .Take(limit)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    companies,
                    count = companies.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting HubSpot companies");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("deals/{userId}")]
        public async Task<IActionResult> GetDeals(int userId, [FromQuery] int limit = 50)
        {
            try
            {
                var deals = await _context.HubSpotDeals
                    .Where(d => d.UserId == userId)
                    .OrderByDescending(d => d.LastModifiedDate)
                    .Take(limit)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    deals,
                    count = deals.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting HubSpot deals");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("sync-status/{userId}")]
        public async Task<IActionResult> GetSyncStatus(int userId)
        {
            try
            {
                var contactCount = await _context.HubSpotContacts.CountAsync(c => c.UserId == userId);
                var companyCount = await _context.HubSpotCompanies.CountAsync(c => c.UserId == userId);
                var dealCount = await _context.HubSpotDeals.CountAsync(d => d.UserId == userId);

                return Ok(new
                {
                    success = true,
                    contactCount,
                    companyCount,
                    dealCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting HubSpot sync status");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class HubSpotCallbackRequest
    {
        public int UserId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }
}