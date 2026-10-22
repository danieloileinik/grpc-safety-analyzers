using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace GrpcSafety.Analyzers.Protobuf;

internal static class ProtoLexer
{
    public static ImmutableArray<string> Tokenize(string source, CancellationToken cancellationToken)
    {
        var tokens = ImmutableArray.CreateBuilder<string>();
        var position = 0;
        while (position < source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var character = source[position];
            if (char.IsWhiteSpace(character) || (position == 0 && character == '\uFEFF'))
            {
                position++;
                continue;
            }

            if (character == '/' && position + 1 < source.Length)
            {
                if (source[position + 1] == '/')
                {
                    while (position < source.Length && source[position] != '\n') position++;
                    continue;
                }

                if (source[position + 1] == '*')
                {
                    position += 2;
                    while (position + 1 < source.Length && !(source[position] == '*' && source[position + 1] == '/'))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        position++;
                    }

                    if (position + 1 >= source.Length) return default;
                    position += 2;
                    continue;
                }
            }

            var start = position++;
            if (character is '\'' or '"')
            {
                var closed = false;
                while (position < source.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var current = source[position++];
                    if (current is '\r' or '\n') return default;
                    if (current == '\\')
                    {
                        if (position >= source.Length || source[position] is '\r' or '\n') return default;
                        position++;
                    }
                    else if (current == character)
                    {
                        closed = true;
                        break;
                    }
                }

                if (!closed) return default;
            }
            else if (IsLetter(character) || character is >= '0' and <= '9')
            {
                while (position < source.Length &&
                       (IsLetter(source[position]) || source[position] is >= '0' and <= '9'))
                    position++;
            }
            else if ("{}()[];=.,:+-<>/".IndexOf(character) < 0)
            {
                return default;
            }

            tokens.Add(source.Substring(start, position - start));
        }

        return tokens.ToImmutable();
    }

    public static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !IsLetter(value[0])) return false;
        return value.All(character => IsLetter(character) || character is >= '0' and <= '9');
    }

    private static bool IsLetter(char value)
    {
        return value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '_';
    }
}
