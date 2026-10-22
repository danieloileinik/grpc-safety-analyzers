using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class MissingDeadlinePropagationTests
{
    [Theory]
    [InlineData("")]
    [InlineData(", deadline: null")]
    [InlineData(", deadline: DateTime.MaxValue")]
    [InlineData(", deadline: default(DateTime?)")]
    [InlineData(", options: default(CallOptions)")]
    [InlineData(", options: new CallOptions()")]
    [InlineData(", options: new CallOptions(deadline: DateTime.MaxValue)")]
    [InlineData(", options: new CallOptions(deadline: context.Deadline).WithDeadline(DateTime.MaxValue)")]
    [InlineData(
        ", options: new CallOptions(deadline: context.Deadline).WithHeaders(new Metadata()).WithDeadline(DateTime.MaxValue)")]
    public async Task HandlerWithoutDeadlineReportsOnlyGrpc002(string arguments)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcHandlerSource.Wrap(
                $"{{|#0:{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(){arguments})|}};")
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingDeadlinePropagation).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("void Forward() { CALL; } Forward();")]
    [InlineData("Action forward = () => CALL; forward();")]
    [InlineData("Func<Task> forward = async () => { await Task.Yield(); CALL; }; _ = forward();")]
    [InlineData("void Outer() { void Inner() { CALL; } Inner(); } Outer();")]
    [InlineData("void Forward() { Action inner = () => CALL; inner(); } Forward();")]
    public async Task NestedFunctionsRemainInHandlerScope(string body)
    {
        // Arrange
        var call = $"{{|#0:{GrpcTestSource.DirectClient}.SayHello(new HelloRequest())|}}";
        var test = new GrpcAnalyzerTest { TestCode = GrpcHandlerSource.Wrap(body.Replace("CALL", call)) };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingDeadlinePropagation).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(
        "public override Task ListHello(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)")]
    [InlineData(
        "public override Task<HelloReply> UploadHello(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)")]
    [InlineData(
        "public override Task StreamHello(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)")]
    public async Task StreamingHandlersAreRecognized(string signature)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcHandlerSource.Wrap(
                $"{{|#0:{GrpcTestSource.DirectClient}.SayHello(new HelloRequest())|}};",
                signature: signature)
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingDeadlinePropagation).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task DirectCallInvokerInHandlerReportsGrpc002()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcHandlerSource.Wrap(
                """
                Method<HelloRequest, HelloReply> method = null;
                {|#0:GrpcChannel.ForAddress("https://localhost").CreateCallInvoker()
                    .BlockingUnaryCall(method, null, new CallOptions(), new HelloRequest())|};
                """)
        };
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingDeadlinePropagation).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task SeparateHelperMethodIsOutsideHandlerScope()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcHandlerSource.Wrap(
                "Forward(context);",
                $$"""
                  private static void Forward(ServerCallContext context)
                  {
                      {|#0:{{GrpcTestSource.DirectClient}}.SayHello(new HelloRequest())|};
                  }
                  """)
        };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("client.SayHello(new HelloRequest());", false)]
    [InlineData("{|#0:client.SayHello(new HelloRequest())|};", true)]
    [InlineData("client?{|#0:.SayHello(new HelloRequest())|};", true)]
    public async Task UnresolvedRegistrationHonorsStrictMode(string body, bool strict)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcHandlerSource.Wrap(body, constructor: "(Greeter.GreeterClient client)")
        };
        if (strict)
        {
            test.Configure("grpc_safety.strict = true");
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(DiagnosticDescriptors.MissingDeadlinePropagation).WithLocation(0));
        }

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(
        "new Greeter.GreeterClient(GrpcChannel.ForAddress(\"https://localhost\", new GrpcChannelOptions { MaxRetryAttempts = DateTime.UtcNow.Day }))")]
    [InlineData("new Greeter.GreeterClient((CallInvoker)null)")]
    public async Task OpaqueConfigurationDoesNotReportEvenInStrictMode(string client)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
            { TestCode = GrpcHandlerSource.Wrap($"{client}.SayHello(new HelloRequest());") };
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task RuleCanBeDisabledThroughEditorConfig()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcHandlerSource.Wrap($"{GrpcTestSource.DirectClient}.SayHello(new HelloRequest());")
        };
        test.Configure("dotnet_diagnostic.GRPC002.severity = none");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task IntermediateServiceBaseRetainsHandlerIdentity()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcHandlerSource
                .Wrap($"{{|#0:{GrpcTestSource.DirectClient}.SayHello(new HelloRequest())|}};")
                .Replace(": Greeter.GreeterBase", ": IntermediateService")
        };
        test.TestState.Sources.Add(
            ("IntermediateService.cs", """
                                       using System.Threading.Tasks;
                                       using Grpc.Core;
                                       using GrpcSafety.Analyzers.Tests.Fixtures;

                                       public abstract class IntermediateService : Greeter.GreeterBase
                                       {
                                           public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
                                               => base.SayHello(request, context);
                                       }
                                       """));
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingDeadlinePropagation).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task UnrelatedBaseWithContextParameterIsNotAnRpcHandler()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcHandlerSource
                .Wrap($"{{|#0:{GrpcTestSource.DirectClient}.SayHello(new HelloRequest())|}};")
                .Replace(": Greeter.GreeterBase", ": PretendBase")
        };
        test.TestState.Sources.Add(
            ("PretendBase.cs", """
                               using System.Threading.Tasks;
                               using Grpc.Core;
                               using GrpcSafety.Analyzers.Tests.Fixtures;

                               public abstract class PretendBase
                               {
                                   public abstract Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context);
                               }
                               """));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }
}
