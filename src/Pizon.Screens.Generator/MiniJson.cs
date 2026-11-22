using System.Globalization;
using System.Text;

namespace Pizon.Screens.Generator;

/// <summary>
/// A small JSON reader, enough for Durdraw files. It avoids loading System.Text.Json into the compiler.
/// Objects become <see cref="Dictionary{TKey, TValue}"/>, arrays <see cref="List{T}"/>, numbers
/// <see cref="double"/>, and <c>true</c>, <c>false</c> and <c>null</c> become <see cref="bool"/> and null.
/// </summary>
internal sealed class MiniJson
{
    private readonly string text;
    private int pos;

    private MiniJson(string text) => this.text = text;

    /// <summary>
    /// Parses a JSON document.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="FormatException">The text is not valid JSON.</exception>
    public static object? Parse(string json)
    {
        var reader = new MiniJson(json);
        var value = reader.ReadValue();
        reader.SkipSpace();
        if (reader.pos != json.Length)
        {
            throw reader.Error("unexpected text after the end");
        }

        return value;
    }

    private object? ReadValue()
    {
        SkipSpace();
        if (pos >= text.Length)
        {
            throw Error("unexpected end");
        }

        switch (text[pos])
        {
            case '{':
                return ReadObject();
            case '[':
                return ReadArray();
            case '"':
                return ReadString();
            case 't':
                return ReadWord("true", true);
            case 'f':
                return ReadWord("false", false);
            case 'n':
                return ReadWord("null", null);
            default:
                return ReadNumber();
        }
    }

    private Dictionary<string, object?> ReadObject()
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        pos++;
        SkipSpace();
        if (Peek() == '}')
        {
            pos++;
            return result;
        }

        while (true)
        {
            SkipSpace();
            if (Peek() != '"')
            {
                throw Error("expected a property name");
            }

            var key = ReadString();
            SkipSpace();
            Expect(':');
            result[key] = ReadValue();
            SkipSpace();
            if (Peek() == ',')
            {
                pos++;
                continue;
            }

            Expect('}');
            return result;
        }
    }

    private List<object?> ReadArray()
    {
        var result = new List<object?>();
        pos++;
        SkipSpace();
        if (Peek() == ']')
        {
            pos++;
            return result;
        }

        while (true)
        {
            result.Add(ReadValue());
            SkipSpace();
            if (Peek() == ',')
            {
                pos++;
                continue;
            }

            Expect(']');
            return result;
        }
    }

    private string ReadString()
    {
        pos++;
        var sb = new StringBuilder();
        while (true)
        {
            if (pos >= text.Length)
            {
                throw Error("unterminated string");
            }

            var c = text[pos++];
            if (c == '"')
            {
                return sb.ToString();
            }

            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (pos >= text.Length)
            {
                throw Error("unterminated string");
            }

            var e = text[pos++];
            switch (e)
            {
                case 'n':
                    sb.Append('\n');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'u':
                    if (pos + 4 > text.Length || !int.TryParse(text.Substring(pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                    {
                        throw Error("bad \\u escape");
                    }

                    sb.Append((char)code);
                    pos += 4;
                    break;
                default:
                    sb.Append(e);
                    break;
            }
        }
    }

    private object? ReadWord(string word, object? value)
    {
        if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0)
        {
            throw Error("unexpected character");
        }

        pos += word.Length;
        return value;
    }

    private double ReadNumber()
    {
        var start = pos;
        while (pos < text.Length && "+-0123456789.eE".IndexOf(text[pos]) >= 0)
        {
            pos++;
        }

        if (pos == start || !double.TryParse(text.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            throw Error("unexpected character");
        }

        return number;
    }

    private void SkipSpace()
    {
        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
        {
            pos++;
        }
    }

    private char Peek() => pos < text.Length ? text[pos] : '\0';

    private void Expect(char c)
    {
        if (Peek() != c)
        {
            throw Error("expected '" + c + "'");
        }

        pos++;
    }

    private FormatException Error(string message) =>
        new(string.Format(CultureInfo.InvariantCulture, "Bad JSON at character {0}: {1}.", pos, message));
}
