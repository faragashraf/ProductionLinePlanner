namespace ProductionLinePlanner.Domain.Entities;

public class RefreshToken
{
    private RefreshToken() { }

    public RefreshToken(
        Guid id,
        Guid appUserId,
        string tokenHash,
        DateTime expiresAtUtc,
        DateTime? createdAtUtc = null)
    {
        if (appUserId == Guid.Empty)
        {
            throw new ArgumentException("AppUserId is required.", nameof(appUserId));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("TokenHash is required.", nameof(tokenHash));
        }

        if (expiresAtUtc <= (createdAtUtc ?? DateTime.UtcNow))
        {
            throw new ArgumentException("ExpiresAtUtc must be in the future.", nameof(expiresAtUtc));
        }

        Id = id;
        AppUserId = appUserId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
    }

    public Guid Id { get; init; }
    public Guid AppUserId { get; private set; }
    public AppUser? AppUser { get; set; }
    public string TokenHash { get; private set; } = string.Empty;
    public bool IsRevoked { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? RevokedReason { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastUsedAtUtc { get; private set; }

    public bool IsUsable(DateTime nowUtc) => !IsRevoked && ExpiresAtUtc > nowUtc;

    public void MarkAsUsed(DateTime atUtc)
    {
        LastUsedAtUtc = atUtc;
    }

    public void Revoke(DateTime atUtc, string reason)
    {
        if (IsRevoked)
        {
            return;
        }

        IsRevoked = true;
        RevokedAtUtc = atUtc;
        RevokedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
