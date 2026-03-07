using PlaNEvent.Api.Data;
using PlaNEvent.Api.Models;

namespace PlaNEvent.Api.Services;

public interface IActivityService
{
    Task LogAsync(string actorUserId, string action, string metadata = "", CancellationToken cancellationToken = default);
}

public sealed class ActivityService(AppDbContext dbContext) : IActivityService
{
    public async Task LogAsync(string actorUserId, string action, string metadata = "", CancellationToken cancellationToken = default)
    {
        dbContext.ActivityLogs.Add(new ActivityLog
        {
            ActorUserId = actorUserId,
            Action = action,
            Metadata = metadata
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
