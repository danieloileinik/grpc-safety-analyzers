using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Protobuf;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Semantics;

internal sealed class GrpcMethodIdentityResolver(GrpcSymbols symbols)
{
    private readonly GrpcCallRecognizer _calls = new(symbols);

    private readonly ConcurrentDictionary<IMethodSymbol, ProtoMethodIdentity?> _generatedMethods =
        new(SymbolEqualityComparer.Default);

    private readonly LocalValueResolver _locals = new(symbols.Compilation);

    public ProtoMethodIdentity? Resolve(GrpcCall call, CancellationToken cancellationToken)
    {
        return call.IsCallInvoker
            ? ReadMethodArgument(call.Invocation, cancellationToken)
            : _generatedMethods.GetOrAdd(
                call.Invocation.TargetMethod,
                method =>
                    ReadGeneratedMethod(
                        method,
                        new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
                        cancellationToken));
    }

    private ProtoMethodIdentity? ReadGeneratedMethod(IMethodSymbol method,
        HashSet<IMethodSymbol> visited,
        CancellationToken cancellationToken)
    {
        if (visited.Count >= 16 || !visited.Add(method)) return null;
        ProtoMethodIdentity? found = null;
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax(cancellationToken);
            var model = symbols.Compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var syntax in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetOperation(syntax, cancellationToken) is not IInvocationOperation invocation
                    || !_calls.TryRecognize(invocation, out var nested)) continue;
                var identity = nested.IsCallInvoker
                    ? ReadMethodArgument(invocation, cancellationToken)
                    : SymbolEqualityComparer.Default.Equals(
                        invocation.TargetMethod.ContainingType,
                        method.ContainingType)
                        ? ReadGeneratedMethod(invocation.TargetMethod, visited, cancellationToken)
                        : null;
                if (identity is null || found is not null) return null;
                found = identity;
            }
        }

        return found;
    }

    private ProtoMethodIdentity? ReadMethodArgument(IInvocationOperation invocation,
        CancellationToken cancellationToken)
    {
        var argument = invocation.Arguments.FirstOrDefault(item => item.Parameter?.Name == "method");
        if (argument is null) return null;
        var operation = _locals.Resolve(argument.Value, cancellationToken);
        if (operation is IFieldReferenceOperation field)
            operation = ReadFieldInitializer(field.Field, cancellationToken) ?? operation;
        if (operation is not IObjectCreationOperation { Initializer: null } creation
            || !GrpcSymbols.Is(creation.Type, symbols.Method)) return null;
        var service = creation.Arguments.FirstOrDefault(item => item.Parameter?.Name == "serviceName");
        var method = creation.Arguments.FirstOrDefault(item => item.Parameter?.Name == "name");
        var serviceName = service is null ? null : ReadString(service.Value, cancellationToken);
        var methodName = method is null ? null : ReadString(method.Value, cancellationToken);
        return string.IsNullOrEmpty(serviceName) || string.IsNullOrEmpty(methodName)
            ? null
            : new ProtoMethodIdentity(serviceName!, methodName!);
    }

    private string? ReadString(IOperation operation, CancellationToken cancellationToken)
    {
        operation = _locals.Resolve(operation, cancellationToken);
        if (operation is IFieldReferenceOperation field)
            operation = ReadFieldInitializer(field.Field, cancellationToken) ?? operation;
        return operation.ConstantValue is { HasValue: true, Value: string value } ? value : null;
    }

    private IOperation? ReadFieldInitializer(IFieldSymbol field, CancellationToken cancellationToken)
    {
        if (!field.IsStatic || !field.IsReadOnly
                            || field.ContainingType.StaticConstructors.Any(constructor =>
                                !constructor.DeclaringSyntaxReferences.IsEmpty)
                            || field.DeclaringSyntaxReferences.Length != 1
                            || field.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken)
                                is not VariableDeclaratorSyntax { Initializer.Value: { } initializer }) return null;
        var model = symbols.Compilation.GetSemanticModel(initializer.SyntaxTree);
        return model.GetOperation(initializer, cancellationToken) is { } value ? DeadlineResolver.Unwrap(value) : null;
    }
}
