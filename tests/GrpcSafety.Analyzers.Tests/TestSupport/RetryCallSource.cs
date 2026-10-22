namespace GrpcSafety.Analyzers.Tests.TestSupport;

internal static class RetryCallSource
{
    public static string Options(string? methods = null, string settings = "")
    {
        return $$"""
                 new GrpcChannelOptions {
                     ServiceConfig = new Grpc.Net.Client.Configuration.ServiceConfig {
                         MethodConfigs = { {{methods ?? DefaultMethod()}} }
                     }{{settings}}
                 }
                 """;
    }

    public static string DefaultMethod(string policy = RetryTestSource.Retry)
    {
        return RetryTestSource.Method(policy, "MethodName.Default");
    }

    public static string Client(string? methods = null, string settings = "")
    {
        return "new Greeter.GreeterClient(GrpcChannel.ForAddress(\"https://localhost\", " + Options(methods, settings) +
               "))";
    }

    public static string Wrap(string body, string parameters = "")
    {
        return "using Grpc.Net.Client.Configuration;\n" + GrpcTestSource.Wrap(body, parameters);
    }
}
