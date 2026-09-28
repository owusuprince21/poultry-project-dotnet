using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PoultryFarm.Api.Authorization;
using PoultryFarm.Api.Services;
using PoultryFarm.Application.Common.Interfaces;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Communication;
using PoultryFarm.Domain.Companies;
using PoultryFarm.Domain.Marketplace;
using PoultryFarm.Infrastructure.Identity;
using PoultryFarm.Infrastructure.Persistence;

namespace PoultryFarm.Api.Controllers;

[ApiController]
[Route("api/marketplace")]
public sealed class MarketplaceController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IConfiguration configuration,
    JwtTokenIssuer tokenIssuer,
    IChatMessageProtector messageProtector,
    IActivityNotifier activityNotifier,
    IEmailSender emailSender,
    ControllerAudit audit,
    MarketplaceChatFanout chatFanout) : ControllerBase
{
    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/jpg",
        "image/png",
        "image/webp",
        "image/avif",
        "image/gif"
    };

    private static readonly HashSet<string> KnownImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".avif",
        ".gif"
    };

    private static string? ResolveImageContentType(string? contentType, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType) && AllowedImageContentTypes.Contains(contentType))
        {
            return contentType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : contentType;
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(extension))
        {
            return null;
        }

        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            ".gif" => "image/gif",
            _ => null
        };
    }

    [AllowAnonymous]
    [HttpPost("registrations")]
    public async Task<ActionResult<FarmerRegistrationDto>> SubmitRegistration(
        CreateFarmerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FarmName))
        {
            return BadRequest(new { farmName = "Farm name is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new { email = "Email is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { phone = "Phone is required." });
        }

        if (string.IsNullOrWhiteSpace(request.ContactFirstName) || string.IsNullOrWhiteSpace(request.ContactLastName))
        {
            return BadRequest(new { detail = "Contact first and last name are required." });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var pendingExists = await dbContext.FarmerRegistrations.AnyAsync(
            x => x.Email == email && x.Status == FarmerRegistrationStatus.Pending,
            cancellationToken);
        if (pendingExists)
        {
            return BadRequest(new { email = "A pending registration already exists for this email." });
        }

        var registration = new FarmerRegistration
        {
            FarmName = request.FarmName.Trim(),
            ContactFirstName = request.ContactFirstName.Trim(),
            ContactLastName = request.ContactLastName.Trim(),
            Email = email,
            Phone = request.Phone.Trim(),
            Location = request.Location?.Trim(),
            Notes = request.Notes?.Trim(),
            RequestedUsername = null,
            Status = FarmerRegistrationStatus.Pending
        };

        dbContext.FarmerRegistrations.Add(registration);
        await dbContext.SaveChangesAsync(cancellationToken);

        var applicantName = $"{registration.ContactFirstName} {registration.ContactLastName}".Trim();
        await activityNotifier.NotifySystemAdminsAsync(
            "New farm registration",
            $"{applicantName} applied for {registration.FarmName}. Review KYC and approve or reject.",
            actorName: applicantName,
            kind: "activity",
            targetType: "farmer_registration",
            targetId: registration.Id,
            cancellationToken: cancellationToken);

        return Ok(ToRegistrationDto(registration));
    }

    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    [HttpGet("registrations/pending-count")]
    public async Task<ActionResult<object>> GetPendingRegistrationCount(CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var count = await dbContext.FarmerRegistrations.CountAsync(
            x => x.Status == FarmerRegistrationStatus.Pending,
            cancellationToken);

        return Ok(new { count });
    }

    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    [HttpGet("registrations")]
    public async Task<ActionResult<IReadOnlyCollection<FarmerRegistrationDto>>> GetRegistrations(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var query = dbContext.FarmerRegistrations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<FarmerRegistrationStatus>(status, true, out var parsed))
        {
            query = query.Where(x => x.Status == parsed);
        }

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new FarmerRegistrationDto(
                x.Id,
                x.FarmName,
                x.ContactFirstName,
                x.ContactLastName,
                x.Email,
                x.Phone,
                x.Location,
                x.Notes,
                x.RequestedUsername,
                x.Status.ToString(),
                x.ReviewNotes,
                x.ReviewedAt,
                x.CompanyId,
                x.FarmAdminUserId,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    [HttpPost("registrations/{id:guid}/approve")]
    public async Task<ActionResult<ApproveRegistrationResponse>> ApproveRegistration(
        Guid id,
        ApproveFarmerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var registration = await dbContext.FarmerRegistrations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (registration is null)
        {
            return NotFound(new { detail = "Registration was not found." });
        }

        if (registration.Status != FarmerRegistrationStatus.Pending)
        {
            return BadRequest(new { detail = "Only pending registrations can be approved." });
        }

        var username = await CreateUniqueUsernameAsync(
            registration.ContactFirstName,
            registration.ContactLastName,
            cancellationToken);

        var normalizedFarmName = registration.FarmName.Trim().ToLowerInvariant();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var company = await dbContext.Companies
            .FirstOrDefaultAsync(x => x.Name.ToLower() == normalizedFarmName, cancellationToken);
        if (company is null)
        {
            company = new Company
            {
                Name = registration.FarmName.Trim(),
                Code = await NextCompanyCodeAsync(cancellationToken),
                Email = registration.Email,
                Phone = registration.Phone,
                Address = registration.Location,
                IsActive = true
            };
            dbContext.Companies.Add(company);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var temporaryPassword = string.IsNullOrWhiteSpace(request.TemporaryPassword)
            ? GenerateTemporaryPassword()
            : request.TemporaryPassword.Trim();

        var farmAdmin = new ApplicationUser
        {
            UserName = username,
            Email = registration.Email,
            FirstName = registration.ContactFirstName,
            LastName = registration.ContactLastName,
            CompanyId = company.Id,
            FarmRole = UserRole.Admin,
            IsSystemAdmin = false,
            MustChangePassword = true,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(farmAdmin, temporaryPassword);
        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { detail = createResult.Errors.Select(x => x.Description).ToArray() });
        }

        await userManager.AddToRoleAsync(farmAdmin, "Admin");

        var invite = await CreatePasswordInviteAsync(farmAdmin.Id, cancellationToken);

        registration.Status = FarmerRegistrationStatus.Approved;
        registration.ReviewedAt = DateTimeOffset.UtcNow;
        registration.ReviewedByUserId = GetCurrentUserId();
        registration.ReviewNotes = request.ReviewNotes?.Trim();
        registration.CompanyId = company.Id;
        registration.FarmAdminUserId = farmAdmin.Id;

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var farmAppBase = configuration["Apps:FarmBlazorBaseUrl"] ?? "http://localhost:5083";
        var inviteUrl = $"{farmAppBase.TrimEnd('/')}/setup-password?token={Uri.EscapeDataString(invite.RawToken)}";

        var emailSent = await SendApprovalInviteEmailAsync(
            registration.Email,
            registration.ContactFirstName,
            registration.FarmName,
            username,
            inviteUrl,
            invite.ExpiresAt,
            cancellationToken);

        await audit.WriteAsync(
            "Approve",
            "Marketplace",
            "FarmRegistration",
            registration.FarmName,
            $"Approved farmer registration for {registration.FarmName} ({registration.Email}).",
            registration.Id,
            company.Id,
            company.Name,
            cancellationToken: cancellationToken);

        return Ok(new ApproveRegistrationResponse(
            ToRegistrationDto(registration),
            company.Id,
            company.Code,
            farmAdmin.Id,
            username,
            temporaryPassword,
            invite.RawToken,
            inviteUrl,
            invite.ExpiresAt,
            emailSent));
    }

    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    [HttpPost("registrations/{id:guid}/reject")]
    public async Task<ActionResult<FarmerRegistrationDto>> RejectRegistration(
        Guid id,
        RejectFarmerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var registration = await dbContext.FarmerRegistrations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (registration is null)
        {
            return NotFound(new { detail = "Registration was not found." });
        }

        if (registration.Status != FarmerRegistrationStatus.Pending)
        {
            return BadRequest(new { detail = "Only pending registrations can be rejected." });
        }

        registration.Status = FarmerRegistrationStatus.Rejected;
        registration.ReviewedAt = DateTimeOffset.UtcNow;
        registration.ReviewedByUserId = GetCurrentUserId();
        registration.ReviewNotes = request.ReviewNotes?.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            "Reject",
            "Marketplace",
            "FarmRegistration",
            registration.FarmName,
            $"Rejected farmer registration for {registration.FarmName} ({registration.Email}).",
            registration.Id,
            cancellationToken: cancellationToken);

        return Ok(ToRegistrationDto(registration));
    }

    [Authorize(Policy = AppPolicies.SystemAdminOnly)]
    [HttpPost("registrations/{id:guid}/resend-invite")]
    public async Task<ActionResult<ResendInviteResponse>> ResendInvite(Guid id, CancellationToken cancellationToken)
    {
        if (!PermissionHelpers.IsSuperAdmin(User))
        {
            return Forbid();
        }

        var registration = await dbContext.FarmerRegistrations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (registration is null || registration.FarmAdminUserId is null)
        {
            return NotFound(new { detail = "Approved registration with farm admin was not found." });
        }

        var invite = await CreatePasswordInviteAsync(registration.FarmAdminUserId.Value, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var farmAppBase = configuration["Apps:FarmBlazorBaseUrl"] ?? "http://localhost:5083";
        var inviteUrl = $"{farmAppBase.TrimEnd('/')}/setup-password?token={Uri.EscapeDataString(invite.RawToken)}";

        var username = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == registration.FarmAdminUserId.Value)
            .Select(x => x.UserName)
            .FirstOrDefaultAsync(cancellationToken) ?? registration.RequestedUsername ?? "farmadmin";

        var emailSent = await SendApprovalInviteEmailAsync(
            registration.Email,
            registration.ContactFirstName,
            registration.FarmName,
            username,
            inviteUrl,
            invite.ExpiresAt,
            cancellationToken);

        return Ok(new ResendInviteResponse(invite.RawToken, inviteUrl, invite.ExpiresAt, emailSent));
    }

    [AllowAnonymous]
    [HttpGet("farms")]
    public async Task<ActionResult<IReadOnlyCollection<MarketplaceFarmCardDto>>> GetPublicFarms(
        CancellationToken cancellationToken)
    {
        var farms = await QueryVisibleMarketplaceFarmsAsync(cancellationToken);
        return Ok(farms);
    }

    [AllowAnonymous]
    [HttpGet("farms/{id:guid}")]
    public async Task<ActionResult<MarketplaceFarmDetailDto>> GetPublicFarm(
        Guid id,
        CancellationToken cancellationToken)
    {
        var registration = await (
            from reg in dbContext.FarmerRegistrations.AsNoTracking()
            join company in dbContext.Companies.AsNoTracking() on reg.CompanyId equals company.Id
            join user in dbContext.Users.AsNoTracking() on reg.FarmAdminUserId equals user.Id
            where reg.CompanyId == id
                  && reg.Status == FarmerRegistrationStatus.Approved
                  && company.IsActive
                  && !user.IsDeleted
                  && !user.MustChangePassword
                  && user.LastSeenAt != null
            select new { reg, company, user })
            .FirstOrDefaultAsync(cancellationToken);

        if (registration is null)
        {
            return NotFound(new { detail = "Farm was not found or is not yet active on the marketplace." });
        }

        var listings = await dbContext.MarketplaceListings
            .AsNoTracking()
            .Where(x => x.CompanyId == id && x.Status == MarketplaceListingStatus.Published)
            .OrderByDescending(x => x.PublishedAt)
            .Select(x => new MarketplaceListingPublicDto(
                x.Id,
                x.CompanyId,
                x.Company!.Name,
                x.ListingType.ToString(),
                x.Title,
                x.Description,
                x.QuantityOffered,
                x.UnitLabel,
                x.PriceAmount,
                x.PriceText,
                x.ImageUrl,
                x.PublishedAt))
            .ToListAsync(cancellationToken);

        var sellsEggs = listings.Any(x => x.ListingType.Equals("Eggs", StringComparison.OrdinalIgnoreCase));
        var sellsBirds = listings.Any(x => x.ListingType.Equals("Birds", StringComparison.OrdinalIgnoreCase));
        var location = FirstNonEmpty(registration.reg.Location, registration.company.Address);
        var summary = BuildFarmSummary(registration.reg.Notes, sellsEggs, sellsBirds);

        return Ok(new MarketplaceFarmDetailDto(
            registration.company.Id,
            registration.company.Name,
            location,
            summary,
            sellsEggs,
            sellsBirds,
            listings.Count,
            registration.company.Phone ?? registration.reg.Phone,
            registration.company.Email ?? registration.reg.Email,
            $"{registration.reg.ContactFirstName} {registration.reg.ContactLastName}".Trim(),
            registration.reg.Notes,
            registration.reg.ReviewedAt ?? registration.reg.CreatedAt,
            listings));
    }

    private async Task<IReadOnlyCollection<MarketplaceFarmCardDto>> QueryVisibleMarketplaceFarmsAsync(
        CancellationToken cancellationToken)
    {
        var rows = await (
            from reg in dbContext.FarmerRegistrations.AsNoTracking()
            join company in dbContext.Companies.AsNoTracking() on reg.CompanyId equals company.Id
            join user in dbContext.Users.AsNoTracking() on reg.FarmAdminUserId equals user.Id
            where reg.Status == FarmerRegistrationStatus.Approved
                  && company.IsActive
                  && !user.IsDeleted
                  && !user.MustChangePassword
                  && user.LastSeenAt != null
            orderby company.Name
            select new
            {
                company.Id,
                company.Name,
                Location = reg.Location ?? company.Address,
                reg.Notes,
                Phone = company.Phone ?? reg.Phone,
                JoinedAt = reg.ReviewedAt ?? reg.CreatedAt
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        var companyIds = rows.Select(x => x.Id).ToList();
        var listingStats = await dbContext.MarketplaceListings
            .AsNoTracking()
            .Where(x => companyIds.Contains(x.CompanyId) && x.Status == MarketplaceListingStatus.Published)
            .GroupBy(x => x.CompanyId)
            .Select(g => new
            {
                CompanyId = g.Key,
                Count = g.Count(),
                SellsEggs = g.Any(x => x.ListingType == MarketplaceListingType.Eggs),
                SellsBirds = g.Any(x => x.ListingType == MarketplaceListingType.Birds)
            })
            .ToListAsync(cancellationToken);

        var statsByCompany = listingStats.ToDictionary(x => x.CompanyId);

        return rows.Select(row =>
        {
            statsByCompany.TryGetValue(row.Id, out var stats);
            var sellsEggs = stats?.SellsEggs ?? false;
            var sellsBirds = stats?.SellsBirds ?? false;
            return new MarketplaceFarmCardDto(
                row.Id,
                row.Name,
                row.Location,
                BuildFarmSummary(row.Notes, sellsEggs, sellsBirds),
                sellsEggs,
                sellsBirds,
                stats?.Count ?? 0,
                row.Phone,
                row.JoinedAt);
        }).ToList();
    }

    private static string BuildFarmSummary(string? notes, bool sellsEggs, bool sellsBirds)
    {
        if (!string.IsNullOrWhiteSpace(notes))
        {
            var text = notes.Trim().Replace('\n', ' ');
            return text.Length > 160 ? text[..160].TrimEnd() + "…" : text;
        }

        if (sellsEggs && sellsBirds)
        {
            return "Verified poultry farm offering eggs and birds.";
        }

        if (sellsEggs)
        {
            return "Verified poultry farm offering fresh eggs.";
        }

        if (sellsBirds)
        {
            return "Verified poultry farm offering bird batches.";
        }

        return "Verified poultry farm on Akokɔ Papa.";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    [AllowAnonymous]
    [HttpGet("listings")]
    public async Task<ActionResult<IReadOnlyCollection<MarketplaceListingPublicDto>>> GetPublicListings(
        CancellationToken cancellationToken)
    {
        var listings = await dbContext.MarketplaceListings
            .AsNoTracking()
            .Where(x => x.Status == MarketplaceListingStatus.Published)
            .OrderByDescending(x => x.PublishedAt)
            .Select(x => new MarketplaceListingPublicDto(
                x.Id,
                x.CompanyId,
                x.Company!.Name,
                x.ListingType.ToString(),
                x.Title,
                x.Description,
                x.QuantityOffered,
                x.UnitLabel,
                x.PriceAmount,
                x.PriceText,
                x.ImageUrl,
                x.PublishedAt))
            .ToListAsync(cancellationToken);

        return Ok(listings);
    }

    [AllowAnonymous]
    [HttpGet("listings/{id:guid}")]
    public async Task<ActionResult<MarketplaceListingPublicDto>> GetPublicListing(Guid id, CancellationToken cancellationToken)
    {
        var listing = await dbContext.MarketplaceListings
            .AsNoTracking()
            .Where(x => x.Id == id && x.Status == MarketplaceListingStatus.Published)
            .Select(x => new MarketplaceListingPublicDto(
                x.Id,
                x.CompanyId,
                x.Company!.Name,
                x.ListingType.ToString(),
                x.Title,
                x.Description,
                x.QuantityOffered,
                x.UnitLabel,
                x.PriceAmount,
                x.PriceText,
                x.ImageUrl,
                x.PublishedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return listing is null ? NotFound() : Ok(listing);
    }

    [AllowAnonymous]
    [HttpGet("activities")]
    public async Task<ActionResult<IReadOnlyCollection<FarmActivityPublicDto>>> GetPublicActivities(
        [FromQuery] string? reactorKey,
        CancellationToken cancellationToken)
    {
        var normalizedReactorKey = ResolveReactorKey(reactorKey);
        var currentUserId = GetCurrentUserId();

        var postRows = await dbContext.FarmActivityPosts
            .AsNoTracking()
            .Where(x => x.IsPublished)
            .OrderByDescending(x => x.PublishedAt)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                FarmName = x.Company!.Name,
                x.Title,
                x.Body,
                x.MediaUrl,
                x.PublishedAt,
                LikeCount = x.Likes.Count(),
                CommentCount = x.Comments.Count(),
                x.ShareCount,
                LikedByMe = normalizedReactorKey != null
                    && x.Likes.Any(l => l.ReactorKey == normalizedReactorKey)
            })
            .ToListAsync(cancellationToken);

        if (postRows.Count == 0)
        {
            return Ok(Array.Empty<FarmActivityPublicDto>());
        }

        var postIds = postRows.Select(x => x.Id).ToList();

        var commentRows = await dbContext.FarmActivityComments
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.PostId,
                x.ParentCommentId,
                x.AuthorName,
                x.AuthorCompanyId,
                x.AuthorUserId,
                x.AuthorReactorKey,
                x.Body,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var commentIds = commentRows.Select(x => x.Id).ToList();
        var reactionRows = commentIds.Count == 0
            ? []
            : await dbContext.FarmActivityCommentReactions
                .AsNoTracking()
                .Where(x => commentIds.Contains(x.CommentId))
                .Select(x => new { x.CommentId, x.Emoji, x.ReactorKey })
                .ToListAsync(cancellationToken);

        var reactionsByComment = reactionRows
            .GroupBy(x => x.CommentId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.Emoji)
                    .Select(emojiGroup => new FarmActivityCommentReactionDto(
                        emojiGroup.Key,
                        emojiGroup.Count(),
                        normalizedReactorKey != null
                            && emojiGroup.Any(r => r.ReactorKey == normalizedReactorKey)))
                    .OrderByDescending(x => x.Count)
                    .ToList());

        bool CanDelete(Guid? authorUserId, string? authorReactorKey) =>
            (normalizedReactorKey != null && authorReactorKey == normalizedReactorKey)
            || (currentUserId != null && authorUserId == currentUserId);

        List<FarmActivityCommentReactionDto> ReactionsFor(Guid commentId) =>
            reactionsByComment.TryGetValue(commentId, out var list) ? list : [];

        var commentsByPost = commentRows
            .GroupBy(x => x.PostId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var imageRows = await dbContext.FarmActivityImages
            .AsNoTracking()
            .Where(x => postIds.Contains(x.PostId))
            .OrderBy(x => x.SortOrder)
            .Select(x => new { x.PostId, x.Url })
            .ToListAsync(cancellationToken);
        var imagesByPost = imageRows
            .GroupBy(x => x.PostId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Url).ToList());

        List<string> MediaFor(Guid postId, string? mediaUrl)
        {
            if (imagesByPost.TryGetValue(postId, out var urls) && urls.Count > 0)
            {
                return urls;
            }

            return string.IsNullOrWhiteSpace(mediaUrl) ? [] : [mediaUrl];
        }

        var result = postRows.Select(post =>
        {
            commentsByPost.TryGetValue(post.Id, out var postComments);
            postComments ??= [];

            var roots = postComments
                .Where(c => c.ParentCommentId is null)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c =>
                {
                    var replies = postComments
                        .Where(r => r.ParentCommentId == c.Id)
                        .OrderByDescending(r => r.CreatedAt)
                        .Select(r => new FarmActivityCommentDto(
                            r.Id,
                            r.ParentCommentId,
                            r.AuthorName,
                            r.AuthorCompanyId,
                            r.Body,
                            r.CreatedAt,
                            CanDelete(r.AuthorUserId, r.AuthorReactorKey),
                            ReactionsFor(r.Id),
                            Array.Empty<FarmActivityCommentDto>()))
                        .ToList();

                    return new FarmActivityCommentDto(
                        c.Id,
                        c.ParentCommentId,
                        c.AuthorName,
                        c.AuthorCompanyId,
                        c.Body,
                        c.CreatedAt,
                        CanDelete(c.AuthorUserId, c.AuthorReactorKey),
                        ReactionsFor(c.Id),
                        replies);
                })
                .ToList();

            return new FarmActivityPublicDto(
                post.Id,
                post.CompanyId,
                post.FarmName,
                post.Title,
                post.Body,
                post.MediaUrl,
                MediaFor(post.Id, post.MediaUrl),
                post.PublishedAt,
                post.LikeCount,
                post.CommentCount,
                post.ShareCount,
                post.LikedByMe,
                roots);
        }).ToList();

        return Ok(result);
    }

    [AllowAnonymous]
    [HttpPost("activities/{id:guid}/likes")]
    public async Task<ActionResult<FarmActivityLikeResponse>> ToggleActivityLike(
        Guid id,
        ToggleFarmActivityLikeRequest request,
        CancellationToken cancellationToken)
    {
        var post = await dbContext.FarmActivityPosts
            .FirstOrDefaultAsync(x => x.Id == id && x.IsPublished, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        var reactorKey = ResolveReactorKey(request.ReactorKey);
        if (string.IsNullOrWhiteSpace(reactorKey))
        {
            return BadRequest(new { reactorKey = "A reactor key or signed-in user is required to like a post." });
        }

        var existing = await dbContext.FarmActivityLikes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.PostId == id && x.ReactorKey == reactorKey, cancellationToken);

        var liked = false;
        if (existing is null)
        {
            dbContext.FarmActivityLikes.Add(new FarmActivityLike
            {
                PostId = id,
                UserId = GetCurrentUserId(),
                ReactorKey = reactorKey,
                CreatedByUserId = GetCurrentUserId()
            });
            liked = true;
        }
        else
        {
            dbContext.FarmActivityLikes.Remove(existing);
            liked = false;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var likeCount = await dbContext.FarmActivityLikes
            .CountAsync(x => x.PostId == id && !x.IsDeleted, cancellationToken);

        return Ok(new FarmActivityLikeResponse(id, likeCount, liked));
    }

    [AllowAnonymous]
    [HttpPost("activities/{id:guid}/share")]
    public async Task<ActionResult<FarmActivityShareResponse>> ShareActivity(
        Guid id,
        CancellationToken cancellationToken)
    {
        var post = await dbContext.FarmActivityPosts
            .FirstOrDefaultAsync(x => x.Id == id && x.IsPublished, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        post.ShareCount += 1;
        post.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var marketplaceBase = (configuration["Apps:MarketplaceBaseUrl"] ?? "http://localhost:5084").TrimEnd('/');
        var shareUrl = $"{marketplaceBase}/activities?post={id}";
        return Ok(new FarmActivityShareResponse(id, post.ShareCount, shareUrl));
    }

    [AllowAnonymous]
    [HttpPost("activities/{id:guid}/comments")]
    public async Task<ActionResult<FarmActivityCommentDto>> AddActivityComment(
        Guid id,
        CreateFarmActivityCommentRequest request,
        CancellationToken cancellationToken)
    {
        var post = await dbContext.FarmActivityPosts
            .FirstOrDefaultAsync(x => x.Id == id && x.IsPublished, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        var body = request.Body?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            return BadRequest(new { body = "Comment is required." });
        }

        if (body.Length > 2000)
        {
            return BadRequest(new { body = "Comment must be 2000 characters or fewer." });
        }

        string authorName;
        Guid? authorUserId = GetCurrentUserId();
        Guid? authorCompanyId = null;

        // Marketplace guests must type a name. Prefer the submitted name whenever present.
        if (!string.IsNullOrWhiteSpace(request.AuthorName))
        {
            authorName = request.AuthorName.Trim();
        }
        else if (User.Identity?.IsAuthenticated == true)
        {
            var user = authorUserId is Guid uid
                ? await userManager.FindByIdAsync(uid.ToString())
                : null;
            if (user is null)
            {
                return BadRequest(new { authorName = "Your name is required to comment." });
            }

            authorCompanyId = user.CompanyId;
            if (user.CompanyId is Guid companyId)
            {
                var companyName = await dbContext.Companies
                    .AsNoTracking()
                    .Where(x => x.Id == companyId)
                    .Select(x => x.Name)
                    .FirstOrDefaultAsync(cancellationToken);
                authorName = string.IsNullOrWhiteSpace(companyName)
                    ? DisplayBuyerName(user)
                    : companyName;
            }
            else
            {
                authorName = DisplayBuyerName(user);
            }
        }
        else
        {
            return BadRequest(new { authorName = "Your name is required to comment." });
        }

        if (authorName.Length > 200)
        {
            authorName = authorName[..200];
        }

        Guid? parentCommentId = null;
        if (request.ParentCommentId is Guid parentId)
        {
            var parent = await dbContext.FarmActivityComments
                .FirstOrDefaultAsync(x => x.Id == parentId && x.PostId == post.Id, cancellationToken);
            if (parent is null)
            {
                return BadRequest(new { parentCommentId = "Parent comment was not found on this post." });
            }

            // One-level threads: replies always hang off the root comment.
            parentCommentId = parent.ParentCommentId ?? parent.Id;
        }

        var authorReactorKey = ResolveReactorKey(request.ReactorKey);

        var comment = new FarmActivityComment
        {
            PostId = post.Id,
            ParentCommentId = parentCommentId,
            AuthorUserId = authorUserId,
            AuthorCompanyId = authorCompanyId,
            AuthorReactorKey = authorReactorKey,
            AuthorName = authorName,
            Body = body,
            CreatedByUserId = authorUserId
        };

        dbContext.FarmActivityComments.Add(comment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new FarmActivityCommentDto(
            comment.Id,
            comment.ParentCommentId,
            comment.AuthorName,
            comment.AuthorCompanyId,
            comment.Body,
            comment.CreatedAt,
            true,
            [],
            []));
    }

    [AllowAnonymous]
    [HttpDelete("activities/comments/{commentId:guid}")]
    public async Task<IActionResult> DeleteActivityComment(
        Guid commentId,
        [FromQuery] string? reactorKey,
        CancellationToken cancellationToken)
    {
        var comment = await dbContext.FarmActivityComments
            .Include(x => x.Post)
            .FirstOrDefaultAsync(x => x.Id == commentId, cancellationToken);
        if (comment is null || comment.Post is null || !comment.Post.IsPublished)
        {
            return NotFound();
        }

        var normalizedReactorKey = ResolveReactorKey(reactorKey);
        var currentUserId = GetCurrentUserId();
        var ownsComment =
            (normalizedReactorKey != null && comment.AuthorReactorKey == normalizedReactorKey)
            || (currentUserId is Guid userId && comment.AuthorUserId == userId);

        if (!ownsComment)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { detail = "You can only delete your own comments." });
        }

        comment.IsDeleted = true;
        comment.UpdatedAt = DateTimeOffset.UtcNow;
        comment.UpdatedByUserId = currentUserId;

        if (comment.ParentCommentId is null)
        {
            var replies = await dbContext.FarmActivityComments
                .Where(x => x.ParentCommentId == comment.Id)
                .ToListAsync(cancellationToken);
            foreach (var reply in replies)
            {
                reply.IsDeleted = true;
                reply.UpdatedAt = DateTimeOffset.UtcNow;
                reply.UpdatedByUserId = currentUserId;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { detail = "Comment deleted." });
    }

    [AllowAnonymous]
    [HttpPost("activities/comments/{commentId:guid}/reactions")]
    public async Task<ActionResult<FarmActivityCommentReactionToggleResponse>> ToggleCommentReaction(
        Guid commentId,
        ToggleFarmActivityCommentReactionRequest request,
        CancellationToken cancellationToken)
    {
        var comment = await dbContext.FarmActivityComments
            .Include(x => x.Post)
            .FirstOrDefaultAsync(x => x.Id == commentId, cancellationToken);
        if (comment is null || comment.Post is null || !comment.Post.IsPublished)
        {
            return NotFound();
        }

        if (!FarmActivityReactionEmojis.IsAllowed(request.Emoji))
        {
            return BadRequest(new { emoji = "Choose one of the supported reaction emojis." });
        }

        var reactorKey = ResolveReactorKey(request.ReactorKey);
        if (string.IsNullOrWhiteSpace(reactorKey))
        {
            return BadRequest(new { reactorKey = "A reactor key is required to react." });
        }

        var emoji = request.Emoji.Trim();
        var existing = await dbContext.FarmActivityCommentReactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                x => x.CommentId == commentId && x.ReactorKey == reactorKey && x.Emoji == emoji,
                cancellationToken);

        var reacted = false;
        if (existing is null)
        {
            dbContext.FarmActivityCommentReactions.Add(new FarmActivityCommentReaction
            {
                CommentId = commentId,
                Emoji = emoji,
                ReactorKey = reactorKey,
                UserId = GetCurrentUserId(),
                CreatedByUserId = GetCurrentUserId()
            });
            reacted = true;
        }
        else
        {
            dbContext.FarmActivityCommentReactions.Remove(existing);
            reacted = false;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var reactionRows = await dbContext.FarmActivityCommentReactions
            .AsNoTracking()
            .Where(x => x.CommentId == commentId)
            .Select(x => new { x.Emoji, x.ReactorKey })
            .ToListAsync(cancellationToken);

        var reactions = reactionRows
            .GroupBy(x => x.Emoji)
            .Select(g => new FarmActivityCommentReactionDto(
                g.Key,
                g.Count(),
                g.Any(r => r.ReactorKey == reactorKey)))
            .OrderByDescending(x => x.Count)
            .ToList();

        return Ok(new FarmActivityCommentReactionToggleResponse(commentId, reacted, reactions));
    }

    [AllowAnonymous]
    [HttpPost("inquiries")]
    public async Task<ActionResult<MarketplaceInquiryDto>> CreateInquiry(
        CreateMarketplaceInquiryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ListingId == Guid.Empty)
        {
            return BadRequest(new { listingId = "Listing is required." });
        }

        if (string.IsNullOrWhiteSpace(request.BuyerName))
        {
            return BadRequest(new { buyerName = "Buyer name is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { message = "Message is required." });
        }

        if (string.IsNullOrWhiteSpace(request.BuyerEmail) && string.IsNullOrWhiteSpace(request.BuyerPhone))
        {
            return BadRequest(new { detail = "Provide an email or phone number." });
        }

        var listing = await dbContext.MarketplaceListings
            .FirstOrDefaultAsync(x => x.Id == request.ListingId && x.Status == MarketplaceListingStatus.Published, cancellationToken);
        if (listing is null)
        {
            return NotFound(new { detail = "Listing was not found or is not published." });
        }

        var inquiry = new MarketplaceInquiry
        {
            ListingId = listing.Id,
            CompanyId = listing.CompanyId,
            BuyerName = request.BuyerName.Trim(),
            BuyerEmail = request.BuyerEmail?.Trim(),
            BuyerPhone = request.BuyerPhone?.Trim(),
            Message = request.Message.Trim(),
            QuantityRequested = request.QuantityRequested,
            Status = MarketplaceInquiryStatus.New
        };

        dbContext.MarketplaceInquiries.Add(inquiry);
        await dbContext.SaveChangesAsync(cancellationToken);

        var preview = inquiry.Message.Length > 140 ? inquiry.Message[..140] + "…" : inquiry.Message;
        await activityNotifier.NotifyCompanyAsync(
            listing.CompanyId,
            $"Buyer message · {listing.Title}",
            $"{inquiry.BuyerName} left a message: {preview}",
            actorName: inquiry.BuyerName,
            kind: "marketplace_inquiry",
            targetType: "marketplace_inquiry",
            targetId: inquiry.Id,
            recipientRoles: [UserRole.Admin, UserRole.Worker],
            cancellationToken: cancellationToken);

        return Ok(ToInquiryDto(inquiry, listing.Title));
    }

    [AllowAnonymous]
    [HttpPost("guest-session")]
    public async Task<ActionResult<GuestSessionResponse>> CreateGuestSession(
        CreateGuestSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { name = "Name is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Email) && string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { detail = "Provide an email or phone number." });
        }

        var email = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : request.Email.Trim().ToLowerInvariant();
        var phone = request.Phone?.Trim();
        var nameParts = request.Name.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts[0];
        var lastName = nameParts.Length > 1 ? nameParts[1] : string.Empty;

        ApplicationUser? buyer = null;
        if (!string.IsNullOrWhiteSpace(email))
        {
            buyer = await userManager.Users.FirstOrDefaultAsync(
                x => x.FarmRole == UserRole.MarketplaceBuyer && x.Email == email && !x.IsDeleted,
                cancellationToken);
        }

        if (buyer is null && !string.IsNullOrWhiteSpace(phone))
        {
            buyer = await userManager.Users.FirstOrDefaultAsync(
                x => x.FarmRole == UserRole.MarketplaceBuyer && x.PhoneNumber == phone && !x.IsDeleted,
                cancellationToken);
        }

        if (buyer is null)
        {
            var usernameSeed = !string.IsNullOrWhiteSpace(email)
                ? email.Split('@')[0]
                : $"buyer{phone?.Where(char.IsDigit).Take(8).Aggregate("", (a, b) => a + b)}";
            usernameSeed = new string(usernameSeed.Where(char.IsLetterOrDigit).Take(16).ToArray()).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(usernameSeed))
            {
                usernameSeed = "buyer";
            }

            var username = $"{usernameSeed}{Random.Shared.Next(1000, 9999)}";
            while (await userManager.FindByNameAsync(username) is not null)
            {
                username = $"{usernameSeed}{Random.Shared.Next(1000, 9999)}";
            }

            buyer = new ApplicationUser
            {
                UserName = username,
                Email = email,
                PhoneNumber = phone,
                FirstName = firstName,
                LastName = lastName,
                FarmRole = UserRole.MarketplaceBuyer,
                EmailConfirmed = !string.IsNullOrWhiteSpace(email),
                PhoneNumberConfirmed = !string.IsNullOrWhiteSpace(phone)
            };

            var password = $"Guest@{Guid.NewGuid():N}!";
            var createResult = await userManager.CreateAsync(buyer, password);
            if (!createResult.Succeeded)
            {
                return BadRequest(new { detail = createResult.Errors.Select(x => x.Description).ToArray() });
            }
        }
        else
        {
            buyer.FirstName = firstName;
            buyer.LastName = lastName;
            if (!string.IsNullOrWhiteSpace(email))
            {
                buyer.Email = email;
            }

            if (!string.IsNullOrWhiteSpace(phone))
            {
                buyer.PhoneNumber = phone;
            }

            await userManager.UpdateAsync(buyer);
        }

        var token = await tokenIssuer.CreateTokenAsync(buyer, cancellationToken: cancellationToken);
        return Ok(new GuestSessionResponse(
            token,
            buyer.Id,
            DisplayBuyerName(buyer),
            buyer.Email,
            buyer.PhoneNumber));
    }

    [Authorize]
    [HttpPost("conversations")]
    public async Task<ActionResult<MarketplaceConversationDto>> StartConversation(
        StartMarketplaceConversationRequest request,
        CancellationToken cancellationToken)
    {
        var buyer = await GetCurrentBuyerAsync();
        if (buyer is null)
        {
            return Unauthorized(new { detail = "Guest buyer session is invalid or expired. Start chat again." });
        }

        if (request.ListingId == Guid.Empty)
        {
            return BadRequest(new { listingId = "Listing is required." });
        }

        var listing = await dbContext.MarketplaceListings
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Id == request.ListingId && x.Status == MarketplaceListingStatus.Published, cancellationToken);
        if (listing is null)
        {
            return NotFound(new { detail = "Listing was not found or is not published." });
        }

        var farmAdmin = await dbContext.Users
            .Where(x => x.CompanyId == listing.CompanyId && x.FarmRole == UserRole.Admin && !x.IsDeleted)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (farmAdmin is null)
        {
            return BadRequest(new { detail = "This farm has no admin available to chat." });
        }

        var conversation = await dbContext.MarketplaceConversations
            .FirstOrDefaultAsync(x => x.ListingId == listing.Id && x.BuyerUserId == buyer.Id, cancellationToken);

        if (conversation is null)
        {
            conversation = new MarketplaceConversation
            {
                ListingId = listing.Id,
                CompanyId = listing.CompanyId,
                BuyerUserId = buyer.Id,
                FarmContactUserId = farmAdmin.Id,
                CreatedByUserId = buyer.Id
            };
            dbContext.MarketplaceConversations.Add(conversation);
            await dbContext.SaveChangesAsync(cancellationToken);

            await activityNotifier.NotifyCompanyAsync(
                listing.CompanyId,
                "Marketplace buyer chat",
                $"{DisplayBuyerName(buyer)} started a chat about \"{listing.Title}\".",
                buyer.Id,
                DisplayBuyerName(buyer),
                "marketplace_chat",
                "marketplace_conversation",
                conversation.Id,
                [UserRole.Admin, UserRole.Worker],
                cancellationToken);
        }

        ChatMessageDto? firstMessage = null;
        if (!string.IsNullOrWhiteSpace(request.Message))
        {
            firstMessage = await SendConversationMessageAsync(
                conversation,
                buyer,
                farmAdmin,
                request.Message.Trim(),
                listing.Title,
                cancellationToken);
        }

        return Ok(new MarketplaceConversationDto(
            conversation.Id,
            conversation.ListingId,
            listing.Title,
            listing.Company?.Name ?? "Farm",
            conversation.CompanyId,
            conversation.BuyerUserId,
            conversation.FarmContactUserId,
            DisplayBuyerName(farmAdmin),
            firstMessage));
    }

    [Authorize]
    [HttpGet("conversations")]
    public async Task<ActionResult<IReadOnlyCollection<MarketplaceConversationDto>>> GetBuyerConversations(
        CancellationToken cancellationToken)
    {
        var buyer = await GetCurrentBuyerAsync();
        if (buyer is null)
        {
            return Unauthorized(new { detail = "Guest buyer session required." });
        }

        var items = await dbContext.MarketplaceConversations
            .AsNoTracking()
            .Where(x => x.BuyerUserId == buyer.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MarketplaceConversationDto(
                x.Id,
                x.ListingId,
                x.Listing!.Title,
                x.Company!.Name,
                x.CompanyId,
                x.BuyerUserId,
                x.FarmContactUserId,
                null,
                null))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [Authorize]
    [HttpGet("conversations/{id:guid}")]
    public async Task<ActionResult<MarketplaceConversationDto>> GetConversation(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var conversation = await dbContext.MarketplaceConversations
            .AsNoTracking()
            .Include(x => x.Listing)
            .Include(x => x.Company)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (conversation is null)
        {
            return NotFound();
        }

        var isBuyer = conversation.BuyerUserId == userId;
        var isFarmStaff = await CanAccessCompanyMarketplaceChatAsync(userId.Value, conversation.CompanyId, cancellationToken);
        if (!isBuyer && !isFarmStaff)
        {
            return Forbid();
        }

        var farmContact = await dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == conversation.FarmContactUserId, cancellationToken);

        return Ok(new MarketplaceConversationDto(
            conversation.Id,
            conversation.ListingId,
            conversation.Listing?.Title ?? "Listing",
            conversation.Company?.Name ?? "Farm",
            conversation.CompanyId,
            conversation.BuyerUserId,
            conversation.FarmContactUserId,
            farmContact is null ? null : DisplayBuyerName(farmContact),
            null));
    }

    [Authorize]
    [HttpGet("farm/listings")]
    public async Task<ActionResult<IReadOnlyCollection<MarketplaceListingManageDto>>> GetFarmListings(
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId))
        {
            return Forbid();
        }

        var listings = await dbContext.MarketplaceListings
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MarketplaceListingManageDto(
                x.Id,
                x.ListingType.ToString(),
                x.Title,
                x.Description,
                x.QuantityOffered,
                x.UnitLabel,
                x.PriceAmount,
                x.PriceText,
                x.ImageUrl,
                x.Status.ToString(),
                x.BatchId,
                x.BatchVariantId,
                x.PublishedAt,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(listings);
    }

    [Authorize]
    [HttpPost("farm/uploads")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult<MarketplaceUploadResponse>> UploadFarmMedia(
        IFormFile file,
        [FromForm] string? folder,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out _) || !CanManageMarketplace())
        {
            return Forbid();
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { file = "An image file is required." });
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            return BadRequest(new { file = "Image must be 5 MB or smaller." });
        }

        var contentType = ResolveImageContentType(file.ContentType, file.FileName);
        if (contentType is null)
        {
            return BadRequest(new { file = "Use a JPEG, PNG, WebP, AVIF, or GIF image." });
        }

        var targetFolder = string.Equals(folder, "activities", StringComparison.OrdinalIgnoreCase)
            ? "activities"
            : "listings";
        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 10 || !KnownImageExtensions.Contains(extension))
        {
            extension = contentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/avif" => ".avif",
                "image/gif" => ".gif",
                _ => ".jpg"
            };
        }

        await using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);

        var asset = new MarketplaceMediaAsset
        {
            Folder = targetFolder,
            FileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}",
            ContentType = contentType,
            SizeBytes = memory.Length,
            Data = memory.ToArray(),
            CreatedByUserId = GetCurrentUserId()
        };

        dbContext.MarketplaceMediaAssets.Add(asset);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Served from Postgres on the home-server DB so images travel with your hosted data.
        return Ok(new MarketplaceUploadResponse($"/api/marketplace/media/{asset.Id}"));
    }

    [AllowAnonymous]
    [HttpGet("media/{id:guid}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetMedia(Guid id, CancellationToken cancellationToken)
    {
        var asset = await dbContext.MarketplaceMediaAssets
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.ContentType, x.FileName, x.Data })
            .FirstOrDefaultAsync(cancellationToken);

        if (asset is null)
        {
            return NotFound();
        }

        return File(asset.Data, asset.ContentType, enableRangeProcessing: false);
    }

    [Authorize]
    [HttpPost("farm/listings")]
    public async Task<ActionResult<MarketplaceListingManageDto>> CreateFarmListing(
        CreateMarketplaceListingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId) || !CanManageMarketplace())
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { title = "Title is required." });
        }

        if (!Enum.TryParse<MarketplaceListingType>(request.ListingType, true, out var listingType))
        {
            return BadRequest(new { listingType = "Listing type must be Eggs or Birds." });
        }

        if (request.QuantityOffered is <= 0)
        {
            return BadRequest(new { quantityOffered = "Quantity must be greater than zero, or leave it blank to mark quantity unknown." });
        }

        if (request.PriceAmount is < 0)
        {
            return BadRequest(new { priceAmount = "Price cannot be negative." });
        }

        if (listingType == MarketplaceListingType.Birds && request.BatchId is Guid batchId)
        {
            var batch = await dbContext.Batches.FirstOrDefaultAsync(
                x => x.Id == batchId && x.CompanyId == companyId,
                cancellationToken);
            if (batch is null)
            {
                return BadRequest(new { batchId = "Batch was not found for this farm." });
            }
        }

        if (listingType == MarketplaceListingType.Eggs && request.BatchVariantId is Guid variantId)
        {
            var variantOk = await dbContext.BatchVariants
                .AnyAsync(x => x.Id == variantId && x.Batch!.CompanyId == companyId, cancellationToken);
            if (!variantOk)
            {
                return BadRequest(new { batchVariantId = "Batch variant was not found for this farm." });
            }
        }

        var priceAmount = request.PriceAmount;
        var priceText = priceAmount is decimal amount
            ? GhsMoney.Format(amount)
            : null;

        var publish = request.PublishNow;
        var listing = new MarketplaceListing
        {
            CompanyId = companyId,
            ListingType = listingType,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            QuantityOffered = request.QuantityOffered,
            UnitLabel = listingType == MarketplaceListingType.Eggs ? "crate" : "bird",
            PriceAmount = priceAmount,
            PriceText = priceText,
            ImageUrl = NormalizeMediaUrl(request.ImageUrl),
            BatchId = request.BatchId,
            BatchVariantId = request.BatchVariantId,
            Status = publish ? MarketplaceListingStatus.Published : MarketplaceListingStatus.Draft,
            PublishedAt = publish ? DateTimeOffset.UtcNow : null,
            CreatedByUserId = GetCurrentUserId()
        };

        dbContext.MarketplaceListings.Add(listing);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToManageListingDto(listing));
    }

    [Authorize]
    [HttpPut("farm/listings/{id:guid}")]
    public async Task<ActionResult<MarketplaceListingManageDto>> UpdateFarmListing(
        Guid id,
        CreateMarketplaceListingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId) || !CanManageMarketplace())
        {
            return Forbid();
        }

        var listing = await dbContext.MarketplaceListings
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new { title = "Title is required." });
        }

        if (!Enum.TryParse<MarketplaceListingType>(request.ListingType, true, out var listingType))
        {
            return BadRequest(new { listingType = "Listing type must be Eggs or Birds." });
        }

        if (request.QuantityOffered is <= 0)
        {
            return BadRequest(new { quantityOffered = "Quantity must be greater than zero, or leave it blank to mark quantity unknown." });
        }

        if (request.PriceAmount is < 0)
        {
            return BadRequest(new { priceAmount = "Price cannot be negative." });
        }

        if (listingType == MarketplaceListingType.Birds && request.BatchId is Guid batchId)
        {
            var batch = await dbContext.Batches.FirstOrDefaultAsync(
                x => x.Id == batchId && x.CompanyId == companyId,
                cancellationToken);
            if (batch is null)
            {
                return BadRequest(new { batchId = "Batch was not found for this farm." });
            }
        }

        if (listingType == MarketplaceListingType.Eggs && request.BatchVariantId is Guid variantId)
        {
            var variantOk = await dbContext.BatchVariants
                .AnyAsync(x => x.Id == variantId && x.Batch!.CompanyId == companyId, cancellationToken);
            if (!variantOk)
            {
                return BadRequest(new { batchVariantId = "Batch variant was not found for this farm." });
            }
        }

        var priceAmount = request.PriceAmount;
        var priceText = priceAmount is decimal amount
            ? GhsMoney.Format(amount)
            : null;

        listing.ListingType = listingType;
        listing.Title = request.Title.Trim();
        listing.Description = request.Description?.Trim();
        listing.QuantityOffered = request.QuantityOffered;
        listing.UnitLabel = listingType == MarketplaceListingType.Eggs ? "crate" : "bird";
        listing.PriceAmount = priceAmount;
        listing.PriceText = priceText;
        listing.ImageUrl = NormalizeMediaUrl(request.ImageUrl);
        listing.BatchId = request.BatchId;
        listing.BatchVariantId = request.BatchVariantId;
        listing.UpdatedByUserId = GetCurrentUserId();

        if (request.PublishNow && listing.Status != MarketplaceListingStatus.Published)
        {
            listing.Status = MarketplaceListingStatus.Published;
            listing.PublishedAt ??= DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            "Update",
            "Marketplace",
            "MarketplaceListing",
            listing.Title,
            $"Updated {listing.ListingType} listing '{listing.Title}'.",
            listing.Id,
            companyId,
            cancellationToken: cancellationToken);

        return Ok(ToManageListingDto(listing));
    }

    [Authorize]
    [HttpPost("farm/listings/{id:guid}/publish")]
    public async Task<ActionResult<MarketplaceListingManageDto>> PublishListing(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId) || !CanManageMarketplace())
        {
            return Forbid();
        }

        var listing = await dbContext.MarketplaceListings
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        listing.Status = MarketplaceListingStatus.Published;
        listing.PublishedAt = DateTimeOffset.UtcNow;
        listing.UpdatedByUserId = GetCurrentUserId();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToManageListingDto(listing));
    }

    [Authorize]
    [HttpPost("farm/listings/{id:guid}/withdraw")]
    public async Task<ActionResult<MarketplaceListingManageDto>> WithdrawListing(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId) || !CanManageMarketplace())
        {
            return Forbid();
        }

        var listing = await dbContext.MarketplaceListings
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        listing.Status = MarketplaceListingStatus.Withdrawn;
        listing.UpdatedByUserId = GetCurrentUserId();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToManageListingDto(listing));
    }

    [Authorize]
    [HttpPost("farm/listings/{id:guid}/sold-out")]
    public async Task<ActionResult<MarketplaceListingManageDto>> MarkSoldOut(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId) || !CanManageMarketplace())
        {
            return Forbid();
        }

        var listing = await dbContext.MarketplaceListings
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        listing.Status = MarketplaceListingStatus.SoldOut;
        listing.UpdatedByUserId = GetCurrentUserId();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToManageListingDto(listing));
    }

    [Authorize]
    [HttpDelete("farm/listings/{id:guid}")]
    public async Task<IActionResult> DeleteListing(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId) || !CanManageMarketplace())
        {
            return Forbid();
        }

        var listing = await dbContext.MarketplaceListings
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        listing.IsDeleted = true;
        listing.UpdatedByUserId = GetCurrentUserId();
        await dbContext.SaveChangesAsync(cancellationToken);

        await audit.WriteAsync(
            "Delete",
            "Marketplace",
            "MarketplaceListing",
            listing.Title,
            $"Deleted {listing.ListingType} listing '{listing.Title}'.",
            listing.Id,
            companyId,
            cancellationToken: cancellationToken);

        return NoContent();
    }

    [Authorize]
    [HttpGet("farm/activities")]
    public async Task<ActionResult<IReadOnlyCollection<FarmActivityManageDto>>> GetFarmActivities(
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId))
        {
            return Forbid();
        }

        var posts = await dbContext.FarmActivityPosts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Body,
                x.MediaUrl,
                x.IsPublished,
                x.PublishedAt,
                x.CreatedAt,
                LikeCount = x.Likes.Count(l => !l.IsDeleted),
                CommentCount = x.Comments.Count(c => !c.IsDeleted),
                x.ShareCount,
                x.CreatedByUserId
            })
            .ToListAsync(cancellationToken);

        var postIds = posts.Select(x => x.Id).ToList();
        var imageRows = postIds.Count == 0
            ? []
            : await dbContext.FarmActivityImages
                .AsNoTracking()
                .Where(x => postIds.Contains(x.PostId))
                .OrderBy(x => x.SortOrder)
                .Select(x => new { x.PostId, x.Url })
                .ToListAsync(cancellationToken);
        var imagesByPost = imageRows
            .GroupBy(x => x.PostId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Url).ToList());

        return Ok(posts.Select(x => new FarmActivityManageDto(
            x.Id,
            x.Title,
            x.Body,
            x.MediaUrl,
            x.IsPublished,
            x.PublishedAt,
            x.CreatedAt,
            x.LikeCount,
            x.CommentCount,
            x.ShareCount,
            imagesByPost.TryGetValue(x.Id, out var urls) && urls.Count > 0
                ? urls
                : string.IsNullOrWhiteSpace(x.MediaUrl) ? [] : [x.MediaUrl],
            x.CreatedByUserId)).ToList());
    }

    [Authorize]
    [HttpPost("farm/activities")]
    public async Task<ActionResult<FarmActivityManageDto>> CreateFarmActivity(
        CreateFarmActivityRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId) || !CanManageMarketplace())
        {
            return Forbid();
        }

        var body = request.Body?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            return BadRequest(new { body = "Post text is required." });
        }

        var title = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = body.Length <= 80 ? body : body[..80].TrimEnd() + "…";
        }

        var publish = request.PublishNow;
        var mediaUrls = NormalizeActivityMedia(request.MediaUrls, request.MediaUrl);
        var post = new FarmActivityPost
        {
            CompanyId = companyId,
            Title = title,
            Body = body,
            MediaUrl = mediaUrls.FirstOrDefault(),
            IsPublished = publish,
            PublishedAt = publish ? DateTimeOffset.UtcNow : null,
            CreatedByUserId = GetCurrentUserId(),
            Images = mediaUrls.Select((url, index) => new FarmActivityImage
            {
                Url = url,
                SortOrder = index
            }).ToList()
        };

        dbContext.FarmActivityPosts.Add(post);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new FarmActivityManageDto(
            post.Id, post.Title, post.Body, post.MediaUrl, post.IsPublished, post.PublishedAt, post.CreatedAt, 0, 0, 0, mediaUrls, post.CreatedByUserId));
    }

    [Authorize]
    [HttpPut("farm/activities/{id:guid}")]
    public async Task<ActionResult<FarmActivityManageDto>> UpdateFarmActivity(
        Guid id,
        CreateFarmActivityRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId) || !CanManageMarketplace())
        {
            return Forbid();
        }

        var post = await dbContext.FarmActivityPosts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        var userId = GetCurrentUserId();
        if (!CanEditActivity(post, userId))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { detail = "Only the person who wrote this post can edit it." });
        }

        var body = request.Body?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            return BadRequest(new { body = "Post text is required." });
        }

        var title = request.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            title = body.Length <= 80 ? body : body[..80].TrimEnd() + "…";
        }

        var mediaUrls = NormalizeActivityMedia(request.MediaUrls, request.MediaUrl);
        var isPublished = request.PublishNow;
        var publishedAt = post.PublishedAt;
        if (request.PublishNow)
        {
            publishedAt ??= DateTimeOffset.UtcNow;
        }

        var likeCount = await dbContext.FarmActivityLikes.CountAsync(x => x.PostId == id, cancellationToken);
        var commentCount = await dbContext.FarmActivityComments.CountAsync(x => x.PostId == id, cancellationToken);

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            await dbContext.FarmActivityPosts
                .Where(x => x.Id == id && x.CompanyId == companyId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Title, title)
                    .SetProperty(x => x.Body, body)
                    .SetProperty(x => x.MediaUrl, mediaUrls.FirstOrDefault())
                    .SetProperty(x => x.IsPublished, isPublished)
                    .SetProperty(x => x.PublishedAt, publishedAt)
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow)
                    .SetProperty(x => x.UpdatedByUserId, userId), cancellationToken);

            await dbContext.FarmActivityImages
                .Where(x => x.PostId == id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDeleted, true), cancellationToken);

            for (var index = 0; index < mediaUrls.Count; index++)
            {
                dbContext.FarmActivityImages.Add(new FarmActivityImage
                {
                    PostId = id,
                    Url = mediaUrls[index],
                    SortOrder = index
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

        return Ok(new FarmActivityManageDto(
            post.Id,
            title,
            body,
            mediaUrls.FirstOrDefault(),
            isPublished,
            publishedAt,
            post.CreatedAt,
            likeCount,
            commentCount,
            post.ShareCount,
            mediaUrls,
            post.CreatedByUserId));
    }

    private bool CanEditActivity(FarmActivityPost post, Guid? userId) =>
        post.CreatedByUserId is Guid author
            ? author == userId
            : PermissionHelpers.IsCompanyAdmin(User) || PermissionHelpers.IsSystemAdmin(User);

    [Authorize]
    [HttpDelete("farm/activities/{id:guid}")]
    public async Task<IActionResult> DeleteFarmActivity(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId) || !CanManageMarketplace())
        {
            return Forbid();
        }

        var post = await dbContext.FarmActivityPosts
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        post.IsDeleted = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new { detail = "Activity removed." });
    }

    [Authorize]
    [HttpGet("farm/inquiries")]
    public async Task<ActionResult<IReadOnlyCollection<MarketplaceInquiryDto>>> GetFarmInquiries(
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId))
        {
            return Forbid();
        }

        var inquiries = await dbContext.MarketplaceInquiries
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new MarketplaceInquiryDto(
                x.Id,
                x.ListingId,
                x.Listing!.Title,
                x.BuyerName,
                x.BuyerEmail,
                x.BuyerPhone,
                x.Message,
                x.QuantityRequested,
                x.Status.ToString(),
                x.FarmResponseNotes,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(inquiries);
    }

    [Authorize]
    [HttpPut("farm/inquiries/{id:guid}/status")]
    public async Task<ActionResult<MarketplaceInquiryDto>> UpdateInquiryStatus(
        Guid id,
        UpdateInquiryStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetManagedCompanyId(out var companyId) || !CanManageMarketplace())
        {
            return Forbid();
        }

        if (!Enum.TryParse<MarketplaceInquiryStatus>(request.Status, true, out var status))
        {
            return BadRequest(new { status = "Invalid status." });
        }

        var inquiry = await dbContext.MarketplaceInquiries
            .Include(x => x.Listing)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId, cancellationToken);
        if (inquiry is null)
        {
            return NotFound();
        }

        inquiry.Status = status;
        inquiry.FarmResponseNotes = request.FarmResponseNotes?.Trim();
        inquiry.UpdatedByUserId = GetCurrentUserId();
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToInquiryDto(inquiry, inquiry.Listing?.Title ?? string.Empty));
    }

    private async Task<bool> SendApprovalInviteEmailAsync(
        string email,
        string contactFirstName,
        string farmName,
        string username,
        string inviteUrl,
        DateTimeOffset inviteExpiresAt,
        CancellationToken cancellationToken)
    {
        var (subject, html, text) = FarmRegistrationEmailComposer.BuildApprovalInvite(
            contactFirstName,
            farmName,
            username,
            inviteUrl,
            inviteExpiresAt);

        var result = await emailSender.SendAsync(email, subject, html, text, cancellationToken);
        return result.Success;
    }

    private async Task<(string RawToken, DateTimeOffset ExpiresAt)> CreatePasswordInviteAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.PasswordInvites
            .Where(x => x.UserId == userId && x.ConsumedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var invite in existing)
        {
            invite.IsDeleted = true;
        }

        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var entity = new PasswordInvite
        {
            UserId = userId,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        dbContext.PasswordInvites.Add(entity);
        return (rawToken, entity.ExpiresAt);
    }

    private bool TryGetManagedCompanyId(out Guid companyId)
    {
        companyId = Guid.Empty;
        if (PermissionHelpers.IsSystemAdmin(User))
        {
            var claim = PermissionHelpers.GetCompanyId(User);
            return Guid.TryParse(claim, out companyId) && companyId != Guid.Empty;
        }

        if (PermissionHelpers.IsCompanyAdmin(User) || PermissionHelpers.IsWorker(User))
        {
            var claim = PermissionHelpers.GetCompanyId(User);
            return Guid.TryParse(claim, out companyId) && companyId != Guid.Empty;
        }

        return false;
    }

    private bool CanManageMarketplace() =>
        PermissionHelpers.IsSystemAdmin(User)
        || PermissionHelpers.IsCompanyAdmin(User)
        || (PermissionHelpers.IsWorker(User) &&
            User.FindAll("allowed_page").Any(c =>
                string.Equals(c.Value, WorkerPageKeys.Marketplace, StringComparison.OrdinalIgnoreCase)));

    private async Task<ChatMessageDto> SendConversationMessageAsync(
        MarketplaceConversation conversation,
        ApplicationUser buyer,
        ApplicationUser farmAdmin,
        string body,
        string listingTitle,
        CancellationToken cancellationToken)
    {
        var message = new ChatMessage
        {
            SenderUserId = buyer.Id,
            RecipientUserId = farmAdmin.Id,
            CompanyId = conversation.CompanyId,
            MarketplaceConversationId = conversation.Id,
            ListingId = conversation.ListingId,
            Body = messageProtector.Protect(body),
            SentAt = DateTimeOffset.UtcNow,
            CreatedByUserId = buyer.Id
        };

        dbContext.ChatMessages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        var dto = new ChatMessageDto(
            message.Id,
            message.SenderUserId,
            message.RecipientUserId,
            body,
            message.SentAt,
            true,
            DisplayBuyerName(buyer),
            DisplayBuyerName(farmAdmin),
            MarketplaceBuyerUserId: conversation.BuyerUserId);

        await chatFanout.PublishMessageAsync(
            "chat.message",
            dto,
            conversation.CompanyId,
            conversation.BuyerUserId,
            buyer.Id,
            $"Buyer chat · {listingTitle}",
            DisplayBuyerName(buyer),
            createNotifications: true,
            cancellationToken);

        return dto;
    }

    private async Task<bool> CanAccessCompanyMarketplaceChatAsync(Guid userId, Guid companyId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null || user.CompanyId != companyId)
        {
            return false;
        }

        if (user.FarmRole == UserRole.Admin)
        {
            return true;
        }

        return user.FarmRole == UserRole.Worker;
    }

    private async Task<ApplicationUser?> GetCurrentBuyerAsync()
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(userId.Value.ToString());
        return user is { FarmRole: UserRole.MarketplaceBuyer, IsDeleted: false } ? user : null;
    }

    private static string DisplayBuyerName(ApplicationUser user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? user.UserName ?? "Buyer" : name;
    }

    private Guid? GetCurrentUserId()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out var parsed) ? parsed : null;
    }

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }

    private static string GenerateCompanyCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 10).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }

    private async Task<string> NextCompanyCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = GenerateCompanyCode();
            if (!await dbContext.Companies.AnyAsync(x => x.Code == code, cancellationToken))
            {
                return code;
            }
        }

        return GenerateCompanyCode();
    }

    private async Task<string> CreateUniqueUsernameAsync(string firstName, string lastName, CancellationToken cancellationToken)
    {
        var stem = new string($"{firstName}{lastName}".Where(char.IsLetter).ToArray()).ToLowerInvariant();
        if (stem.Length > 16)
        {
            stem = stem[..16];
        }

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "farm";
        }

        for (var attempt = 0; attempt < 12; attempt++)
        {
            var candidate = $"{stem}{Random.Shared.Next(10000, 100000)}";
            if (await userManager.FindByNameAsync(candidate) is null)
            {
                return candidate;
            }
        }

        return $"{stem}{Guid.NewGuid().ToString("N")[..8]}";
    }

    private static string GenerateTemporaryPassword() =>
        $"Farm@{Random.Shared.Next(100000, 999999)}!";

    private static FarmerRegistrationDto ToRegistrationDto(FarmerRegistration x) =>
        new(x.Id, x.FarmName, x.ContactFirstName, x.ContactLastName, x.Email, x.Phone, x.Location, x.Notes,
            x.RequestedUsername, x.Status.ToString(), x.ReviewNotes, x.ReviewedAt, x.CompanyId, x.FarmAdminUserId, x.CreatedAt);

    private static MarketplaceListingManageDto ToManageListingDto(MarketplaceListing x) =>
        new(x.Id, x.ListingType.ToString(), x.Title, x.Description, x.QuantityOffered, x.UnitLabel, x.PriceAmount,
            x.PriceText, x.ImageUrl, x.Status.ToString(), x.BatchId, x.BatchVariantId, x.PublishedAt, x.CreatedAt);

    private static string? NormalizeMediaUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith('/'))
        {
            return trimmed;
        }

        return "/" + trimmed.TrimStart('/');
    }

    private const int MaxActivityImages = 10;

    private static List<string> NormalizeActivityMedia(IReadOnlyList<string>? mediaUrls, string? mediaUrl)
    {
        var source = mediaUrls is { Count: > 0 }
            ? mediaUrls
            : string.IsNullOrWhiteSpace(mediaUrl) ? [] : new[] { mediaUrl };

        var list = new List<string>();
        foreach (var raw in source)
        {
            var url = NormalizeMediaUrl(raw);
            if (string.IsNullOrWhiteSpace(url) || list.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            list.Add(url);
            if (list.Count == MaxActivityImages)
            {
                break;
            }
        }

        return list;
    }

    private string? ResolveReactorKey(string? requestedKey)
    {
        if (GetCurrentUserId() is Guid userId)
        {
            return $"user:{userId:N}";
        }

        if (string.IsNullOrWhiteSpace(requestedKey))
        {
            return null;
        }

        var key = requestedKey.Trim();
        return key.Length > 100 ? key[..100] : key;
    }

    private static MarketplaceInquiryDto ToInquiryDto(MarketplaceInquiry x, string listingTitle) =>
        new(x.Id, x.ListingId, listingTitle, x.BuyerName, x.BuyerEmail, x.BuyerPhone, x.Message,
            x.QuantityRequested, x.Status.ToString(), x.FarmResponseNotes, x.CreatedAt);
}

