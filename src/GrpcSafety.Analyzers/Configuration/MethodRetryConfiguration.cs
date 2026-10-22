using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class MethodRetryConfiguration(
    Location location,
    ImmutableArray<MethodNamePattern> names,
    int? attempts)
{
    public Location Location { get; } = location;

    public ImmutableArray<MethodNamePattern> Names { get; } = names;

    public int? Attempts { get; } = attempts;
}
