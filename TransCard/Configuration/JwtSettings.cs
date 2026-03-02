namespace TransCard.Configuration;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 60;
    public string TestClientId { get; set; } = string.Empty;
    public string TestClientSecret { get; set; } = string.Empty;
}
