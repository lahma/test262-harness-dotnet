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

    [TestCase("built-ins/Atomics/waitAsync/descriptor.js", false, Category = "Atomics.waitAsync,Atomics")]
    [TestCase("built-ins/Atomics/waitAsync/length.js", false, Category = "Atomics.waitAsync,Atomics")]
    public void Atomics_waitAsync(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    [TestCase("built-ins/Foo/bar.js", false, Category = "Foo")]
    public void Foo(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

}
