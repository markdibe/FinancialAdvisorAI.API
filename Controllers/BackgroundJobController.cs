using FinancialAdvisorAI.API.Repositories;
using FinancialAdvisorAI.API.Services.BackgroundJobs;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialAdvisorAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BackgroundJobController : ControllerBase
    {
        private readonly ILogger<BackgroundJobController> _logger;

        public BackgroundJobController(ILogger<BackgroundJobController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Trigger incremental sync for a specific user
        /// </summary>
        [HttpPost("sync/incremental/{userId}")]
        public IActionResult TriggerIncrementalSync(int userId)
        {
            var jobId = BackgroundJob.Enqueue<SyncBackgroundJob>(
                job => job.IncrementalSyncUserAsync(userId));

            _logger.LogInformation("Enqueued incremental sync for user {UserId}, JobId: {JobId}",
                userId, jobId);

            return Ok(new
            {
                success = true,
                message = "Incremental sync job enqueued",
                jobId
            });
        }

        /// <summary>
        /// Trigger full sync for a specific user
        /// </summary>
        [HttpPost("sync/full/{userId}")]
        public IActionResult TriggerFullSync(int userId)
        {
            var jobId = BackgroundJob.Enqueue<SyncBackgroundJob>(
                job => job.FullSyncUserAsync(userId));

            _logger.LogInformation("Enqueued full sync for user {UserId}, JobId: {JobId}",
                userId, jobId);

            return Ok(new
            {
                success = true,
                message = "Full sync job enqueued",
                jobId
            });
        }

        /// <summary>
        /// Trigger sync for all users immediately
        /// </summary>
        [HttpPost("sync/all")]
        public IActionResult TriggerSyncAll()
        {
            var jobId = BackgroundJob.Enqueue<SyncBackgroundJob>(
                job => job.IncrementalSyncAllUsersAsync());

            _logger.LogInformation("Enqueued sync for all users, JobId: {JobId}", jobId);

            return Ok(new
            {
                success = true,
                message = "Sync all users job enqueued",
                jobId
            });
        }

        /// <summary>
        /// Get sync logs for a user
        /// </summary>
        [HttpGet("logs/{userId}")]
        public async Task<IActionResult> GetSyncLogs(
            int userId,
            [FromServices] AppDbContext context,
            [FromQuery] int limit = 20)
        {
            var logs = await context.SyncLogs
                .Where(l => l.UserId == userId)
                .OrderByDescending(l => l.StartedAt)
                .Take(limit)
                .ToListAsync();

            return Ok(new
            {
                success = true,
                logs,
                count = logs.Count
            });
        }
    }
}