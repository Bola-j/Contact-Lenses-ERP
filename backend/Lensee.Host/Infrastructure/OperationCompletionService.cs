using Lensee.Modules.CRM.Data;
using Lensee.Modules.Operations.Data;

namespace Lensee.Host.Infrastructure;

public sealed class OperationCompletionService
{
    private readonly CrmDbContext _crmDbContext;
    private readonly OperationsDbContext _operationsDbContext;

    public OperationCompletionService(CrmDbContext crmDbContext, OperationsDbContext operationsDbContext)
    {
        _crmDbContext = crmDbContext;
        _operationsDbContext = operationsDbContext;
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _crmDbContext.SaveChangesAsync(cancellationToken);
        await _operationsDbContext.SaveChangesAsync(cancellationToken);
    }
}