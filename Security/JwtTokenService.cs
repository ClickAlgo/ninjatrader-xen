using Microsoft.IdentityModel.Tokens;
using NinjaTrader_Xen.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace NinjaTrader_Xen.Security;

public static class JwtTokenService
{
    public static string Create(int subscriberId, string email, IConfiguration configuration)
    {
        var secret = configuration["Auth:JwtSecret"]
            ?? throw new InvalidOperationException("Auth:JwtSecret is not configured.");

        var expiryMinutes = configuration.GetValue("Auth:JwtExpiryMinutes", 480);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sid", subscriberId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim("platform_id", PlatformIds.NinjaTrader.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Auth:JwtIssuer"],
            audience: configuration["Auth:JwtAudience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
