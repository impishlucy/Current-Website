using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Net.Http.Headers;
using System.Text.Json;

namespace API;

public static class AdminEndpoints
{
    private static readonly HttpClient _httpClient = new();
    
    public static void MapAdminEndpoints(this WebApplication app, string jwtKey, string jwtIssuer, string jwtAudience)
    {
        var adminGroup = app.MapGroup("/admin");
        
        // GET /admin/login
        adminGroup.MapGet("/login", async (HttpContext ctx) =>
        {
            // Get MEOW header (Discord OAuth data)
            if (!ctx.Request.Headers.TryGetValue("MEOW", out var meowHeader))
            {
                return Results.BadRequest(new { error = "MEOW header missing" });
            }
            
            // Parse Discord OAuth data
            DiscordOAuthData? discordData;
            try
            {
                discordData = JsonSerializer.Deserialize<DiscordOAuthData>(meowHeader.ToString());
            }
            catch
            {
                return Results.BadRequest(new { error = "Invalid MEOW header format" });
            }
            
            if (discordData == null)
            {
                return Results.BadRequest(new { error = "Invalid Discord data" });
            }
            
            // Verify with Discord API
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new AuthenticationHeaderValue("Bearer", discordData.AccessToken);
                
                var response = await _httpClient.GetAsync("https://discord.com/api/users/@me");
                
                if (!response.IsSuccessStatusCode)
                {
                    return Results.Unauthorized();
                }
                
                var userData = await response.Content.ReadAsStringAsync();
                var discordUser = JsonSerializer.Deserialize<DiscordUser>(userData);
                
                // Check against ADMIN_ID from .env
                var adminId = Environment.GetEnvironmentVariable("ADMIN_ID");
                
                if (discordUser?.Id != adminId)
                {
                    return Results.Forbid();
                }
            }
            catch (Exception ex)
            {
                return Results.Problem($"Discord API error: {ex.Message}");
            }
            
            // Generate JWT tokens
            var jwtToken = GenerateJwtToken(discordData.UserId, jwtKey, jwtIssuer, jwtAudience);
            var refreshToken = GenerateRefreshToken();
            
            return Results.Ok(new
            {
                accessToken = jwtToken,
                refreshToken,
                expiresIn = 3600 // 1 hour
            });
        });
        
        // Middleware to validate JWT for other admin endpoints
        adminGroup.AddEndpointFilter(async (ctx, next) =>
        {
            var authHeader = ctx.HttpContext.Request.Headers["Authorization"].ToString();
            
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Results.Unauthorized();
            }
            
            var token = authHeader.Substring("Bearer ".Length).Trim();
            
            try
            {
                var principal = ValidateJwtToken(token, jwtKey, jwtIssuer, jwtAudience);
                ctx.HttpContext.Items["User"] = principal;
                
                // Generate new token (token refresh)
                var userId = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var newToken = userId != null ? GenerateJwtToken(userId, jwtKey, jwtIssuer, jwtAudience) : null;
                
                var result = await next(ctx);
                
                // Add new token to response headers
                if (newToken != null && result is IResult okResult)
                {
                    ctx.HttpContext.Response.Headers["X-New-Token"] = newToken;
                }
                
                return result;
            }
            catch
            {
                return Results.Unauthorized();
            }
        });
        
        // GET /admin/dashboard
        adminGroup.MapGet("/dashboard", async (Database db) =>
        {
            try
            {
                var data = await db.GetAllDataAsync();
                return Results.Ok(data);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Database error: {ex.Message}");
            }
        });
        
        // PUT /admin/update/user/basic
        adminGroup.MapPut("/update/user/basic", async (HttpContext ctx, Database db) =>
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.Body);
                var body = await reader.ReadToEndAsync();
                var userData = JsonSerializer.Deserialize<UserUpdateData>(body);
                
                if (userData == null)
                {
                    return Results.BadRequest(new { error = "Invalid request body" });
                }
                
                // Get user ID from JWT
                var userId = ctx.Items["User"] is ClaimsPrincipal principal 
                    ? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                    : null;
                
                if (userId == null)
                {
                    return Results.Unauthorized();
                }
                
                var success = await db.UpdateUserBasicAsync(userId, userData);
                
                return success 
                    ? Results.Ok(new { message = "User updated successfully" })
                    : Results.Problem("Failed to update user");
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error updating user: {ex.Message}");
            }
        });
    }
    
    private static string GenerateJwtToken(string userId, string key, string issuer, string audience)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
        
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        
        var token = new JwtSecurityToken(issuer, audience, claims, null, DateTime.UtcNow.AddHours(1), credentials);
        
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    
    private static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
    }
    
    private static ClaimsPrincipal? ValidateJwtToken(string token, string key, string issuer, string audience)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
        
        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }
}

// DTOs for Discord OAuth
public class DiscordOAuthData
{
    public string AccessToken { get; set; } = "";
    public string TokenType { get; set; } = "";
    public int ExpiresIn { get; set; }
    public string RefreshToken { get; set; } = "";
    public string Scope { get; set; } = "";
    public string UserId { get; set; } = "";
}

public class DiscordUser
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Discriminator { get; set; } = "";
    public string? Avatar { get; set; }
}

public class UserUpdateData
{
    public string? Name { get; set; }
    public string? Bio { get; set; }
    public string? Pfp { get; set; } // Base64 encoded image or URL
}