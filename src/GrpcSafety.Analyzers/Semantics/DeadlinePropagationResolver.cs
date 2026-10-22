using System.Threading;
using GrpcSafety.Analyzers.Analysis;

namespace GrpcSafety.Analyzers.Semantics;

internal sealed class DeadlinePropagationResolver(GrpcSymbols symbols) : IDeadlinePropagationResolver
{
    private readonly RpcHandlerRecognizer _handlers = new(symbols);

    public ProtectionState Resolve(GrpcCall call, ProtectionState deadline, CancellationToken cancellationToken)
    {
        // A finite or inherited deadline and central deadline propagation are already merged by the classifier.
        return _handlers.FindContext(call, cancellationToken) is null ? ProtectionState.Unknown : deadline;
    }
}
