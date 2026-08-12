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
- OpenAI, Claude, DeepSeek and Moonshot providers with configurable model pricing
- GPT 5.6 Luna low-cost model and retained, currently hidden Kimi/Moonshot integration
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
- Compound-request RAG query decomposition, category-aware retrieval,
  deduplication and a three-result injection limit
- Strategy RAG allocation that combines relevant indicator examples with a
  strategy structure example
- Configurable RAG similarity threshold, total context cap and multi-result
  confidence diagnostics
- Integrated Prompt Builder and persistent staged Build Plans
- Project persistence, source snapshots and compressed database conversation memory
- Semantic Luna request routing that separates coding requests from questions
  without a keyword dictionary
- RAG only for build, modify, convert and repair intents; questions and analysis
  bypass retrieval
- Durable project memory limited to build, modify and convert requirements so
  questions, analysis and repair chatter do not dilute future model context
- Clarification and Prompt Builder stages bypass RAG and memory until an approved
  coding request or Build Plan step is ready
- Source upload, formatted code display, copy and download
- Build-gated NinjaTrader Add-On ZIP generation alongside `.cs` download
- Indicator reference-image uploads for supported vision models
- Cancel generation, clear input and clear task controls
- Feedback and diagnostic problem reporting
- Contextual feedback invitation when a response appears not to meet the user's expectations
- Oversized-request protection that directs complex builds into shorter
  build-test-build Prompt Builder steps
- Persistent light/dark workspace theme with dark as the default
- Requirements verification with percentage status and focused repair
- Isolated NinjaTrader assembly preflight builds with compiler-error repair
- Automatic Build Check after Build Plan steps and AI-generated repairs when
  the response contains a complete C# source file
- Controlled repair workflow that reports each failed build and requires user
  action before another repair, preventing uncontrolled repair loops
- Repair requests use the current source and exact diagnostics as authoritative
  context and bypass unrelated RAG examples
- Strategy Analyzer Summary CSV analysis with optional Trades CSV context
- GPT Luna semantic prechecks reused by Prompt Builder with short-lived decision caching
- Neutral request progress messaging suitable for coding requests and questions
- Structured response rendering for Markdown headings from levels one through six
- Automated regression-test project covering routing, RAG, memory and request policies
- Global exception handling with configurable file logging and safe client responses

## Reusable Xen platform blueprint

The following completed architecture should be carried into future
platform-specific Xen products rather than rebuilt independently:

- Platform-scoped subscribers, credits, projects and RAG records using `PlatformId`
- Shared authentication, Stripe top-ups, trial-abuse controls and disposable-email checks
- Provider-neutral streaming clients, model metadata, pricing and persisted model selection
- Task-specific system prompts with platform lifecycle and API rules isolated from shared prompts
- Semantic request routing before retrieval: coding intent uses platform RAG;
  questions and analysis do not
- RAG query decomposition, category routing, deduplication, context budgets and debug diagnostics
- Compressed durable project memory containing requirements and authoritative code,
  while excluding ordinary questions and transient repair discussion
- Existing-code continuity for strategies and indicators: uploaded source remains
  the authoritative implementation across acknowledgement, clarification,
  follow-up modification, project save/reload and model changes, without asking
  the user to paste or upload it again
- Prompt Builder, clarification workflow and staged Build Plans that retrieve examples
  only when an actionable coding step is ready
- Project history, source snapshots, requirements verification, build/compile checks
  and focused user-controlled repair cycles
- Shared credit enforcement, cancellation, exception handling, configurable logs,
  feedback capture and regression tests
- Platform adapter responsibilities: task names, RAG categories, system prompts,
  compiler/build integration, source packaging and installation guidance

## Complete feature inventory for future Xen platforms

This is the implementation checklist for future platform-specific Xen versions.
Items marked completed are proven in NinjaTrader Xen and should normally be
reused. Platform-specific APIs, lifecycle rules and packaging must still be
validated independently.

### Foundation and account services

- [x] Separate application and deployment from existing production Xen sites
- [x] Shared multi-platform database with platform-scoped subscribers, code,
  projects and usage records
