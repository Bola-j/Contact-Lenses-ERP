using Lensee.Host.Infrastructure;
using Lensee.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Lensee.Host.Endpoints;

public static class MerchantExpiryRecallEndpoints
{
    public static IEndpointRouteBuilder MapMerchantExpiryRecallEndpoints(this IEndpointRouteBuilder routes)
    {
        var recalls = routes.MapGroup("/api/v1/merchant-expiry-recalls").WithTags("Merchant expiry recalls");
        recalls.MapGet("/", ListAsync).RequireAuthorization("merchant-recalls.read");
        recalls.MapPost("/{id:guid}/return-draft", CreateReturnDraftAsync).RequireAuthorization("merchant-recalls.manage");
        recalls.MapPost("/{id:guid}/no-stock", RecordNoStockAsync).RequireAuthorization("merchant-recalls.manage");

        var config = routes.MapGroup("/api/v1/alerts/config/merchant-expiry-recall").WithTags("Alerts");
        config.MapGet("/", GetConfigAsync).RequireAuthorization("merchant-recalls.read");
        config.MapPut("/", UpdateConfigAsync).RequireAuthorization("merchant-recalls.manage");
        return routes;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] string? status,
        [FromQuery] Guid? merchantId,
        MerchantExpiryRecallService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(status, merchantId, cancellationToken));

    private static async Task<IResult> CreateReturnDraftAsync(
        Guid id,
        CreateMerchantRecallReturnDraftRequest request,
        MerchantExpiryRecallService service,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateReturnDraftAsync(
            id,
            request.ReceivingLocationId,
            request.Quantity,
            request.Notes,
            actorId,
            cancellationToken);
        return ToResult(result, value => Results.Created($"/api/v1/operations/{value.OperationId}", value));
    }

    private static async Task<IResult> RecordNoStockAsync(
        Guid id,
        MerchantRecallNoStockRequest request,
        MerchantExpiryRecallService service,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Results.Unauthorized();
        }

        var result = await service.RecordNoStockAsync(id, request.Note, actorId, cancellationToken);
        return ToResult(result, Results.Ok);
    }

    private static async Task<IResult> GetConfigAsync(
        MerchantExpiryRecallService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetConfigAsync(cancellationToken));

    private static async Task<IResult> UpdateConfigAsync(
        MerchantExpiryRecallConfigRequest request,
        MerchantExpiryRecallService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await service.UpdateConfigAsync(
                request.ThresholdValue,
                request.ThresholdUnit,
                request.IsActive,
                cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["thresholdValue"] = [exception.Message] });
        }
    }

    private static IResult ToResult<T>(MerchantRecallCommandResult<T> result, Func<T, IResult> success)
    {
        if (result.NotFound)
        {
            return Results.NotFound();
        }
        if (result.Errors.Count > 0)
        {
            return Results.ValidationProblem(result.Errors.ToDictionary(pair => pair.Key, pair => pair.Value));
        }
        return success(result.Value!);
    }
}

public sealed record CreateMerchantRecallReturnDraftRequest(Guid ReceivingLocationId, int Quantity, string? Notes);
public sealed record MerchantRecallNoStockRequest(string? Note);
public sealed record MerchantExpiryRecallConfigRequest(int ThresholdValue, string? ThresholdUnit, bool IsActive);
