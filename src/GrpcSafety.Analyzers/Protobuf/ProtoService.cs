using System.Collections.Immutable;
using GrpcSafety.Analyzers.Analysis;

namespace GrpcSafety.Analyzers.Protobuf;

internal sealed class ProtoService(string name, bool isKnown, ImmutableDictionary<string, ProtectionState> methods)
{
    public string Name { get; } = name;

    public bool IsKnown { get; } = isKnown;

    public ImmutableDictionary<string, ProtectionState> Methods { get; } = methods;
}
