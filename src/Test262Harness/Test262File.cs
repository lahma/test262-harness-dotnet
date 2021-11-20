using System.Runtime.CompilerServices;
using YamlDotNet.RepresentationModel;

namespace Test262Harness;

/// <summary>
/// A test case described in https://github.com/tc39/test262/blob/HEAD/CONTRIBUTING.md#test-case-style .
/// </summary>
/// <remarks>
/// Equality is based on <see cref="FileName"/> and <see cref="Strict"/>.
/// </remarks>
public sealed class Test262File : IEquatable<Test262File>
{
    private const string YamlSectionStartMarker = "/*---";
    private const string YamlSectionEndMarker = "---*/";

    private readonly int _yamlStart;
    private readonly int _yamlEnd;

    private string[] _features = Array.Empty<string>();
    private string[] _flags = Array.Empty<string>();
    private string[] _includes = Array.Empty<string>();
    private string[] _locale = Array.Empty<string>();

    private Test262File(
        string fileName,
        string contents,
        int yamlStart,
        int yamlEnd)
    {
        FileName = fileName;
        _yamlStart = yamlStart;
        _yamlEnd = yamlEnd;
        Contents = contents;
    }


    /// <summary>
    /// The root-relative filename separated with slashes.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// This key identifies the hash ID from the portion of the ECMAScript draft which is
    /// most recent to the date the test was added.
    /// </summary>
    public string EcmaScriptId { get; private set; } = "";

    /// <summary>
    /// The author of a test case.
    /// </summary>
    public string Author { get; private set; } = "";

    /// <summary>
    /// Short description of the test case.
    /// </summary>
    public string Description { get; private set; } = "";

    /// <summary>
    /// This allows a long, free-form comment. The comment is almost always a direct quote from ECMAScript.
    /// It is used to indicate the observable being tested within the file.
    /// </summary>
    public string Info { get; private set; } = "";

    /// <summary>
    /// Some tests require the use of one or more specific human languages as exposed by ECMA402 as a means to verify
    /// semantics which cannot be observed in the abstract.
    /// </summary>
    public ReadOnlySpan<string> Locale => _locale.AsSpan();

    /// <summary>
    /// Some tests require the use of language features that are not directly described by the test file's location
    /// in the directory structure. These features should be specified with this key.
    /// See https://github.com/tc39/test262/blob/main/features.txt file for a complete list of available values.
    /// </summary>
    public ReadOnlySpan<string> Features => _features.AsSpan();

    /// <summary>
    /// This key is for boolean properties associated with the test.
    ///
    ///  onlyStrict - only run the test in strict mode
    ///  noStrict - only run the test in "sloppy" mode
    ///  module - interpret the source text as module code
    ///  raw - execute the test without any modification (no helpers will be available); necessary to test the behavior of directive prologue; implies noStrict
    ///  async - defer interpretation of test results until after the invocation of the global $DONE function
    ///  generated - informative flag used to denote test files that were created procedurally using the project's test generation tool; refer to Procedurally-generated tests for more information on this process
    ///  CanBlockIsFalse - only run the test when the [[CanBlock]] property of the Agent Record executing the test file is false
    ///  CanBlockIsTrue - only run the test when the [[CanBlock]] property of the Agent Record executing the test file is true
    ///  non-deterministic - informative flag used to communicate that the semantics under test are intentionally under-specified, so the test's passing or failing status is neither reliable nor an indication of conformance    /// </summary>
    public ReadOnlySpan<string> Flags => _flags.AsSpan();

    /// <summary>
    /// This key names a list of helper files that will be included in the test environment prior to running the test.
    /// The helper files are found in the harness/ directory.
    /// </summary>
    public ReadOnlySpan<string> Includes => _includes.AsSpan();

    public NegativeTestCase? NegativeTestCase { get; private set; }

    /// <summary>
    /// The full contents of the file.
    /// </summary>
    public string Contents { get; }

    /// <summary>
    /// The copyright block of the test.
    /// </summary>
    public ReadOnlySpan<char> Copyright => Contents.AsSpan(0, _yamlStart);

    /// <summary>
    /// The actual code to be interpreted.
    /// </summary>
    public ReadOnlySpan<char> Program => Contents.AsSpan(_yamlEnd);

    public bool Strict { get; private set; }

    public bool Negative => Array.IndexOf(_flags, "negative") != -1 || NegativeTestCase is not null;

