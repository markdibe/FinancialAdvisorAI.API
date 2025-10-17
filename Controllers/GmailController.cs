using FinancialAdvisorAI.API.Models;
using FinancialAdvisorAI.API.Repositories;
using FinancialAdvisorAI.API.Services;
using Google.Apis.Gmail.v1;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialAdvisorAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GmailController : ControllerBase
    {
        private readonly EmailService _emailService;
        private readonly AppDbContext _context;
        private readonly ILogger<GmailController> _logger;

        public GmailController(
            EmailService emailService,
            AppDbContext context,
            ILogger<GmailController> logger)
        {
            _emailService = emailService;
            _context = context;
            _logger = logger;
        }

        [HttpPost("sync/{userId}")]
        public async Task<IActionResult> SyncEmails(int userId, [FromQuery] int maxResults = 100)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                _logger.LogInformation("Starting Gmail sync for user {UserId}", userId);

                // Get messages from Gmail
                var messages = await _emailService.ListMessagesAsync(user, maxResults);

                int newEmails = 0;
                int updatedEmails = 0;

                foreach (var message in messages)
                {
                    var messageId = message.Id;

                    // Check if email already exists
                    var existingEmail = await _context.EmailCaches
                        .FirstOrDefaultAsync(e => e.UserId == userId && e.MessageId == messageId);

                    var emailCache = existingEmail ?? new EmailCache { UserId = userId };

                    emailCache.MessageId = messageId;
                    emailCache.ThreadId = message.ThreadId;
                    emailCache.Subject = _emailService.GetMessageSubject(message);
                    emailCache.FromEmail = _emailService.GetMessageFrom(message);
                    emailCache.Body = _emailService.GetMessageBody(message);
                    emailCache.Snippet = message.Snippet;
                    emailCache.EmailDate = _emailService.GetMessageDate(message);
                    emailCache.IsRead = !message.LabelIds?.Contains("UNREAD") ?? true;
                    emailCache.IsSent = message.LabelIds?.Contains("SENT") ?? false;
                    emailCache.UpdatedAt = DateTime.UtcNow;

                    if (existingEmail == null)
                    {
                        _context.EmailCaches.Add(emailCache);
                        newEmails++;
                    }
                    else
                    {
                        updatedEmails++;
                    }
                }

                user.LastGmailSync = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Gmail sync completed for user {UserId}. New: {New}, Updated: {Updated}",
                    userId, newEmails, updatedEmails);

                return Ok(new
                {
                    success = true,
                    message = "Gmail sync completed",
                    newEmails,
                    updatedEmails,
                    totalProcessed = messages.Count,
                    lastSync = user.LastGmailSync
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing Gmail for user {UserId}", userId);
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }

        [HttpGet("emails/{userId}")]
        public async Task<IActionResult> GetEmails(
            int userId,
            [FromQuery] int limit = 50,
            [FromQuery] string? search = null)
        {
            try
            {
                var query = _context.EmailCaches
                    .Where(e => e.UserId == userId);

                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(e =>
                        e.Subject.Contains(search) ||
                        e.FromEmail!.Contains(search) ||
                        e.Body!.Contains(search));
                }

                var emails = await query
                    .OrderByDescending(e => e.EmailDate)
                    .Take(limit)
                    .Select(e => new
                    {
                        e.Id,
                        e.MessageId,
                        e.Subject,
                        e.FromEmail,
                        e.Snippet,
                        e.EmailDate,
                        e.IsRead,
                        e.IsSent
                    })
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    emails,
                    count = emails.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting emails for user {UserId}", userId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("email/{userId}/{emailId}")]
        public async Task<IActionResult> GetEmail(int userId, int emailId)
        {
            try
            {
                var email = await _context.EmailCaches
                    .FirstOrDefaultAsync(e => e.UserId == userId && e.Id == emailId);

                if (email == null)
                {
                    return NotFound(new { error = "Email not found" });
                }

                return Ok(new
                {
                    success = true,
                    email
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting email {EmailId} for user {UserId}", emailId, userId);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendEmail([FromBody] SendEmailRequest request)
        {
            try
            {
                var user = await _context.Users.FindAsync(request.UserId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                var message = await _emailService.SendMessageAsync(
                    user,
                    request.To,
                    request.Subject,
                    request.Body);

                return Ok(new
                {
                    success = true,
                    message = "Email sent successfully",
                    messageId = message.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email for user {UserId}", request.UserId);
                return StatusCode(500, new
                {
                    success = false,
                    error = ex.Message
                });
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

                var emailCount = await _context.EmailCaches.CountAsync(e => e.UserId == userId);

                return Ok(new
                {
                    success = true,
                    lastSync = user.LastGmailSync,
                    emailCount,
                    hasSynced = user.LastGmailSync.HasValue
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sync status for user {UserId}", userId);
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class SendEmailRequest
    {
        public int UserId { get; set; }
        public string To { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
    }
}