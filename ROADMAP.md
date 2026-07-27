# NinjaTrader Xen Roadmap

## Product baseline

The current dark Manrope interface with restrained NinjaTrader-inspired
red-orange accents is the approved visual baseline.

Future work should reuse mature cTrader Xen workflows and infrastructure while
retaining the NinjaTrader theme and NinjaScript terminology. Do not copy the
cTrader visual theme or introduce cTrader-specific features.

## Next development priorities

### 1. Conversation and project persistence

- Projects list
- Conversation history
- Create, rename, load and delete projects
- Persist task, model, messages and latest complete source
- Restore a project after signing in on another browser
- Enforce `PlatformId = 2` on every project and conversation query

### 2. NinjaTrader RAG

- Retrieve only `Code.PlatformId = 2`
- NinjaTrader Strategy and Indicator categories
- Curated, compile-tested NinjaScript examples
- Similarity confidence handling
- Clear fallback when API usage cannot be verified

### 3. Code workspace

- Code View
- Dedicated C# editor with syntax highlighting
- Copy and `.cs` download
- Preserve the latest complete generated source
- Source snapshots and version restore
- Snippet View for explanation-only responses

### 4. Generation controls

- Stop generation
- Clear active task
- Active-task status
- Prevent task changes during generation
- Better retry and provider-error messages
- Model-specific cost warnings where appropriate

### 5. Guided NinjaTrader workflows

- Task guides for Strategies and Indicators
- Convert strategy from another platform
- Convert indicator from another platform
- Strategy Analyzer and backtesting guidance
- Additional tasks behind a compact “More tasks” control
- Prompt examples tailored to NinjaScript

### 6. Repair workflow

- Upload or paste an existing `.cs` file
- Diagnose NinjaScript compiler output
- Automated repair attempts
- Version comparison and rollback
- Compilation support only after a safe NinjaTrader build architecture is proven

### 7. Account and support

- Top-up workflow
- Logout control in the workspace
- Feedback
- Report a bug
- Video guide
- Prompt help
- User guide
- Account identity in the navigation

## Deferred or optional

- Light mode: optional; dark remains the default and primary design.
- Server-side NinjaTrader compilation and import packages.
- NinjaTrader backtest-report parsing.
- Screenshot/image input.

## Explicitly excluded

- cTrader cBots
- cAlgo APIs
- cTrader plugins and trading panels
- `.algo` files
- cTrader documentation retrieval
- cTrader backtest parsing
