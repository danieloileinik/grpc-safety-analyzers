using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Configuration;

namespace GrpcSafety.Analyzers.Semantics;

internal interface IPropagationResolver
{
    // Scope includes nested local functions/lambdas in handlers, but excludes other layers.
    ProtectionState Resolve(GrpcCall call, ClientConfiguration configuration, CancellationToken cancellationToken);
}
