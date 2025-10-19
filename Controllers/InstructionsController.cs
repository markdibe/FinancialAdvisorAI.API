using FinancialAdvisorAI.API.Models;
using FinancialAdvisorAI.API.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinancialAdvisorAI.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InstructionsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<InstructionsController> _logger;

        public InstructionsController(
            AppDbContext context,
            ILogger<InstructionsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get all instructions for a user
        /// </summary>
        [HttpGet("user/{userId}")]
        public async Task<IActionResult> GetInstructions(int userId)
        {
            try
            {
                var instructions = await _context.OngoingInstructions
                    .Where(i => i.UserId == userId)
                    .OrderByDescending(i => i.Priority)
                    .ThenByDescending(i => i.CreatedAt)
                    .ToListAsync();

                return Ok(new
                {
                    success = true,
                    instructions,
                    count = instructions.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting instructions for user {UserId}", userId);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Create a new instruction
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateInstruction([FromBody] CreateInstructionRequest request)
        {
            try
            {
                var user = await _context.Users.FindAsync(request.UserId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                var instruction = new OngoingInstruction
                {
                    UserId = request.UserId,
                    InstructionText = request.InstructionText,
                    TriggerType = request.TriggerType ?? "All",
                    Priority = request.Priority,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.OngoingInstructions.Add(instruction);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Created instruction {Id} for user {UserId}",
                    instruction.Id, request.UserId);

                return Ok(new
                {
                    success = true,
                    message = "Instruction created successfully",
                    instruction
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating instruction");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Update an instruction
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateInstruction(int id, [FromBody] UpdateInstructionRequest request)
        {
            try
            {
                var instruction = await _context.OngoingInstructions.FindAsync(id);
                if (instruction == null)
                {
                    return NotFound(new { error = "Instruction not found" });
                }

                if (request.InstructionText != null)
                    instruction.InstructionText = request.InstructionText;

                if (request.TriggerType != null)
                    instruction.TriggerType = request.TriggerType;

                if (request.Priority.HasValue)
                    instruction.Priority = request.Priority.Value;

                if (request.IsActive.HasValue)
                    instruction.IsActive = request.IsActive.Value;

                instruction.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Updated instruction {Id}", id);

                return Ok(new
                {
                    success = true,
                    message = "Instruction updated successfully",
                    instruction
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating instruction {Id}", id);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Delete an instruction
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteInstruction(int id)
        {
            try
            {
                var instruction = await _context.OngoingInstructions.FindAsync(id);
                if (instruction == null)
                {
                    return NotFound(new { error = "Instruction not found" });
                }

                _context.OngoingInstructions.Remove(instruction);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Deleted instruction {Id}", id);

                return Ok(new
                {
                    success = true,
                    message = "Instruction deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting instruction {Id}", id);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Toggle instruction active status
        /// </summary>
        [HttpPost("{id}/toggle")]
        public async Task<IActionResult> ToggleInstruction(int id)
        {
            try
            {
                var instruction = await _context.OngoingInstructions.FindAsync(id);
                if (instruction == null)
                {
                    return NotFound(new { error = "Instruction not found" });
                }

                instruction.IsActive = !instruction.IsActive;
                instruction.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Toggled instruction {Id} to {Status}",
                    id, instruction.IsActive ? "active" : "inactive");

                return Ok(new
                {
                    success = true,
                    message = $"Instruction {(instruction.IsActive ? "activated" : "deactivated")}",
                    instruction
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling instruction {Id}", id);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Get agent activities for a user
        /// </summary>
        [HttpGet("activities/{userId}")]
        public async Task<IActionResult> GetActivities(
            int userId,
            [FromQuery] int limit = 50,
            [FromQuery] bool unreadOnly = false)
        {
            try
            {
                var query = _context.AgentActivities
                    .Where(a => a.UserId == userId);

                if (unreadOnly)
                {
                    query = query.Where(a => !a.IsRead);
                }

                var activities = await query
                    .Include(a => a.OngoingInstruction)
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(limit)
                    .ToListAsync();

                var unreadCount = await _context.AgentActivities
                    .Where(a => a.UserId == userId && !a.IsRead)
                    .CountAsync();

                return Ok(new
                {
                    success = true,
                    activities,
                    count = activities.Count,
                    unreadCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting activities for user {UserId}", userId);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Mark activities as read
        /// </summary>
        [HttpPost("activities/mark-read")]
        public async Task<IActionResult> MarkActivitiesRead([FromBody] MarkReadRequest request)
        {
            try
            {
                var activities = await _context.AgentActivities
                    .Where(a => request.ActivityIds.Contains(a.Id))
                    .ToListAsync();

                foreach (var activity in activities)
                {
                    activity.IsRead = true;
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"Marked {activities.Count} activities as read"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking activities as read");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }
    }

    // Request models
    public class CreateInstructionRequest
    {
        public int UserId { get; set; }
        public string InstructionText { get; set; } = string.Empty;
        public string? TriggerType { get; set; }
        public int Priority { get; set; } = 0;
    }

    public class UpdateInstructionRequest
    {
        public string? InstructionText { get; set; }
        public string? TriggerType { get; set; }
        public int? Priority { get; set; }
        public bool? IsActive { get; set; }
    }

    public class MarkReadRequest
    {
        public List<int> ActivityIds { get; set; } = new();
    }
}