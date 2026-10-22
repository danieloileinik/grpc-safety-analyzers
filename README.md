# GrpcSafety.Analyzers

A C# analyzer that reports gRPC client calls without a deadline during compilation.

**Preview: `0.1.0-alpha.1`. Only GRPC001 currently produces diagnostics.**
The analyzer can inspect a limited set of client constructions and deadline
expressions. When it cannot determine whether a call has a deadline, it does not
report a warning by default. A build without warnings does not prove that all
calls have deadlines.

## Install the preview

The steps below build a local NuGet package from a checkout of this repository.
They do not require a published release.

Use the .NET 10 SDK selected by
[global.json](https://github.com/danieloileinik/grpc-safety-analyzers/blob/main/global.json).
NuGet access is required to restore dependencies. The analyzer is built against
Roslyn 4.14; the .NET 10 compiler host is the verified environment.

### 1. Build the package

Run from this repository's root:

```sh
dotnet pack src/GrpcSafety.Analyzers/GrpcSafety.Analyzers.csproj \
  --configuration Release --output artifacts/packages
```

This creates `artifacts/packages/GrpcSafety.Analyzers.0.1.0-alpha.1.nupkg`.

### 2. Reference it from your client project

Add the following to the `.csproj` that contains your gRPC client calls:

```xml
<ItemGroup>
  <PackageReference Include="GrpcSafety.Analyzers"
                    Version="0.1.0-alpha.1"
                    PrivateAssets="all" />
</ItemGroup>
```

Keep your existing gRPC and generated-client references. This package supplies
the analyzer; it does not supply a gRPC client or application runtime assembly.
`PrivateAssets="all"` keeps the analyzer reference private to this project.

### 3. Restore and build your client project

Run from this repository's root. Replace `/path/to/Client.csproj` with the path to
your client project:

```sh
dotnet restore /path/to/Client.csproj \
  --source "$PWD/artifacts/packages" --source https://api.nuget.org/v3/index.json
dotnet build /path/to/Client.csproj --no-restore
```

The analyzer runs during the build. GRPC001 is a warning by default; you can
change its severity in `.editorconfig` as shown below.

## What GRPC001 reports

GRPC001 means that the analyzer found no deadline for the call and no central
deadline mechanism in the client configuration it could inspect. Central
protection is configured for the client or channel rather than passed in the
call's arguments.

These examples assume a generated `Greeter.GreeterClient`, `HelloRequest`, and
the `System`, `Grpc.Core`, `Grpc.Net.Client`, and generated-type namespace imports.
Construction stays inline to make configuration visible to the preview analyzer.

### A call without a deadline

```csharp
new Greeter.GreeterClient(GrpcChannel.ForAddress("https://localhost"))
    .SayHello(new HelloRequest()); // GRPC001: Set a deadline for this gRPC call
```

An omitted deadline, `null`, `default(DateTime?)`, or `DateTime.MaxValue` counts
as an absent deadline. Empty `CallOptions` also produce GRPC001:

```csharp
new Greeter.GreeterClient(GrpcChannel.ForAddress("https://localhost"))
    .SayHello(new HelloRequest(), options: new CallOptions()); // GRPC001
```

### A call with a known finite deadline

```csharp
new Greeter.GreeterClient(GrpcChannel.ForAddress("https://localhost"))
    .SayHello(
        new HelloRequest(),
        deadline: new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
```

This produces no warning. The fixed date demonstrates an expression the analyzer
can resolve. It checks whether the value is finite; it does not check whether
the deadline is appropriate or still in the future.

A supported deadline can also be passed through `new CallOptions(deadline: ...)`
or `.WithDeadline(...)`. `WithDeadline` replaces the previous deadline. Replacing
it with `DateTime.MaxValue` produces GRPC001 when the supported options chain has
no propagation token. If the token cannot be resolved, the analyzer stays silent
because a deadline might be inherited through it.

## Calls without warnings

The analyzer uses `Unknown` for values or configuration it cannot resolve.
`Unknown` suppresses diagnostics in the following cases.

### The client is stored or passed into a method

```csharp
void Send(Greeter.GreeterClient client)
{
    client.SayHello(new HelloRequest()); // No warning in default mode.
}
```

The analyzer cannot determine how this client was configured. The same applies
to a client read from a local variable or field, even if its construction is
visible nearby. Strict mode can warn for these clients under an additional
assumption described below.

### The deadline is computed at runtime

```csharp
new Greeter.GreeterClient(GrpcChannel.ForAddress("https://localhost"))
    .SayHello(new HelloRequest(), deadline: DateTime.UtcNow.AddSeconds(5));
```

The current analyzer cannot resolve this expression. It produces no warning
in either default or strict mode.

Other unresolved inputs include nonempty channel options, custom call invokers,
factories, dependency-injection registrations, and configuration outside the
current compilation. Generated call sites are excluded from analysis; calls to
generated clients from ordinary source code are analyzed.

## Configure diagnostics

Add these settings to an `.editorconfig` that applies to your client source files:

```editorconfig
root = true

[*.cs]
dotnet_diagnostic.GRPC001.severity = warning
grpc_safety.strict = false
```

Use `error` instead of `warning` to fail the build, or `none` to disable GRPC001.
The other diagnostic IDs are reserved for planned rules and do not currently
produce warnings.

### Strict mode

Set `grpc_safety.strict = true` to assume that clients read from a local variable,
field, or parameter have no central deadline mechanism. Such a call then produces
GRPC001 if its explicit deadline is confirmed absent. This assumption can produce
a warning for a client whose deadline is configured elsewhere.

| Call evidence | Default mode | Strict mode |
| --- | --- | --- |
| Client comes from a local, field, or parameter; deadline is absent. | No warning. | GRPC001. |
| Explicit deadline is a supported finite value. | No warning. | No warning. |
| Explicit deadline is an unresolved expression. | No warning. | No warning. |
| Inline construction uses unresolved channel options or a custom invoker. | No warning. | No warning. |

Conditional access (`client?.SayHello(...)`) uses the same configuration evidence
as a regular call. A missing or invalid strict option is treated as `false`.

## Supported analysis and limitations

- Calls to generated clients and the five standard `CallInvoker` RPC methods,
  including their overrides, are recognized. Unrelated overloads are ignored.
- Client configuration is resolved for an inline generated constructor using
  `GrpcChannel.ForAddress` with a constant string address and omitted, null, or
  empty literal channel options. Direct `CreateCallInvoker()` chains are also
  supported.
- Deadline resolution covers supported constant-argument `DateTime` constructors,
  sentinel/default values, and supported `CallOptions` construction and
  `WithDeadline` chains. Unsupported expressions remain `Unknown`.
- Tracking client assignments, `AddGrpcClient`, `ConfigureChannel`, interceptors,
  and `EnableCallContextPropagation` is not implemented.
- RPC handler detection, parent-deadline inheritance, retry/hedging policy
  resolution, and `.proto` idempotency parsing are not implemented.
- `CancellationToken` checking, code fixes, and a server retry test-helper package
  are outside the current implementation.

## Development

To build and run the test suite, use the .NET 10 SDK and run from the repository
root:

```sh
dotnet restore GrpcSafety.Analyzers.slnx
dotnet build GrpcSafety.Analyzers.slnx --configuration Release --no-restore
dotnet test GrpcSafety.Analyzers.slnx --configuration Release --no-build --no-restore
```

The first test run also needs NuGet access to download reference assemblies.
The analyzer targets `netstandard2.0`; tests target `net10.0` and use
`Microsoft.CodeAnalysis.Testing`, xUnit, and a real generated gRPC client.
The suite covers deadline replacement, conditional access, strict mode, severity,
call recognition, and classifier priorities. GRPC002–004 are tested through
classifier facts rather than end-to-end analyzer diagnostics.

### Architecture

`GrpcSafetyAnalyzer` recognizes calls, collects evidence through
`CallAnalysisPipeline`, and reports the result selected by `CallSafetyClassifier`.
Evidence is `Protected`, `Unprotected`, or `Unknown`; default-initialized evidence
and missing resolver implementations stay `Unknown`.

| Location | Responsibility |
| --- | --- |
| `src/GrpcSafety.Analyzers/Analysis/` | Evidence, pipeline, and classification. |
| `src/GrpcSafety.Analyzers/Semantics/` | Call recognition, deadlines, and propagation contract. |
| `src/GrpcSafety.Analyzers/Configuration/` | Analyzer options, client configuration, and retry contract. |
| `src/GrpcSafety.Analyzers/Protobuf/` | Proto identity and declared-idempotency contract. |
| `tests/GrpcSafety.Analyzers.Tests/` | Classifier and analyzer tests. |

Resolver interfaces are internal extension points. New implementations must
preserve uncertainty and feed the shared classifier. `AdditionalFiles` are passed
through the pipeline but are not parsed yet.

### Planned rules and implementation order

First, extend GRPC001 to track client construction and configuration in the same
compilation. Then implement these rules in order:

| Order | Rule | Intended check |
| --- | --- | --- |
| 1 | GRPC004 | Retry or hedging permits multiple attempts without an overall deadline. |
| 2 | GRPC002 | A call inside an RPC handler neither inherits the parent deadline nor has a finite deadline of its own. |
| 3 | GRPC003 | Retry or hedging permits multiple attempts without declared idempotency in the matching `.proto` method. |

Retry analysis must account for the channel's `MaxRetryAttempts`. Handler analysis
must include nested local functions and lambdas. Proto matching must handle
service/method selectors, accept only `IDEMPOTENT` or `NO_SIDE_EFFECTS`, and keep
ambiguous or unsupported input `Unknown`, including `MethodName.Default`.
Declared idempotency is a contract, not proof of server deduplication.

The classifier selects at most one diagnostic per call. Timing priority is
`GRPC002 > GRPC004 > GRPC001`; GRPC003 is selected only if no timing diagnostic
applies. All four descriptors default to Warning. Retry-related diagnostics
require a confirmed effective attempt count greater than one.

Validation against a corpus of public gRPC examples is still pending. Later work
includes a GRPC002 code fix for `context.Deadline` and a separate server retry
test-helper package.

## License

[MIT](https://github.com/danieloileinik/grpc-safety-analyzers/blob/main/LICENSE).
