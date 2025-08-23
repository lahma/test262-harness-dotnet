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

    public string ExcludedFilesSource { get; set; } = "";
}
