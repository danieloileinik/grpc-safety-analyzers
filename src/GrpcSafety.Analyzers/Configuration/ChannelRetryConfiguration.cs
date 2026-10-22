using System.Collections.Immutable;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class ChannelRetryConfiguration(ImmutableArray<MethodRetryConfiguration> methods, int? maxRetryAttempts)
{
    public ImmutableArray<MethodRetryConfiguration> Methods { get; } = methods;

    public int? MaxRetryAttempts { get; } = maxRetryAttempts;
}
