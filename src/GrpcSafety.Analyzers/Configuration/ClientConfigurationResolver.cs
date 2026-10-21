using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class ClientConfigurationResolver(GrpcSymbols symbols) : IClientConfigurationResolver
{
    public ClientConfiguration Resolve(GrpcCall call, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var instance = DeadlineResolver.Unwrap(call.Invocation.Instance);

        // Local/field/DI provenance needs flow analysis and remains unresolved in the scaffold.
        if (instance is ILocalReferenceOperation or IFieldReferenceOperation or IParameterReferenceOperation)
        {
            return ClientConfiguration.Unresolved;
        }

        if (call.IsCallInvoker)
        {
            return ResolveInvoker(instance);
        }

        if (instance is not IObjectCreationOperation { Initializer: null, Constructor: { } constructor } creation
            || !SymbolEqualityComparer.Default.Equals(creation.Type, call.Invocation.TargetMethod.ContainingType)
            || !symbols.HasGeneratorAttribute(constructor))
        {
            return ClientConfiguration.Opaque;
        }

        var channel = creation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name is "channel");
        if (channel is not null)
        {
            return ResolveChannel(DeadlineResolver.Unwrap(channel.Value));
        }

        var invoker = creation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name is "callInvoker");
        return invoker is null
            ? ClientConfiguration.Opaque
            : ResolveInvoker(DeadlineResolver.Unwrap(invoker.Value));
    }

    private ClientConfiguration ResolveInvoker(IOperation? operation) =>
        operation is IInvocationOperation { TargetMethod.Name: "CreateCallInvoker" } invocation
            && GrpcSymbols.Is(invocation.TargetMethod.ContainingType, symbols.Channel)
                ? ResolveChannel(DeadlineResolver.Unwrap(invocation.Instance))
                : ClientConfiguration.Opaque;

    private ClientConfiguration ResolveChannel(IOperation? operation)
    {
        if (operation is not IInvocationOperation { TargetMethod: { Name: "ForAddress", IsStatic: true } } invocation
            || !GrpcSymbols.Is(invocation.TargetMethod.ContainingType, symbols.Channel))
        {
            return ClientConfiguration.Opaque;
        }

        var address = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name is "address");
        if (address?.Value.ConstantValue is not { HasValue: true, Value: string })
        {
            return ClientConfiguration.Opaque;
        }

        var options = invocation.Arguments.FirstOrDefault(argument => GrpcSymbols.Is(argument.Parameter?.Type, symbols.ChannelOptions));
        var value = DeadlineResolver.Unwrap(options?.Value);

        return value switch
        {
            null => ClientConfiguration.WithoutCentralDeadline,
            { ConstantValue: { HasValue: true, Value: null } } => ClientConfiguration.WithoutCentralDeadline,
            IObjectCreationOperation { Initializer: null or { Initializers.IsEmpty: true } } creation
                when GrpcSymbols.Is(creation.Type, symbols.ChannelOptions) => ClientConfiguration.WithoutCentralDeadline,
            _ => ClientConfiguration.Opaque,
        };
    }
}
