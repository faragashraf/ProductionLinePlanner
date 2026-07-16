using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class AppUser
{
    public const int MaxFullNameLength = 200;
    public const int MaxLoginIdentifierLength = 200;

    private AppUser() { }

    public AppUser(
        Guid id,
        string fullName,
        string email,
        string passwordHash,
        bool isActive = true,
        string preferredLanguage = "en",
        DateTime? createdAtUtc = null)
    {
        var normalizedFullName = NormalizeFullName(fullName);
        var normalizedLoginIdentifier = NormalizeLoginIdentifier(email);
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("PasswordHash is required.", nameof(passwordHash));

        Id = id;
        FullName = normalizedFullName;
        Email = normalizedLoginIdentifier;
        PasswordHash = passwordHash;
        IsActive = isActive;
        PreferredLanguage = preferredLanguage;
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; init; }
    public string FullName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public string PreferredLanguage { get; private set; } = "en";
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public List<AppRole> Roles { get; } = [];
    public List<RefreshToken> RefreshTokens { get; } = [];
    public List<UserPermissionOverride> PermissionOverrides { get; } = [];

    public void AssignRole(AppRole role)
    {
        if (Roles.All(x => x.Id != role.Id))
        {
            Roles.Add(role);
            UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public void ChangePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("PasswordHash is required.", nameof(passwordHash));

        PasswordHash = passwordHash;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void UpdateProfile(string fullName, string loginIdentifier, bool isActive)
    {
        FullName = NormalizeFullName(fullName);
        Email = NormalizeLoginIdentifier(loginIdentifier);
        IsActive = isActive;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public static string NormalizeLoginIdentifier(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length == 0)
            throw new ArgumentException("Login identifier is required.", nameof(value));
        if (normalized.Length > MaxLoginIdentifierLength)
            throw new ArgumentException($"Login identifier cannot exceed {MaxLoginIdentifierLength} characters.", nameof(value));

        return normalized;
    }

    private static string NormalizeFullName(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            throw new ArgumentException("FullName is required.", nameof(value));
        if (normalized.Length > MaxFullNameLength)
            throw new ArgumentException($"FullName cannot exceed {MaxFullNameLength} characters.", nameof(value));

        return normalized;
    }
}
