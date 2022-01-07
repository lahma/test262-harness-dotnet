namespace Test262Harness;

public sealed class Test262Runner
{
    private readonly Test262RunnerOptions _options;

    public Test262Runner(Test262RunnerOptions options)
    {
        _options = options;
    }

    public TestExecutionSummary Run(params Test262File[] files)
    {
        return Run((IEnumerable<Test262File>) files);
    }

    public TestExecutionSummary Run(IEnumerable<Test262File> files)
    {
        var summary = new TestExecutionSummary();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism
        };
        Parallel.ForEach(files, options, file =>
        {
            RunTest(file, summary);
            _options.OnTestExecuted(file);
        });
        return summary;
    }

    private void RunTest(Test262File test262File, TestExecutionSummary testExecutionSummary)
    {
        var shouldThrow = _options.ShouldThrow(test262File);

        try
        {
            _options.Execute(test262File);

            if (shouldThrow)
            {
                if (_options.IsIgnored(test262File))
                {
                    testExecutionSummary.Allowed.FalsePositive.Add(test262File);
                }
                else
                {
                    testExecutionSummary.Disallowed.FalsePositive.Add(test262File);
                }
            }
            else
            {
                testExecutionSummary.Allowed.Success.Add(test262File);
            }
        }
        catch (Exception ex)
        {
            var validError = test262File.NegativeTestCase?.Phase == TestingPhase.Parse && _options.IsParseError(ex)
                             || test262File.NegativeTestCase?.Phase == TestingPhase.Resolution && _options.IsResolutionError(ex)
                             || test262File.NegativeTestCase?.Phase == TestingPhase.Runtime && _options.IsRuntimeError(ex);

            if (!validError)
            {
                // unhandled
                if (_options.IsIgnored(test262File))
                {
                    testExecutionSummary.Allowed.FalseNegative.Add(test262File);
                }
                else
                {
                    testExecutionSummary.Disallowed.Failure.Add(test262File);
                }
            }
            else if (!shouldThrow)
            {
                if (_options.IsIgnored(test262File))
                {
                    testExecutionSummary.Allowed.FalseNegative.Add(test262File);
                }
                else
                {
                    testExecutionSummary.Disallowed.FalseNegative.Add(test262File);
                }
            }
            else
            {
                // valid and should throw
                testExecutionSummary.Allowed.Failure.Add(test262File);
            }
        }
    }
}
