namespace PoultryFarm.Blazor.Services;

public sealed record ManagedCompany(
    Guid Id,
    string Name,
    string Code,
    string? Email,
    string? Phone,
    string? Address,
    bool IsActive);

public sealed record ManagedUser(
    Guid Id,
    string Username,
    string? Email,
    string? FirstName,
    string? LastName,
    string Role,
    Guid? CompanyId,
    string? CompanyName,
    string? CompanyCode,
    bool IsSystemAdmin,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset CreatedAt);

public sealed class CreateCompanyModel
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
}

public sealed class UpdateCompanyModel
{
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record TenantOverview(
    Guid Id,
    string Name,
    string Code,
    string? Email,
    string? Phone,
    bool IsActive,
    DateTimeOffset CreatedAt,
    int TotalUsers,
    int FarmAdmins,
    int Workers,
    int BlockedUsers,
    int ActiveBatches,
    int ActiveBirds);

public sealed record AuditLogEntry(
    DateTimeOffset Timestamp,
    string Event,
    string Actor,
    string Role,
    string Target,
    string Detail,
    string Category,
    string? CompanyName);

public sealed record AccountProfile(
    Guid Id,
    string? Username,
    string? Email,
    string? FirstName,
    string? LastName,
    bool TwoFactorEnabled);

public sealed class UpdateProfileModel
{
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public sealed class ChangePasswordModel
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed record TwoFactorSetup(
    string SharedKey,
    string AuthenticatorUri,
    string QrCodeSvg,
    bool IsEnabled,
    string? Account);

public sealed class CreateFarmAdminModel
{
    public Guid CompanyId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public sealed class CreateManagedUserModel
{
    public Guid? CompanyId { get; set; }
    public string Role { get; set; } = "worker";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public sealed class CreateSystemUserModel
{
    public string Role { get; set; } = "sub_admin";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public sealed class UpdateManagedUserModel
{
    public Guid? CompanyId { get; set; }
    public string Role { get; set; } = "worker";
    public string Username { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }

}

public sealed class CreateWorkerModel
{
    public Guid? CompanyId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Email { get; set; }
}

public sealed record AssistanceMessage(
    Guid Id,
    string Sender,
    string Body,
    DateTimeOffset SentAt);

public sealed record ChatContact(
    Guid Id,
    string Username,
    string? FirstName,
    string? LastName,
    string Role,
    Guid? CompanyId,
    string? CompanyName,
    DateTimeOffset? LastSeenAt,
    bool IsOnline,
    int UnreadCount = 0,
    string? LastMessagePreview = null,
    DateTimeOffset? LastMessageAt = null,
    string? ListingTitle = null,
    string? Email = null,
    string? Phone = null,
    bool CanReply = true);

public sealed record TeamChatMessage(
    Guid Id,
    Guid SenderUserId,
    Guid RecipientUserId,
    string Body,
    DateTimeOffset SentAt,
    bool IsMine,
    string? SenderDisplayName = null,
    string? RecipientDisplayName = null,
    DateTimeOffset? ReadAt = null,
    Guid? ReplyToMessageId = null,
    string? ReplyPreview = null,
    string? ReplySenderDisplayName = null,
    DateTimeOffset? EditedAt = null,
    bool CanEdit = false,
    bool CanDelete = false,
    IReadOnlyCollection<ChatReaction>? Reactions = null);

public sealed record ChatReaction(
    string Emoji,
    int Count,
    bool ReactedByMe);