public sealed record CreateFarmerRegistrationRequest(
    string FarmName,
    string ContactFirstName,
    string ContactLastName,
    string Email,
    string Phone,
    string? Location,
    string? Notes,
    string? RequestedUsername);

public sealed record ApproveFarmerRegistrationRequest(
    string? Username,
    string? TemporaryPassword,
    string? ReviewNotes);

public sealed record RejectFarmerRegistrationRequest(string? ReviewNotes);

public sealed record FarmerRegistrationDto(
    Guid Id,
    string FarmName,
    string ContactFirstName,
    string ContactLastName,
    string Email,
    string Phone,
    string? Location,
    string? Notes,
    string? RequestedUsername,
    string Status,
    string? ReviewNotes,
    DateTimeOffset? ReviewedAt,
    Guid? CompanyId,
    Guid? FarmAdminUserId,
    DateTimeOffset CreatedAt);

public sealed record ApproveRegistrationResponse(
    FarmerRegistrationDto Registration,
    Guid CompanyId,
    string CompanyCode,
    Guid FarmAdminUserId,
    string Username,
    string TemporaryPassword,
    string InviteToken,
    string InviteUrl,
    DateTimeOffset InviteExpiresAt,
    bool EmailSent);

public sealed record ResendInviteResponse(
    string InviteToken,
    string InviteUrl,
    DateTimeOffset InviteExpiresAt,
    bool EmailSent);

