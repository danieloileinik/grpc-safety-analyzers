using System.Threading;
using GrpcSafety.Analyzers.Analysis;

namespace GrpcSafety.Analyzers.Semantics;

internal interface IDeadlinePropagationResolver
{
    // Scope includes nested local functions/lambdas in handlers, but excludes other layers.
    ProtectionState Resolve(GrpcCall call, ProtectionState deadline, CancellationToken cancellationToken);
}
