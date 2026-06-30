using Microsoft.AspNetCore.Authorization;

namespace PoultryFarm.Api.Authorization;

public sealed class WorkerWriteRequirement : IAuthorizationRequirement
{
}