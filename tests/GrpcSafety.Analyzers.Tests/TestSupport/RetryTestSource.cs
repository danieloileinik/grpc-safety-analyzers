using System;

namespace GrpcSafety.Analyzers.Tests.TestSupport;

internal static class RetryTestSource
{
    public const string Proto = """
                                syntax = "proto3";
                                package safety.testing;
                                message Request { string name = 1; }
                                message Reply { }
                                service Greeter {
                                  rpc SayHello (Request) returns (Reply);
                                  rpc ReadHello (Request) returns (Reply) { option idempotency_level = NO_SIDE_EFFECTS; }
                                  rpc SaveHello (Request) returns (Reply) { option idempotency_level = IDEMPOTENT; }
                                }
                                service OtherGreeter { rpc SayHello (Request) returns (Reply); }
                                """;

    public const string Retry = """
                                new RetryPolicy {
                                    MaxAttempts = 3,
                                    InitialBackoff = TimeSpan.FromSeconds(1),
                                    MaxBackoff = TimeSpan.FromSeconds(5),
                                    BackoffMultiplier = 2,
                                    RetryableStatusCodes = { StatusCode.Unavailable }
                                }
                                """;

    public const string Hedging = "new HedgingPolicy { MaxAttempts = 3 }";

    public static string Method(string policy = Retry,
        string name = "new MethodName { Service = \"safety.testing.Greeter\", Method = \"SayHello\" }")
    {
        var property = policy.Contains("HedgingPolicy", StringComparison.Ordinal) ? "HedgingPolicy" : "RetryPolicy";
        return $$"""
                 new MethodConfig { Names = { {{name}} }, {{property}} = {{policy}} }
                 """;
    }

    public static string Wrap(string methods, string channelSettings = "", string setup = "", bool factory = false)
    {
        var options = $$"""
                        ServiceConfig = new ServiceConfig { MethodConfigs = { {{methods}} } }{{channelSettings}}
                        """;
        var construction = factory
            ? $$"""services.AddGrpcClient<Greeter.GreeterClient>().ConfigureChannel(options => { options.ServiceConfig = new ServiceConfig { MethodConfigs = { {{methods}} } }; {{channelSettings}} });"""
            : $$"""GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions { {{options}} });""";
        return $$"""
                 using System;
                 using Grpc.Core;
                 using Grpc.Net.Client;
                 using Grpc.Net.Client.Configuration;
                 using GrpcSafety.Analyzers.Tests.Fixtures;
                 {{(factory ? "using Microsoft.Extensions.DependencyInjection;" : "")}}

                 public static class Registration
                 {
                     public static void Register({{(factory ? "IServiceCollection services" : "")}})
                     {
                         {{setup}}
                         {{construction}}
                     }

                     private static int Attempts() => 3;
                     private static void Change(object value) { }
                 }
                 """;
    }
}
