using System.Collections.Immutable;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Protobuf;

internal interface IProtoContractResolver
{
    // Missing, ambiguous, or unsupported proto syntax must return Unknown.
    ProtectionState ResolveIdempotency(
        ProtoMethodIdentity method,
        ImmutableArray<AdditionalText> additionalFiles,
        CancellationToken cancellationToken);
}
