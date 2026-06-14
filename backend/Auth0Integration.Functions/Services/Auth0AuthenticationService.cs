using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Auth0Integration.Functions.Configuration;

namespace Auth0Integration.Functions.Services;

public class Auth0AuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly Auth0Options _options;

    public Auth0AuthenticationService(IHttpClientFactory httpClientFactory, IOptions<Auth0Options> options)
    {
        _httpClient = httpClientFactory.CreateClient();
        _options = options.Value;
    }

    public async Task<PasswordlessStartResult> StartPasswordlessAsync(string email, string state)
    {
        var url = $"https://{_options.Domain}/passwordless/start";

        var body = new
        {
            client_id = _options.BackendClientId,
            client_secret = _options.BackendClientSecret,
            connection = _options.PasswordlessConnection,
            email,
            send = "code",
            authParams = new
            {
                scope = "openid profile email",
                state
            }
        };

        var response = await _httpClient.PostAsJsonAsync(url, body);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            throw new HttpRequestException(
                $"Auth0 passwordless start failed: {response.StatusCode} - {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new PasswordlessStartResult
        {
            Otc = result.GetProperty("_id").GetString() ?? string.Empty,
            Email = result.GetProperty("email").GetString() ?? string.Empty
        };
    }

    public async Task<Auth0TokenResult> ExchangeOtpForTokensAsync(string email, string otp)
    {
        var url = $"https://{_options.Domain}/oauth/token";

        var body = new
        {
            grant_type = "http://auth0.com/oauth/grant-type/passwordless/otp",
            client_id = _options.BackendClientId,
            client_secret = _options.BackendClientSecret,
            username = email,
            otp = otp,
            realm = _options.PasswordlessConnection,
            audience = _options.Audience,
            scope = "openid profile email"
        };

        var response = await _httpClient.PostAsJsonAsync(url, body);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<JsonElement>();
            throw new HttpRequestException(
                $"Auth0 OTP exchange failed: {response.StatusCode} - {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new Auth0TokenResult
        {
            AccessToken = result.GetProperty("access_token").GetString() ?? string.Empty,
            IdToken = result.GetProperty("id_token").GetString() ?? string.Empty,
            TokenType = result.GetProperty("token_type").GetString() ?? "Bearer",
            ExpiresIn = result.GetProperty("expires_in").GetInt32()
        };
    }
}

public class PasswordlessStartResult
{
    public string Otc { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class Auth0TokenResult
{
    public string AccessToken { get; set; } = string.Empty;
    public string IdToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
}
