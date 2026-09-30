namespace PoultryFarm.Api.Services;

public interface IFarmAssistantAgent
{
    Task<string> GetReplyAsync(Guid companyId, string userDisplayName, string prompt, CancellationToken cancellationToken = default);

    Task<string> AdviseOnObservationAsync(Guid companyId, string userDisplayName, string category, DateOnly date, string notes, CancellationToken cancellationToken = default);
}
