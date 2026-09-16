using System.Buffers;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

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

    private static readonly string _useStrictWithNewLine = $"\"use strict\";{Environment.NewLine}";

    private string[] _features = [];
    private string[] _flags = [];
    private string[] _includes = [];
    private string[] _locale = [];

    private Test262File(string fileName)
    {
        FileName = fileName;
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
    /// The actual code to be interpreted.
    /// </summary>
    public string Program { get; private set; } = "";

    public bool Strict { get; private set; }

    public bool Negative => Array.IndexOf(_flags, "negative") != -1 || NegativeTestCase is not null;

    /// <summary>
    /// Type of code, script or module.
    /// </summary>
    public ProgramType Type { get; private set; } = ProgramType.Script;

    public static IEnumerable<Test262File> FromFile(string filePath, bool generateInverseStrictTestCase = true)
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

        return FromStream(File.OpenRead(filePath), filePath.Substring(testPathIndex + 1), generateInverseStrictTestCase);
    }

    private static string NormalizedFilePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// Reads the whole stream as UTF-8 text.
    /// </summary>
    /// <remarks>
    /// Test262 files are small and their length is known up front, so the bytes go into one pooled
    /// buffer and are decoded once. Going through a <see cref="StreamReader"/> and a
    /// <see cref="StringBuilder"/> instead costs roughly twice the allocation per file - which is
    /// worth caring about for a suite that reads a hundred thousand of them.
    /// </remarks>
    private static string ReadToEnd(Stream stream)
    {
        if (!stream.CanSeek)
        {
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        var remaining = stream.Length - stream.Position;
        if (remaining <= 0)
        {
            return "";
        }

        if (remaining > int.MaxValue)
        {
            throw new ArgumentException($"Test case is too large to read: {remaining} bytes.", nameof(stream));
        }

        var length = (int) remaining;
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var offset = 0;
            while (offset < length)
            {
                var read = stream.Read(buffer, offset, length - offset);
                if (read <= 0)
                {
                    break;
                }
                offset += read;
            }

            // Skip the UTF-8 byte order mark, which StreamReader would have consumed for us.
            var start = offset >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(buffer, start, offset - start);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static IEnumerable<Test262File> FromStream(Stream stream, string fileName, bool generateInverseStrictTestCase = true)
    {
        fileName = NormalizedFilePath(fileName);

        var contents = ReadToEnd(stream);

        var yamlStartIndex = contents.IndexOf(YamlSectionStartMarker, StringComparison.Ordinal);

        if (yamlStartIndex < 0)
        {
            throw new ArgumentException($"Test case {fileName} is invalid, cannot find YAML section start.");
        }

        var yamlEndIndex = contents.IndexOf(YamlSectionEndMarker, yamlStartIndex, StringComparison.Ordinal);

        if (yamlEndIndex < 0)
        {
            throw new ArgumentException($"Test case {fileName} is invalid, cannot find YAML section end.");
        }

        var yaml = contents.AsMemory(yamlStartIndex + YamlSectionStartMarker.Length, yamlEndIndex - YamlSectionEndMarker.Length - yamlStartIndex);
        if (yaml.IsEmpty)
        {
            throw new ArgumentException($"Test case {fileName} is invalid, cannot find YAML section.");
        }

        var onlyStrict = false;
        var noStrict = false;
        var test = new Test262File(fileName);
        try
        {
            var parser = new Parser(new MemoryReader(yaml));
            ParseFrontmatter(parser, test, ref onlyStrict, ref noStrict);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Could not lod YAML content from file {fileName}: {ex.Message}", ex);
        }

        test.Program = contents;

        if (!generateInverseStrictTestCase)
        {
            yield return test;
            yield break;
        }

        // we produce two results, non-strict and strict based on configuration
        // this follows the tests262 stream logic
        if (test.Type == ProgramType.Script && !onlyStrict)
        {
            yield return test;
        }

        if (!noStrict)
        {
            yield return test.AsStrict();
        }
    }

    /// <summary>
    /// Creates strict version of the test case by adding `use strict`; directive to the beginning of the program.
    /// </summary>
    public Test262File AsStrict()
    {
        if (Strict)
        {
            return this;
        }

        var clone = (Test262File) this.MemberwiseClone();
        clone.Strict = true;
        clone.Program = _useStrictWithNewLine + Program;
        return clone;
    }

    /// <summary>
    /// Reads the frontmatter mapping straight off the parser's event stream.
    /// </summary>
    /// <remarks>
    /// The obvious way to do this is YamlDotNet's representation model - load a <c>YamlStream</c> and walk
    /// the nodes. That builds a node object per scalar, sequence and mapping in every file, which measured
    /// at roughly 16 KB per test case, about three quarters of everything this method allocates. The parser
    /// underneath is the same one the representation model uses, so the YAML dialect it accepts is unchanged;
    /// only the tree is gone.
    /// </remarks>
    private static void ParseFrontmatter(IParser parser, Test262File test, ref bool onlyStrict, ref bool noStrict)
    {
        parser.Consume<StreamStart>();
        parser.Consume<DocumentStart>();
        parser.Consume<MappingStart>();

        while (!parser.Accept<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;
            switch (key)
            {
                case "esid" or "es5id" or "es6id":
                    test.EcmaScriptId = parser.Consume<Scalar>().Value;
                    break;
                case "description":
                    test.Description = parser.Consume<Scalar>().Value;
                    break;
                case "info":
                    test.Info = parser.Consume<Scalar>().Value;
                    break;
                case "author":
                    test.Author = parser.Consume<Scalar>().Value;
                    break;
                case "features":
                    test._features = ReadStringArray(parser);
                    break;
                case "includes":
                    test._includes = ReadStringArray(parser);
                    break;
                case "locale":
                    test._locale = ReadStringArray(parser);
                    break;
                case "negative":
                    test.NegativeTestCase = ReadNegative(parser);
                    break;
                case "flags":
                    var flags = ReadStringArray(parser);
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
                            case "raw":
                                noStrict = true;
                                break;
                        }
                    }

                    test._flags = flags;
                    break;
                default:
                    SkipValue(parser);
                    break;
            }
        }
    }

    private static NegativeTestCase ReadNegative(IParser parser)
    {
        var phase = default(TestingPhase);
        var expectedErrorType = default(ExpectedErrorType);

        parser.Consume<MappingStart>();
        while (!parser.Accept<MappingEnd>(out _))
        {
            var key = parser.Consume<Scalar>().Value;
            switch (key)
            {
                case "phase":
                    Enum.TryParse(parser.Consume<Scalar>().Value, ignoreCase: true, out phase);
                    break;
                case "type":
                    Enum.TryParse(parser.Consume<Scalar>().Value, ignoreCase: true, out expectedErrorType);
                    break;
                default:
                    SkipValue(parser);
                    break;
            }
        }
        parser.Consume<MappingEnd>();

        return new NegativeTestCase(phase, expectedErrorType);
    }

    private static string[] ReadStringArray(IParser parser)
    {
        // A single scalar where a sequence is expected is what the representation model accepted too.
        if (parser.Accept<Scalar>(out _))
        {
            return [parser.Consume<Scalar>().Value];
        }

        parser.Consume<SequenceStart>();
        if (parser.Accept<SequenceEnd>(out _))
        {
            parser.Consume<SequenceEnd>();
            return [];
        }

        var result = new List<string>();
        while (!parser.Accept<SequenceEnd>(out _))
        {
            result.Add(parser.Consume<Scalar>().Value);
        }
        parser.Consume<SequenceEnd>();

        return result.ToArray();
    }

    /// <summary>
    /// Consumes one value of any shape, so an unrecognised key does not desynchronise the reader.
    /// </summary>
    private static void SkipValue(IParser parser)
    {
        var depth = 0;
        do
        {
            var current = parser.Consume<ParsingEvent>();
            depth += current.NestingIncrease;
        }
        while (depth > 0);
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
