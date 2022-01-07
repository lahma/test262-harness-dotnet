using System.Collections.Concurrent;

namespace Test262Harness;

/// <summary>
/// Contains test case information from test execution.
/// </summary>
public sealed class TestExecutionSummary
{
    /// <summary>
    /// Successful test case statistics.
    /// </summary>
    public AllowedContainer Allowed { get; } = new();

    /// <summary>
    /// Problematic test case statistics.
    /// </summary>
    public DisallowedContainer Disallowed { get; } = new();

    /// <summary>
    /// Unrecognized test cases.
    /// </summary>
    public List<string> Unrecognized { get; } = new();

    /// <summary>
    /// Were any problems found.
    /// </summary>
    public bool HasProblems => Problems.Any();

    public IEnumerable<Test262File> Problems =>
        Disallowed.FalseNegative.Concat(Disallowed.FalsePositive).Concat(Disallowed.Failure);

    public class AllowedContainer
    {
        /// <summary>
        /// Test cases that were handled successfully, as expected.
        /// </summary>
        public ConcurrentBag<Test262File> Success { get; } = new();

        /// <summary>
        /// Negative cases that threw error, as expected.
        /// </summary>
        public ConcurrentBag<Test262File> Failure { get; } = new();

        /// <summary>
        /// Test cases that succeeded when they should not have.
        /// </summary>
        public ConcurrentBag<Test262File> FalsePositive { get; } = new();

        /// <summary>
        /// Test cases that failed when they should not have.
        /// </summary>
        public ConcurrentBag<Test262File> FalseNegative { get; } = new();
    }

    public class DisallowedContainer
    {
        /// <summary>
        /// Test run failed with unknown error.
        /// </summary>
        public ConcurrentBag<Test262File> Failure { get; } = new();

        /// <summary>
        /// Test cases which did not throw exception when they should have.
        /// </summary>
        public ConcurrentBag<Test262File> FalsePositive { get; } = new();

        /// <summary>
        /// Test cases which thew exception when they should not have.
        /// </summary>
        public ConcurrentBag<Test262File> FalseNegative { get; } = new();
    }
}
