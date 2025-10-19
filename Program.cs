using FinancialAdvisorAI.API.Repositories;
using FinancialAdvisorAI.API.Services;
using FinancialAdvisorAI.API.Services.BackgroundJobs;
using FinancialAdvisorAI.API.Services.BackgroundJobs.FinancialAdvisorAI.API.Services.BackgroundJobs;
using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.EntityFrameworkCore;
using System;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();



builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

//register services 
builder.Services.AddScoped<GoogleAuthService>();
builder.Services.AddScoped<AiChatService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<HubSpotService>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<QdrantService>();
builder.Services.AddScoped<VectorSyncService>();
builder.Services.AddScoped<ToolExecutorService>();
builder.Services.AddScoped<SyncBackgroundJob>();

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage(builder.Configuration.GetConnectionString("HangfireConnection")));


builder.Services.AddHangfireServer();

// Configure CORS to allow Angular app

var frontendUrl = builder.Configuration["Frontend:Url"] ?? "http://localhost:4200";


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp",
        policy =>
        {
            policy.WithOrigins(frontendUrl)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowAngularApp");

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();


app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() },
    DashboardTitle = "Financial Advisor AI - Background Jobs"
});


app.MapControllers();
app.MapFallbackToFile("index.html");


app.UseSwagger();
app.UseSwaggerUI();


using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}


using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

    // Incremental sync every 15 minutes
    recurringJobManager.AddOrUpdate<SyncBackgroundJob>(
        "incremental-sync-all-users",
        job => job.IncrementalSyncAllUsersAsync(),
        "*/15 * * * *", // Every 15 minutes
        new RecurringJobOptions
        {
            TimeZone = TimeZoneInfo.Utc
        });

    // Daily full sync at 2 AM UTC
    recurringJobManager.AddOrUpdate<SyncBackgroundJob>(
        "daily-full-sync",
        job => job.FullSyncAllUsersAsync(),
        "0 2 * * *", // 2 AM daily
        new RecurringJobOptions
        {
            TimeZone = TimeZoneInfo.Utc
        });
}


app.Run();
