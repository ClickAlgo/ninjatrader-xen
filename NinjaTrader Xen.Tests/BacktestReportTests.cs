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
        Assert.Contains(
            "Backtest results are historical and do not guarantee future performance.",
            script);
        Assert.Contains(".backtest-report-section", styles);
        Assert.Contains(".backtest-report-header", styles);
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

    private static string GetProjectRoot(
        [CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            ".."));
}
