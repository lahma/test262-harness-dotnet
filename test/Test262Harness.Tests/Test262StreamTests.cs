namespace Test262Harness.Tests;

public class Test262StreamTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public async Task CanLoadFromGitHub()
    {
        var test262Stream = await Test262StreamExtensions.FromGitHub("28b31c0bf1960878abb36ab8597a0cae224a684d");
        test262Stream.GetHarnessFiles().ToList().Should().NotBeEmpty();
        test262Stream.GetTestFiles().ToList().Should().NotBeEmpty();
    }
}
