using System.Text;
using System.Text.Json;

namespace LocalOrchestrator.Tests.Integration.Helpers;

/// <summary>
/// Helper class to simplify making authenticated HTTP requests to the API Gateway and Keycloak during integration tests.
/// </summary>
public class KeycloakHelper
{
    private readonly HttpClient _client;
    private readonly string _adminUser = "admin";
    private readonly string _adminPassword = "admin";
    private string? _adminToken;
    
    public KeycloakHelper(HttpClient client)
    {
        _client = client;
    }
    
    /// <summary>
    /// Retrieves an admin access token from Keycloak using the Resource Owner Password Credentials flow.
    /// <returns></returns>
    public async Task<string> GetAdminTokenAsync()
    {
        //If the admin token has already been retrieved, return it
        if (_adminToken != null)
            return _adminToken;
        
        //Prepare the token request payload with the admin user's credentials and client information
        var tokenRequest = new Dictionary<string, string>
        {
            { "client_id", "admin-cli" },
            { "username", _adminUser },
            { "password", _adminPassword },
            { "grant_type", "password" }
        };
        
        //Send the token request to Keycloak's token endpoint to retrieve an access token for the admin user
        using var content = new FormUrlEncodedContent(tokenRequest);
        
        //Send the POST request to the token endpoint and ensure it was successful
        var response = await _client.PostAsync("/realms/master/protocol/openid-connect/token", content);
        response.EnsureSuccessStatusCode();
        
        //Read the response content as a string, parse it as JSON, and extract the access token for future authenticated requests
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        _adminToken = json.RootElement.GetProperty("access_token").GetString()!;
        
        return _adminToken;
    }

    public async Task<Guid> CreateUserAsync(string username, string email, string password)
    {
        //Obtain an admin access token to authenticate the request to create a new user in Keycloak
        var token = await GetAdminTokenAsync();
        
        //Prepare the request to create a new user in the "support-hub" realm with the specified username, email, and password
        using var request = new HttpRequestMessage(HttpMethod.Post, "/admin/realms/supporthub/users");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        
        //Create the user payload with the required fields and serialize it to JSON for the request body
        var userPayload = new { username, email, firstName = username, enabled = true };
        request.Content = new StringContent(JsonSerializer.Serialize(userPayload), Encoding.UTF8, "application/json");
        
        //Send the request to create the user and ensure it was successful
        var createResponse = await _client.SendAsync(request);
        createResponse.EnsureSuccessStatusCode();
        
        //Extract the user ID from the Location header of the response, which contains the URL of the newly created user resource
        var userId = createResponse.Headers.Location?.Segments.Last();
        
        //Prepare the request to set the password for the newly created user, using the admin token for authentication
        var passwordPayload = new { type = "password", value = password, temporary = false };
        using var pwdRequest = new HttpRequestMessage(HttpMethod.Put, $"/admin/realms/supporthub/users/{userId}/reset-password");
        pwdRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        pwdRequest.Content = new StringContent(JsonSerializer.Serialize(passwordPayload), Encoding.UTF8, "application/json");
        
        //Send the request to set the user's password and ensure it was successful
        var pwdResponse = await _client.SendAsync(pwdRequest);
        pwdResponse.EnsureSuccessStatusCode();
        
        //Return the user ID of the newly created user as a Guid for use in tests
        return Guid.Parse(userId!);
    }

    /// <summary>
    /// Logs in to Keycloak with the specified username and password to retrieve an access token that can be used for authenticated requests in tests.
    /// </summary>
    /// <param name="username"></param>
    /// <param name="password"></param>
    /// <returns></returns>
    public async Task<string> LoginAsync(string username, string password)
    {
        //Prepare the login request payload with the user's credentials and client information for the Resource Owner Password Credentials flow
        var loginRequest = new Dictionary<string, string>
        {
            { "client_id", "supporthub-frontend" }, 
            { "username", username },
            { "password", password },
            { "grant_type", "password" }
        };
        
        //Send the login request to Keycloak's token endpoint to retrieve an access token for the specified user
        using var content = new FormUrlEncodedContent(loginRequest);
        var response = await _client.PostAsync("/realms/supporthub/protocol/openid-connect/token", content);
        response.EnsureSuccessStatusCode();

        //Read the response content as a string, parse it as JSON, and extract the access token for use in authenticated requests during tests
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("access_token").GetString()!;
    }
}