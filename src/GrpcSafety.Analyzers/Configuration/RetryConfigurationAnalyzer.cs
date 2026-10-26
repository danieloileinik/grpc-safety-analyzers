using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Protobuf;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class RetryConfigurationAnalyzer(GrpcSymbols symbols, ImmutableArray<AdditionalText> additionalFiles)
{
    private readonly INamedTypeSymbol? _clientExtensions = symbols.Compilation.GetTypeByMetadataName(
        "Microsoft.Extensions.DependencyInjection.GrpcClientServiceExtensions");

    private readonly ProtoContractResolver _contracts = new ProtoContractResolver();
    private readonly ClientRegistrationIndex _registrations = new ClientRegistrationIndex(symbols);
    private readonly ConcurrentDictionary<Location, byte> _reported = new ConcurrentDictionary<Location, byte>();

    public void Analyze(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        ChannelRetryConfiguration? configuration = null;
        if (invocation.TargetMethod.Name is "ForAddress" && GrpcSymbols.Is(
                invocation.TargetMethod.ContainingType,
                symbols.Channel))
        {
            var options = invocation.Arguments.FirstOrDefault(argument => GrpcSymbols.Is(
                argument.Parameter?.Type,
                symbols.ChannelOptions));
            if (options is null) return;
            var reader = new ChannelConfigurationReader(symbols);
            if (reader.ReadOptions(options.Value, context.CancellationToken)) configuration = reader.Configuration;
        }
        else if (invocation.TargetMethod.Name is "AddGrpcClient"
                 && GrpcSymbols.Is(invocation.TargetMethod.ContainingType, _clientExtensions)
                 && invocation.TargetMethod.TypeArguments.Length == 1
                 && invocation.TargetMethod.TypeArguments[0] is INamedTypeSymbol client)
        {
            configuration = _registrations.Resolve(client, context.CancellationToken).Retry;
        }

        if (configuration is null || configuration.MaxRetryAttempts is <= 1) return;
        foreach (var method in configuration.Methods)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (method.Attempts is not > 1 ||
                !HasPotentiallyUnsafeMethod(method, configuration, context.CancellationToken)) continue;
            if (_reported.TryAdd(method.Location, 0))
                context.ReportDiagnostic(
                    Diagnostic.Create(DiagnosticDescriptors.UndeclaredIdempotency, method.Location));
        }
    }

    private bool HasPotentiallyUnsafeMethod(MethodRetryConfiguration method,
        ChannelRetryConfiguration channel,
        CancellationToken cancellationToken)
    {
        foreach (var name in method.Names)
        {
            if (name.Service is null) continue; // The set of methods on a default channel is unknown.
            if (name.Method is { } rpc)
            {
                var identity = new ProtoMethodIdentity(name.Service, rpc);
                if (_contracts.ResolveIdempotency(identity, additionalFiles, cancellationToken)
                    is not ProtectionState.Protected) return true;
                continue;
            }

            var service = _contracts.FindService(name.Service, additionalFiles, cancellationToken);
            if (service is null) return true;
            foreach (var candidate in service.Methods)
            {
                if (RpcIdempotencyClassifier.Classify(candidate.Key, candidate.Value)
                    is ProtectionState.Protected) continue;
                var overridden = channel.Methods.Any(other =>
                    other.Names.Any(pattern => pattern.Service == name.Service && pattern.Method == candidate.Key));
                if (!overridden) return true;
            }
        }

        return false;
    }
}
