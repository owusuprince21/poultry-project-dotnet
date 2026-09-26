using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace PoultryFarm.Marketplace.Services;

public sealed class GuestSession(ProtectedSessionStorage sessionStorage)
{
    private const string TokenKey = "marketplace_guest_token";
    private const string UserIdKey = "marketplace_guest_user_id";
    private const string DisplayNameKey = "marketplace_guest_display_name";
    private const string EmailKey = "marketplace_guest_email";
    private const string PhoneKey = "marketplace_guest_phone";
    private const string ReactorKeyStorage = "marketplace_reactor_key";

    public bool IsLoaded { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(Token);
    public string? Token { get; private set; }
    public Guid? UserId { get; private set; }
    public string? DisplayName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? ReactorKey { get; private set; }

    public async Task LoadAsync()
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            Token = (await sessionStorage.GetAsync<string>(TokenKey)).Value;
            UserId = (await sessionStorage.GetAsync<Guid?>(UserIdKey)).Value;
            DisplayName = (await sessionStorage.GetAsync<string>(DisplayNameKey)).Value;
            Email = (await sessionStorage.GetAsync<string>(EmailKey)).Value;
            Phone = (await sessionStorage.GetAsync<string>(PhoneKey)).Value;
            ReactorKey = (await sessionStorage.GetAsync<string>(ReactorKeyStorage)).Value;
        }
        catch (InvalidOperationException)
        {
            // Prerender / JS unavailable.
        }

        if (string.IsNullOrWhiteSpace(ReactorKey))
        {
            ReactorKey = UserId is Guid uid ? $"guest:{uid:N}" : $"anon:{Guid.NewGuid():N}";
            try
            {
                await sessionStorage.SetAsync(ReactorKeyStorage, ReactorKey);
            }
            catch (InvalidOperationException)
            {
                // Prerender / JS unavailable.
            }
        }

        IsLoaded = true;
    }

    public async Task SignInAsync(string token, Guid userId, string displayName, string? email, string? phone)
    {
        Token = token;
        UserId = userId;
        DisplayName = displayName;
        Email = email;
        Phone = phone;
        ReactorKey = $"guest:{userId:N}";
        IsLoaded = true;

        await sessionStorage.SetAsync(TokenKey, token);
        await sessionStorage.SetAsync(UserIdKey, userId);
        await sessionStorage.SetAsync(DisplayNameKey, displayName);
        await sessionStorage.SetAsync(EmailKey, email ?? string.Empty);
        await sessionStorage.SetAsync(PhoneKey, phone ?? string.Empty);
        await sessionStorage.SetAsync(ReactorKeyStorage, ReactorKey);
    }

    public async Task ClearAsync()
    {
        Token = null;
        UserId = null;
        DisplayName = null;
        Email = null;
        Phone = null;

        await sessionStorage.DeleteAsync(TokenKey);
        await sessionStorage.DeleteAsync(UserIdKey);
        await sessionStorage.DeleteAsync(DisplayNameKey);
        await sessionStorage.DeleteAsync(EmailKey);
        await sessionStorage.DeleteAsync(PhoneKey);
    }
}
