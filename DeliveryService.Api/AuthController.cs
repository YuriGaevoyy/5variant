using DeliveryService.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace DeliveryService.Api;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IUserRepository users,
    IPasswordHasher hasher,
    SessionTokens tokens,
    IMemoryCache cache) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("Login")]
    public async Task<ActionResult<LoginResult>> Login([FromBody] LoginInput input, CancellationToken ct)
    {
        var account = await users.FindLogin(input.Login.Trim(), ct);
        if (account is null || !hasher.Verify(input.Password, account.PasswordHash))
            return Unauthorized(new ProblemDetails { Title = "Неверный логин или пароль", Status = 401 });

        var user = new UserInfo(account.Id, account.Login, account.DisplayName, account.Role);
        return Ok(new LoginResult(tokens.Issue(user), user));
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        SessionTokens.Revoke(User, cache);
        return Ok(new { status = "ok" });
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<UserInfo> Me() => Ok(SessionTokens.Current(User));
}
