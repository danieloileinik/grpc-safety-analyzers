using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;

namespace GrpcSafety.Analyzers.Protobuf;

internal sealed class ProtoParser
{
    private readonly CancellationToken _cancellationToken;
    private readonly ImmutableArray<string> _tokens;
    private int _position;

    private ProtoParser(ImmutableArray<string> tokens, CancellationToken cancellationToken)
    {
        _tokens = tokens;
        _cancellationToken = cancellationToken;
    }

    private string Current => _position < _tokens.Length ? _tokens[_position] : string.Empty;

    public static ProtoFile Parse(string source, CancellationToken cancellationToken = default)
    {
        var tokens = ProtoLexer.Tokenize(source, cancellationToken);
        return tokens.IsDefault ? ProtoFile.Unknown : new ProtoParser(tokens, cancellationToken).ReadFile();
    }

    private bool Take(string value)
    {
        if (Current != value) return false;
        _position++;
        return true;
    }

    private string? Identifier()
    {
        if (!ProtoLexer.IsIdentifier(Current)) return null;
        return _tokens[_position++];
    }

    private string? QualifiedName()
    {
        var absolute = Take(".");
        var name = Identifier();
        if (name is null) return null;
        while (Take("."))
        {
            var segment = Identifier();
            if (segment is null) return null;
            name += "." + segment;
        }

        return absolute ? "." + name : name;
    }

    private ProtoFile ReadFile()
    {
        var package = string.Empty;
        var packageSeen = false;
        var syntaxSeen = false;
        var services = ImmutableArray.CreateBuilder<ProtoService>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        while (_position < _tokens.Length)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (Take(";")) continue;
            if (Take("syntax"))
            {
                if (syntaxSeen || !Take("=") ||
                    Current is not ("\"proto2\"" or "\"proto3\"" or "'proto2'" or "'proto3'"))
                    return ProtoFile.Unknown;
                syntaxSeen = true;
                _position++;
                if (!Take(";")) return ProtoFile.Unknown;
            }
            else if (Take("package"))
            {
                var value = QualifiedName();
                if (packageSeen || value is null || value.StartsWith(".", StringComparison.Ordinal) || !Take(";"))
                    return ProtoFile.Unknown;
                package = value;
                packageSeen = true;
            }
            else if (Take("service"))
            {
                var service = ReadService();
                if (service is null || !names.Add(service.Name)) return ProtoFile.Unknown;
                services.Add(service);
            }
            else if (Take("message") || Take("enum") || Take("extend"))
            {
                if (QualifiedName() is null || !Take("{") || !SkipBlock()) return ProtoFile.Unknown;
            }
            else if (Take("option") || Take("import"))
            {
                if (!SkipStatement()) return ProtoFile.Unknown;
            }
            else
            {
                return ProtoFile.Unknown;
            }
        }

        return new ProtoFile(true, package, services.ToImmutable());
    }

    private ProtoService? ReadService()
    {
        var name = Identifier();
        if (name is null || !Take("{")) return null;
        var known = true;
        var methods = ImmutableDictionary.CreateBuilder<string, ProtectionState?>(StringComparer.Ordinal);
        while (!Take("}"))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (Take(";")) continue;
            if (Take("option"))
            {
                known = false;
                if (!SkipStatement()) return null;
                continue;
            }

            if (!Take("rpc")) return null;
            var method = Identifier();
            if (method is null || !Take("(") || !MessageType() || !Take(")")
                || !Take("returns") || !Take("(") || !MessageType() || !Take(")")
                || methods.ContainsKey(method))
                return null;

            ProtectionState? state = null;
            if (!Take(";"))
            {
                if (!Take("{")) return null;
                var seen = false;
                while (!Take("}"))
                {
                    if (Take(";")) continue;
                    var start = _position;
                    if (!Take("option") || !SkipStatement()) return null;
                    if (_tokens[start + 1] != "idempotency_level") continue;
                    var recognized = _position - start == 5
                                     && _tokens[start + 2] == "=";
                    if (!recognized || seen)
                    {
                        state = ProtectionState.Unknown;
                        continue;
                    }

                    seen = true;
                    var option = _tokens[start + 3] switch
                    {
                        "IDEMPOTENT" or "NO_SIDE_EFFECTS" => ProtectionState.Protected,
                        "IDEMPOTENCY_UNKNOWN" => ProtectionState.Unprotected,
                        _ => ProtectionState.Unknown
                    };
                    state = state is ProtectionState.Unknown ? state : option;
                }
            }

            methods.Add(method, state);
        }

        return new ProtoService(name, known, methods.ToImmutable());
    }

    private bool MessageType()
    {
        Take("stream");
        return QualifiedName() is not null;
    }

    private bool SkipBlock()
    {
        var depth = 1;
        while (_position < _tokens.Length)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var token = _tokens[_position++];
            if (token == "{" && ++depth > 256) return false;
            if (token == "}" && --depth == 0) return true;
        }

        return false;
    }

    private bool SkipStatement()
    {
        var delimiters = new Stack<string>();
        while (_position < _tokens.Length)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var token = _tokens[_position++];
            if (token == ";" && delimiters.Count == 0) return true;
            if (token is "{" or "(" or "[")
            {
                if (delimiters.Count >= 256) return false;
                delimiters.Push(token);
            }
            else if (token is "}" or ")" or "]")
            {
                if (delimiters.Count == 0) return false;
                var expected = token switch { "}" => "{", ")" => "(", _ => "[" };
                if (delimiters.Pop() != expected) return false;
            }
        }

        return false;
    }
}
