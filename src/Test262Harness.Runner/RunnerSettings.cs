using System.ComponentModel;
using Spectre.Console.Cli;

namespace Test262Harness.Runner;

public class RunnerSettings : CommandSettings
{
    [Description("Type of host to run tests in")]
    [CommandOption("[hostType]")]
    public string HostType { get; set; } = "";

    [Description("Run this many tests in parallel.")]
    [CommandOption("[threads]")]
    public int Threads { get; set; } = Environment.ProcessorCount;

    [Description("Comma-separated list of features to filter for. Example: --features=\"BigInt,Atomics\"")]
    public string[] Features { get; set; } = Array.Empty<string>();

    [Description("Root test262 directory and is used to locate the includes directory.")]
    [CommandOption("test262Dir")]
    public string Test262Dir { get; set; } = "";

    [Description("Includes directory. Inferred from test262Dir or else detected by walking upward from the first test found.")]
    [CommandOption("includesDir")]
    public string IncludesDir { get; set; } = "";

    [Description("Return a non-zero exit code if one or more tests fail.")]
    [CommandOption("errorForFailures")]
    public string ErrorForFailures { get; set; } = "";
}
