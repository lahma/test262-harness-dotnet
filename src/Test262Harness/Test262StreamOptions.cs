namespace Test262Harness;

public sealed class Test262StreamOptions
{
    public Test262StreamOptions(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
    }

    /// <summary>
    /// Base directory to use, this should have "harness" and "test" sub-directories like in https://github.com/tc39/test262 .
    /// </summary>
    public string BaseDirectory { get; set; }

    /// <summary>
    /// Sub-directories to search. Defaults to: "annexB", "built-ins", "intl402", "language"
    /// </summary>
    public string[] SubDirectories { get; set; } = { "annexB", "built-ins", "intl402", "language" };

    public Func<Test262File, bool> Filter { get; set; } = _ => true;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BaseDirectory))
        {
            throw new ArgumentException("Invalid base directory " + BaseDirectory);
        }

        if (!Directory.Exists(BaseDirectory))
        {
            throw new ArgumentException("Base directory " + BaseDirectory + " does not exist");
        }

        if (SubDirectories is null || SubDirectories.Length == 0)
        {
            throw new ArgumentException("Sub-directories must be specified");
        }
    }
}
