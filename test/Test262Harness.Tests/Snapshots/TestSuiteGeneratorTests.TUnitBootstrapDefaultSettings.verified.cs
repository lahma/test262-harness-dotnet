using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using TUnit.Core;

using Test262Harness;

namespace Generated.Tests;

using static HookType;

/// <summary>
/// Global state for test run. Generated test suite requires that Test262Stream is initialized before tests start to run.
/// </summary>
public static partial class State
{
    /// <summary>
    /// The GitHub SHA used to generate the test suite. Runner should initialize Test262Stream using this value.
    /// </summary>
    public const string GitHubSha = "stable-sha-for-snapshot";

    public const string SuiteDirectory = @"";

    public static Test262Stream Test262Stream { get; set; }

    public static Test262File[] HarnessFiles { get; set; }

    public static Func<Task<Test262Stream>> Test262StreamLoader { get; set; } = () => Test262StreamExtensions.FromGitHub(State.GitHubSha);

}

/// <summary>
/// Handles initializing testing state.
/// </summary>
public static partial class TestHarness
{
    [Before(Assembly)]
    public static async Task RunBeforeAnyTests()
    {
        var stream = await State.Test262StreamLoader();
        State.Test262Stream = stream;
        State.HarnessFiles = State.Test262Stream.GetHarnessFiles().ToArray();

        await InitializeCustomState();
    }

    private static partial Task InitializeCustomState();
}


public abstract partial class Test262Test
{
    protected void RunTestCode(string test, bool strict)
    {
        var testCase = State.Test262Stream.GetTestFile(test);
        if (strict)
        {
            testCase = testCase.AsStrict();
        }

        string lastError = null;
        try
        {
            var executor = BuildTestExecutor(testCase);

            ExecuteTest(executor, testCase);

            if (ShouldThrow(testCase, strict))
            {
                ThrowError(testCase, "Expected error to be thrown, but no error was thrown");
            }
        }
        catch (Exception e)
        {
            lastError = e.ToString();
        }

        if (!testCase.Negative && !string.IsNullOrWhiteSpace(lastError))
        {
            ThrowError(testCase, lastError);
        }
    }

    private partial bool ShouldThrow(Test262File testCase, bool strict); 

    private void ThrowError(Test262File testCase, string lastError)
    {
        // create friendly formatted code as we have different line numbers compared to original file having comments etc
        var code = string.Join(Environment.NewLine, testCase.Program.Split('\n').Select((x, i) => $"{i + 1:00}: {x}"));
        var message = $"{Environment.NewLine}{testCase.FileName}{Environment.NewLine}{Environment.NewLine}{lastError}{Environment.NewLine}{code}";
        Xunit.Assert.Fail(message);
    }
}
