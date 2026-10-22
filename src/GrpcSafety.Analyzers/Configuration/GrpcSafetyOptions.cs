using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GrpcSafety.Analyzers.Configuration;

internal readonly struct GrpcSafetyOptions(bool strict)
{
    private const string StrictKey = "grpc_safety.strict";

    public bool Strict { get; } = strict;

    public static GrpcSafetyOptions Read(AnalyzerConfigOptionsProvider provider, SyntaxTree tree)
    {
        var found = provider.GetOptions(tree).TryGetValue(StrictKey, out var value)
                    || provider.GlobalOptions.TryGetValue(StrictKey, out value);

        return new GrpcSafetyOptions(found && bool.TryParse(value, out var strict) && strict);
    }
}
