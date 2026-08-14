using System.Runtime.CompilerServices;

namespace NinjaTrader_Xen.Tests;

public sealed class BacktestReportTests
{
    [Fact]
    public void WorkspaceRendersBacktestAnalysisAsReportWithPdfAction()
    {
        var root = GetProjectRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));
        var styles = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "css",
            "site.css"));

        Assert.Contains(
            "assistantMessage.classList.add(\"backtest-report-message\")",
            script);
        Assert.Contains("renderBacktestReport(content, assistantText)", script);
        Assert.Contains("Save report as PDF", script);
        Assert.Contains("reportWindow.print()", script);
        Assert.Contains("normalizeBacktestReportSource(source)", script);
        Assert.Contains("section { break-inside: auto", script);
        Assert.Contains("h2, h3 { break-after: avoid-page", script);
        Assert.Contains("BACKTEST_REPORT_INTRODUCTION", script);
        Assert.Contains("backtest-report-introduction", script);
        Assert.Contains("report-introduction", script);
        Assert.Contains(".backtest-report-introduction", styles);
        Assert.Contains(
            "Backtest results are historical and do not guarantee future performance.",
            script);
        Assert.Contains(".backtest-report-section", styles);
        Assert.Contains(".backtest-report-header", styles);
    }

    [Fact]
    public void BacktestPromptProtectsReportAccuracyAndPdfFormatting()
    {
        var root = GetProjectRoot();
        var prompt = File.ReadAllText(Path.Combine(
            root,
            "SystemPrompts",
            "v1",
            "analyse-backtest.txt"));

        Assert.Contains("Preserve the column association and unit", prompt);
        Assert.Contains("bid/ask spread is zero", prompt);
        Assert.Contains("Sharpe and Sortino ratios", prompt);
        Assert.Contains("Do not use LaTeX delimiters", prompt);
    }

    [Fact]
    public void SavedBacktestProjectRestoresReportPresentation()
    {
        var root = GetProjectRoot();
        var script = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));

        Assert.Contains("isBacktestReportSource(turn.content)", script);
        Assert.Contains("renderBacktestReport(content, turn.content)", script);
        Assert.Contains("Performance summary", script);
        Assert.Contains("Key findings", script);
    }

    [Fact]
    public void AnalyzerUploadShowsContextualExportGuide()
    {
        var root = GetProjectRoot();
        var markup = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "workspace.html"));
        var script = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));

        Assert.Contains("How to export results", markup);
        Assert.Contains(
            "https://help.clickalgo.com/ninjatrader-xen/strategy-analyzer/",
            markup);
        Assert.Contains(
            "analyzerExportGuide.hidden = activeTask !== \"analyse-backtest\"",
            script);
    }

    [Fact]
    public void AnalyzerUploadProvidesSeparateSummaryAndTradesInputs()
    {
        var root = GetProjectRoot();
        var markup = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "workspace.html"));
        var script = File.ReadAllText(Path.Combine(
            root,
            "wwwroot",
            "js",
            "workspace.js"));

        Assert.Contains("Upload Summary CSV (required)", script);
        Assert.Contains("analyzerTradesFileInput", markup);
        Assert.Contains("Add Trades CSV (optional)", markup);
        Assert.Contains("importAnalyzerCsvFile(file, \"trades\"", script);
        Assert.Contains("Trade number", script);
        Assert.Contains("item.type !== expectedType", script);
    }

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            ".."));
}
