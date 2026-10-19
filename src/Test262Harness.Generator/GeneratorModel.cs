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

    public bool NonParallelizable => TestCases.Any(tc => tc.NonParallelizable);
}

public class TestCase
{
    public TestCase(Test262File file, string? ignoreReason, bool nonParallelizable = false)
    {
        FileName = file.FileName.StartsWith("test/", StringComparison.OrdinalIgnoreCase) ? file.FileName[5..] : file.FileName;
        Categories = [.. file.Features, .. file.Flags];
        Category = Categories.Length > 0 ? string.Join(",", Categories) : null;
        IgnoreReason = ignoreReason;
        Strict = file.Strict;
        NonParallelizable = nonParallelizable;
    }

    public string FileName { get; set; }

    /// <summary>
    /// The test's features and flags, comma-separated; NUnit splits a category string on commas.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// The test's features and flags, one per element, for frameworks that take categories as a list.
    /// </summary>
    public string[] Categories { get; set; }
    public string? IgnoreReason { get; set; }
    public bool Strict { get; set; }
    public bool NonParallelizable { get; set; }
}
