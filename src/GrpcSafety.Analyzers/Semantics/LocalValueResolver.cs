using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Semantics;

internal sealed class LocalValueResolver(Compilation compilation)
{
    private readonly ConcurrentDictionary<ISymbol, bool> _stableSymbols = new(SymbolEqualityComparer.Default);

    public IOperation Resolve(IOperation operation, CancellationToken cancellationToken)
    {
        var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        operation = DeadlineResolver.Unwrap(operation)!;
        while (operation is ILocalReferenceOperation reference && visited.Count < 32)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(reference.Local)
                || !IsStable(reference.Local, cancellationToken)
                || reference.Local.DeclaringSyntaxReferences.Length != 1
                || reference.Local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken)
                    is not VariableDeclaratorSyntax { Initializer.Value: { } initializer })
                break;

            var model = compilation.GetSemanticModel(initializer.SyntaxTree);
            var value = model.GetOperation(initializer, cancellationToken);
            if (value is null) break;
            operation = DeadlineResolver.Unwrap(value)!;
        }

        return operation;
    }

    public bool IsStable(ISymbol symbol, CancellationToken cancellationToken)
    {
        return _stableSymbols.GetOrAdd(symbol, candidate => IsUnmodified(candidate, cancellationToken));
    }

    private bool IsUnmodified(ISymbol symbol, CancellationToken cancellationToken)
    {
        if (symbol is ILocalSymbol { RefKind: not RefKind.None }
            || symbol.DeclaringSyntaxReferences.Length != 1)
            return false;

        var declaration = symbol.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
        var root = declaration.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault()
                   ?? declaration.SyntaxTree.GetRoot(cancellationToken);
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier.Identifier.ValueText != symbol.Name
                || !SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    symbol))
                continue;

            var reference = model.GetOperation(identifier, cancellationToken);
            if (identifier.Parent is ArgumentSyntax { RefKindKeyword.RawKind: not 0 }
                || identifier.Ancestors().Any(ancestor => ancestor is RefExpressionSyntax))
                return false;
            for (var current = reference; current?.Parent is { } parent; current = parent)
            {
                if ((parent is IAssignmentOperation assignment && ReferenceEquals(current, assignment.Target))
                    || parent is IIncrementOrDecrementOperation
                    || parent is IArgumentOperation { Parameter.RefKind: not RefKind.None }
                    || parent is IAddressOfOperation)
                    return false;

                if (parent is not (ITupleOperation or IConversionOperation or IParenthesizedOperation)) break;
            }
        }

        return true;
    }
}
