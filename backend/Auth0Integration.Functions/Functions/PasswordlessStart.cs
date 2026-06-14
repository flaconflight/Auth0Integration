using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Auth0Integration.Functions.Configuration;
using Auth0Integration.Functions.Models;
using Auth0Integration.Functions.Services;

namespace Auth0Integration.Functions.Functions;

public class PasswordlessStart
{
    private readonly Auth0AuthenticationService _auth0Auth;
    private readonly ICreditContextStore _store;
    private readonly IOptions<Auth0Options> _options;
    private readonly ILogger<PasswordlessStart> _logger;

    public PasswordlessStart(
        Auth0AuthenticationService auth0Auth,
        ICreditContextStore store,
        IOptions<Auth0Options> options,
        ILogger<PasswordlessStart> logger)
    {
        _auth0Auth = auth0Auth;
        _store = store;
        _options = options;
        _logger = logger;
    }

    [Function("PasswordlessStart")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "passwordless/start")] HttpRequestData req)
    {
        _logger.LogInformation("PasswordlessStart invoked");

        PasswordlessStartRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<PasswordlessStartRequest>();
        }
        catch (JsonException)
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "invalid_request", "Invalid JSON body");
        }

        if (request == null || string.IsNullOrWhiteSpace(request.Email))
        {
            return await CreateErrorResponse(req, HttpStatusCode.BadRequest, "invalid_request", "Email is required");
        }

        var correlationId = Guid.NewGuid().ToString("N");

        try
        {
            var result = await _auth0Auth.StartPasswordlessAsync(request.Email, correlationId);

            var entry = new CreditContextEntry
            {
                Email = request.Email,
                CorrelationId = correlationId,
                Otc = result.Otc,
                CreditContext = request.CreditContext ?? JsonSerializer.SerializeToElement(new { }),
                CreatedAt = DateTime.UtcNow
            };

            _store.Store(result.Otc, entry);

            _logger.LogInformation("Passwordless started for {Email}, OTC={Otc}, CorrelationId={CorrId}",
                request.Email, result.Otc, correlationId);

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = $"Verification code sent to {request.Email}",
                correlationId
            });
            return response;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Auth0 passwordless start failed for {Email}", request.Email);
            return await CreateErrorResponse(req, HttpStatusCode.BadGateway, "auth0_error", ex.Message);
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
