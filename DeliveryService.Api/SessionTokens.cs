using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DeliveryService.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace DeliveryService.Api;

public sealed class SessionTokens(SymmetricSecurityKey key)
{
    public const string Issuer = "DeliveryService";
    public const string Audience = "DeliveryService.Client";

    public string Issue(UserInfo user)
    {
        Claim[] claims =
        [
            new("sub", user.Id.ToString(CultureInfo.InvariantCulture)),
            new("jti", Guid.NewGuid().ToString("N")),
            new("login", user.Login),
            new("display_name", user.DisplayName),
            new("role", user.Role)
        ];
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(Issuer, Audience, claims, now, now.AddHours(8),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static UserInfo Current(ClaimsPrincipal principal) => new(
        int.Parse(principal.FindFirst("sub")!.Value, CultureInfo.InvariantCulture),
        principal.FindFirst("login")!.Value, principal.FindFirst("display_name")!.Value,
        principal.FindFirst("role")!.Value);

    public static void Revoke(ClaimsPrincipal principal, IMemoryCache cache)
    {
        var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(principal.FindFirst("exp")!.Value,
            CultureInfo.InvariantCulture));
        cache.Set(principal.FindFirst("jti")!.Value, true, expires);
    }
}
