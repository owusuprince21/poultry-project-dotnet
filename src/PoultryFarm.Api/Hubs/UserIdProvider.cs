using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace PoultryFarm.Api.Hubs;

public sealed class UserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