- [x] Registration, verification, login and persisted authenticated sessions
- [x] Same email permitted across supported platforms while accounts remain scoped
- [x] Once-per-platform free credit with device hash, IP-prefix and grant audit records
- [x] Disposable-email protection
- [x] Configuration-driven exact-IP and IPv4/IPv6 CIDR free-trial blocklist,
  applied only to promotional credit without restricting account or paid usage
- [x] Stripe Checkout top-ups, signed webhooks, transaction history and return pages
- [x] Configurable model pricing with immediate token-based credit deduction
- [x] Healthy, low and exhausted credit states, with generation blocked at zero
- [x] Account, top-up, legal, refund and payment-support journeys

### Models and provider architecture

- [x] Provider-neutral streaming interface
- [x] OpenAI, Anthropic, DeepSeek and Moonshot implementations
- [x] Metadata-driven model list, ordering, descriptions, cost badges and allowlists
- [x] Persisted model selection with safe fallback for retired model identifiers
- [x] Task-specific model restrictions and proven-model repair escalation
- [x] Provider keys, endpoints, model identifiers, pricing and logging in configuration
- [x] Usage parsing, timeout handling, cancellation and incomplete-stream protection
- [x] Low-cost semantic model for request classification and Prompt Builder prechecks

### Workspace and task experience

- [x] Dark professional development workspace with optional persistent light theme
- [x] Build, modify, repair and cross-platform conversion tasks for strategies
  and indicators
- [x] Task-specific prompts, placeholders, upload controls and platform terminology
- [x] Source file and supported reference-image uploads
- [x] Structural source-type validation redirects Strategy/Indicator files to
  the matching existing-code task before RAG or model execution
- [x] Code-aware rendering for user source and model source
- [x] Existing Strategy and Existing Indicator uploads become the authoritative
  current source; after acknowledging receipt, simple follow-up changes reuse the
  upload without requesting it again
- [x] Copy source, download source and platform-native package download
- [x] Generation progress, cancellation, clear input and clear task controls
- [x] Projects, rename/delete/restore actions and source snapshots
- [x] Conversation export in a format accessible to ordinary users
- [x] Responsive model selector and workspace layout
- [x] Product version display, support link and platform user-guide link

### Request planning and context policy

- [x] Semantic classification of questions, coding requests and analysis without
  relying on a fragile keyword dictionary
- [x] Questions bypass RAG and are excluded from durable requirement memory
- [x] Vague material build requests open Prompt Builder before retrieval or coding
- [x] Prompt Builder asks only the necessary number of clarification questions
- [x] Optional baseline suggestions for users who want Xen to choose sensible defaults
- [x] Approved answers become a persistent, staged Build Plan
- [x] Active Build Plans can be exited without deleting project history,
  generated source or snapshots
- [x] Task changes warn when workspace work exists and always clear the active
  Build Plan before opening the new task
- [x] Simple contextual modifications bypass unnecessary replanning
- [x] Contradictory, unsafe or source-deficient repair requests may still ask for clarification
- [x] Oversized prompts are redirected into shorter build-test-build steps
- [x] Original intent is retained across clarification, planning and retrieval
- [x] Prompt-review and routing decisions use short-lived caching where safe

### RAG and platform knowledge

- [x] Platform-scoped code-example database
- [x] Task and category-aware retrieval
- [x] Compound prompt decomposition into focused retrieval queries
- [x] Fenced source removed from retrieval queries
- [x] Results merged by record ID, deduplicated and ranked by best similarity
- [x] Configurable similarity threshold, result count and total context budget
- [x] Strategy retrieval reserves a valid strategy structure example while also
  retrieving requested indicator/API examples
- [x] Clarification, ordinary questions and compiler repair bypass RAG
- [x] Retrieval occurs only when an actionable coding step is ready
- [x] Configurable multi-result confidence diagnostics for development

### Project memory and persistence

