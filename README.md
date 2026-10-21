# GrpcSafety.Analyzers

A Roslyn analyzer for confirmed deadline and retry problems in .NET gRPC clients.
The default behavior is conservative: an unresolved value or configuration stays
`Unknown`. Missing a problem is preferable to reporting an unproven violation.

**Status: an initial scaffold with a working subset of GRPC001.** The other rules
have descriptors, classifier behavior, tests, and resolver contracts. Their
resolvers are not implemented, so they do not produce diagnostics in the shipped
analyzer yet.

## Rules

All descriptors default to **Warning**. Severity is configurable in `.editorconfig`.

| ID | Intended check | Current implementation |
| --- | --- | --- |
| GRPC001 | A call has no deadline and no central deadline mechanism. | Works for inline client/channel construction and supported deadline expressions. |
| GRPC002 | A call inside an RPC handler neither inherits the parent deadline nor supplies a finite deadline. | Classifier and propagation resolver contract only. |
| GRPC003 | Retry or hedging permits multiple attempts without declared idempotency in the matching `.proto` method. | Classifier and retry/proto resolver contracts only. |
| GRPC004 | Retry or hedging permits multiple attempts without an overall deadline. | Classifier and retry resolver contract only. |

The classifier selects at most one diagnostic per call. Timing violations follow
`GRPC002 > GRPC004 > GRPC001`. The current classifier selects GRPC003 only when no
timing diagnostic applies.

## Build and test

Use the .NET 10 SDK selected by [global.json](global.json). Run from the repository
root:

```sh
dotnet restore GrpcSafety.Analyzers.slnx
dotnet build GrpcSafety.Analyzers.slnx --configuration Release --no-restore
dotnet test GrpcSafety.Analyzers.slnx --configuration Release --no-build --no-restore
dotnet pack src/GrpcSafety.Analyzers/GrpcSafety.Analyzers.csproj \
  --configuration Release --no-build --no-restore --output artifacts/packages
```

The first restore and test run require NuGet access. The test framework downloads
reference assemblies as well as the project dependencies.

The analyzer targets `netstandard2.0` and uses Roslyn 4.14 APIs. The tests target
`net10.0`; the .NET 10 compiler host is the currently verified environment.

## Use the local package

The pack command creates
`artifacts/packages/GrpcSafety.Analyzers.0.1.0-alpha.1.nupkg`. This workflow does not
require a published NuGet release.

Add this reference to the consuming project:

```xml
<ItemGroup>
  <PackageReference Include="GrpcSafety.Analyzers"
                    Version="0.1.0-alpha.1"
                    PrivateAssets="all" />
</ItemGroup>
```

Then restore with the local feed and NuGet.org. Replace `path/to/Client.csproj`
with the consuming project path; run these commands from this repository root:

```sh
dotnet restore path/to/Client.csproj \
  --source "$PWD/artifacts/packages" --source https://api.nuget.org/v3/index.json
dotnet build path/to/Client.csproj --no-restore
```

The package places its assembly under `analyzers/dotnet/cs`. It has no application
runtime assembly or gRPC package dependencies of its own.

## Examples

These snippets assume a generated `Greeter.GreeterClient`, `HelloRequest`, and the
usual `System`, `Grpc.Core`, and `Grpc.Net.Client` imports. Construction stays
inline to make configuration visible to the current resolver.

### Reported: no deadline

```csharp
new Greeter.GreeterClient(GrpcChannel.ForAddress("https://localhost"))
    .SayHello(new HelloRequest()); // GRPC001
```

An omitted deadline, `null`, `default(DateTime?)`, or `DateTime.MaxValue` is treated
as an absent deadline. The same applies to empty `CallOptions`:

```csharp
new Greeter.GreeterClient(GrpcChannel.ForAddress("https://localhost"))
    .SayHello(new HelloRequest(), options: new CallOptions()); // GRPC001
```

### Not reported: a known finite deadline

```csharp
new Greeter.GreeterClient(GrpcChannel.ForAddress("https://localhost"))
    .SayHello(
        new HelloRequest(),
        deadline: new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
```

The fixed date illustrates a statically resolvable expression. A supported finite
value can also be passed through `new CallOptions(deadline: ...)` or
`new CallOptions().WithDeadline(...)`. The analyzer checks whether the deadline is
finite; it does not judge whether the chosen time is appropriate or still in the
future.

### Not reported by default: client configuration is unresolved

```csharp
void Send(Greeter.GreeterClient client)
{
    client.SayHello(new HelloRequest()); // Unknown; no diagnostic by default.
}
```

The same limitation applies to a client stored in a local variable or field, even
when its construction is visible nearby. Following those assignments requires
provenance analysis that is not implemented yet.

### Not reported: the deadline expression is unresolved

```csharp
new Greeter.GreeterClient(GrpcChannel.ForAddress("https://localhost"))
    .SayHello(new HelloRequest(), deadline: DateTime.UtcNow.AddSeconds(5));
```

The current resolver leaves this expression `Unknown`. No warning means that a
violation was not proven; it does not mean the analyzer proved the call safe.

## Configuration

```editorconfig
root = true

[*.cs]
dotnet_diagnostic.GRPC001.severity = warning
dotnet_diagnostic.GRPC002.severity = warning
dotnet_diagnostic.GRPC003.severity = warning
dotnet_diagnostic.GRPC004.severity = warning

grpc_safety.strict = false
```

Set a rule's severity to `error` to fail the build, or `none` to disable it.
Severity settings for GRPC002–004 apply once their resolvers are implemented.

### Strict mode

`grpc_safety.strict = true` treats an unresolved client registration as having no
central deadline mechanism. The unresolved-client example above then reports
GRPC001 if the call also has no explicit deadline.

