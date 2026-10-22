using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class ClientConfigurationResolver(GrpcSymbols symbols) : IClientConfigurationResolver
{
    private readonly InjectedClientResolver _injectedClients = new(symbols);
    private readonly LocalValueResolver _locals = new(symbols.Compilation);
    private readonly ClientRegistrationIndex _registrations = new(symbols);

    public ClientConfiguration Resolve(GrpcCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instance = ResolveReceiver(call.Invocation.Instance, cancellationToken);

        if (instance is ILocalReferenceOperation or IFieldReferenceOperation or IParameterReferenceOperation)
        {
            var client = _injectedClients.FindClient(instance, cancellationToken);
            if (client is not null
                && SymbolEqualityComparer.Default.Equals(client, call.Invocation.TargetMethod.ContainingType))
                return _registrations.Resolve(client, cancellationToken);

            return ClientConfiguration.Unresolved;
        }

        if (call.IsCallInvoker) return ResolveInvoker(instance, cancellationToken);

        if (instance is not IObjectCreationOperation { Initializer: null, Constructor: { } constructor } creation
            || !SymbolEqualityComparer.Default.Equals(creation.Type, call.Invocation.TargetMethod.ContainingType)
            || !symbols.HasGeneratorAttribute(constructor))
            return ClientConfiguration.Opaque;

        var channel = creation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "channel");
        if (channel is not null)
            return ResolveChannel(ResolveReceiver(channel.Value, cancellationToken), cancellationToken);

        var invoker = creation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "callInvoker");
        return invoker is null
            ? ClientConfiguration.Opaque
            : ResolveInvoker(ResolveReceiver(invoker.Value, cancellationToken), cancellationToken);
    }

    private IOperation? ResolveReceiver(IOperation? operation, CancellationToken cancellationToken)
    {
        operation = UnwrapReceiver(operation);
        return operation is null ? null : _locals.Resolve(operation, cancellationToken);
    }

    private static IOperation? UnwrapReceiver(IOperation? operation)
    {
        operation = DeadlineResolver.Unwrap(operation);
        while (operation is IConditionalAccessInstanceOperation)
        {
            var parent = operation.Parent;
            while (parent is not null and not IConditionalAccessOperation) parent = parent.Parent;

            operation = parent is IConditionalAccessOperation conditionalAccess
                ? DeadlineResolver.Unwrap(conditionalAccess.Operation)
                : null;
        }

        return operation;
    }

    private ClientConfiguration ResolveInvoker(IOperation? operation, CancellationToken cancellationToken)
    {
        return operation is IInvocationOperation { TargetMethod.Name: "CreateCallInvoker" } invocation
               && GrpcSymbols.Is(invocation.TargetMethod.ContainingType, symbols.Channel)
            ? ResolveChannel(ResolveReceiver(invocation.Instance, cancellationToken), cancellationToken)
            : ClientConfiguration.Opaque;
    }

    private ClientConfiguration ResolveChannel(IOperation? operation, CancellationToken cancellationToken)
    {
        if (operation is not IInvocationOperation { TargetMethod: { Name: "ForAddress", IsStatic: true } } invocation
            || !GrpcSymbols.Is(invocation.TargetMethod.ContainingType, symbols.Channel))
            return ClientConfiguration.Opaque;

        var address = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name is "address");
        if (address?.Value.ConstantValue is not { HasValue: true, Value: string }) return ClientConfiguration.Opaque;

        var options = invocation.Arguments.FirstOrDefault(argument => GrpcSymbols.Is(
            argument.Parameter?.Type,
            symbols.ChannelOptions));
        var value = DeadlineResolver.Unwrap(options?.Value);

        var reader = new ChannelConfigurationReader(symbols);
        return value is null || reader.ReadOptions(value, cancellationToken)
            ? new ClientConfiguration(true, ProtectionState.Unprotected, reader.Configuration)
            : ClientConfiguration.Opaque;
    }
}
