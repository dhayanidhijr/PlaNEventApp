using PlaNEvent.Api.Models;

namespace PlaNEvent.Api.Services;

public interface IOfferingScheduleService
{
    Task RebuildOccurrencesAsync(Offering offering, CancellationToken cancellationToken);
}
