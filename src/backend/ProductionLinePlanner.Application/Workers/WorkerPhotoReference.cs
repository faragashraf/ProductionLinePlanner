namespace ProductionLinePlanner.Application.Workers;

public static class WorkerPhotoReference
{
    public const int Sha256HexLength = 64;
    public const int LegacySha256HexLength = 16;

    public static string Build(Guid workerId, string version)
    {
        if (workerId == Guid.Empty) throw new ArgumentException("WorkerId is required.", nameof(workerId));
        if (!IsFullVersion(version)) throw new ArgumentException("Version must be a full SHA-256 hex value.", nameof(version));
        return $"/api/workers/{workerId:D}/photo?v={version.ToLowerInvariant()}";
    }

    public static bool TryParse(string? reference, Guid workerId, out string version)
    {
        version = string.Empty;
        if (workerId == Guid.Empty || string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var prefix = $"/api/workers/{workerId:D}/photo?v=";
        if (!reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = reference[prefix.Length..];
        if (!IsValidVersion(candidate))
        {
            return false;
        }

        version = candidate.ToLowerInvariant();
        return true;
    }

    public static bool IsValidVersion(string? version) =>
        version is not null
        && version.Length is Sha256HexLength or LegacySha256HexLength
        && IsHex(version);

    public static bool IsFullVersion(string? version) =>
        version is { Length: Sha256HexLength } && IsHex(version);

    public static bool MatchesContentHash(string fullHash, string expectedVersion) =>
        IsFullVersion(fullHash)
        && IsValidVersion(expectedVersion)
        && fullHash.StartsWith(expectedVersion, StringComparison.OrdinalIgnoreCase);

    private static bool IsHex(string value) =>
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
}
