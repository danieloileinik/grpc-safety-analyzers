using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class LiteralConfiguration(
    IObjectCreationOperation creation,
    ImmutableDictionary<string, IOperation> properties,
    ImmutableDictionary<string, ImmutableArray<IOperation>> collections)
{
    public IObjectCreationOperation Creation { get; } = creation;

    public ImmutableDictionary<string, IOperation> Properties { get; } = properties;

    public ImmutableDictionary<string, ImmutableArray<IOperation>> Collections { get; } = collections;
}
