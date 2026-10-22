using System.Collections.Immutable;

namespace GrpcSafety.Analyzers.Protobuf;

internal sealed class ProtoFile(bool isComplete, string package, ImmutableArray<ProtoService> services)
{
    public bool IsComplete { get; } = isComplete;

    public string Package { get; } = package;

    public ImmutableArray<ProtoService> Services { get; } = services;

    public static ProtoFile Unknown => new(false, string.Empty, []);
}
