using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PoultryFarm.Infrastructure.Identity;
using QRCoder;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public sealed class AccountController(UserManager<ApplicationUser> userManager) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        return Ok(ToProfile(user));
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return BadRequest(new { username = "Username is required." });
        }

        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            return BadRequest(new { firstName = "First name is required." });
        }

        if (string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(new { lastName = "Last name is required." });
        }

        var username = request.Username.Trim();
        if (!string.Equals(user.UserName, username, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await userManager.FindByNameAsync(username);
            if (existing is not null && existing.Id != user.Id)
            {
                return BadRequest(new { username = "Username is already taken." });
            }

            var usernameResult = await userManager.SetUserNameAsync(user, username);
            if (!usernameResult.Succeeded)
            {
                return BadRequest(new { detail = usernameResult.Errors.Select(x => x.Description).ToArray() });
            }
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Email = request.Email?.Trim();

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        return Ok(ToProfile(user));
    }

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return BadRequest(new { currentPassword = "Current password is required." });
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new { newPassword = "New password is required." });
        }

        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return BadRequest(new { confirmPassword = "New passwords do not match." });
        }

        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { detail = result.Errors.Select(x => x.Description).ToArray() });
        }

        return Ok(new { detail = "Password changed successfully." });
    }

    [HttpPost("2fa/setup")]
    public async Task<IActionResult> SetupTwoFactor()
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        var rawKey = key ?? string.Empty;
        var authenticatorUri = BuildAuthenticatorUri(user.UserName ?? user.Id.ToString(), rawKey);

        return Ok(new
        {
            sharedKey = FormatAuthenticatorKey(rawKey),
            authenticatorUri,
            qrCodeSvg = CreateQrCodeSvg(authenticatorUri),
            isEnabled = user.TwoFactorEnabled,
            account = user.UserName
        });
    }

    [HttpPost("2fa/enable")]
    public async Task<IActionResult> EnableTwoFactor(TwoFactorCodeRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var code = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { code = "Verification code is required." });
        }

        var validCode = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);

        if (!validCode)
        {
            return BadRequest(new { detail = "Invalid two-factor authentication code." });
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);

        return Ok(new { detail = "Two-factor authentication enabled successfully." });
    }

    [HttpPost("2fa/disable")]
    public async Task<IActionResult> DisableTwoFactor(TwoFactorCodeRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user is null)
        {
            return Unauthorized(new { detail = "User account was not found." });
        }

        var code = NormalizeCode(request.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new { code = "Verification code is required." });
        }

        var validCode = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);

        if (!validCode)
        {
            return BadRequest(new { detail = "Invalid two-factor authentication code." });
        }

        await userManager.SetTwoFactorEnabledAsync(user, false);

        return Ok(new { detail = "Two-factor authentication disabled successfully." });
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var id)
            ? await userManager.FindByIdAsync(id.ToString())
            : null;
    }

    private static string NormalizeCode(string? code) =>
        (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);

    private static string FormatAuthenticatorKey(string key) =>
        string.Join(" ", key.Chunk(4).Select(x => new string(x))).ToLowerInvariant();

    private static string BuildAuthenticatorUri(string account, string key)
    {
        const string issuer = "Poultry Farm Management System";
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedAccount = Uri.EscapeDataString(account);
        return $"otpauth://totp/{encodedIssuer}:{encodedAccount}?secret={key}&issuer={encodedIssuer}&digits=6";
    }

    private static string CreateQrCodeSvg(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new SvgQRCode(data);
        return qrCode.GetGraphic(4);
    }

    private static object ToProfile(ApplicationUser user) =>
        new
        {
            id = user.Id,
            username = user.UserName,
            email = user.Email,
            firstName = user.FirstName,
            lastName = user.LastName,
            twoFactorEnabled = user.TwoFactorEnabled
        };
}

public sealed record UpdateProfileRequest(
    string Username,
    string FirstName,
    string LastName,
    string? Email);

public sealed record ChangePasswordRequest(
    string? CurrentPassword,
    string NewPassword,
    string ConfirmPassword);

public sealed record TwoFactorCodeRequest(string? Code);
