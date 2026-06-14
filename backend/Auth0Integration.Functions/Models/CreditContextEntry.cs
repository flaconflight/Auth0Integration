using System.Text.Json;

namespace Auth0Integration.Functions.Models;

public class CreditContextEntry
{
    public string Email { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Otc { get; set; } = string.Empty;
    public JsonElement CreditContext { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
