using System.Text.Json;

namespace Auth0Integration.Functions.Models;

public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string IdToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public UserProfile? User { get; set; }
}

public class UserProfile
{
    public string Sub { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public JsonElement? AppMetadata { get; set; }
    public JsonElement? UserMetadata { get; set; }
    public List<string> Roles { get; set; } = new();
}
