using JavaScriptEngineSwitcher.Core;

namespace Test262Harness.Runner;


public class Runner
{
    private readonly Dictionary<string, IPrecompiledScript> _includes;
    private readonly IJsEngine _engine;

    public Runner(
        Dictionary<string, IPrecompiledScript> includes,
        IJsEngine engine)
    {
        _includes = includes;
        _engine = engine;
    }

    public void Run(Test262File file)
    {
        _engine.Execute(_includes["sta.js"]);
        _engine.Execute(_includes["assert.js"]);


        foreach (var include in file.Includes)
        {
            _engine.Execute(_includes[include]);
        }

        string? lastError = null;

        try
        {
            _engine.Execute(file.Program);
        }
        catch (JsException j)
        {
            if (!file.Negative)
            {
                throw;
            }
            lastError = j.ToString();
        }
        catch (Exception e)
        {
            if (!file.Negative)
            {
                throw;
            }
            lastError = e.ToString();
        }

        if (!string.IsNullOrWhiteSpace(lastError))
        {
            // TODO throw new XunitException(lastError);
        }
    }
}
