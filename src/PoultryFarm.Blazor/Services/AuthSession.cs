using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace PoultryFarm.Blazor.Services;

public sealed class AuthSession
{
    private readonly ProtectedSessionStorage _sessionStorage;

    private const string TokenKey = "auth_token";
    private const string UserIdKey = "auth_user_id";
    private const string UsernameKey = "auth_username";
    private const string FirstNameKey = "auth_first_name";
    private const string LastNameKey = "auth_last_name";
    private const string RoleKey = "auth_role";
    private const string CompanyIdKey = "auth_company_id";
    private const string CompanyNameKey = "auth_company_name";
    private const string CompanyCodeKey = "auth_company_code";
    private const string MustChangePasswordKey = "auth_must_change_password";

    public AuthSession(ProtectedSessionStorage sessionStorage)
    {
        _sessionStorage = sessionStorage;
    }

    public bool IsLoaded { get; private set; }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token);

    public string? Token { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Username { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? Role { get; private set; }
    public Guid? CompanyId { get; private set; }
    public string? CompanyName { get; private set; }
    public string? CompanyCode { get; private set; }
    public bool MustChangePassword { get; private set; }

    public string DisplayName
    {
        get
        {
            var fullName = $"{FirstName} {LastName}".Trim();
            return string.IsNullOrWhiteSpace(fullName) ? Username ?? "User" : fullName;
        }
    }

    public string FirstNameOrUsername =>
        string.IsNullOrWhiteSpace(FirstName) ? Username ?? "there" : FirstName;

    public string WorkspaceTitle =>
        IsSystemAdmin || string.IsNullOrWhiteSpace(CompanyName)
            ? "Poultry Farm Management System"
            : CompanyName;

    public bool IsSuperAdmin => Role is "super_admin" or "system_admin";
    public bool IsSubAdmin => Role == "sub_admin";
    public bool IsSystemAdmin => IsSuperAdmin || IsSubAdmin;
    public bool IsFarmAdmin => Role == "farm_admin";
    public bool IsWorker => Role == "worker";

    public async Task LoadAsync()
    {
        if (IsLoaded)
        {
            return;
        }

        Token = (await _sessionStorage.GetAsync<string>(TokenKey)).Value;
        UserId = (await _sessionStorage.GetAsync<Guid?>(UserIdKey)).Value;
        Username = (await _sessionStorage.GetAsync<string>(UsernameKey)).Value;
        FirstName = (await _sessionStorage.GetAsync<string>(FirstNameKey)).Value;
        LastName = (await _sessionStorage.GetAsync<string>(LastNameKey)).Value;
        Role = (await _sessionStorage.GetAsync<string>(RoleKey)).Value;
        CompanyId = (await _sessionStorage.GetAsync<Guid?>(CompanyIdKey)).Value;
        CompanyName = (await _sessionStorage.GetAsync<string>(CompanyNameKey)).Value;
        CompanyCode = (await _sessionStorage.GetAsync<string>(CompanyCodeKey)).Value;
        MustChangePassword = (await _sessionStorage.GetAsync<bool>(MustChangePasswordKey)).Value;

        IsLoaded = true;

    }

    public async Task SignInAsync(string token, LoginUser user)
    {
        Token = token;
        UserId = user.Id;
        Username = user.Username;
        FirstName = user.FirstName;
        LastName = user.LastName;
        Role = NormalizeRole(user.Role, user.IsSystemAdmin, user.IsSuperuser);
        CompanyId = user.Company?.Id;
        CompanyName = user.Company?.Name;
        CompanyCode = user.Company?.Code;
        MustChangePassword = user.MustChangePassword;
        IsLoaded = true;

        await _sessionStorage.SetAsync(TokenKey, Token);
        await _sessionStorage.SetAsync(UserIdKey, UserId);
        await _sessionStorage.SetAsync(UsernameKey, Username ?? string.Empty);
        await _sessionStorage.SetAsync(FirstNameKey, FirstName ?? string.Empty);
        await _sessionStorage.SetAsync(LastNameKey, LastName ?? string.Empty);
        await _sessionStorage.SetAsync(RoleKey, Role);
        if (CompanyId.HasValue)
        {
            await _sessionStorage.SetAsync(CompanyIdKey, CompanyId);
        }
        else
        {
            await _sessionStorage.DeleteAsync(CompanyIdKey);
        }
        await _sessionStorage.SetAsync(CompanyNameKey, CompanyName ?? string.Empty);
        await _sessionStorage.SetAsync(CompanyCodeKey, CompanyCode ?? string.Empty);
        await _sessionStorage.SetAsync(MustChangePasswordKey, MustChangePassword);

    }

    public async Task SignOutAsync()
    {
        Token = null;
        UserId = null;
        Username = null;
        FirstName = null;
        LastName = null;
        Role = null;
        CompanyId = null;
        CompanyName = null;
        CompanyCode = null;
        MustChangePassword = false;
        IsLoaded = true;

        await _sessionStorage.DeleteAsync(TokenKey);
        await _sessionStorage.DeleteAsync(UserIdKey);
        await _sessionStorage.DeleteAsync(UsernameKey);
        await _sessionStorage.DeleteAsync(FirstNameKey);
        await _sessionStorage.DeleteAsync(LastNameKey);
        await _sessionStorage.DeleteAsync(RoleKey);
        await _sessionStorage.DeleteAsync(CompanyIdKey);
        await _sessionStorage.DeleteAsync(CompanyNameKey);
        await _sessionStorage.DeleteAsync(CompanyCodeKey);
        await _sessionStorage.DeleteAsync(MustChangePasswordKey);
    }

    public async Task MarkPasswordChangedAsync()
    {
        MustChangePassword = false;
        await _sessionStorage.SetAsync(MustChangePasswordKey, false);
    }

    public async Task UpdateProfileAsync(string username, string? firstName, string? lastName)
    {
        Username = username;
        FirstName = firstName;
        LastName = lastName;

        await _sessionStorage.SetAsync(UsernameKey, Username ?? string.Empty);
        await _sessionStorage.SetAsync(FirstNameKey, FirstName ?? string.Empty);
        await _sessionStorage.SetAsync(LastNameKey, LastName ?? string.Empty);
    }

    private static string NormalizeRole(string? role, bool isSystemAdmin, bool isSuperuser)
    {
        var normalized = (role ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace("-", "_")
            .Replace(" ", "_");

        if (normalized is "superadmin" or "super_admin" or "superuser")
        {
            return "super_admin";
        }

        if (normalized is "subadmin" or "sub_admin")
        {
            return "sub_admin";
        }

        if (isSuperuser)
        {
            return "super_admin";
        }

        if (isSystemAdmin)
        {
            return "sub_admin";
        }

        return normalized switch
        {
            "systemadmin" => "super_admin",
            "system_admin" => "system_admin",

            "farmadmin" => "farm_admin",
            "farm_admin" => "farm_admin",
            "companyadmin" => "farm_admin",
            "company_admin" => "farm_admin",
            "admin" => "farm_admin",

            "farmworker" => "worker",
            "farm_worker" => "worker",
            "worker" => "worker",

            _ => normalized
        };
    }
}

public sealed record LoginResponse(
    bool Requires2Fa,
    string? Token,
    LoginUser? User,
    string? Username,
    string? Message);

public sealed record LoginUser(
    Guid Id,
    string? Username,
    string? Email,
    string? FirstName,
    string? LastName,
    string? Role,
    LoginCompany? Company,
    bool IsActive,
    bool IsSystemAdmin,
    bool IsSuperuser,
    bool MustChangePassword,
    bool TwoFactorEnabled);

public sealed record LoginCompany(
    Guid Id,
    string Name,
    string Code,
    string? Email,
    string? Phone,
    string? Address,
    bool IsActive);
