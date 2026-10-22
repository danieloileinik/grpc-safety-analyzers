using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Semantics;

internal sealed class RpcHandlerRecognizer(GrpcSymbols symbols)
{
    public IParameterSymbol? FindContext(GrpcCall call, CancellationToken cancellationToken)
    {
        var model = symbols.Compilation.GetSemanticModel(call.Invocation.Syntax.SyntaxTree);
        for (var current = model.GetEnclosingSymbol(call.Invocation.Syntax.SpanStart, cancellationToken);
             current is not null;
             current = current.ContainingSymbol)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current is not IMethodSymbol { IsOverride: true } method) continue;

            var context = method.Parameters.FirstOrDefault(parameter =>
                GrpcSymbols.Is(parameter.Type, symbols.ServerCallContext));
            if (context is null) continue;

            for (var overridden = method.OverriddenMethod;
                 overridden is not null;
                 overridden = overridden.OverriddenMethod)
                if (overridden.ContainingType.Name.EndsWith("Base", StringComparison.Ordinal)
                    && (symbols.HasGeneratorAttribute(overridden)
                        || symbols.HasGeneratorAttribute(overridden.ContainingType))
                    && overridden.Parameters.Length > context.Ordinal
                    && GrpcSymbols.Is(overridden.Parameters[context.Ordinal].Type, symbols.ServerCallContext))
                    return context;
        }

        return null;
    }
}
