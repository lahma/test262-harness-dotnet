using System.Linq;

using TUnit.Core;

namespace Generated.Tests;

#pragma warning disable

public class BuiltInsTests : Test262Test
{
    [NotInParallel]
    [Test]
    [Arguments("built-ins/Async/baz.js", false, Categories = new[] { "async" })]
    public void Async(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    [Test]
    [Skip("Feature Atomics.waitAsync excluded")]
    public void Atomics_waitAsync()
    {
    }

    [Test]
    [Arguments("built-ins/Foo/bar.js", false, Categories = new[] { "Foo" })]
    public void Foo(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

}
