using System.Linq;

using TUnit.Core;

namespace Generated.Tests;

#pragma warning disable

public class BuiltInsTests : Test262Test
{
    [Test]
    [Arguments("built-ins/Async/baz.js", false, Categories = new[] { "async" })]
    public void Async(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    [Test]
    [Arguments("built-ins/Atomics/waitAsync/descriptor.js", false, Categories = new[] { "Atomics.waitAsync,Atomics" })]
    [Arguments("built-ins/Atomics/waitAsync/length.js", false, Categories = new[] { "Atomics.waitAsync,Atomics" })]
    public void Atomics_waitAsync(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

    [Test]
    [Arguments("built-ins/Foo/bar.js", false, Categories = new[] { "Foo" })]
    public void Foo(string test, bool strict)
    {
        RunTestCode(test, strict);
    }

}
