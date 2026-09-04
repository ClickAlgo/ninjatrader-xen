using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using NinjaTrader_Xen.Endpoints;
using NinjaTrader_Xen.Infrastructure;
using NinjaTrader_Xen.Logging;
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

var fileLogging = builder.Configuration
    .GetSection(FileLoggingOptions.SectionName)
    .Get<FileLoggingOptions>() ?? new FileLoggingOptions();
if (fileLogging.Enabled)
    builder.Logging.AddProvider(new RollingFileLoggerProvider(fileLogging));

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
builder.Services
    .AddOptions<NinjaTraderCompilerOptions>()
    .Bind(builder.Configuration.GetSection(
        NinjaTraderCompilerOptions.SectionName));

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

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
builder.Services.AddScoped<FreeTrialService>();
builder.Services.AddScoped<DisposableEmailGuard>();
builder.Services.AddHttpClient("mail-check", client =>
{
    client.BaseAddress = new Uri("https://mailcheck.p.rapidapi.com/");
    client.Timeout = TimeSpan.FromSeconds(6);
});
builder.Services.AddHttpClient("proxy-check", client =>
{
    client.BaseAddress = new Uri("https://proxycheck.io/v3/");
    client.Timeout = TimeSpan.FromSeconds(6);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "ClickAlgo-NinjaTrader-Xen/1.0");
});
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
builder.Services.AddHttpClient("moonshot", client =>
{
    var baseUrl =
        builder.Configuration["Moonshot:BaseUrl"] ??
        "https://api.moonshot.ai/v1/";
    if (!baseUrl.EndsWith('/'))
        baseUrl += "/";

    client.BaseAddress = new Uri(baseUrl);
    var apiKey =
        builder.Configuration["Moonshot:ApiKey"] ??
        Environment.GetEnvironmentVariable("MOONSHOT_API_KEY");
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
});
builder.Services.AddSingleton<IAiStreamingProvider, OpenAiStreamingClient>();
builder.Services.AddSingleton<IAiStreamingProvider, ClaudeStreamingClient>();
builder.Services.AddSingleton<IAiStreamingProvider, DeepSeekStreamingClient>();
builder.Services.AddSingleton<IAiStreamingProvider, MoonshotStreamingClient>();
builder.Services.AddSingleton<AiStreamingClient>();
builder.Services.AddSingleton<SystemPromptService>();
builder.Services.AddSingleton<NinjaTraderKnowledgeRetriever>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<RequestRouterService>();
builder.Services.AddSingleton<RequestRoutingCoordinator>();
builder.Services.AddSingleton<PromptBuilderService>();
builder.Services.AddSingleton<RequirementsValidationService>();
builder.Services.AddSingleton<NinjaTraderPreflightCompiler>();
builder.Services.AddScoped<NinjaTrader_Xen.Memory.IProjectMemoryStore,
    NinjaTrader_Xen.Memory.SqlProjectMemoryStore>();
builder.Services.AddScoped<NinjaTrader_Xen.Memory.IExistingCodeStateStore,
    NinjaTrader_Xen.Memory.SqlExistingCodeStateStore>();

var app = builder.Build();

app.UseExceptionHandler();
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
app.MapExistingCodeEndpoints();
app.MapStripeEndpoints();
app.MapPromptBuilderEndpoints();
app.MapFeedbackEndpoints();
app.MapRequirementsEndpoints();
app.MapPreflightBuildEndpoints();

app.Run();

public partial class Program;
