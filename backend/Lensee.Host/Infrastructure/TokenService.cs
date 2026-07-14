using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Microsoft.IdentityModel.Tokens;

namespace Lensee.Host.Infrastructure;

public interface ITokenService
{
    string CreateAccessToken(User user);

    string CreateRefreshToken();

    string HashRefreshToken(string refreshToken);
}

public sealed class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private readonly IClock _clock;

    public TokenService(IConfiguration configuration, IClock clock)
    {
        _configuration = configuration;
        _clock = clock;
    }

    public string CreateAccessToken(User user)
    {
        var secret = _configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
        var issuer = _configuration["Jwt:Issuer"] ?? "Lensee";
        var audience = _configuration["Jwt:Audience"] ?? "Lensee.App";
        var expiryMinutes = _configuration.GetValue("Jwt:AccessTokenMinutes", 15);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(LenseeClaims.UserId, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.Username),
            new(ClaimTypes.Name, user.Username),
            new(LenseeClaims.Role, user.Role),
            new(ClaimTypes.Role, user.Role)
        };

        if (user.LocationId.HasValue)
        {
            claims.Add(new Claim(LenseeClaims.LocationId, user.LocationId.Value.ToString()));
        }

        claims.AddRange(LenseePermissions.ForRole(user.Role).Select(permission => new Claim("permission", permission)));

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: _clock.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string CreateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public string HashRefreshToken(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash);
    }
}
