using API;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Load .env file
DotNetEnv.Env.Load();

// Add services
builder.Services.AddSingleton<Database>();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Initialize database
var db = app.Services.GetRequiredService<Database>();
if (!db.CheckDBHealth())
{
    Console.WriteLine("Database health check failed. Shutting down...");
    return;
}

// Rate limiting storage
var rateLimitStore = new ConcurrentDictionary<string, RateLimitInfo>();

// Rate limiting middleware
app.Use(async (context, next) =>
{
    var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var now = DateTime.UtcNow;
    
    if (!rateLimitStore.TryGetValue(clientIp, out var rateInfo))
    {
        rateInfo = new RateLimitInfo();
        rateLimitStore[clientIp] = rateInfo;
    }
    
    // Check if currently blocked
    if (rateInfo.BlockedUntil > now)
    {
        context.Response.StatusCode = 429; // Too Many Requests
        await context.Response.WriteAsJsonAsync(new 
        { 
            error = "Too many requests. Try again later.",
            retryAfter = (int)(rateInfo.BlockedUntil - now).TotalSeconds 
        });
        return;
    }
    
    // Clean old requests (older than 5 seconds)
    rateInfo.Requests.RemoveAll(r => r < now.AddSeconds(-5));
    
    // Check if limit exceeded
    if (rateInfo.Requests.Count >= 15)
    {
        // Block for 2 minutes
        rateInfo.BlockedUntil = now.AddMinutes(2);
        context.Response.StatusCode = 429;
        await context.Response.WriteAsJsonAsync(new 
        { 
            error = "Rate limit exceeded. Blocked for 2 minutes.",
            retryAfter = 120 
        });
        return;
    }
    
    // Add this request
    rateInfo.Requests.Add(now);
    
    await next();
});

// JWT Configuration
var jwtKey = Environment.GetEnvironmentVariable("JWT_SECRET") ?? "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";
var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "YourAPI";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "YourAPIApp";

// Admin endpoints (require auth)
app.MapAdminEndpoints(jwtKey, jwtIssuer, jwtAudience);

// Public endpoints (no auth)
app.MapPublicEndpoints();

app.Run();

// Rate limit info class
public class RateLimitInfo
{
    public List<DateTime> Requests { get; set; } = new();
    public DateTime BlockedUntil { get; set; } = DateTime.MinValue;
}