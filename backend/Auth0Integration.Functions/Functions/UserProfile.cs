using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Auth0Integration.Functions.Configuration;
using Auth0Integration.Functions.Models;
using Auth0Integration.Functions.Services;

namespace Auth0Integration.Functions.Functions;

public class UserProfileFunction
{
    private readonly Auth0ManagementService _auth0Mgmt;
    private readonly Auth0Options _options;
    private readonly ILogger<UserProfileFunction> _logger;
    private static readonly ConcurrentDictionary<string, Microsoft.IdentityModel.Protocols.ConfigurationManager<OpenIdConnectConfiguration>> _configManagers = new();

    public UserProfileFunction(
        Auth0ManagementService auth0Mgmt,
        IOptions<Auth0Options> options,
        ILogger<UserProfileFunction> logger)
    {
        _auth0Mgmt = auth0Mgmt;
        _options = options.Value;
        _logger = logger;
    }

    [Function("UserProfile")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "user/profile")] HttpRequestData req)
    {
        _logger.LogInformation("UserProfile invoked");

        var authHeader = req.Headers.TryGetValues("Authorization", out var values)
            ? values.FirstOrDefault()
            : null;

        if (string.IsNullOrEmpty(authHeader))
        {
            return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "unauthorized", "Missing Authorization header");
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "unauthorized", "Invalid Authorization header format");
        }

        var accessToken = authHeader["Bearer ".Length..].Trim();

        try
        {
            var principal = await ValidateTokenAsync(accessToken);
            var sub = principal.FindFirst("sub")?.Value
                      ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrEmpty(sub))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "unauthorized", "Invalid token: missing subject");
            }

            var userJson = await _auth0Mgmt.GetUserAsync(sub);
            var roles = await _auth0Mgmt.GetUserRolesAsync(sub);

            var profile = new UserProfile
            {
                Sub = sub,
                Email = userJson.TryGetProperty("email", out var emailProp) ? emailProp.GetString() ?? "" : "",
                EmailVerified = userJson.TryGetProperty("email_verified", out var verifiedProp) && verifiedProp.GetBoolean(),
                AppMetadata = userJson.TryGetProperty("app_metadata", out var appMeta) ? appMeta : null,
                UserMetadata = userJson.TryGetProperty("user_metadata", out var userMeta) ? userMeta : null,
                Roles = roles
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(profile);
            return response;
        }
        catch (SecurityTokenExpiredException)
        {
            return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "token_expired", "Access token has expired");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "invalid_token", "Invalid access token");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "User profile retrieval failed");
            return await CreateErrorResponse(req, HttpStatusCode.InternalServerError, "server_error", "An unexpected error occurred");
        }
    }

    private async Task<System.Security.Claims.ClaimsPrincipal> ValidateTokenAsync(string token)
    {
        var issuer = $"https://{_options.Domain}/";
        var jwksUrl = $"{issuer}.well-known/openid-configuration";

        var configManager = _configManagers.GetOrAdd(jwksUrl, url =>
            new Microsoft.IdentityModel.Protocols.ConfigurationManager<OpenIdConnectConfiguration>(
                url, new OpenIdConnectConfigurationRetriever()));

        var oidcConfig = await configManager.GetConfigurationAsync();

        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudiences = new[] { _options.Audience, _options.BackendClientId },
            IssuerSigningKeys = oidcConfig.SigningKeys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateToken(token, validationParameters, out _);
    }

    private static async Task<HttpResponseData> CreateErrorResponse(
        HttpRequestData req, HttpStatusCode status, string error, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new ApiErrorResponse { Error = error, Message = message });
        return response;
    }
}
