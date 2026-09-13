using MySqlConnector;

namespace API;

public class Database : IDisposable
{
    private readonly string _connectionString;
    private MySqlConnection? _connection;
    
    public Database()
    {
        var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
        var user = Environment.GetEnvironmentVariable("DB_USER") ?? "root";
        var password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "";
        var database = Environment.GetEnvironmentVariable("DB_NAME") ?? "website";
        
        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = uint.Parse(port),
            UserID = user,
            Password = password,
            Database = database,
            SslMode = MySqlSslMode.None,
            AllowPublicKeyRetrieval = true,
            ConvertZeroDateTime = true
        };
        
        _connectionString = builder.ConnectionString;
    }
    
    public bool CheckDBHealth()
    {
        try
        {
            var builder = new MySqlConnectionStringBuilder(_connectionString);
            var serverConnectionString = builder.ConnectionString;
            
            // Remove database from connection string for initial check
            var parts = serverConnectionString.Split(';');
            var serverParts = parts.Where(p => !p.StartsWith("Database=")).ToArray();
            serverConnectionString = string.Join(";", serverParts);
            
            using var connection = new MySqlConnection(serverConnectionString);
            connection.Open();
            
            using var cmd = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{builder.Database}`", connection);
            cmd.ExecuteNonQuery();
            
            using var dbConnection = new MySqlConnection(_connectionString);
            dbConnection.Open();
            
            CreateTables(dbConnection);
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database health check failed: {ex.Message}");
            return false;
        }
    }
    
    private void CreateTables(MySqlConnection connection)
    {
        var commands = new[]
        {
            @"CREATE TABLE IF NOT EXISTS login (
                id INT AUTO_INCREMENT PRIMARY KEY,
                discord_id VARCHAR(255) UNIQUE NOT NULL,
                username VARCHAR(255),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                last_login TIMESTAMP NULL
            )",
            @"CREATE TABLE IF NOT EXISTS user (
                id INT AUTO_INCREMENT PRIMARY KEY,
                login_id INT,
                name VARCHAR(255),
                bio TEXT,
                pfp VARCHAR(500),
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                FOREIGN KEY (login_id) REFERENCES login(id) ON DELETE CASCADE
            )",
            @"CREATE TABLE IF NOT EXISTS blog (
                id INT AUTO_INCREMENT PRIMARY KEY,
                title VARCHAR(255) NOT NULL,
                slug VARCHAR(255) UNIQUE NOT NULL,
                content LONGTEXT,
                author_id INT,
                published_at TIMESTAMP NULL,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                FOREIGN KEY (author_id) REFERENCES user(id) ON DELETE SET NULL
            )",
            @"CREATE TABLE IF NOT EXISTS projects (
                id INT AUTO_INCREMENT PRIMARY KEY,
                title VARCHAR(255) NOT NULL,
                description TEXT,
                image_url VARCHAR(500),
                github_url VARCHAR(500),
                live_url VARCHAR(500),
                technologies JSON,
                user_id INT,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                FOREIGN KEY (user_id) REFERENCES user(id) ON DELETE CASCADE
            )"
        };
        
        foreach (var command in commands)
        {
            using var cmd = new MySqlCommand(command, connection);
            cmd.ExecuteNonQuery();
        }
    }
    
    public async Task<Dictionary<string, object>> GetAllDataAsync()
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var result = new Dictionary<string, object>();
        result["users"] = await GetUsersFromConnectionAsync(connection);
        result["blogs"] = await GetBlogsFromConnectionAsync(connection);
        result["projects"] = await GetProjectsFromConnectionAsync(connection);
        
        return result;
    }
    
    public async Task<Dictionary<string, object>> GetHomeDataAsync()
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var result = new Dictionary<string, object>();
        result["latestBlogs"] = await GetLatestBlogsAsync(connection, 5);
        result["featuredProjects"] = await GetFeaturedProjectsAsync(connection, 6);
        result["userInfo"] = await GetMainUserInfoAsync(connection);
        
        return result;
    }
    
    // PUBLIC METHODS (No parameters, open their own connection)
    public async Task<List<Dictionary<string, object>>> GetBlogsAsync()
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = new MySqlCommand("SELECT * FROM blog ORDER BY published_at DESC", connection);
        return await ExecuteReaderAsync(cmd);
    }

    public async Task<List<Dictionary<string, object>>> GetProjectsAsync()
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        using var cmd = new MySqlCommand("SELECT * FROM projects ORDER BY created_at DESC", connection);
        return await ExecuteReaderAsync(cmd);
    }
    
    public async Task<bool> UpdateUserBasicAsync(string userId, UserUpdateData data)
    {
        using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var updates = new List<string>();
        var parameters = new Dictionary<string, object?>();
        
        if (data.Name != null) { updates.Add("name = @name"); parameters["@name"] = data.Name; }
        if (data.Bio != null) { updates.Add("bio = @bio"); parameters["@bio"] = data.Bio; }
        if (data.Pfp != null) { updates.Add("pfp = @pfp"); parameters["@pfp"] = data.Pfp; }
        
        if (updates.Count == 0) return false;
        
        var sql = $"UPDATE user SET {string.Join(", ", updates)} WHERE id = @userId";
        parameters["@userId"] = userId;
        
        using var cmd = new MySqlCommand(sql, connection);
        foreach (var param in parameters)
        {
            cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
        }
        
        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    // PRIVATE HELPERS
    private async Task<List<Dictionary<string, object>>> GetUsersFromConnectionAsync(MySqlConnection connection)
    {
        using var cmd = new MySqlCommand("SELECT * FROM user", connection);
        return await ExecuteReaderAsync(cmd);
    }
    
    private async Task<List<Dictionary<string, object>>> GetBlogsFromConnectionAsync(MySqlConnection connection)
    {
        using var cmd = new MySqlCommand("SELECT * FROM blog ORDER BY published_at DESC", connection);
        return await ExecuteReaderAsync(cmd);
    }
    
    private async Task<List<Dictionary<string, object>>> GetProjectsFromConnectionAsync(MySqlConnection connection)
    {
        using var cmd = new MySqlCommand("SELECT * FROM projects ORDER BY created_at DESC", connection);
        return await ExecuteReaderAsync(cmd);
    }
    
    private async Task<List<Dictionary<string, object>>> GetLatestBlogsAsync(MySqlConnection connection, int count)
    {
        using var cmd = new MySqlCommand("SELECT * FROM blog WHERE published_at IS NOT NULL ORDER BY published_at DESC LIMIT @count", connection);
        cmd.Parameters.AddWithValue("@count", count);
        return await ExecuteReaderAsync(cmd);
    }
    
    private async Task<List<Dictionary<string, object>>> GetFeaturedProjectsAsync(MySqlConnection connection, int count)
    {
        using var cmd = new MySqlCommand("SELECT * FROM projects ORDER BY created_at DESC LIMIT @count", connection);
        cmd.Parameters.AddWithValue("@count", count);
        return await ExecuteReaderAsync(cmd);
    }
    
    private async Task<Dictionary<string, object>> GetMainUserInfoAsync(MySqlConnection connection)
    {
        using var cmd = new MySqlCommand("SELECT * FROM user LIMIT 1", connection);
        var result = await ExecuteReaderAsync(cmd);
        return result.FirstOrDefault() ?? new Dictionary<string, object>();
    }
    
    private async Task<List<Dictionary<string, object>>> ExecuteReaderAsync(MySqlCommand cmd)
    {
        var results = new List<Dictionary<string, object>>();
        
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                // FIX: Use synchronous GetValue(i) instead of GetValueAsync
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            }
            results.Add(row);
        }
        
        return results;
    }
    
    public void Dispose()
    {
        _connection?.Dispose();
    }
}