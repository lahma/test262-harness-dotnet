namespace Test262Harness;

public sealed class NegativeTestCase
{
    public TestingPhase Phase { get; set; }

    public ExpectedErrorType Type { get; set; } = default;

    public string[] Flags = Array.Empty<string>();
}
