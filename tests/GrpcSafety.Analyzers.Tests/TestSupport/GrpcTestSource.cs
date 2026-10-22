namespace GrpcSafety.Analyzers.Tests.TestSupport;

internal static class GrpcTestSource
{
    public const string DirectClient = "new Greeter.GreeterClient(GrpcChannel.ForAddress(\"https://localhost\"))";

    public static string Wrap(string body, string parameters = "")
    {
        return $$"""
                 using System;
                 using Grpc.Core;
                 using Grpc.Net.Client;
                 using GrpcSafety.Analyzers.Tests.Fixtures;

                 public static class CallSite
                 {
                     public static void Invoke({{parameters}})
                     {
                         {{body}}
                     }
                 }
                 """;
    }
}
