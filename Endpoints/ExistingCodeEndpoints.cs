using NinjaTrader_Xen.Memory;
using NinjaTrader_Xen.Models;
using System.Security.Claims;

namespace NinjaTrader_Xen.Endpoints;

public static class ExistingCodeEndpoints
{
    public static IEndpointRouteBuilder MapExistingCodeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects/{projectId:guid}/existing-code")
            .RequireAuthorization();
        group.MapGet("/", Get);
        group.MapPut("/", Save);
        return app;
    }

    private static async Task<IResult> Get(Guid projectId, HttpContext context,
        IExistingCodeStateStore store, CancellationToken cancellationToken)
    {
        if (!TryGetSubscriberId(context, out var subscriberId))
            return Results.Unauthorized();
        return Results.Ok(await store.GetAsync(subscriberId, projectId,
            cancellationToken) ?? ExistingCodeState.Empty);
    }

    private static async Task<IResult> Save(Guid projectId,
        SaveExistingCodeStateRequest request, HttpContext context,
        IExistingCodeStateStore store, CancellationToken cancellationToken)
    {
        if (!TryGetSubscriberId(context, out var subscriberId))
            return Results.Unauthorized();
        if (!ExistingCodeContext.IsExistingCodeTask(request.Task))
            return Results.BadRequest(new { message = "Source state is limited to existing-code tasks." });
        if (request.Sources.Count > 4)
            return Results.BadRequest(new { message = "Add no more than four source files." });
        if (request.Sources.Count(source => source.Role == "current-source") > 1)
            return Results.BadRequest(new { message = "Only one current source is allowed." });
        if (request.Sources.Any(source => string.IsNullOrWhiteSpace(source.Code) ||
                source.Code.Length > 500_000 || string.IsNullOrWhiteSpace(source.FileName)) ||
            request.Sources.Sum(source => source.Code.Length) > 800_000)
            return Results.BadRequest(new { message = "The supplied source is empty or too large." });

        var existing = await store.GetAsync(subscriberId, projectId, cancellationToken);
        var state = new ExistingCodeState(
            request.Sources.Select(source => source with
            {
                Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id.Trim(),
                FileName = Path.GetFileName(source.FileName.Trim()),
                Role = source.Role is "additional-source" or "reference-source"
                    ? source.Role
                    : "current-source",
                Code = source.Code.Trim()
            }).ToArray(),
            existing?.Decisions ?? request.Decisions ?? [],
            existing?.WorkingCode ?? request.WorkingCode);
        await store.SaveAsync(subscriberId, projectId, request.Task, state,
            cancellationToken);
        return Results.Ok(state);
    }

    private static bool TryGetSubscriberId(HttpContext context, out int subscriberId) =>
        int.TryParse(context.User.FindFirstValue("sid"), out subscriberId) &&
        subscriberId > 0;
}