public sealed record MarketplaceListingPublicDto(
    Guid Id,
    Guid CompanyId,
    string FarmName,
    string ListingType,
    string Title,
    string? Description,
    decimal? QuantityOffered,
    string UnitLabel,
    decimal? PriceAmount,
    string? PriceText,
    string? ImageUrl,
    DateTimeOffset? PublishedAt);

public sealed record MarketplaceFarmCardDto(
    Guid Id,
    string Name,
    string? Location,
    string Summary,
    bool SellsEggs,
    bool SellsBirds,
    int ActiveListingCount,
    string? ContactPhone,
    DateTimeOffset? JoinedAt);

public sealed record MarketplaceFarmDetailDto(
    Guid Id,
    string Name,
    string? Location,
    string Summary,
    bool SellsEggs,
    bool SellsBirds,
    int ActiveListingCount,
    string? ContactPhone,
    string? ContactEmail,
    string ContactName,
    string? Notes,
    DateTimeOffset? JoinedAt,
    IReadOnlyCollection<MarketplaceListingPublicDto> Listings);

public sealed record MarketplaceListingManageDto(
    Guid Id,
    string ListingType,
    string Title,
    string? Description,
    decimal? QuantityOffered,
    string UnitLabel,
    decimal? PriceAmount,
    string? PriceText,
    string? ImageUrl,
    string Status,
    Guid? BatchId,
    Guid? BatchVariantId,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt);

