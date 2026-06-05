using Microsoft.Data.SqlClient;

namespace LocalOrchestrator.Tests.Integration.Helpers;

/// <summary>
/// Helper class to manage database interactions during integration tests.
/// </summary>
public class DatabaseHelper
{
    private readonly string _organizationDbString;
    private readonly string _ticketDbString;

    public DatabaseHelper(string? organizationDbString, string? ticketDbString)
    {
        _organizationDbString = organizationDbString ?? throw new ArgumentNullException(nameof(organizationDbString));
        _ticketDbString = ticketDbString ?? throw new ArgumentNullException(nameof(ticketDbString));
    }

    /// <summary>
    /// Executes a SQL query against the specified database and returns the result as a string.
    /// This can be used to verify database state during tests or to retrieve specific values for assertions.
    /// </summary>
    /// <param name="dbName"></param>
    /// <param name="query"></param>
    /// <returns></returns>
    public async Task<string> GetStringAsync(string dbName, string query)
    {
        //Determine which connection string to use based on the specified database name
        var connectionString = dbName == "OrgDb" ? _organizationDbString : _ticketDbString;

        //Get the sql connection and open it
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        //Execute the SQL query and return the result as a string
        await using var cmd = new SqlCommand(query, conn);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Waits for a specific value to be present in the database for the specified number of retries with a delay between each retry.
    /// </summary>
    /// <param name="dbName"></param>
    /// <param name="query"></param>
    /// <param name="expectedValue"></param>
    /// <param name="maxRetries"></param>
    /// <param name="delayMs"></param>
    /// <returns></returns>
    public async Task<bool> WaitForStateAsync(string dbName, string query, string expectedValue, int maxRetries = 15, int delayMs = 500)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            //Check if the expected value is present in the database
            var result = await GetStringAsync(dbName, query);
            if (result == expectedValue)
                return true;

            await Task.Delay(delayMs);
        }
        return false;
    }

    /// <summary>
    /// Executes SQL queries to delete all data from the organization and ticket databases.
    /// </summary>
    /// <returns></returns>
    public async Task ExecuteCleanupAsync()
    {
        try
        {
            //Get the sql connection to the organization database
            await using var organizationConn = new SqlConnection(_organizationDbString);
            await organizationConn.OpenAsync();

            //Execute the SQL query to delete all data from the organization database
            await using var organizationCmd = organizationConn.CreateCommand();
            organizationCmd.CommandText = @"
                IF OBJECT_ID('Users', 'U') IS NOT NULL DELETE FROM Users; 
                IF OBJECT_ID('Organizations', 'U') IS NOT NULL DELETE FROM Organizations;";
            await organizationCmd.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            //Ignore any errors that occur due to database errors
        }

        try
        {
            //Get the sql connection to the ticket database
            await using var ticketConn = new SqlConnection(_ticketDbString);
            await ticketConn.OpenAsync();

            //Execute the SQL query to delete all data from the ticket database
            await using var ticketCmd = ticketConn.CreateCommand();
            ticketCmd.CommandText = @"
                IF OBJECT_ID('TicketAuditLogs', 'U') IS NOT NULL DELETE FROM TicketAuditLogs; 
                IF OBJECT_ID('TicketMessages', 'U') IS NOT NULL DELETE FROM TicketMessages; 
                IF OBJECT_ID('TicketTags', 'U') IS NOT NULL DELETE FROM TicketTags; 
                IF OBJECT_ID('OperatorTags', 'U') IS NOT NULL DELETE FROM OperatorTags;
                IF OBJECT_ID('Tickets', 'U') IS NOT NULL DELETE FROM Tickets; 
                IF OBJECT_ID('Tags', 'U') IS NOT NULL DELETE FROM Tags;
                IF OBJECT_ID('UserSnapshots', 'U') IS NOT NULL DELETE FROM UserSnapshots; 
                IF OBJECT_ID('OrganizationSnapshots', 'U') IS NOT NULL DELETE FROM OrganizationSnapshots;";
            await ticketCmd.ExecuteNonQueryAsync();
        }
        catch (SqlException)
        {
            //Ignore any errors that occur due to database errors
        }
    }

    /// <summary>
    /// Retrieves the organization's creation confirmation token from the database.
    /// </summary>
    public async Task<string?> GetOrganizationCreationTokenAsync(Guid orgId)
    {
        //Get the SQL connection to the organization database
        await using var conn = new SqlConnection(_organizationDbString);
        await conn.OpenAsync();
        
        //Execute the SQL query to retrieve the organization's creation confirmation token
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CreationToken FROM Organizations WHERE Id = @OrgId";
        cmd.Parameters.AddWithValue("@OrgId", orgId);
        
        //Return the found token
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    public async Task<List<Guid>> GetAllUserSnapshotIdsAsync()
    {
        //Get the SQL connection to the ticket database
        await using var conn = new SqlConnection(_ticketDbString);
        await conn.OpenAsync();
        
        //Execute the SQL query to retrieve all the user snapshot ids
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id FROM UserSnapshots";
        
        var snapshotIds = new List<Guid>();

        //Return the found user ids
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            snapshotIds.Add(reader.GetGuid(0));
        }
        return snapshotIds;
    }

    /// <summary>
    /// Retrieves the user's invitation token from the database.
    /// </summary>
    public async Task<string?> GetUserInvitationTokenAsync(Guid userId)
    {
        //Get the SQL connection to the organization database
        await using var conn = new SqlConnection(_organizationDbString);
        await conn.OpenAsync();
        
        //Execute the SQL query to retrieve the user's invitation token
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT InvitationToken FROM Users WHERE Id = @UserId";
        cmd.Parameters.AddWithValue("@UserId", userId);
        
        //Return the found token
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }
}