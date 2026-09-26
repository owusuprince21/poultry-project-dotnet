using Microsoft.AspNetCore.Authorization;
using PoultryFarm.Domain.Marketplace;

namespace PoultryFarm.Api.Authorization;

public sealed class WorkerWriteRequirementHandler
    : AuthorizationHandler<WorkerWriteRequirement>
{
    private static readonly Dictionary<string, string> ControllerToPageKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eggproduction"] = WorkerPageKeys.Production,
        ["egginventory"] = WorkerPageKeys.Production,
        ["feed"] = WorkerPageKeys.Feed,
        ["feedconsumption"] = WorkerPageKeys.Feed,
        ["feedstock"] = WorkerPageKeys.Feed,
        ["feedstocklot"] = WorkerPageKeys.Feed,
        ["feedstocklots"] = WorkerPageKeys.Feed,
        ["birdhealth"] = WorkerPageKeys.Health,
        ["birdhealthevent"] = WorkerPageKeys.Health,
        ["birdhealthevents"] = WorkerPageKeys.Health,
        ["eggsale"] = WorkerPageKeys.Sales,
        ["eggsales"] = WorkerPageKeys.Sales,
        ["sales"] = WorkerPageKeys.Sales,
        ["assistance"] = WorkerPageKeys.FarmAssistance,
        ["ai"] = WorkerPageKeys.FarmAssistance
    };

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
        if (!ControllerToPageKey.TryGetValue(normalizedResource, out var pageKey))
        {
            return Task.CompletedTask;
        }

        var allowed = user.FindAll("allowed_page")
            .Select(x => x.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // If no page claims were issued, fall back to legacy open worker writes for mapped resources.
        if (allowed.Count == 0 || allowed.Contains(pageKey))
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
