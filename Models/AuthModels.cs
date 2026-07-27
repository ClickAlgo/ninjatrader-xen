namespace NinjaTrader_Xen.Models;

public sealed record RegisterRequest(string Email, string Password);

public sealed record LoginRequest(
    string Email,
    string Password,
    string? DeviceFingerprint);
