using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Semantics;

internal readonly struct GrpcCall(IInvocationOperation invocation, bool isCallInvoker)
{
    public IInvocationOperation Invocation { get; } = invocation;

    public bool IsCallInvoker { get; } = isCallInvoker;
}
