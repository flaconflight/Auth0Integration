using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Auth0Integration.Functions.Models;
using Auth0Integration.Functions.Services;

namespace Auth0Integration.Functions.Functions;

public class VerifyOtc
{
    private readonly Auth0AuthenticationService _auth0Auth;
    private readonly Auth0ManagementService _auth0Mgmt;
    private readonly ICreditContextStore _store;
    private readonly ILogger<VerifyOtc> _logger;

    public VerifyOtc(
        Auth0AuthenticationService auth0Auth,
        Auth0ManagementService auth0Mgmt,
        ICreditContextStore store,
        ILogger<VerifyOtc> logger)
    {
        _auth0Auth = auth0Auth;
        _auth0Mgmt = auth0Mgmt;
        _store = store;
        _logger = logger;
    }

    [Function("VerifyOtc")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/verify-otc")] HttpRequestData req)
    {
        _logger.LogInformation("VerifyOtc invoked");

        VerifyOtcRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<VerifyOtcRequest>();
        }
        catch (JsonException)
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "invalid_request", "Invalid JSON body");
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Otc))
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "invalid_request", "Email and OTC are required");
        }

        var creditEntry = _store.Retrieve(request.Otc);
        if (creditEntry == null)
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "otc_not_found",
                "The verification code was not found or has expired. Please request a new code.");
        }

        if (!string.Equals(creditEntry.Email, request.Email, StringComparison.OrdinalIgnoreCase))
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "email_mismatch",
                $"This code was issued for {creditEntry.Email}. Please contact your administrator.");
        }

        try
        {
            var tokenResult = await _auth0Auth.ExchangeOtpForTokensAsync(request.Email, request.Otc);

            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(tokenResult.AccessToken);
            var sub = jwtToken.Subject;

            _logger.LogInformation("User {Sub} authenticated via OTC", sub);

            _store.Remove(request.Otc);

            var userJson = await _auth0Mgmt.GetUserAsync(sub);

            if (creditEntry.CreditContext.ValueKind != JsonValueKind.Undefined &&
                creditEntry.CreditContext.ValueKind != JsonValueKind.Null)
            {
                var existingApps = new List<JsonElement>();
                if (userJson.TryGetProperty("app_metadata", out var existingMetadata) &&
                    existingMetadata.ValueKind == JsonValueKind.Object &&
                    existingMetadata.TryGetProperty("creditApplications", out var existingAppsProp) &&
                    existingAppsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var app in existingAppsProp.EnumerateArray())
                    {
                        existingApps.Add(app);
                    }
                }

                var newAppId = creditEntry.CreditContext.TryGetProperty("applicationId", out var idProp)
                    ? idProp.GetString()
                    : null;

                if (newAppId != null && existingApps.Any(a =>
                    a.TryGetProperty("applicationId", out var eid) && eid.GetString() == newAppId))
                {
                    return await CreateErrorResponse(req, HttpStatusCode.Conflict, "duplicate_application",
                        $"Credit application {newAppId} is already linked to this user.");
                }

                var enrichedNode = JsonNode.Parse(creditEntry.CreditContext.GetRawText());
                if (enrichedNode is JsonObject enrichedObj)
                {
                    enrichedObj["linkedAt"] = DateTime.UtcNow.ToString("O");
                    enrichedObj["linkedByOtc"] = true;
                }

                existingApps.Add(JsonSerializer.SerializeToElement(enrichedNode));

                using var metaStream = new MemoryStream();
                using (var metaWriter = new Utf8JsonWriter(metaStream))
                {
                    metaWriter.WriteStartObject();

                    if (existingMetadata.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in existingMetadata.EnumerateObject())
                        {
                            if (prop.Name != "creditApplications")
                            {
                                prop.WriteTo(metaWriter);
                            }
                        }
                    }

                    metaWriter.WriteStartArray("creditApplications");
                    foreach (var app in existingApps)
                    {
                        app.WriteTo(metaWriter);
                    }
                    metaWriter.WriteEndArray();

                    metaWriter.WriteEndObject();
                    metaWriter.Flush();
                }

                var metadataElement = JsonSerializer.Deserialize<JsonElement>(metaStream.ToArray());
                await _auth0Mgmt.UpdateUserAppMetadataAsync(sub, metadataElement);
                _logger.LogInformation("Credit application {AppId} linked to user {Sub}", newAppId ?? "unknown", sub);

                userJson = await _auth0Mgmt.GetUserAsync(sub);
            }

            var roles = await _auth0Mgmt.GetUserRolesAsync(sub);

            var userProfile = new UserProfile
            {
                Sub = sub,
                Email = userJson.TryGetProperty("email", out var emailProp) ? emailProp.GetString() ?? "" : "",
                EmailVerified = userJson.TryGetProperty("email_verified", out var verifiedProp) && verifiedProp.GetBoolean(),
                AppMetadata = userJson.TryGetProperty("app_metadata", out var appMeta) ? appMeta : null,
                UserMetadata = userJson.TryGetProperty("user_metadata", out var userMeta) ? userMeta : null,
                Roles = roles
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new TokenResponse
            {
                AccessToken = tokenResult.AccessToken,
                IdToken = tokenResult.IdToken,
                TokenType = tokenResult.TokenType,
                ExpiresIn = tokenResult.ExpiresIn,
                User = userProfile
            });
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "OTC exchange failed for {Email}", request.Email);

            if (ex.Message.Contains("401") || ex.Message.Contains("invalid_grant") || ex.Message.Contains("invalid_otp"))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "invalid_otc",
                    "The verification code is invalid or expired. Please request a new code.");
            }

            return await CreateErrorResponse(req, HttpStatusCode.BadGateway, "auth0_error",
                "Authentication service error. Please try again.");
        }
    }

    private static async Task<HttpResponseData> CreateErrorResponse(
        HttpRequestData req, HttpStatusCode status, string error, string message)
    {
        var response = req.CreateResponse(status);
        await response.WriteAsJsonAsync(new ApiErrorResponse { Error = error, Message = message });
        return response;
    }
}
