using System.Threading;
using GrpcSafety.Analyzers.Semantics;

namespace GrpcSafety.Analyzers.Configuration;

internal interface IClientConfigurationResolver
{
    // Extend with compilation-wide AddGrpcClient/ConfigureChannel registration indexing.
    ClientConfiguration Resolve(GrpcCall call, CancellationToken cancellationToken);
}
