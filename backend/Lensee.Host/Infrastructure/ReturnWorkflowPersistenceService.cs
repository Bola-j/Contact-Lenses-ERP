using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// Persistence boundary for the return workflow.  The endpoint adapter may
/// coordinate the request, but database flushes for return transitions go
/// through this scoped service so the workflow can be tested independently.
/// </summary>
public interface IReturnWorkflowPersistenceService
{
    Task SaveAsync(DbContext context, CancellationToken cancellationToken);
}

public sealed class ReturnWorkflowPersistenceService : IReturnWorkflowPersistenceService
{
    public Task SaveAsync(DbContext context, CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
