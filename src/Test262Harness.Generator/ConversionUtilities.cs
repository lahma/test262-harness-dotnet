using System.Runtime.CompilerServices;
using Cysharp.Text;

namespace Test262Harness.TestSuite.Generator
{
    public static class ConversionUtilities
    {
        public static string ConvertToUpperCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            input = ConvertDashesToCamelCase(Capitalize(input)
                .Replace(" ", "_")
                .Replace("/", "_")
                .Replace(".", "_"));

            return input;
        }

        [MethodImpl((MethodImplOptions) 256)]
        private static string Capitalize(string input)
        {
            if (char.IsUpper(input[0]))
            {
                return input;
            }

            if (input.Length == 1)
            {
                return char.ToUpperInvariant(input[0]).ToString();
            }

            return char.ToUpperInvariant(input[0]) + input.Substring(1);
        }

        private static string ConvertDashesToCamelCase(string input)
        {
            if (!input.Contains('-'))
            {
                // no conversion necessary
                return input;
            }

            // we are removing at least one character
            using var sb = ZString.CreateStringBuilder();
            var caseFlag = false;
            foreach (var c in input)
            {
                if (c == '-')
                {
                    caseFlag = true;
                }
                else if (caseFlag)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    caseFlag = false;
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
