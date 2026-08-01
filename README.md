# NinjaTrader Xen

NinjaTrader Xen is the isolated NinjaTrader edition of Xen. It runs as a
separate ASP.NET Core application and IIS website while sharing the existing
CodePilot database.

## Platform isolation

- cTrader uses `PlatformId = 1`.
- NinjaTrader uses `PlatformId = 2`.
- The server owns the platform identity. Browser requests must not select or
  override `PlatformId`.
- Subscriber and RAG queries must always filter by the configured platform.

## Configuration

Do not commit credentials. Configure these values through IIS environment
variables or another deployment secret provider:

- `ConnectionStrings__CodePilot`
- `OpenAI__ApiKey`

Production also requires secure environment values for `Auth__JwtSecret`,
`Stripe__SecretKey`, `Stripe__WebhookSecret`, the enabled AI providers, and
SMTP credentials. Start from `appsettings.Production.example.json`; keep RAG
debug output disabled and restrict `AllowedHosts` to the deployed hostname.
The IIS application-pool identity must have modify access to the configured
file-log and preflight-build temporary directories.

## Deliberately excluded

The initial application does not include:

- cTrader build verification or `.algo` downloads
- cTrader backtest report analysis
- cTrader plugins or trading panels
- cTrader documentation retrieval
- cTrader prompts, examples, branding, or task descriptions

## Development phases

1. Application foundation and platform isolation.
2. Shared authentication, subscriber entitlement, billing, and credits.
3. AI chat, conversations, project memory, and NinjaTrader prompt stack.
4. NinjaTrader RAG examples using `Code.PlatformId = 2`.
5. NinjaTrader strategies, indicators, existing-code editing, and source export.
6. Optional NinjaTrader compilation and backtest support.
