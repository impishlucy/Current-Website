namespace API;

public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this WebApplication app)
    {
        var dataGroup = app.MapGroup("/data");
        
        // GET /data/home
        dataGroup.MapGet("/home", async (Database db) =>
        {
            try
            {
                var homeData = await db.GetHomeDataAsync();
                return Results.Ok(homeData);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Database error: {ex.Message}");
            }
        });
        
        // GET /data/blog
        dataGroup.MapGet("/blog", async (Database db) =>
        {
            try
            {
                var blogs = await db.GetBlogsAsync();
                return Results.Ok(blogs);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Database error: {ex.Message}");
            }
        });
        
        // GET /data/projects
        dataGroup.MapGet("/projects", async (Database db) =>
        {
            try
            {
                var projects = await db.GetProjectsAsync();
                return Results.Ok(projects);
            }
            catch (Exception ex)
            {
                return Results.Problem($"Database error: {ex.Message}");
            }
        });
    }
}