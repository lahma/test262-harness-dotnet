using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using JavaScriptEngineSwitcher.Core;
using JavaScriptEngineSwitcher.Jint;
using Spectre.Console;

namespace Test262Harness.Runner;

public static class Program
{
    public static int Main(string[] args)
    {
        var rootDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location) ?? string.Empty;
        var projectRoot = Path.Combine(rootDirectory, "../../..");
        var rootTestDirectory = Path.Combine(projectRoot, "node_modules", "test262");
        if (!Directory.Exists(rootTestDirectory))
        {
            AnsiConsole.Markup(CultureInfo.InvariantCulture, "[red]Could not find test262 test suite from {0}, did you forget to run npm ci?[/]", rootTestDirectory);
            return -1;
        }

        var stream = Test262Stream.FromDirectory(rootTestDirectory, options =>
        {
            options.LogInfo = (s, objects) =>
            {
                s = s.Replace("{0}", "[yellow]{0}[/]").Replace("{1}", "[yellow]{1}[/]");
                AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, s, objects);
            };
            options.LogError = (s, objects) =>
            {
                s = $"[red]{s}[/]";
                s = s.Replace("{0}", "[yellow]{0}[/]").Replace("{1}", "[yellow]{1}[/]");
                AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, s, objects);
            };
        });

        // we materialize to give better feedback on progress
        var test262Files = new ConcurrentBag<Test262File>();

        var summary = new TestExecutionSummary();

        var targets = new Dictionary<string, Func<IJsEngine>>
        {
            { "Jint", new JintJsEngineFactory().CreateEngine }
        };

        AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(),
                new ElapsedTimeColumn()
            )
            .Start(ctx =>
            {
                var readTask = ctx.AddTask("Loading tests", maxValue: 90_000);
                readTask.StartTask();

                Parallel.ForEach(stream.GetTestFiles(), file =>
                {
                    test262Files.Add(file);
                    readTask.Increment(1);
                });

                readTask.MaxValue = test262Files.Count;
                readTask.StopTask();

                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, "Found [green]{0}[/] test cases to test against", test262Files.Count);

                foreach (var pair in targets)
                {
                    var testTask = ctx.AddTask($"[{pair.Key}] Running tests", maxValue: test262Files.Count);

                    var options = new Test262RunnerOptions {
                        Execute = file =>
                        {
                            var engine = pair.Value();
                            engine.Execute(file.Program);
                        },
                        OnTestExecuted = _ => testTask.Increment(1)
                    };

                    var executor = new Test262Runner(options);
                    executor.Run(stream.GetHarnessFiles());
                    testTask.StopTask();
                }
            });

        AnsiConsole.WriteLine("Testing complete.");

        Report(summary);

        return summary.HasProblems ? 1 : 0;
    }

    private static void JintFactory(IJsEngine engine)
    {
        //engine.EmbedHostObject("print", new ClrFunctionInstance(_engine, "print", (thisObj, args) => TypeConverter.ToString(args.At(0))));

        /*
        var o = _engine.Realm.Intrinsics.Object.Construct(Arguments.Empty);
        o.FastSetProperty("evalScript", new PropertyDescriptor(new ClrFunctionInstance(_engine, "evalScript",
            (thisObj, args) =>
            {
                if (args.Length > 1)
                {
                    throw new Exception("only script parsing supported");
                }

                var options = new ParserOptions {AdaptRegexp = true, Tolerant = false};
                var parser = new JavaScriptParser(args.At(0).AsString(), options);
                var script = parser.ParseScript();

                return _engine.Evaluate(script);
            }), true, true, true));

        o.FastSetProperty("createRealm", new PropertyDescriptor(new ClrFunctionInstance(_engine, "createRealm",
            (thisObj, args) =>
            {
                var realm = _engine._host.CreateRealm();
                realm.GlobalObject.Set("global", realm.GlobalObject);
                return realm.GlobalObject;
            }), true, true, true));

        o.FastSetProperty("detachArrayBuffer", new PropertyDescriptor(new ClrFunctionInstance(_engine, "detachArrayBuffer",
            (thisObj, args) =>
            {
                var buffer = (ArrayBufferInstance) args.At(0);
                buffer.DetachArrayBuffer();
                return JsValue.Undefined;
            }), true, true, true));
        engine.EmbedHostObject("$262", o);
*/
    }

    private static void Report(TestExecutionSummary testExecutionSummary)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine("Summary:");

        AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, " [green]:check_mark: {0}[/] valid programs parsed without error", testExecutionSummary.Allowed.Success.Count);
        AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, " [green]::check_mark: {0}[/] invalid programs produced a parsing error", testExecutionSummary.Allowed.Failure.Count);
        AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, " [green]::check_mark: {0}[/] invalid programs did not produce a parsing error (and in allow file)", testExecutionSummary.Allowed.FalsePositive.Count);
        AnsiConsole.MarkupLine(CultureInfo.InvariantCulture, " [green]::check_mark: {0}[/] valid programs produced a parsing error (and in allow file)", testExecutionSummary.Allowed.FalseNegative.Count);

        var items = new (ConcurrentBag<Test262File> Tests, string Label)[]
        {
            (testExecutionSummary.Disallowed.Failure, "invalid programs produced a parsing error (in violation of the whitelist file)"),
            (testExecutionSummary.Disallowed.FalsePositive, "invalid programs did not produce a parsing error (without a corresponding entry in the whitelist file)"),
            (testExecutionSummary.Disallowed.FalseNegative, "valid programs produced a parsing error (without a corresponding entry in the whitelist file)")
        };

        if (testExecutionSummary.HasProblems)
        {
            AnsiConsole.WriteLine();

            foreach (var (tests, label) in items)
            {
                if (tests.IsEmpty)
                {
                    continue;
                }

                AnsiConsole.MarkupLine($" [red]:cross_mark: {tests.Count}[/] {label}");

                AnsiConsole.MarkupLine("Details:");
                foreach (var file in tests)
                {
                    //AnsiConsole.WriteLine($"  {file}");
                }
            }
        }

        if (testExecutionSummary.Unrecognized.Count > 0)
        {
            AnsiConsole.MarkupLine($" :cross_mark: {testExecutionSummary.Unrecognized.Count} non-existent programs specified in the allow list file");
            AnsiConsole.MarkupLine("Details:");
            foreach (var file in testExecutionSummary.Unrecognized)
            {
                AnsiConsole.WriteLine($"  {file}");
            }
        }
    }

}
