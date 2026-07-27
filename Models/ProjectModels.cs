namespace NinjaTrader_Xen.Models;

public sealed record SaveProjectRequest(
    Guid ProjectId,
    string Title,
    string Task,
    string Model,
    IReadOnlyList<ChatTurn> Messages,
    string? LatestCode);

public sealed record RenameProjectRequest(string Title);