    /// <summary>
    /// Type of code, script or module.
    /// </summary>
    public ProgramType Type { get; private set; } = ProgramType.Script;

    public static async IAsyncEnumerable<Test262File> FromFile(
        string filePath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var testPathIndex = filePath.LastIndexOf("\\test\\", StringComparison.OrdinalIgnoreCase);
        if (testPathIndex < 0)
        {
            testPathIndex = filePath.LastIndexOf("/test/", StringComparison.OrdinalIgnoreCase);
        }

        if (testPathIndex < 0)
        {
            throw new ArgumentException($"Given path {filePath} doesn't contain 'test'");
        }

        // the name we refer as rooted
        var normalizedTestFileNamePath = filePath.Substring(testPathIndex + 1).Replace('\\', '/');

        using var reader = new StreamReader(File.OpenRead(filePath));
        var content = await reader.ReadToEndAsync();

        var yamlSectionStart = content.IndexOf(YamlSectionStartMarker, StringComparison.OrdinalIgnoreCase) + YamlSectionStartMarker.Length;
        var yamlSectionEnd = content.IndexOf(YamlSectionEndMarker, StringComparison.OrdinalIgnoreCase);

        if (yamlSectionStart < 0 || yamlSectionEnd < 0)
        {
            throw new ArgumentException($"Test case {normalizedTestFileNamePath} is invalid, cannot find YAML section.");
        }

        var yaml = content.Substring(yamlSectionStart, yamlSectionEnd - yamlSectionStart);

        var yamlStream = new YamlStream();
        yamlStream.Load(new StringReader(yaml));
        var document = yamlStream.Documents[0];

        var onlyStrict = false;
        var noStrict = false;
        var test = new Test262File(normalizedTestFileNamePath, yaml, yamlSectionStart, yamlSectionEnd);
        foreach (var node in (YamlMappingNode) document.RootNode)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scalar = (YamlScalarNode) node.Key;
            var key = scalar.Value;
            switch (key)
            {
                case "esid" or "es5id" or "es6id":
                    test.EcmaScriptId = ReadString(node);
                    break;
                case "description":
                    test.Description = ReadString(node);
                    break;
                case "info":
                    test.Info = ReadString(node);
                    break;
                case "author":
                    test.Author = ReadString(node);
                    break;
                case "features":
                    test._features = ReadStringArray(node.Value);
                    break;
                case "includes":
                    test._includes = ReadStringArray(node.Value);
                    break;
                case "locale":
                    test._locale = ReadStringArray(node.Value);
                    break;
                case "negative":
                    var source = (YamlMappingNode) node.Value;
                    Enum.TryParse<TestingPhase>(source["phase"].ToString(), out var phase);
                    Enum.TryParse<ExpectedErrorType>(source["type"].ToString(), out var expectedErrorType);

                    test.NegativeTestCase = new NegativeTestCase(phase, expectedErrorType);
                    break;
                case "flags":
                    var flags = ReadStringArray(node.Value);
                    foreach (var flag in flags)
                    {
                        switch (flag)
                        {
                            case "module":
                                test.Type = ProgramType.Module;
                                break;
                            case "onlyStrict":
                                onlyStrict = true;
                                break;
                            case "noStrict":
                                noStrict = true;
                                break;
                        }
                    }

                    test._flags = flags;
                    break;
            }
        }


        // we produce two results, non-strict and strict based on configuration
        // this follows the tests262 stream logic
        if (test.Type == ProgramType.Script && !onlyStrict)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return test;
        }

        if (!noStrict)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return test.AsStrict();;
        }
    }

    private Test262File AsStrict()
    {
        var clone = (Test262File) this.MemberwiseClone();
        clone.Strict = true;
        return clone;
    }

    private static string ReadString(KeyValuePair<YamlNode, YamlNode> node)
    {
        return node.Value.ToString();
    }

    private static string[] ReadStringArray(YamlNode node)
    {
        var sequenceNode = (YamlSequenceNode) node;
        if (sequenceNode.Children.Count == 0)
        {
            return Array.Empty<string>();
        }

        var result = new string[sequenceNode.Children.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = sequenceNode.Children[i].ToString();
        }

        return result;
    }

    public override string ToString()
    {
        var mode = Strict ? "(strict mode)" : "(default)";
        return FileName + mode;
    }

    public bool Equals(Test262File? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return FileName == other.FileName && Strict == other.Strict;
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is Test262File other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (FileName.GetHashCode() * 397) ^ Strict.GetHashCode();
        }
    }
}
