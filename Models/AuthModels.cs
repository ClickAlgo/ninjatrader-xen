namespace NinjaTrader_Xen.Models;

public sealed record RegisterRequest(string Email, string Password, string? TurnstileToken = null);

public sealed record LoginRequest(
    string Email,
    string Password,
    string? DeviceFingerprint);
