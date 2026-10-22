using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Protobuf;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class RetryPolicyResolver(GrpcSymbols symbols) : IRetryPolicyResolver
{
    private readonly GrpcMethodIdentityResolver _methods = new(symbols);

    public RetrySafety Resolve(GrpcCall call,
        ClientConfiguration configuration,
        IProtoContractResolver? protoContracts,
        ImmutableArray<AdditionalText> additionalFiles,
        CancellationToken cancellationToken)
    {
        var channel = configuration.Retry;
        if (channel is null) return RetrySafety.Unknown;
        if (channel.MaxRetryAttempts is <= 1) return new RetrySafety(ProtectionState.Protected, false);

        var identity = channel.Methods.Any(method => method.Names.Any(name => name.Service is not null))
            ? _methods.Resolve(call, cancellationToken)
            : null;
        MethodRetryConfiguration? selected = null;
        var specificity = -1;
        foreach (var method in channel.Methods)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var name in method.Names)
            {
                if (identity is null && name.Service is not null) return RetrySafety.Unknown;
                var match = Match(name, identity);
                if (match <= specificity) continue;
                specificity = match;
                selected = method;
            }
        }

        if (selected?.Attempts is not > 1) return RetrySafety.Unknown;
        var idempotency = identity is { } resolved && protoContracts is not null
            ? protoContracts.ResolveIdempotency(resolved, additionalFiles, cancellationToken)
            : ProtectionState.Unknown;
        return new RetrySafety(idempotency, true);
    }

    private static int Match(MethodNamePattern name, ProtoMethodIdentity? identity)
    {
        if (name.Service is null) return 0;
        if (identity is not { } method || name.Service != method.Service) return -1;
        return name.Method is null ? 1 : name.Method == method.Method ? 2 : -1;
    }
}
