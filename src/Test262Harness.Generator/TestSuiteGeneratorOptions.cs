using System.Text.Json.Serialization;

namespace Test262Harness.TestSuite.Generator;

public class TestSuiteGeneratorOptions
{
    public string SuiteGitSha { get; set; } = "";
    public string SuiteDirectory { get; set; } = "";

    [JsonIgnore]
    public TestFramework TestFramework { get; set; } = TestFramework.NUnit;

    public string TargetPath { get; set; } = ".";

    public string Namespace { get; set; } = "Test262Harness.TestSuite";
    public bool Parallel { get; set; } = true;

    public string[] ExcludedFeatures { get; set; } = [];
    public string[] ExcludedFlags { get; set; } = [];
    public string[] ExcludedDirectories { get; set; } = [];
    public string[] ExcludedFiles { get; set; } = [];

    public string[] NonParallelFeatures { get; set; } = [];
    public string[] NonParallelFlags { get; set; } = [];
    public string[] NonParallelFiles { get; set; } = [];

    public string ExcludedFilesSource { get; set; } = "";

    /// <summary>
    /// Total number of shards to split the generated suite into. Defaults to 1 (no sharding).
    /// When greater than 1, each generation only emits the test cases belonging to <see cref="ShardIndex"/>,
    /// letting a CI matrix compile and run distinct slices of the suite in parallel. The assignment is a
    /// stable, order-independent hash of the test file name, so the shards are deterministic across runs,
    /// machines and operating systems, and their union is exactly the full (unsharded) suite.
    /// </summary>
    [JsonIgnore]
    public int ShardCount { get; set; } = 1;

    /// <summary>
    /// Zero-based index of the shard to emit when <see cref="ShardCount"/> is greater than 1.
    /// Must be in the range <c>[0, ShardCount)</c>.
    /// </summary>
    [JsonIgnore]
    public int ShardIndex { get; set; }
}
