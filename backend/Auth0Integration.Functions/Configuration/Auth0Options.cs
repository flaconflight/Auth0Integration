namespace Auth0Integration.Functions.Configuration;

public class Auth0Options
{
    public const string SectionName = "Auth0";

    public string Domain { get; set; } = string.Empty;
    public string BackendClientId { get; set; } = string.Empty;
    public string BackendClientSecret { get; set; } = string.Empty;
    public string ManagementClientId { get; set; } = string.Empty;
    public string ManagementClientSecret { get; set; } = string.Empty;
    public string PasswordlessConnection { get; set; } = "email";
    public string Audience { get; set; } = string.Empty;
    public string ManagementAudience { get; set; } = string.Empty;
}
