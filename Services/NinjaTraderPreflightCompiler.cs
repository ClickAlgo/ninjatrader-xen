using Microsoft.Extensions.Options;
using NinjaTrader_Xen.Models;
using NinjaTrader_Xen.Options;
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace NinjaTrader_Xen.Services;

public sealed partial class NinjaTraderPreflightCompiler
{
    private readonly IOptionsMonitor<NinjaTraderCompilerOptions> _options;
    private readonly ILogger<NinjaTraderPreflightCompiler> _logger;
    private readonly SemaphoreSlim _buildSlots;

    public NinjaTraderPreflightCompiler(
        IOptionsMonitor<NinjaTraderCompilerOptions> options,
        ILogger<NinjaTraderPreflightCompiler> logger)
    {
        _options = options;
        _logger = logger;
        _buildSlots = new SemaphoreSlim(
            Math.Clamp(options.CurrentValue.MaxConcurrentBuilds, 1, 8));
    }

    public bool TryGetConfigurationError(out string? error)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled)
        {
            error = "NinjaTrader preflight builds are disabled.";
            return false;
        }

        if (!File.Exists(options.DotNetPath))
        {
            error = "The configured .NET build executable was not found.";
            return false;
        }

        if (!Directory.Exists(options.NinjaTraderBinPath))
        {
            error = "The configured NinjaTrader installation was not found.";
            return false;
        }

        foreach (var assembly in RequiredAssemblyPaths(options))
        {
            if (!File.Exists(assembly))
            {
                error =
                    $"A required NinjaTrader assembly was not found: {Path.GetFileName(assembly)}.";
                return false;
            }
        }

        error = null;
        return true;
    }

    public async Task<PreflightBuildResult> BuildAsync(
        string source,
        CancellationToken cancellationToken)
    {
        if (!TryGetConfigurationError(out var configurationError))
        {
            throw new InvalidOperationException(configurationError);
        }

        await _buildSlots.WaitAsync(cancellationToken);
        var timer = Stopwatch.StartNew();
        string? buildDirectory = null;
        try
        {
            var options = _options.CurrentValue;
            var tempRoot = Path.GetFullPath(options.TempRoot);
            Directory.CreateDirectory(tempRoot);
            buildDirectory = Path.Combine(
                tempRoot,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(buildDirectory);

            var projectPath = Path.Combine(
                buildDirectory,
                "NinjaScriptPreflight.csproj");
            var sourcePath = Path.Combine(
                buildDirectory,
                "SubmittedNinjaScript.cs");
            await File.WriteAllTextAsync(
                projectPath,
                CreateProject(options),
                Encoding.UTF8,
                cancellationToken);
            await File.WriteAllTextAsync(
                sourcePath,
                source,
                Encoding.UTF8,
                cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = options.DotNetPath,
                WorkingDirectory = buildDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("build");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("--nologo");
            startInfo.ArgumentList.Add("--verbosity");
            startInfo.ArgumentList.Add("minimal");
            startInfo.ArgumentList.Add("--no-cache");
            startInfo.ArgumentList.Add(
                "--property:RestoreIgnoreFailedSources=true");
            startInfo.Environment["DOTNET_CLI_HOME"] =
                Path.Combine(tempRoot, ".dotnet");
            startInfo.Environment["DOTNET_NOLOGO"] = "1";
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "The NinjaTrader preflight build process could not be started.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(
                Math.Clamp(options.TimeoutSeconds, 15, 180)));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                await Task.WhenAll(standardOutput, standardError);
                throw;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                await Task.WhenAll(standardOutput, standardError);
                return Failed(
                    timer,
                    new PreflightBuildError(
                        null,
                        null,
                        null,
                        "TIMEOUT",
                        "The NinjaTrader preflight build timed out."));
            }

            var combinedOutput =
                await standardOutput + Environment.NewLine + await standardError;
            if (process.ExitCode == 0)
            {
                return new PreflightBuildResult(
                    true,
                    [],
                    timer.ElapsedMilliseconds);
            }

            var errors = ParseErrors(combinedOutput);
            if (errors.Count == 0)
            {
                errors.Add(new PreflightBuildError(
                    null,
                    null,
                    null,
                    "BUILD",
                    "The preflight build failed without a structured compiler error."));
            }

            return new PreflightBuildResult(
                false,
                errors,
                timer.ElapsedMilliseconds);
        }
        finally
        {
            timer.Stop();
            if (buildDirectory is not null)
                TryDelete(buildDirectory);
            _buildSlots.Release();
        }
    }

    private static PreflightBuildResult Failed(
        Stopwatch timer,
        PreflightBuildError error) =>
        new(false, [error], timer.ElapsedMilliseconds);

    private static string CreateProject(
        NinjaTraderCompilerOptions options)
    {
        var binPath = Path.GetFullPath(options.NinjaTraderBinPath);
        var customAssembly = Path.GetFullPath(options.CustomAssemblyPath);
        var references = new List<(string Name, string Path)>
        {
            ("NinjaTrader.Core", Path.Combine(binPath, "NinjaTrader.Core.dll")),
            ("NinjaTrader.Gui", Path.Combine(binPath, "NinjaTrader.Gui.dll")),
            ("NinjaTrader.Custom", customAssembly)
        };
        AddOptionalReference(
            references,
            "SharpDX",
            Path.Combine(binPath, "SharpDX.dll"));
        AddOptionalReference(
            references,
            "SharpDX.Direct2D1",
            Path.Combine(binPath, "SharpDX.Direct2D1.dll"));

        var referenceXml = string.Join(
            Environment.NewLine,
            references.Select(reference => $"""
                <Reference Include="{Escape(reference.Name)}">
                  <HintPath>{Escape(reference.Path)}</HintPath>
                  <Private>false</Private>
                </Reference>
            """));

        return $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
                <OutputType>Library</OutputType>
                <PlatformTarget>x64</PlatformTarget>
                <UseWPF>true</UseWPF>
                <LangVersion>latest</LangVersion>
                <Nullable>disable</Nullable>
                <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="System.ComponentModel.DataAnnotations" />
            {{referenceXml}}
              </ItemGroup>
            </Project>
            """;
    }

    private static IEnumerable<string> RequiredAssemblyPaths(
        NinjaTraderCompilerOptions options)
    {
        var binPath = Path.GetFullPath(options.NinjaTraderBinPath);
        yield return Path.Combine(binPath, "NinjaTrader.Core.dll");
        yield return Path.Combine(binPath, "NinjaTrader.Gui.dll");
        yield return Path.GetFullPath(options.CustomAssemblyPath);
    }

    private static void AddOptionalReference(
        ICollection<(string Name, string Path)> references,
        string name,
        string path)
    {
        if (File.Exists(path))
            references.Add((name, path));
    }

    private static string Escape(string value) =>
        SecurityElement.Escape(value) ?? string.Empty;

    private static List<PreflightBuildError> ParseErrors(string output)
    {
        var errors = new List<PreflightBuildError>();
        foreach (var originalLine in output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries))
        {
            var line = originalLine.Trim();
            var located = LocatedErrorPattern().Match(line);
            if (located.Success)
            {
                errors.Add(new PreflightBuildError(
                    Path.GetFileName(located.Groups["file"].Value),
                    int.Parse(located.Groups["line"].Value),
                    int.Parse(located.Groups["column"].Value),
                    located.Groups["code"].Value,
                    CleanMessage(located.Groups["message"].Value)));
                continue;
            }

            var general = GeneralErrorPattern().Match(line);
            if (general.Success)
            {
                errors.Add(new PreflightBuildError(
                    null,
                    null,
                    null,
                    general.Groups["code"].Value,
                    CleanMessage(general.Groups["message"].Value)));
            }
        }

        return errors
            .DistinctBy(error => new
            {
                error.File,
                error.Line,
                error.Column,
                error.Code,
                error.Message
            })
            .Take(50)
            .ToList();
    }

    private static string CleanMessage(string message) =>
        ProjectSuffixPattern().Replace(message.Trim(), string.Empty).Trim();

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
            // The process may have exited between the checks.
        }
    }

    private void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Unable to remove NinjaTrader preflight directory {Directory}.",
                directory);
        }
    }

    [GeneratedRegex(
        @"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s+error\s+(?<code>[A-Z]+\d+):\s+(?<message>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex LocatedErrorPattern();

    [GeneratedRegex(
        @"(?:^|:\s+)error\s+(?<code>[A-Z]+\d+):\s+(?<message>.+)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex GeneralErrorPattern();

    [GeneratedRegex(@"\s+\[[^\]]+\.csproj\]\s*$")]
    private static partial Regex ProjectSuffixPattern();
}
