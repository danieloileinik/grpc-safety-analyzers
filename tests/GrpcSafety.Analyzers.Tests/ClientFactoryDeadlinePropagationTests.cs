using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class ClientFactoryDeadlinePropagationTests
{
    private const string ClientMembers = """
                                         private readonly Greeter.GreeterClient _client;

                                         public ForwardingService(Greeter.GreeterClient client)
                                         {
                                             _client = client;
                                         }
                                         """;

    [Theory]
    [InlineData("services.AddGrpcClient<Greeter.GreeterClient>().EnableCallContextPropagation();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>(options => options.Address = new Uri(\"https://localhost\")).EnableCallContextPropagation();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>().EnableCallContextPropagation(options => options.SuppressContextNotFoundErrors = true);")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>().ConfigureChannel(options => { }).EnableCallContextPropagation();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>().ConfigureChannel(options => options.MaxRetryAttempts = 3).EnableCallContextPropagation();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>().EnableCallContextPropagation().ConfigureChannel(options => { });")]
    [InlineData(
        "IHttpClientBuilder builder = services.AddGrpcClient<Greeter.GreeterClient>(); builder.EnableCallContextPropagation();")]
    [InlineData(
        "GrpcServerHttpClientBuilderExtensions.EnableCallContextPropagation(services.AddGrpcClient<Greeter.GreeterClient>());")]
    public async Task VisibleDeadlinePropagationProtectsInjectedClientInStrictMode(string registration)
    {
        // Arrange
        var test = CreateTest(registration);
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("services.AddGrpcClient<Greeter.GreeterClient>();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>(options => options.Address = new Uri(\"https://localhost\"));")]
    [InlineData("services.AddGrpcClient<Greeter.GreeterClient>().ConfigureChannel(options => { });")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>().ConfigureChannel(options => options.MaxRetryAttempts = 3);")]
    [InlineData("IHttpClientBuilder builder = services.AddGrpcClient<Greeter.GreeterClient>();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>(); services.AddGrpcClient<OtherGreeter.OtherGreeterClient>().EnableCallContextPropagation();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>().ConfigureChannel(options => options.ServiceConfig = new Grpc.Net.Client.Configuration.ServiceConfig());")]
    public async Task ResolvedRegistrationWithoutProtectionReportsGrpc002(string registration)
    {
        // Arrange
        var test = CreateTest(registration, "{|#0:_client.SayHello(new HelloRequest())|};");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingDeadlinePropagation).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("services.AddGrpcClient<Greeter.GreeterClient>(Configure);")]
    [InlineData("services.AddGrpcClient<Greeter.GreeterClient>().AddInterceptor<OpaqueInterceptor>();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>().EnableCallContextPropagation().AddInterceptor<OpaqueInterceptor>();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>().ConfigureChannel(options => options.MaxRetryAttempts = DateTime.UtcNow.Day);")]
    [InlineData("services.AddGrpcClient<Greeter.GreeterClient>(\"named\").EnableCallContextPropagation();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>(); services.AddGrpcClient<Greeter.GreeterClient>().EnableCallContextPropagation();")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>(); services.AddSingleton<Greeter.GreeterClient>(provider => new Greeter.GreeterClient(Grpc.Net.Client.GrpcChannel.ForAddress(\"https://localhost\")));")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>(); services.AddSingleton(typeof(Greeter.GreeterClient), provider => new Greeter.GreeterClient(Grpc.Net.Client.GrpcChannel.ForAddress(\"https://localhost\")));")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>(); services.Add(ServiceDescriptor.Transient(typeof(Greeter.GreeterClient), provider => new Greeter.GreeterClient(Grpc.Net.Client.GrpcChannel.ForAddress(\"https://localhost\"))));")]
    [InlineData(
        "if (DateTime.UtcNow.Ticks > 0) services.AddGrpcClient<Greeter.GreeterClient>().EnableCallContextPropagation();")]
    [InlineData(
        "IHttpClientBuilder builder = services.AddGrpcClient<Greeter.GreeterClient>(); if (DateTime.UtcNow.Ticks > 0) builder.EnableCallContextPropagation();")]
    [InlineData(
        "IHttpClientBuilder builder = services.AddGrpcClient<Greeter.GreeterClient>(); IHttpClientBuilder alias = builder; alias.EnableCallContextPropagation();")]
    [InlineData(
        "IHttpClientBuilder builder = services.AddGrpcClient<Greeter.GreeterClient>(); ConfigureBuilder(builder);")]
    [InlineData(
        "services.AddGrpcClient<Greeter.GreeterClient>(); services.Configure<Grpc.Net.ClientFactory.GrpcClientFactoryOptions>(options => { });")]
    public async Task OpaqueOrAmbiguousRegistrationRemainsUnknownInStrictMode(string registration)
    {
        // Arrange
        var test = CreateTest(registration);
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("client.SayHello(new HelloRequest());", true)]
    [InlineData("Greeter.GreeterClient alias = client; alias.SayHello(new HelloRequest());", true)]
    [InlineData("client?{|#0:.SayHello(new HelloRequest())|};", false)]
    public async Task PrimaryConstructorInjectionIsResolved(string body, bool deadlinePropagation)
    {
        // Arrange
        var test = CreateTest(
            "services.AddGrpcClient<Greeter.GreeterClient>()" +
            (deadlinePropagation ? ".EnableCallContextPropagation();" : ";"));
        test.TestState.Sources.Clear();
        test.TestCode = GrpcHandlerSource.Wrap(body, constructor: "(Greeter.GreeterClient client)");
        AddRegistration(
            test,
            "services.AddGrpcClient<Greeter.GreeterClient>()" +
            (deadlinePropagation ? ".EnableCallContextPropagation();" : ";"));
        test.Configure("grpc_safety.strict = true");
        if (!deadlinePropagation)
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(DiagnosticDescriptors.MissingDeadlinePropagation).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task DirectClientDoesNotUseAnotherInstancesDiDeadlinePropagation()
    {
        // Arrange
        var test = CreateTest(
            "services.AddGrpcClient<Greeter.GreeterClient>().EnableCallContextPropagation();",
            $"{{|#0:{GrpcTestSource.DirectClient}.SayHello(new HelloRequest())|}};");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingDeadlinePropagation).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task PrimaryConstructorFieldInitializerRetainsInjectionProvenance()
    {
        // Arrange
        var test = CreateTest("services.AddGrpcClient<Greeter.GreeterClient>().EnableCallContextPropagation();");
        test.TestState.Sources.Clear();
        test.TestCode = GrpcHandlerSource.Wrap(
            "_client.SayHello(new HelloRequest());",
            "private readonly Greeter.GreeterClient _client = client;",
            constructor: "(Greeter.GreeterClient client)");
        AddRegistration(test, "services.AddGrpcClient<Greeter.GreeterClient>().EnableCallContextPropagation();");
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    private static GrpcAnalyzerTest CreateTest(string registration,
        string body = "_client.SayHello(new HelloRequest());")
    {
        var test = new GrpcAnalyzerTest { TestCode = GrpcHandlerSource.Wrap(body, ClientMembers) };
        test.AddClientFactoryReferences();
        AddRegistration(test, registration);
        test.TestState.Sources.Add(
            ("OpaqueInterceptor.cs", """
                                     public sealed class OpaqueInterceptor : Grpc.Core.Interceptors.Interceptor { }
                                     """));
        return test;
    }

    private static void AddRegistration(GrpcAnalyzerTest test, string registration)
    {
        test.TestState.Sources.Add(
            ("Registration.cs", $$"""
                                  using System;
                                  using Grpc.Net.ClientFactory;
                                  using GrpcSafety.Analyzers.Tests.Fixtures;
                                  using Microsoft.Extensions.DependencyInjection;

                                  public static class ClientRegistration
                                  {
                                      public static void Register(IServiceCollection services)
                                      {
                                          {{registration}}
                                      }

                                      private static void Configure(GrpcClientFactoryOptions options) { }
                                      private static void ConfigureBuilder(IHttpClientBuilder builder) { }
                                  }
                                  """));
    }
}
