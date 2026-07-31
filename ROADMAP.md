# NinjaTrader Xen Roadmap

## Product position

NinjaTrader Xen is an advanced AI development, conversion and repair workspace
for NinjaTrader strategies and indicators.

The native NinjaTrader AI Strategy Builder has the strongest integrated path
for simple strategy generation, compilation and immediate backtesting. Xen
should differentiate through advanced code quality, indicators, existing-code
development, cross-platform conversion, requirements auditing, repair,
multiple model choices and persistent source history.

The approved interface baseline is the dark Manrope theme with restrained
NinjaTrader-inspired red-orange accents.

## Current beta baseline

Completed:

- Registration, authentication and platform-specific accounts
- Free trial credit, pay-as-you-go usage and Stripe top-ups
- Low-credit and exhausted-credit controls
- OpenAI, Claude, DeepSeek and Moonshot providers with model pricing
- Kimi K2.7 Code low-cost coding model
- Seven specialist tasks:
  - Build Strategy
  - Build Indicator
  - Existing Strategy
  - Existing Indicator
  - Convert Strategy
  - Convert Indicator
  - Analyse Backtest
- Dedicated system prompts for every task
- NinjaTrader RAG using `Code.PlatformId = 2`
- Integrated Prompt Builder and persistent staged Build Plans
- Project persistence, conversation history and source snapshots
- Source upload, formatted code display, copy and download
- Indicator reference-image uploads for supported vision models
- Cancel generation, clear input and clear task controls
- Feedback and diagnostic problem reporting
- Persistent light/dark workspace theme with dark as the default
- Requirements verification with percentage status and focused repair
- Isolated NinjaTrader assembly preflight builds with compiler-error repair
- Strategy Analyzer Summary CSV analysis with optional Trades CSV context

## Next development priorities

### 1. Strategy Analyzer analysis expansion

Build on the Summary and Trades CSV workflow without attempting server-side
NinjaTrader backtesting.

- Add screenshot input for charts and non-exportable result views
- Support comparisons between multiple saved backtests
- Add richer trade-distribution analysis for larger Trades exports
- Connect observed results to the current project's stated requirements
- Never present historical performance as a profitability guarantee

#### Backtest-to-Code Improvement Workflow

Add a guided post-launch workflow that turns selected backtest findings into
controlled strategy changes:

- Let the user choose a specific improvement objective from the analysis
- Attach strategy source or open the relevant saved strategy project
- Relate the selected finding to the current implementation
- Present a focused change plan for approval before modifying code
- Generate the revised strategy, then run Build Check and requirements verification
- Ask the user to rerun Strategy Analyzer and compare the new results
- Warn against blindly optimizing every weak metric or fitting changes to one data set

The workflow must not rewrite source from performance figures alone. Code
changes require the relevant strategy source and explicit user approval.

### 2. NinjaTrader Xen connector investigation

Research a small supported NinjaTrader Add-On or connector that can safely:

- Send the active NinjaScript source to its Xen project
- Send compiler errors to the repair workflow
- Receive revised source
- Report compilation success or failure
- Transfer Strategy Analyzer results

Do not begin implementation until the supported NinjaTrader extension,
authentication and local-security architecture have been proven.

### 3. Product guidance and launch preparation

- Task-specific help and examples
- Requirements-verification documentation
- Compiler-repair documentation
- Conversion limitations and copyright guidance
- Production monitoring and provider diagnostics
- Beta feedback review and onboarding refinements

## Deferred or optional

- Server-side NinjaTrader compilation
- Server-side NinjaTrader backtesting
- Automatic optimization
- Direct live-trading or account control

## Explicitly excluded

- cTrader cBots and cAlgo APIs
- cTrader plugins, panels and `.algo` packages
- cTrader documentation retrieval
- Claims of guaranteed compilation, performance or profitability
