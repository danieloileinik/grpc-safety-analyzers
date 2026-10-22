using System.Collections.Immutable;
using System.Threading;
using GrpcSafety.Analyzers.Configuration;
using GrpcSafety.Analyzers.Protobuf;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Analysis;

internal sealed class CallAnalysisPipeline(
    GrpcSymbols symbols,
    ImmutableArray<AdditionalText> additionalFiles,
    IClientConfigurationResolver? clientConfigurations = null,
    IDeadlinePropagationResolver? deadlinePropagation = null,
    IRetryPolicyResolver? retryPolicies = null,
    IProtoContractResolver? protoContracts = null)
{
    private readonly IClientConfigurationResolver _clientConfigurations =
        clientConfigurations ?? new ClientConfigurationResolver(symbols);

    private readonly DeadlineResolver _deadlines = new DeadlineResolver(symbols);

    public CallSafetyClassification Analyze(GrpcCall call,
        GrpcSafetyOptions options,
        CancellationToken cancellationToken)
    {
        ClientConfiguration configuration = _clientConfigurations.Resolve(call, cancellationToken);

        // Missing extension implementations preserve Unknown until the next implementation stages.
        ProtectionState deadlinePropagationState = deadlinePropagation?.Resolve(call, configuration, cancellationToken) ??
                                           ProtectionState.Unknown;
        RetrySafety retry = retryPolicies?.Resolve(call, configuration, protoContracts, additionalFiles, cancellationToken) ??
                            RetrySafety.Unknown;

        return CallSafetyClassifier.Classify(_deadlines.Resolve(call), configuration, deadlinePropagationState, retry, options);
    }
}
