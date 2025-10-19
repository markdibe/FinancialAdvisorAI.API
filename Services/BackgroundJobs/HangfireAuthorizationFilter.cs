using Hangfire.Dashboard;

namespace FinancialAdvisorAI.API.Services.BackgroundJobs
{
    namespace FinancialAdvisorAI.API.Services.BackgroundJobs
    {
        public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
        {
            public bool Authorize(DashboardContext context)
            {
                // For development: allow all
                // For production: implement proper authentication
                return true;

                // Production example:
                // var httpContext = context.GetHttpContext();
                // return httpContext.User.Identity?.IsAuthenticated ?? false;
            }
        }
    }
}
