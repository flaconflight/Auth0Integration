namespace Auth0Integration.Functions.Models;

public class VerifyOtcRequest
{
    public string Email { get; set; } = string.Empty;
    public string Otc { get; set; } = string.Empty;
}
