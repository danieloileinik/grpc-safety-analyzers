using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class LiteralConfigurationReader(GrpcSymbols symbols)
{
    private readonly LocalValueResolver _locals = new(symbols.Compilation);

    public LiteralConfiguration? Read(IOperation operation, string metadataName, CancellationToken cancellationToken)
    {
        operation = DeadlineResolver.Unwrap(operation)!;
        if (operation is ILocalReferenceOperation local &&
            !HasOnlyConfigurationUses(local.Local, cancellationToken)) return null;
        operation = _locals.Resolve(operation, cancellationToken);
        if (operation is not IObjectCreationOperation { Arguments.IsEmpty: true } creation
            || !GrpcSymbols.Is(creation.Type, symbols.Compilation.GetTypeByMetadataName(metadataName)))
            return null;

        var properties = ImmutableDictionary.CreateBuilder<string, IOperation>();
        var collections =
            ImmutableDictionary.CreateBuilder<string, ImmutableArray<IOperation>>();
        if (creation.Initializer is not null)
            foreach (var initializer in creation.Initializer.Initializers)
                if (initializer is ISimpleAssignmentOperation
                    {
                        Target: IPropertyReferenceOperation property
                    } assignment)
                {
                    if (properties.ContainsKey(property.Property.Name)) return null;
                    properties.Add(property.Property.Name, assignment.Value);
                }
                else if (initializer is IMemberInitializerOperation
                         {
                             InitializedMember: IPropertyReferenceOperation member
                         } collection)
                {
                    var values = ImmutableArray.CreateBuilder<IOperation>();
                    foreach (var item in collection.Initializer.Initializers)
                    {
                        if (item is not IInvocationOperation { IsImplicit: true, Arguments.Length: 1 } add
                            || add.TargetMethod.Name != "Add") return null;
                        values.Add(add.Arguments[0].Value);
                    }

                    if (collections.ContainsKey(member.Property.Name)) return null;
                    collections.Add(member.Property.Name, values.ToImmutable());
                }
                else
                {
                    return null;
                }

        return new LiteralConfiguration(creation, properties.ToImmutable(), collections.ToImmutable());
    }

    private bool HasOnlyConfigurationUses(ILocalSymbol local, CancellationToken cancellationToken)
    {
        if (!_locals.IsStable(local, cancellationToken) || local.DeclaringSyntaxReferences.Length != 1) return false;
        var declaration = local.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken);
        var scope = declaration.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault()
                    ?? declaration.SyntaxTree.GetRoot(cancellationToken);
        var model = symbols.Compilation.GetSemanticModel(scope.SyntaxTree);
        var uses = 0;
        foreach (var identifier in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (identifier.Identifier.ValueText != local.Name
                || !SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(identifier, cancellationToken).Symbol,
                    local)) continue;
            // A second owner could mutate the object through a different configuration graph.
            if (++uses > 1) return false;
            var current = model.GetOperation(identifier, cancellationToken);
            while (current?.Parent is IConversionOperation conversion) current = conversion;
            if (current?.Parent is ISimpleAssignmentOperation
                {
                    Target: IPropertyReferenceOperation property
                } assignment
                && ReferenceEquals(assignment.Value, current) &&
                IsConfigurationType(property.Property.ContainingType)) continue;
            if (current?.Parent is IArgumentOperation { Parent: IInvocationOperation invocation } && (invocation is
                        { IsImplicit: true, TargetMethod.Name: "Add", Parent: IObjectOrCollectionInitializerOperation }
                    || (invocation.TargetMethod.Name == "ForAddress"
                        && GrpcSymbols.Is(
                            invocation.TargetMethod.ContainingType,
                            symbols.Channel)))) continue;
            return false;
        }

        return true;
    }

    private static bool IsConfigurationType(INamedTypeSymbol type)
    {
        return type.ToDisplayString() is "Grpc.Net.Client.GrpcChannelOptions"
            or "Grpc.Net.Client.Configuration.ServiceConfig"
            or "Grpc.Net.Client.Configuration.MethodConfig";
    }
}
