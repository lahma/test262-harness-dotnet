using System.Linq;

namespace Generated.Tests;

#pragma warning disable

public class BuiltInsTests : Test262Test
{
    [TestCase("built-ins/Async/baz.js", false, Category = "async")]
    public void Async(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    [Test]
    [Ignore("Feature Atomics.waitAsync excluded")]
    public void Atomics_waitAsync()
    {
    }

    [TestCase("built-ins/Foo/bar.js", false, Category = "Foo")]
    public void Foo(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

}