public sealed record CreateMarketplaceListingRequest(
    string ListingType,
    string Title,
    string? Description,
    decimal? QuantityOffered,
    string? UnitLabel,
    decimal? PriceAmount,
    string? ImageUrl,
    Guid? BatchId,
    Guid? BatchVariantId,
    bool PublishNow);

public sealed record MarketplaceUploadResponse(string Url);

public sealed record FarmActivityPublicDto(
    Guid Id,
    Guid CompanyId,
    string FarmName,
    string Title,
    string Body,
    string? MediaUrl,
    IReadOnlyList<string> MediaUrls,
    DateTimeOffset? PublishedAt,
    int LikeCount,
    int CommentCount,
    int ShareCount,
    bool LikedByMe,
    IReadOnlyList<FarmActivityCommentDto> Comments);

public sealed record FarmActivityCommentDto(
    Guid Id,
    Guid? ParentCommentId,
    string AuthorName,
    Guid? AuthorCompanyId,
    string Body,
    DateTimeOffset CreatedAt,
    bool CanDelete,
    IReadOnlyList<FarmActivityCommentReactionDto> Reactions,
    IReadOnlyList<FarmActivityCommentDto> Replies);

public sealed record FarmActivityCommentReactionDto(
    string Emoji,
    int Count,
    bool ReactedByMe);