Strict mode changes only that registration assumption:

| Evidence | Default mode | Strict mode |
| --- | --- | --- |
| Client comes from a local, field, or parameter; deadline is absent. | `Unknown`; no diagnostic. | GRPC001. |
| Explicit deadline is a supported finite value. | `Protected`. | `Protected`. |
| Explicit deadline is an unresolved expression. | `Unknown`. | `Unknown`. |
| Inline construction uses opaque channel options or a custom invoker; deadline is absent. | `Unknown`. | `Unknown`. |

Default mode has lower recall because unresolved registrations suppress warnings.
Strict mode can warn for a client whose protection is configured elsewhere. A
missing or invalid strict option is treated as `false`.

## Architecture

There is one analyzer entry point and one classification pipeline:

1. **Recognize the call.** `GrpcCallRecognizer` uses Roslyn symbols to identify
   generated client methods and the supported `CallInvoker` call methods.
2. **Collect evidence.** `DeadlineResolver` inspects arguments;
   `IClientConfigurationResolver` resolves the client's central protection.
   Propagation, retry, and proto contracts have separate resolver interfaces.
3. **Classify.** `CallSafetyClassifier` combines evidence, applies strict mode,
   and chooses a diagnostic. Rule priority lives here.
4. **Report.** `GrpcSafetyAnalyzer` reports the selected diagnostic at the call.

Each aspect uses the same states:

| State | Meaning |
| --- | --- |
| `Protected` | The relevant protection is confirmed. |
| `Unprotected` | The relevant violation is confirmed, subject to the strict registration assumption. |
| `Unknown` | Available evidence cannot establish either result. |

Default-initialized evidence is `Unknown`. Missing propagation and retry resolver
implementations also return `Unknown` through the pipeline. For retry safety, the
classifier separately requires a confirmed effective attempt count greater than
one before considering retry-related diagnostics.

| Location | Responsibility |
| --- | --- |
| `src/GrpcSafety.Analyzers/GrpcSafetyAnalyzer.cs` | Roslyn registration and diagnostic reporting. |
| `src/GrpcSafety.Analyzers/Analysis/` | Evidence states, pipeline, and shared classifier. |
| `src/GrpcSafety.Analyzers/Semantics/` | Symbol matching, call recognition, deadline resolution, and propagation contract. |
| `src/GrpcSafety.Analyzers/Configuration/` | Analyzer options, client configuration, and retry resolver contract. |
| `src/GrpcSafety.Analyzers/Protobuf/` | Proto method identity and declared-idempotency resolver contract. |
| `tests/GrpcSafety.Analyzers.Tests/` | Classifier and analyzer tests using a real generated gRPC client. |

The extension interfaces are internal implementation boundaries. Adding a new
resolver should preserve uncertainty and feed the shared classifier.

## Current limits

- Client configuration is resolved only for an inline generated constructor using
  `GrpcChannel.ForAddress` with a constant string address and omitted, null, or
  empty literal channel options. Direct `CreateCallInvoker()` chains are also
  supported. Nonempty channel options remain `Unknown`.
- Local/field assignment tracking, DI registration indexing, `AddGrpcClient`,
  `ConfigureChannel`, interceptors, and `EnableCallContextPropagation` are pending.
- Deadline resolution covers supported constant-argument `DateTime` constructors,
  sentinel/default values, and supported `CallOptions` expressions. Unsupported
  expressions remain `Unknown`. An opaque propagation token keeps an otherwise
  absent deadline `Unknown`.
- RPC handler detection, parent-deadline inheritance, and stricter derived
  deadlines are pending. The intended GRPC002 scope includes local functions and
  lambdas inside a handler; calls in other layers are outside that rule's scope.
  GRPC001 has no handler-only restriction.
- Retry/hedging policy resolution, effective attempt limits, and `.proto` parsing
  are pending. `AdditionalFiles` are passed through the pipeline but not parsed.
- Runtime configuration, JSON, opaque factories, and configuration in another
  compilation cannot establish protection in default mode.
- Generated call sites are excluded from analysis. A generated client can still
  be recognized when called from ordinary source code.
- `CancellationToken` checking is outside this project's scope; it is delegated
  to MA0040. Declared idempotency is a contract, not proof of server behavior or
  deduplication. No code fixes or server test-helper package exist yet.

## Tests and next steps

The suite uses `Microsoft.CodeAnalysis.Testing` and xUnit. Its 66 cases cover
supported GRPC001 paths, conservative and unknown cases, strict mode, severity,
and classifier priorities. Negative and unknown cases outnumber reported cases.
GRPC002–004 tests exercise classifier facts directly; they are not end-to-end
coverage of those rules.

The implementation order is:

1. Extend GRPC001 with client provenance and configuration visible in the same
   compilation, including supported central deadline mechanisms.
2. Implement GRPC004 with literal service/method/retry/hedging configuration and
   effective attempts capped by the channel's `MaxRetryAttempts`.
3. Implement GRPC002 for generated service-base handlers with `ServerCallContext`,
   including nested local functions and lambdas.
4. Implement the minimal `.proto` parser and GRPC003. Match service/method names;
   a service-only selector covers its methods, while `MethodName.Default` remains
   `Unknown`. Only `IDEMPOTENT` and `NO_SIDE_EFFECTS` satisfy the declared contract.
   Missing, ambiguous, or unsupported proto input must remain `Unknown`.
5. Run the analyzer against a corpus of public gRPC examples and review unexpected
   diagnostics. This corpus validation has not been performed yet.

After the MVP: a GRPC002 code fix that inserts `context.Deadline`, and a separate
server retry test-helper package.

## License

[MIT](LICENSE).
