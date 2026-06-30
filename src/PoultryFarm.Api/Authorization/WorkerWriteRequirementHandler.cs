using Microsoft.AspNetCore.Authorization;

namespace PoultryFarm.Api.Authorization;

public sealed class WorkerWriteRequirementHandler
    : AuthorizationHandler<WorkerWriteRequirement>
{
    private static readonly HashSet<string> WorkerWriteResources =
    [
        "eggproduction",
        "feedconsumption",
        "egginventory",
        "eggsale",
        "eggsales",
        "feedstock",
        "feedstocklot",
        "feedstocklots",
        "birdhealthevent",
        "birdhealthevents"
    ];

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        WorkerWriteRequirement requirement)
    {
        var user = context.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        if (PermissionHelpers.IsSystemAdmin(user) || PermissionHelpers.IsCompanyAdmin(user))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (!PermissionHelpers.IsWorker(user))
        {
            return Task.CompletedTask;
        }

        var httpContext = context.Resource as HttpContext;

        if (httpContext is null)
        {
            return Task.CompletedTask;
        }

        if (HttpMethods.IsGet(httpContext.Request.Method) ||
            HttpMethods.IsHead(httpContext.Request.Method) ||
            HttpMethods.IsOptions(httpContext.Request.Method))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var routeResource =
            httpContext.Request.RouteValues["controller"]?.ToString() ??
            httpContext.Request.RouteValues["resource"]?.ToString() ??
            string.Empty;

        var normalizedResource = NormalizeResourceName(routeResource);

        if (WorkerWriteResources.Contains(normalizedResource))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    private static string NormalizeResourceName(string value)
    {
        return new string(
            value
                .Trim()
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
    }
}