using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using NinjaTrader_Xen.Endpoints;
using NinjaTrader_Xen.Options;
using NinjaTrader_Xen.Services;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    "chatsettings.json",
    optional: false,
    reloadOnChange: true);

builder.Services
    .AddOptions<PlatformOptions>()
    .Bind(builder.Configuration.GetSection(PlatformOptions.SectionName))
    .Validate(
        options => options.Id == PlatformIds.NinjaTrader,
        $"This application must use NinjaTrader PlatformId {PlatformIds.NinjaTrader}.")
    .Validate(
        options => string.Equals(options.Name, "NinjaTrader", StringComparison.OrdinalIgnoreCase),
        "This application must identify itself as NinjaTrader.")
    .ValidateOnStart();

builder.Services.AddHealthChecks();

var jwtSecret = builder.Configuration["Auth:JwtSecret"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Auth:JwtSecret must be supplied through secure deployment configuration and contain at least 32 characters.");
    }

    jwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    builder.Configuration["Auth:JwtSecret"] = jwtSecret;
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Auth:JwtIssuer"],
            ValidAudience = builder.Configuration["Auth:JwtAudience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var platformClaim = context.Principal?.FindFirstValue("platform_id");
                if (platformClaim != PlatformIds.NinjaTrader.ToString())
                    context.Fail("Token is not valid for NinjaTrader Xen.");

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<AccountEmailSender>();
builder.Services.AddHttpClient("openai", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/");

    var apiKey = builder.Configuration["OpenAI:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
});
builder.Services.AddHttpClient("claude", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Claude:BaseUrl"] ?? "https://api.anthropic.com/");
    var apiKey = builder.Configuration["Claude:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
    client.DefaultRequestHeaders.Add(
        "anthropic-version",
        builder.Configuration["Claude:Version"] ?? "2023-06-01");
});
builder.Services.AddHttpClient("deepseek", client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["DeepSeek:BaseUrl"] ?? "https://api.deepseek.com/");
    var apiKey = builder.Configuration["DeepSeek:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
});
builder.Services.AddSingleton<IAiStreamingProvider, OpenAiStreamingClient>();
builder.Services.AddSingleton<IAiStreamingProvider, ClaudeStreamingClient>();
builder.Services.AddSingleton<IAiStreamingProvider, DeepSeekStreamingClient>();
builder.Services.AddSingleton<AiStreamingClient>();
builder.Services.AddSingleton<SystemPromptService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/status", () => Results.Ok(new
{
    application = "NinjaTrader Xen",
    platform = "NinjaTrader",
    platformId = PlatformIds.NinjaTrader,
    status = "ready"
}));

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapChatEndpoints();
app.MapProjectEndpoints();

app.Run();

public partial class Program;
