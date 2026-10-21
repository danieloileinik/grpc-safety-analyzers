using System.Collections.Immutable;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Protobuf;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Configuration;

internal interface IRetryPolicyResolver
{
    // Resolve literal policies, MethodName matching, and the channel's MaxRetryAttempts cap.
    RetrySafety Resolve(
        GrpcCall call,
        ClientConfiguration configuration,
        IProtoContractResolver? protoContracts,
        ImmutableArray<AdditionalText> additionalFiles,
        CancellationToken cancellationToken);
}
