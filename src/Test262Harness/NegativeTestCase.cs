namespace Test262Harness;

public sealed class NegativeTestCase
{
    public NegativeTestCase(
        TestingPhase phase,
        ExpectedErrorType expectedErrorType)
    {
        Phase = phase;
        Type = expectedErrorType;
    }

    public TestingPhase Phase { get; }
    public ExpectedErrorType Type { get; }
}