public sealed record CreateFarmActivityCommentRequest(
    string? AuthorName,
    string Body,
    Guid? ParentCommentId,
    string? ReactorKey);

public sealed record ToggleFarmActivityCommentReactionRequest(
    string Emoji,
    string? ReactorKey);

public sealed record FarmActivityCommentReactionToggleResponse(
    Guid CommentId,
    bool Reacted,
    IReadOnlyList<FarmActivityCommentReactionDto> Reactions);

public sealed record ToggleFarmActivityLikeRequest(string? ReactorKey);

public sealed record FarmActivityLikeResponse(Guid PostId, int LikeCount, bool LikedByMe);

public sealed record FarmActivityShareResponse(Guid PostId, int ShareCount, string ShareUrl);

public sealed record FarmActivityManageDto(
    Guid Id,
    string Title,
    string Body,
    string? MediaUrl,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    int LikeCount,
    int CommentCount,
    int ShareCount,
    IReadOnlyList<string> MediaUrls,
    Guid? CreatedByUserId);

public sealed record CreateFarmActivityRequest(
    string? Title,
    string Body,
    string? MediaUrl,
    bool PublishNow,
    IReadOnlyList<string>? MediaUrls = null);

public sealed record CreateMarketplaceInquiryRequest(
    Guid ListingId,
    string BuyerName,
    string? BuyerEmail,
    string? BuyerPhone,
    string Message,
    decimal? QuantityRequested);

public sealed record MarketplaceInquiryDto(
    Guid Id,
    Guid ListingId,
    string ListingTitle,
    string BuyerName,
    string? BuyerEmail,
    string? BuyerPhone,
    string Message,
    decimal? QuantityRequested,
    string Status,
    string? FarmResponseNotes,
    DateTimeOffset CreatedAt);

public sealed record UpdateInquiryStatusRequest(string Status, string? FarmResponseNotes);

public sealed record CreateGuestSessionRequest(string Name, string? Email, string? Phone);

public sealed record GuestSessionResponse(
    string Token,
    Guid UserId,
    string DisplayName,
    string? Email,
    string? Phone);

public sealed record StartMarketplaceConversationRequest(
    Guid ListingId,
    string? Message,
    decimal? QuantityRequested);

public sealed record MarketplaceConversationDto(
    Guid Id,
    Guid ListingId,
    string ListingTitle,
    string FarmName,
    Guid CompanyId,
    Guid BuyerUserId,
    Guid FarmContactUserId,
    string? FarmContactName,
    ChatMessageDto? FirstMessage);
