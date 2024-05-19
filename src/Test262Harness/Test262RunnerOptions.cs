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

    public Action<Test262File> Execute { get; set; }= _ => throw new NotImplementedException("Execute callback not implemented");

    public Func<Test262File, bool> IsIgnored { get; set; }  = _ => false;

    /// <summary>
    /// Logic to determine whether given test case should throw. For parsers this might mean to check only parse phase, for
    /// interpreters also runtime errors should be considered.
    /// </summary>
    public Func<Test262File, bool> ShouldThrow { get; set; } = _ => true;

    /// <summary>
    /// Logic to tell whether thrown exception was parsing error.
    /// </summary>
    public Func<Exception, bool> IsParseError { get; set; } = _ => false;

    /// <summary>
    /// Logic to tell whether thrown exception was resolution error.
    /// </summary>
    public Func<Exception, bool> IsResolutionError { get; set; } = _ => false;

    /// <summary>
    /// Logic to tell whether thrown exception was resolution error.
    /// </summary>
    public Func<Exception, bool> IsRuntimeError { get; set; } = _ => false;

    public Action<Test262File> OnTestExecuted { get; set; } = _ => { };

}
