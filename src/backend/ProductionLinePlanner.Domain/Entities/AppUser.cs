using ProductionLinePlanner.Domain.Enums;

namespace ProductionLinePlanner.Domain.Entities;

public class AppUser
{
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
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("FullName is required.", nameof(fullName));
        if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
            throw new ArgumentException("A valid email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("PasswordHash is required.", nameof(passwordHash));

        Id = id;
        FullName = fullName.Trim();
        Email = email.Trim();
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
}
