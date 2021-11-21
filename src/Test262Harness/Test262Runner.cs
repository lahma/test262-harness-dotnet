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
        var shouldThrow = test262File.NegativeTestCase?.Type == ExpectedErrorType.SyntaxError;

        try
        {
            _options.Execute(test262File);

            if (shouldThrow)
            {
                if (_options.IsIgnored(test262File))
                {
                    testExecutionSummary.AllowedFalsePositive.Add(test262File);
                }
                else
                {
                    testExecutionSummary.DisallowedSuccess.Add(test262File);
                }
            }
            else
            {
                testExecutionSummary.AllowedSuccess.Add(test262File);
            }
        }
        catch (Exception ex)
        {
            if (shouldThrow && _options.IsParseError(ex))
            {
                testExecutionSummary.AllowedFailure.Add(test262File);
            }
            if (_options.IsIgnored(test262File))
            {
                testExecutionSummary.AllowedFalseNegative.Add(test262File);
            }
            else
            {
                testExecutionSummary.DisallowedFalseNegative.Add(test262File);
            }
        }
    }
}
