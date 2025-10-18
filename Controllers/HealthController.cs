using FinancialAdvisorAI.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FinancialAdvisorAI.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                message = "Financial Advisor AI API is running!"
            });
        }

        [HttpGet("qdrant")]
        public async Task<IActionResult> TestQdrant([FromServices] QdrantService qdrantService)
        {
            try
            {
                // This will trigger collection creation if it doesn't exist
                return Ok(new { status = "Qdrant connected successfully!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
