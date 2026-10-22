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

    private readonly IDeadlinePropagationResolver _deadlinePropagation =
        deadlinePropagation ?? new DeadlinePropagationResolver(symbols);

    private readonly DeadlineResolver _deadlines = new(symbols);
    private readonly IRetryPolicyResolver _retryPolicies = retryPolicies ?? new RetryPolicyResolver(symbols);

    public CallSafetyClassification Analyze(GrpcCall call,
        GrpcSafetyOptions options,
        CancellationToken cancellationToken)
    {
        var configuration = _clientConfigurations.Resolve(call, cancellationToken);

        var explicitDeadline = _deadlines.Resolve(call, cancellationToken);
        var deadline = CallSafetyClassifier.ClassifyDeadline(explicitDeadline, configuration, options);
        var deadlinePropagationState = _deadlinePropagation.Resolve(call, deadline, cancellationToken);
        var retry = _retryPolicies.Resolve(call, configuration, protoContracts, additionalFiles, cancellationToken);

        return CallSafetyClassifier.Classify(explicitDeadline, configuration, deadlinePropagationState, retry, options);
    }
}
