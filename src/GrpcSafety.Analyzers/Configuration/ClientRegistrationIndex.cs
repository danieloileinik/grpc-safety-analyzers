using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class ClientRegistrationIndex(GrpcSymbols symbols)
{
    private readonly INamedTypeSymbol? _builder = symbols.Compilation.GetTypeByMetadataName(
        "Microsoft.Extensions.DependencyInjection.IHttpClientBuilder");

    private readonly INamedTypeSymbol? _channelExtensions = symbols.Compilation.GetTypeByMetadataName(
        "Microsoft.Extensions.DependencyInjection.GrpcHttpClientBuilderExtensions");

    private readonly INamedTypeSymbol? _clientExtensions = symbols.Compilation.GetTypeByMetadataName(
        "Microsoft.Extensions.DependencyInjection.GrpcClientServiceExtensions");

    private readonly ConcurrentDictionary<INamedTypeSymbol, ClientConfiguration> _clients =
        new(SymbolEqualityComparer.Default);

    private readonly INamedTypeSymbol? _deadlinePropagationExtensions = symbols.Compilation.GetTypeByMetadataName(
        "Microsoft.Extensions.DependencyInjection.GrpcServerHttpClientBuilderExtensions");

    private readonly INamedTypeSymbol? _factoryOptions = symbols.Compilation.GetTypeByMetadataName(
        "Grpc.Net.ClientFactory.GrpcClientFactoryOptions");

    private readonly LocalValueResolver _locals = new(symbols.Compilation);

    public ClientConfiguration Resolve(INamedTypeSymbol client, CancellationToken cancellationToken)
    {
        if (_clientExtensions is null) return ClientConfiguration.Unresolved;
        return _clients.GetOrAdd(client, type => FindRegistration(type, cancellationToken));
    }

    private ClientConfiguration FindRegistration(INamedTypeSymbol client, CancellationToken cancellationToken)
    {
        IInvocationOperation? registration = null;
        foreach (var tree in symbols.Compilation.SyntaxTrees)
        {
            var model = symbols.Compilation.GetSemanticModel(tree);
            foreach (var syntax in tree
                         .GetRoot(cancellationToken)
                         .DescendantNodes()
                         .OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetOperation(syntax, cancellationToken) is not IInvocationOperation invocation) continue;
                var method = invocation.TargetMethod;
                if (method.Name is "AddGrpcClient" && GrpcSymbols.Is(method.ContainingType, _clientExtensions)
                                                   && method.TypeArguments.Length == 1
                                                   && SymbolEqualityComparer.Default.Equals(
                                                       method.TypeArguments[0],
                                                       client))
                {
                    if (registration is not null) return ClientConfiguration.Opaque;
                    registration = invocation;
                }
                else if (IsConflictingRegistration(invocation, client))
                {
                    return ClientConfiguration.Opaque;
                }
            }
        }

        if (registration is null) return ClientConfiguration.Unresolved;
        if (IsConditional(registration.Syntax)
            || registration.Arguments.Any(argument => argument.Parameter?.Name is "name")
            || !HasTransparentActions(registration, "Address", cancellationToken))
            return ClientConfiguration.Opaque;

        var deadlinePropagationEnabled = false;
        var channel = new ChannelConfigurationReader(symbols);
        if (!ReadChain(registration, ref deadlinePropagationEnabled, channel, cancellationToken, out var terminal))
            return ClientConfiguration.Opaque;

        if (terminal.Parent is IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator })
        {
            if (!_locals.IsStable(declarator.Symbol, cancellationToken)) return ClientConfiguration.Opaque;
            var scope = terminal.Syntax.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault()
                        ?? terminal.Syntax.SyntaxTree.GetRoot(cancellationToken);
            var model = symbols.Compilation.GetSemanticModel(scope.SyntaxTree);
            foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (identifier.Identifier.ValueText != declarator.Symbol.Name
                    || !SymbolEqualityComparer.Default.Equals(
                        model.GetSymbolInfo(identifier, cancellationToken).Symbol,
                        declarator.Symbol))
                    continue;

                if (IsConditional(identifier)
                    || model.GetOperation(identifier, cancellationToken) is not ILocalReferenceOperation reference
                    || !ReadChain(reference, ref deadlinePropagationEnabled, channel, cancellationToken, out var end)
                    || end.Parent is not IExpressionStatementOperation)
                    return ClientConfiguration.Opaque;
            }
        }
        else if (terminal.Parent is not IExpressionStatementOperation)
        {
            return ClientConfiguration.Opaque;
        }

        return new ClientConfiguration(
            true,
            deadlinePropagationEnabled ? ProtectionState.Protected : ProtectionState.Unprotected,
            channel.Configuration);
    }

    private bool ReadChain(IOperation start,
        ref bool deadlinePropagationEnabled,
        ChannelConfigurationReader channel,
        CancellationToken cancellationToken,
        out IOperation terminal)
    {
        terminal = start;
        while (FluentParent(terminal) is { } next)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = next.TargetMethod;
            if (method.Name is "EnableCallContextPropagation" && GrpcSymbols.Is(
                                                                  method.ContainingType,
                                                                  _deadlinePropagationExtensions)
                                                              && HasTransparentActions(
                                                                  next,
                                                                  "SuppressContextNotFoundErrors",
                                                                  cancellationToken))
            {
                deadlinePropagationEnabled = true;
            }
            else if (method.Name is "ConfigureChannel" && GrpcSymbols.Is(method.ContainingType, _channelExtensions)
                                                       && channel.ReadCallback(next, cancellationToken))
            {
                // All callbacks contribute to the final channel configuration in registration order.
            }
            else
            {
                return false;
            }

            terminal = next;
        }

        return true;
    }

    private IInvocationOperation? FluentParent(IOperation operation)
    {
        var current = operation;
        while (current.Parent is IConversionOperation) current = current.Parent;
        if (current.Parent is IArgumentOperation argument && GrpcSymbols.Is(argument.Parameter?.Type, _builder)
                                                          && argument.Parent is IInvocationOperation extension)
            return extension;

        return current.Parent is IInvocationOperation invocation && ReferenceEquals(invocation.Instance, current)
            ? invocation
            : null;
    }

    private bool HasTransparentActions(IInvocationOperation invocation,
        string allowedProperty,
        CancellationToken cancellationToken)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type.TypeKind is not TypeKind.Delegate) continue;
            var operation = _locals.Resolve(argument.Value, cancellationToken);
            if (operation is IDelegateCreationOperation delegateCreation) operation = delegateCreation.Target;
            if (operation is not IAnonymousFunctionOperation lambda) return false;

            foreach (var statement in lambda.Body.Operations)
            {
                if (statement is IReturnOperation { ReturnedValue: null }) continue;
                if (statement is not IExpressionStatementOperation
                    {
                        Operation: ISimpleAssignmentOperation
                        {
                            Target: IPropertyReferenceOperation property
                        } assignment
                    }
                    || property.Property.Name != allowedProperty
                    || property.Instance is not IParameterReferenceOperation parameter
                    || !lambda.Symbol.Parameters.Contains(parameter.Parameter, SymbolEqualityComparer.Default)
                    || !IsLiteralValue(assignment.Value))
                    return false;
            }
        }

        return true;
    }

    private bool IsLiteralValue(IOperation operation)
    {
        operation = DeadlineResolver.Unwrap(operation)!;
        return operation.ConstantValue.HasValue
               || (operation is IObjectCreationOperation { Initializer: null } creation
                   && GrpcSymbols.Is(creation.Type, symbols.Uri)
                   && creation.Arguments.All(argument => argument.Value.ConstantValue.HasValue));
    }

    private bool IsConflictingRegistration(IInvocationOperation invocation, INamedTypeSymbol client)
    {
        var method = invocation.TargetMethod;
        return (method.Name is "AddSingleton" or "AddScoped" or "AddTransient" or "TryAddSingleton" or "TryAddScoped"
                    or "TryAddTransient"
                    or "Add" or "TryAdd" or "TryAddEnumerable" or "Replace" or "RemoveAll"
                && (method.TypeArguments.Any(type => SymbolEqualityComparer.Default.Equals(type, client))
                    || invocation.Arguments.Any(argument => MentionsClient(argument.Value, client))))
               || (method.Name is "Configure" or "ConfigureAll" or "PostConfigure" or "PostConfigureAll"
                   && method.TypeArguments.Any(type => GrpcSymbols.Is(type, _factoryOptions)));
    }

    private static bool MentionsClient(IOperation operation, INamedTypeSymbol client, int depth = 0)
    {
        if (depth > 32) return true;
        if ((operation is ITypeOfOperation typeOf && SymbolEqualityComparer.Default.Equals(typeOf.TypeOperand, client))
            || (operation is IInvocationOperation invocation
                && invocation.TargetMethod.TypeArguments.Any(type =>
                    SymbolEqualityComparer.Default.Equals(type, client))))
            return true;

        return operation.ChildOperations.Any(child => MentionsClient(child, client, depth + 1));
    }

    private static bool IsConditional(SyntaxNode syntax)
    {
        return syntax
            .Ancestors()
            .Any(ancestor => ancestor is IfStatementSyntax or ConditionalExpressionSyntax
                or SwitchStatementSyntax or SwitchExpressionSyntax or ForStatementSyntax or ForEachStatementSyntax
                or ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax or TryStatementSyntax
                or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
    }
}
