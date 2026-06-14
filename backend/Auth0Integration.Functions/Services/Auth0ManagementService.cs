using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Auth0Integration.Functions.Configuration;
using Auth0Integration.Functions.Models;

namespace Auth0Integration.Functions.Services;

public class Auth0ManagementService
{
    private readonly HttpClient _httpClient;
    private readonly Auth0Options _options;
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public Auth0ManagementService(IHttpClientFactory httpClientFactory, IOptions<Auth0Options> options)
    {
        _httpClient = httpClientFactory.CreateClient();
        _options = options.Value;
    }

    private async Task<string> GetManagementTokenAsync()
    {
        if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
        {
            return _cachedToken;
        }

        var url = $"https://{_options.Domain}/oauth/token";
        var body = new
        {
            client_id = _options.ManagementClientId,
            client_secret = _options.ManagementClientSecret,
            audience = $"https://{_options.Domain}/api/v2/",
            grant_type = "client_credentials",
            scope = "read:users update:users read:roles create:roles read:users_app_metadata update:users_app_metadata"
        };

        var response = await _httpClient.PostAsJsonAsync(url, body);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        _cachedToken = result.GetProperty("access_token").GetString() ?? string.Empty;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(result.GetProperty("expires_in").GetInt32() - 60);

        return _cachedToken;
    }

    public async Task<JsonElement> GetUserAsync(string userId)
    {
        var token = await GetManagementTokenAsync();
        var url = $"https://{_options.Domain}/api/v2/users/{userId}";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public async Task UpdateUserAppMetadataAsync(string userId, JsonElement appMetadata)
    {
        var token = await GetManagementTokenAsync();
        var url = $"https://{_options.Domain}/api/v2/users/{userId}";

        var body = new
        {
            app_metadata = appMetadata
        };

        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            throw new HttpRequestException(
                $"Auth0 Management API update failed: {response.StatusCode} - {error}");
        }
    }

    public async Task AssignRolesAsync(string userId, List<string> roleIds)
    {
        var token = await GetManagementTokenAsync();
        var url = $"https://{_options.Domain}/api/v2/users/{userId}/roles";

        var body = new
        {
            roles = roleIds
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            throw new HttpRequestException(
                $"Auth0 Management API role assignment failed: {response.StatusCode} - {error}");
        }
    }

    public async Task<List<string>> GetUserRolesAsync(string userId)
    {
        var token = await GetManagementTokenAsync();
        var url = $"https://{_options.Domain}/api/v2/users/{userId}/roles";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var roles = await response.Content.ReadFromJsonAsync<List<JsonElement>>() ?? new();
        return roles.Select(r => r.GetProperty("name").GetString() ?? string.Empty)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
    }
}
