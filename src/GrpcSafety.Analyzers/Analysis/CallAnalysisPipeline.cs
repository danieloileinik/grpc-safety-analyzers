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
    IPropagationResolver? propagation = null,
    IRetryPolicyResolver? retryPolicies = null,
    IProtoContractResolver? protoContracts = null)
{
    private readonly IClientConfigurationResolver clientConfigurations = clientConfigurations ?? new ClientConfigurationResolver(symbols);
    private readonly DeadlineResolver deadlines = new(symbols);

    public CallSafetyClassification Analyze(GrpcCall call, GrpcSafetyOptions options, CancellationToken cancellationToken)
    {
        var configuration = clientConfigurations.Resolve(call, cancellationToken);

        // Missing extension implementations preserve Unknown until the next implementation stages.
        var propagationState = propagation?.Resolve(call, configuration, cancellationToken) ?? ProtectionState.Unknown;
        var retry = retryPolicies?.Resolve(call, configuration, protoContracts, additionalFiles, cancellationToken) ?? RetrySafety.Unknown;

        return CallSafetyClassifier.Classify(deadlines.Resolve(call), configuration, propagationState, retry, options);
    }
}
