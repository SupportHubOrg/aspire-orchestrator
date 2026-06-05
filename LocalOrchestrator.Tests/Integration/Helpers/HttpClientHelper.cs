using System.Text;
using System.Text.Json;

namespace LocalOrchestrator.Tests.Integration.Helpers;

/// <summary>
/// Helper class to simplify making authenticated HTTP requests to the API Gateway and Keycloak during integration tests.
/// </summary>
public class HttpClientHelper
{
    private readonly HttpClient _client;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    
    public HttpClientHelper(HttpClient client)
    {
        _client = client;
    }
    
    /// <summary>
    /// Sets the Bearer token for authentication on the HttpClient, allowing subsequent requests to be authenticated against protected endpoints.
    /// </summary>
    /// <param name="token"></param>
    public void SetAuthToken(string token)
    {
        //Set the Authorization header with the Bearer token
        _client.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }
    
    /// <summary>
    /// Sends a POST request with a JSON payload to the specified endpoint and deserializes the JSON response into the specified type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="endpoint"></param>
    /// <param name="payload"></param>
    /// <returns></returns>
    public async Task<T> PostAsync<T>(string endpoint, object payload)
    {
        //Serialize the payload to JSON and create StringContent for the request body
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        //Send the POST request to the specified endpoint
        var response = await _client.PostAsync(endpoint, content);
        response.EnsureSuccessStatusCode();
        
        //Read the response content as a string and deserialize it into the specified type
        var responseString = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(responseString, _jsonOptions)!;
    }
    
    /// <summary>
    /// Sends a GET request to the specified endpoint and deserializes the JSON response into the specified type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="endpoint"></param>
    /// <returns></returns>
    public async Task<T> GetAsync<T>(string endpoint)
    {
        //Send the GET request to the specified endpoint
        var response = await _client.GetAsync(endpoint);
        response.EnsureSuccessStatusCode();
        
        //Read the response content as a string and deserialize it into the specified type
        var responseString = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(responseString, _jsonOptions)!;
    }

    /// <summary>
    /// Sends a PUT request with a JSON payload to the specified endpoint and deserializes the JSON response into the specified type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="endpoint"></param>
    /// <param name="payload"></param>
    /// <returns></returns>
    public async Task<T> PutAsync<T>(string endpoint, object? payload)
    {
        //Serialize the payload to JSON and create StringContent for the request body, or set content to null if payload is null
        var content = payload != null 
            ? new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") 
            : null;
        
        //Send the PUT request to the specified endpoint
        var response = await _client.PutAsync(endpoint, content);
        response.EnsureSuccessStatusCode();
        
        //Read the response content as a string and deserialize it into the specified type, or return default if response is empty
        var responseString = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(responseString)) return default!;
        return JsonSerializer.Deserialize<T>(responseString, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>
    /// Sends a DELETE request to the specified endpoint and ensures a successful response status code, throwing an exception if the request fails.
    /// </summary>
    /// <param name="endpoint"></param>
    /// <returns></returns>
    public async Task DeleteAsync(string endpoint)
    {
        //Send the DELETE request to the specified endpoint
        var response = await _client.DeleteAsync(endpoint);
        response.EnsureSuccessStatusCode();
    }
}