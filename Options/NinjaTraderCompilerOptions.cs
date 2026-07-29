namespace NinjaTrader_Xen.Options;

public sealed class NinjaTraderCompilerOptions
{
    public const string SectionName = "NinjaTraderCompiler";

    public bool Enabled { get; set; } = true;
    public string DotNetPath { get; set; } =
        @"C:\Program Files\dotnet\dotnet.exe";
    public string NinjaTraderBinPath { get; set; } =
        @"C:\Program Files\NinjaTrader 8\bin";
    public string CustomAssemblyPath { get; set; } =
        @"C:\Program Files\NinjaTrader 8\bin\Custom\Backup\NinjaTrader.Custom.dll";
    public string TempRoot { get; set; } =
        @"C:\XenBuilds\NinjaTrader";
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxConcurrentBuilds { get; set; } = 2;
}
