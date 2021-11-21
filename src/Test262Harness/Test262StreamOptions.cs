namespace Test262Harness;

public sealed class Test262StreamOptions
{
    public Test262StreamOptions(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
    }

    public Test262StreamOptions(IEnumerable<string> files)
    {
        Files = files;
    }

    /// <summary>
    /// Base directory to use, this should have "harness" and "test" sub-directories like in https://github.com/tc39/test262 .
    /// </summary>
    public string? BaseDirectory { get; set; }

    /// <summary>
    /// Sub-directories to search. Defaults to: "annexB", "built-ins", "intl402", "language"
    /// </summary>
    public string[] SubDirectories { get; set; } = { "annexB", "built-ins", "intl402", "language" };

    /// <summary>
    /// Possibility to filter matched files.
    /// </summary>
    public Func<Test262File, bool> Filter { get; set; } = _ => true;

    /// <summary>
    /// Provide a list of files instead of a base path.
    /// </summary>
    public IEnumerable<string>? Files { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseDirectory) && Files is null)
        {
            throw new ArgumentException("Need to provide either BaseDirectory of Files");
        }

        if (!string.IsNullOrWhiteSpace(BaseDirectory) && !Directory.Exists(BaseDirectory))
        {
            throw new ArgumentException("Base directory " + BaseDirectory + " does not exist");
        }

        if (!string.IsNullOrWhiteSpace(BaseDirectory) && SubDirectories is null || SubDirectories.Length == 0)
        {
            throw new ArgumentException("Sub-directories must be specified");
        }
    }
}
