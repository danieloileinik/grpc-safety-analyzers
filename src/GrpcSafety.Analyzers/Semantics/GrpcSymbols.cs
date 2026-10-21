using System.Linq;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Semantics;

internal sealed class GrpcSymbols(Compilation compilation)
{
    public INamedTypeSymbol? ClientBase { get; } = compilation.GetTypeByMetadataName("Grpc.Core.ClientBase`1");

    public INamedTypeSymbol? CallInvoker { get; } = compilation.GetTypeByMetadataName("Grpc.Core.CallInvoker");

    public INamedTypeSymbol? CallOptions { get; } = compilation.GetTypeByMetadataName("Grpc.Core.CallOptions");

    public INamedTypeSymbol? Channel { get; } = compilation.GetTypeByMetadataName("Grpc.Net.Client.GrpcChannel");

    public INamedTypeSymbol? ChannelOptions { get; } = compilation.GetTypeByMetadataName("Grpc.Net.Client.GrpcChannelOptions");

    public INamedTypeSymbol? GeneratedCodeAttribute { get; } = compilation.GetTypeByMetadataName("System.CodeDom.Compiler.GeneratedCodeAttribute");

    public bool IsAvailable => ClientBase is not null && CallInvoker is not null && CallOptions is not null;

    public static bool Is(ITypeSymbol? type, ITypeSymbol? expected) =>
        expected is not null && SymbolEqualityComparer.Default.Equals(type?.OriginalDefinition, expected);

    public bool HasGeneratorAttribute(ISymbol symbol) => symbol.GetAttributes().Any(attribute =>
        Is(attribute.AttributeClass, GeneratedCodeAttribute)
        && attribute.ConstructorArguments.Length > 0
        && attribute.ConstructorArguments[0].Value is "grpc_csharp_plugin");

    public static bool DerivesFrom(INamedTypeSymbol? type, INamedTypeSymbol? expected)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (Is(current, expected))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsNullableDateTime(ITypeSymbol? type) =>
        type is INamedTypeSymbol
        {
            OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
            TypeArguments.Length: 1,
        } nullable && nullable.TypeArguments[0].SpecialType is SpecialType.System_DateTime;
}
