using Lensee.Host.Services;

namespace Lensee.Host.Endpoints;

public static class OutboxEndpoints
{
    public static RouteGroupBuilder MapOutboxEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/outbox").WithTags("Outbox").RequireAuthorization("settings.write");
        group.MapGet("/dead-letters", ListDeadLettersAsync);
        group.MapPost("/dead-letters/{id:guid}/retry", RetryDeadLetterAsync);
        return group;
    }

    private static async Task<IResult> ListDeadLettersAsync(OutboxOperationsService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.ListDeadLettersAsync(cancellationToken));

    private static async Task<IResult> RetryDeadLetterAsync(Guid id, OutboxOperationsService service, CancellationToken cancellationToken) =>
        await service.RetryAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
}
