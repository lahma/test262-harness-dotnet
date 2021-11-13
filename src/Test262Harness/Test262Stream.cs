namespace Test262Harness;

public sealed class Test262Stream
{
    private readonly string _baseDirectory;

    public Test262Stream(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
    }

    public IEnumerable<Test262File> GetTestCases()
    {
        var targetFiles = Array.Empty<string>()
            .Concat(Directory.GetFiles(Path.Combine(_baseDirectory, "test", "annexB"), "*.*", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(_baseDirectory, "test", "built-ins"), "*.*", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(_baseDirectory, "test", "intl402"), "*.*", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(_baseDirectory, "test", "language"), "*.*", SearchOption.AllDirectories))
            .Where(x => x.IndexOf("_FIXTURE", StringComparison.OrdinalIgnoreCase) == -1);

        foreach (var filePath in targetFiles)
        {
            foreach (var testCase in Test262File.FromFile(filePath))
            {
                yield return testCase;
            }
        }
    }
}
