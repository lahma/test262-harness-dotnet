namespace Test262Harness.TestSuite.Generator;

public class GeneratorModel
{
    public bool Parallel { get; set; } = true;
    public string Namespace { get; init; } = "";

    public string TestClassName { get; init; } = "";
    public List<TestCaseGroup> TestCaseGroupings { get; set; } = new();
}

public class TestCaseGroup
{
    public TestCaseGroup(string name, List<TestCase> testCases)
    {
        Name = name;
        TestCases = testCases;

        IgnoreCount = testCases.Count(x => !string.IsNullOrWhiteSpace(x.IgnoreReason)); 
        if (testCases.Count == IgnoreCount)
        {
            // ignore the whole method
            var reasons = testCases.Select(x => x.IgnoreReason).Distinct().ToList();
            if (reasons.Count > 1 && reasons.TrueForAll(r => r != null && r.StartsWith("File ", StringComparison.OrdinalIgnoreCase)))
            {
                IgnoreReason = "All files ignored";
            }
            else
            {
                IgnoreReason = string.Join(", ", reasons);
            }
            TestCases.Clear();
        }
    }

    public string Name { get; }
    public List<TestCase> TestCases { get; }
    public int IgnoreCount { get; }
    public string? IgnoreReason { get; }
}

public class TestCase
{
    public TestCase(Test262File file, string? ignoreReason)
    {
        FileName = file.FileName.StartsWith("test/", StringComparison.OrdinalIgnoreCase) ? file.FileName[5..] : file.FileName;
        Category = file.Features.Length > 0 || file.Flags.Length > 0
            ? string.Join(",", file.Features.ToArray().Concat(file.Flags.ToArray()))
            : null;
        IgnoreReason = ignoreReason;
        Strict = file.Strict;
    }

    public string FileName { get; set; }
    public string? Category { get; set; }
    public string? IgnoreReason { get; set; }
    public bool Strict { get; set; }
}
