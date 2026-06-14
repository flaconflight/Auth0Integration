using System.Text.Json;

namespace Auth0Integration.Functions.Models;

public class PasswordlessStartRequest
{
    public string Email { get; set; } = string.Empty;
    public JsonElement? CreditContext { get; set; }
}
