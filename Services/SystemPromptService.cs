namespace NinjaTrader_Xen.Services;

public sealed class SystemPromptService
{
    private readonly string _corePrompt;
    private readonly IReadOnlyDictionary<string, string> _taskPrompts;

    public SystemPromptService(IWebHostEnvironment environment)
    {
        var promptRoot = Path.Combine(
            environment.ContentRootPath,
            "SystemPrompts",
            "v1");

        _corePrompt = ReadPrompt(promptRoot, "core-system.txt");
        _taskPrompts = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["build-strategy"] =
                ReadPrompt(promptRoot, "build-strategy.txt"),
            ["build-indicator"] =
                ReadPrompt(promptRoot, "build-indicator.txt"),
            ["existing-strategy"] =
                ReadPrompt(promptRoot, "existing-strategy.txt"),
            ["existing-indicator"] =
                ReadPrompt(promptRoot, "existing-indicator.txt"),
            ["convert-strategy"] =
                ReadPrompt(promptRoot, "convert-strategy.txt"),
            ["convert-indicator"] =
                ReadPrompt(promptRoot, "convert-indicator.txt")
        };
    }

    public string Build(string task)
    {
        if (!_taskPrompts.TryGetValue(task, out var taskPrompt))
            throw new InvalidOperationException(
                $"No system prompt is configured for task '{task}'.");

        return $"{_corePrompt}\n\n{taskPrompt}";
    }

    private static string ReadPrompt(string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Required system prompt '{fileName}' was not found.");
        }

        return File.ReadAllText(path).Trim();
    }
}
