using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using ProductionLinePlanner.Domain.Entities;
using ProductionLinePlanner.Domain.Enums;

public static class AuthTokenService
{
    public static string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(48);
        return Convert.ToBase64String(randomBytes);
    }

    public static string HashRefreshToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentNullException(nameof(token));
        }

        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

public static string CreateAccessToken(
        AppUser user,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc,
        string issuer,
        string audience,
        SymmetricSecurityKey signingKey)
    {
        var tokenId = Guid.NewGuid().ToString("N");
        var roles = user.Roles.Select(r => new Claim(ClaimTypes.Role, r.Role.ToString())).ToArray();
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, tokenId),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Sid, user.Id.ToString()),
            new("token_version", "1")
        };
        claims.AddRange(roles);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAtUtc,
            NotBefore = issuedAtUtc,
            IssuedAt = issuedAtUtc,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }
}
