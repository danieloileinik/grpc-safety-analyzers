using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Semantics;

internal sealed class GrpcCallRecognizer(GrpcSymbols symbols)
{
    public bool TryRecognize(IInvocationOperation invocation, out GrpcCall call)
    {
        var method = invocation.TargetMethod;
        var isCallInvoker = GrpcSymbols.DerivesFrom(method.ContainingType, symbols.CallInvoker)
            && method.Name is "AsyncUnaryCall" or "BlockingUnaryCall" or "AsyncServerStreamingCall"
                or "AsyncClientStreamingCall" or "AsyncDuplexStreamingCall";

        var isGeneratedClientCall = GrpcSymbols.DerivesFrom(method.ContainingType, symbols.ClientBase)
            && IsGenerated(method)
            && method.Parameters.Any(parameter =>
                GrpcSymbols.Is(parameter.Type, symbols.CallOptions)
                || parameter.Name is "deadline" && GrpcSymbols.IsNullableDateTime(parameter.Type));

        call = new(invocation, isCallInvoker);
        return isCallInvoker || isGeneratedClientCall;
    }

    private bool IsGenerated(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (symbols.HasGeneratorAttribute(current))
            {
                return true;
            }
        }

        return false;
    }
}
