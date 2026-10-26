using System.Collections.Immutable;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Protobuf;

internal interface IProtoContractResolver
{
    // Explicit proto declarations override method naming heuristics.
    ProtectionState ResolveIdempotency(
        ProtoMethodIdentity method,
        ImmutableArray<AdditionalText> additionalFiles,
        CancellationToken cancellationToken);
}
