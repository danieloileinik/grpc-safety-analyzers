using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class InjectedClientResolver(GrpcSymbols symbols)
{
    private readonly LocalValueResolver _locals = new(symbols.Compilation);

    public INamedTypeSymbol? FindClient(IOperation operation, CancellationToken cancellationToken)
    {
        operation = _locals.Resolve(operation, cancellationToken);
        if (operation is IParameterReferenceOperation parameter)
            return IsInjectedParameter(parameter.Parameter, cancellationToken)
                ? parameter.Type as INamedTypeSymbol
                : null;

        if (operation is not IFieldReferenceOperation { Field: { IsReadOnly: true, IsStatic: false } field }
            || field.Type is not INamedTypeSymbol client
            || field.DeclaringSyntaxReferences.Length != 1
            || field.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not VariableDeclaratorSyntax
                fieldDeclaration)
            return null;

        if (fieldDeclaration.Initializer is { } initializer)
        {
            var model = symbols.Compilation.GetSemanticModel(initializer.SyntaxTree);
            return model.GetOperation(initializer.Value, cancellationToken) is { } value
                   && _locals.Resolve(value, cancellationToken) is IParameterReferenceOperation source
                   && IsInjectedParameter(source.Parameter, cancellationToken)
                   && SymbolEqualityComparer.Default.Equals(source.Type, client)
                ? client
                : null;
        }

        var assigned = false;
        foreach (var constructor in field.ContainingType.InstanceConstructors)
        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax(cancellationToken);
            var model = symbols.Compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var assignment in declaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetOperation(assignment, cancellationToken) is not ISimpleAssignmentOperation
                    {
                        Target: IFieldReferenceOperation target
                    } operationAssignment
                    || !SymbolEqualityComparer.Default.Equals(target.Field, field))
                    continue;

                assigned = true;
                if (_locals.Resolve(operationAssignment.Value, cancellationToken) is not IParameterReferenceOperation
                        source
                    || !IsInjectedParameter(source.Parameter, cancellationToken)
                    || !SymbolEqualityComparer.Default.Equals(source.Type, client))
                    return null;
            }
        }

        return assigned ? client : null;
    }

    private bool IsInjectedParameter(IParameterSymbol parameter, CancellationToken cancellationToken)
    {
        return parameter is
               {
                   ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor }, Type: INamedTypeSymbol type
               }
               && GrpcSymbols.DerivesFrom(type, symbols.ClientBase)
               && _locals.IsStable(parameter, cancellationToken);
    }
}
