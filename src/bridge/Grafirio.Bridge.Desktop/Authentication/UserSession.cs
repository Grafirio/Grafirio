namespace Grafirio.Bridge.Desktop;

/// <summary>A desktop user's session, separate from the bridge machine identity.</summary>
public sealed record UserSession(
    string AccessToken,
    string? RefreshToken,
    string? IdToken,
    DateTimeOffset? ExpiresAt = null)
{
    public override string ToString() => "UserSession { Tokens = [REDACTED] }";
}