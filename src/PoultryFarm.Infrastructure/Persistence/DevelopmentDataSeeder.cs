using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;
using PoultryFarm.Domain.Identity;
using PoultryFarm.Domain.Marketplace;
using PoultryFarm.Infrastructure.Identity;

namespace PoultryFarm.Infrastructure.Persistence;

public static class DevelopmentDataSeeder
{
    public static async Task ApplyMigrationsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public static async Task SeedDevelopmentDataAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        await dbContext.Database.MigrateAsync();

        foreach (var role in new[] { "SystemAdmin", "SubAdmin", "Admin", "Worker" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        var company = await dbContext.Companies.FirstOrDefaultAsync(x => x.Code == "DEMOFARM01");
        if (company is null)
        {
            company = new Company
            {
                Name = "Demo Poultry Farm",
                Code = "DEMOFARM01",
                Email = "admin@demo-farm.local",
                Phone = "+233000000000",
                Address = "Accra, Ghana"
            };
            dbContext.Companies.Add(company);
            await dbContext.SaveChangesAsync();
        }

        await EnsureUserAsync(
            userManager,
            username: "admin",
            password: "Admin@12345",
            email: "admin@demo-farm.local",
            firstName: "System",
            lastName: "Admin",
            company.Id,
            UserRole.SystemAdmin,
            isSystemAdmin: true,
            mustChangePassword: false,
            roleName: "SystemAdmin");

        await EnsureUserAsync(
            userManager,
            username: "farmadmin",
            password: "FarmAdmin@12345",
            email: "farmadmin@demo-farm.local",
            firstName: "Farm",
            lastName: "Admin",
            company.Id,
            UserRole.Admin,
            isSystemAdmin: false,
            mustChangePassword: true,
            roleName: "Admin");

        await EnsureUserAsync(
            userManager,
            username: "worker",
            password: "Worker@12345",
            email: "worker@demo-farm.local",
            firstName: "Farm",
            lastName: "Worker",
            company.Id,
            UserRole.Worker,
            isSystemAdmin: false,
            mustChangePassword: true,
            roleName: "Worker");

        var worker = await userManager.FindByNameAsync("worker");
        if (worker is not null &&
            !await dbContext.WorkerPagePermissions.AnyAsync(x => x.UserId == worker.Id))
        {
            foreach (var key in new[]
                     {
                         WorkerPageKeys.Dashboard,
                         WorkerPageKeys.Production,
                         WorkerPageKeys.Feed,
                         WorkerPageKeys.Health,
                         WorkerPageKeys.Sales,
                         WorkerPageKeys.DailySummary,
                         WorkerPageKeys.FarmAssistance,
                         WorkerPageKeys.Settings
                     })
            {
                dbContext.WorkerPagePermissions.Add(new WorkerPagePermission
                {
                    UserId = worker.Id,
                    PageKey = key
                });
            }

            await dbContext.SaveChangesAsync();
        }

        if (!await dbContext.FarmerRegistrations.AnyAsync())
        {
            dbContext.FarmerRegistrations.Add(new FarmerRegistration
            {
                FarmName = "Sunrise Layers",
                ContactFirstName = "Ama",
                ContactLastName = "Mensah",
                Email = "ama@sunrise-layers.local",
                Phone = "+233111111111",
                Location = "Kumasi, Ghana",
                Notes = "Interested in listing eggs online.",
                RequestedUsername = "sunriseadmin",
                Status = FarmerRegistrationStatus.Pending
            });
            await dbContext.SaveChangesAsync();
        }

        if (!await dbContext.MarketplaceListings.AnyAsync(x => x.CompanyId == company.Id))
        {
            dbContext.MarketplaceListings.Add(new MarketplaceListing
            {
                CompanyId = company.Id,
                ListingType = MarketplaceListingType.Eggs,
                Title = "Fresh brown eggs — farm gate",
                Description = "Sorted medium/large eggs from Demo Poultry Farm.",
                QuantityOffered = 30,
                UnitLabel = "crates",
                PriceAmount = 45m,
                PriceText = GhsMoney.Format(45m),
                Status = MarketplaceListingStatus.Published,
                PublishedAt = DateTimeOffset.UtcNow
            });
            dbContext.FarmActivityPosts.Add(new FarmActivityPost
            {
                CompanyId = company.Id,
                Title = "Week 12 production update",
                Body = "Layers are performing well with steady daily collections. Share what is working on your farm this week.",
                IsPublished = true,
                PublishedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        }
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        string username,
        string password,
        string email,
        string firstName,
        string lastName,
        Guid companyId,
        UserRole farmRole,
        bool isSystemAdmin,
        bool mustChangePassword,
        string roleName)
    {
        var user = await userManager.FindByNameAsync(username);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = username,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                CompanyId = companyId,
                FarmRole = farmRole,
                IsSystemAdmin = isSystemAdmin,
                MustChangePassword = mustChangePassword,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
            }
        }
        else
        {
            user.Email ??= email;
            user.FirstName ??= firstName;
            user.LastName ??= lastName;
            user.CompanyId ??= companyId;
            user.FarmRole = farmRole;
            user.IsSystemAdmin = isSystemAdmin;
            await userManager.UpdateAsync(user);
        }

        if (!await userManager.IsInRoleAsync(user, roleName))
        {
            await userManager.AddToRoleAsync(user, roleName);
        }
    }
}
