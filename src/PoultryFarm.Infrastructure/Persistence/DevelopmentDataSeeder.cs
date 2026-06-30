using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PoultryFarm.Domain.Common;
using PoultryFarm.Domain.Companies;
using PoultryFarm.Infrastructure.Identity;

namespace PoultryFarm.Infrastructure.Persistence;

public static class DevelopmentDataSeeder
{
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