- [x] Automatic project creation and saving when useful source is returned
- [x] Subsequent modifications update the same project and preserve revisions
- [x] Compressed database conversation memory for long-running projects
- [x] Authoritative current source and durable requirements preserved during compression
- [x] Uploaded existing source persists across acknowledgement and clarification
  turns, subsequent modifications, project saves/reloads and model changes
- [x] Questions, analysis chatter and transient repair discussion excluded from
  durable requirements
- [x] Build diagnostics and repair results retained with the project
- [x] Duplicate diagnostic noise avoided where practical
- [x] Model selection restored independently from project history

### Validation, compilation and repair

- [x] Requirements-to-code verification with percentage and traffic-light status
- [x] Focused repair request generated from missing requirements
- [x] Isolated compilation against installed platform assemblies
- [x] Structured, grouped and readable compiler diagnostics
- [x] Manual Build Check available on generated source
- [x] Prominent accessible Build Check progress state with disabled action,
  working label, spinner and reduced-motion treatment
- [x] Automatic Build Check after completed Build Plan steps
- [x] Automatic Build Check after AI repairs that return complete source
- [x] Incomplete repair responses are saved with a clear explanation that no build ran
- [x] Failed builds expose a repair action and remain under user control
- [x] After repeated failed repairs, offer escalation to a proven coding model
- [x] Never automatically repeat repair indefinitely
- [x] Remind users that server compilation does not replace platform runtime,
  simulation or backtest validation

### Analysis and improvement

- [x] Platform backtest-summary import and AI analysis
- [x] Optional detailed trade export context
- [x] Performance analysis separated from code generation when source is unavailable
- [x] Dedicated Strategy Analyzer report presentation with section cards and
  browser-native print/PDF export, including restored saved reports
- [ ] Guided backtest-to-code improvement workflow requiring source and user approval
- [ ] Comparison of multiple saved backtests
- [ ] Screenshot support for non-exportable analyzer views
- [ ] Richer trade-distribution and robustness analysis
- [ ] Verification that a proposed improvement still matches stated requirements

### Reliability, safety and operations

- [x] Regression-test project covering routing, RAG, memory and workflow policies
- [x] Global exception handling with safe client responses
- [x] Configurable file logging and provider diagnostics
- [x] Generation cancellation and server-side cancellation propagation
- [x] Credit enforcement before costly provider work
- [x] Prompt-size protection and controlled context budgets
- [x] No execution of generated trading source on the server
- [x] No claims of guaranteed compilation, performance or profitability
- [x] Feedback/problem report captures relevant prompt, model and latest source context
- [x] Contextual feedback invitation for visibly frustrated users
- [ ] Production telemetry, provider health monitoring and beta feedback review

### Documentation and launch

- [x] Product homepage describing only currently available capabilities
- [x] Platform-specific user-guide structure and workspace links
- [x] Contextual task guidance links, including Strategy Analyzer CSV export help
- [x] Credit terms, refund policy and payment-support information
- [ ] Complete task guides with screenshots
- [ ] Requirements verification, Build Check and repair documentation
- [ ] Conversion limitations and source-ownership guidance
- [ ] Final accessibility, mobile, security and production deployment review

### Required implementation order for a new platform

1. Prove platform scoping, authentication, credits and an isolated deployment.
2. Define platform tasks, lifecycle rules, system prompts and RAG categories.
3. Add provider routing, request classification and durable memory. Prove that
   uploaded Existing Strategy and Existing Indicator source remains authoritative
   through acknowledgement, follow-up modification, save/reload and model changes.
4. Add Prompt Builder and Build Plans before expanding complex generation.
5. Curate and test platform examples, retrieval allocation and context budgets.
6. Add projects, snapshots, source downloads and native packaging.
7. Prove assembly/API compilation in isolation, then add controlled repair.
8. Add requirements verification and platform backtest-result analysis.
9. Add regression coverage, logging, documentation and launch monitoring.

For TradingView Xen, apply this contract to uploaded `.pine` source for both
Pine Script strategies and indicators. Regression coverage must exercise the
complete sequence: upload source, acknowledge receipt, request a simple change,
return the complete revised source without another source request, save the
project, reopen it and successfully perform another modification.

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
