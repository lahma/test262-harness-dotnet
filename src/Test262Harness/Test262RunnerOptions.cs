using System.ComponentModel;

namespace Test262Harness;

public sealed class Test262RunnerOptions
{
    public int MaxDegreeOfParallelism { get; set; } = Environment.ProcessorCount;

    [Description("List of features to filter for. Example: BigInt,Atomics")]
    public string[] Features { get; set; } = Array.Empty<string>();

    [Description("Root test262 directory and is used to locate the includes directory.")]
    public string Test262Dir { get; set; } = "";

    [Description("Includes directory. Inferred from test262Dir or else detected by walking upward from the first test found.")]
    public string IncludesDir { get; set; } = "";

    public Action<Test262File> Execute = _ => throw new NotImplementedException("Execute callback not implemented");

    public Func<Test262File, bool> IsIgnored { get; set; }  = _ => false;

    public Func<Exception, bool> IsParseError { get; set; } = _ => throw new NotImplementedException("IsParseError callback not implemented");

    public Action<Test262File> OnTestExecuted { get; set; } = _ => { };
}
