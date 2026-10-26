# GrpcSafety.Analyzers

A Roslyn analyzer for C# gRPC clients. It reports missing deadlines, lost parent
RPC deadlines, and retry or hedging policies without a time limit or sufficient
evidence of RPC idempotency. It runs during compilation; no server is required.

A **deadline** limits how long a call can take. **Deadline propagation** passes
an incoming RPC's deadline to outgoing calls. A deadline on a retried call limits
the total time for all attempts.

## Rules

| Rule    | Reports when                                                                                                 | Fix                                                   |
|---------|--------------------------------------------------------------------------------------------------------------|-------------------------------------------------------|
| GRPC001 | A call has no deadline and its resolved client setup supplies none.                                          | Set a deadline.                                       |
| GRPC002 | An outgoing call inside an RPC handler neither inherits the parent deadline nor has its own finite deadline. | Pass `context.Deadline` or set a finite deadline.     |
| GRPC003 | Retry or hedging permits multiple attempts and RPC idempotency is not established by its name or contract.   | Confirm and declare idempotency, or disable retries.   |
| GRPC004 | Retry or hedging permits multiple attempts without a deadline for the whole call.                            | Set an overall deadline.                              |

GRPC003 points to `MethodConfig`. The other rules point to calls. Each call gets
at most one warning, in priority order **GRPC002 > GRPC004 > GRPC001**. GRPC003
can appear alongside a call warning.

Deadline rules require confirmed evidence of missing protection. GRPC003 warns
about potential risk when a confirmed retry or hedging policy can repeat an RPC
whose idempotency is not established. Unresolved retry policies produce no retry
warning. **No warning does not prove a call is protected**; analysis can miss
problems.

## Install the preview

Current version: **0.1.0-alpha.4**. These steps build a local package from a
checkout of this repository. Use the .NET 10 SDK selected by `global.json`.
The analyzer uses Roslyn 4.14; .NET 10 is the verified compiler host.

From the repository root:

```sh
dotnet pack src/GrpcSafety.Analyzers/GrpcSafety.Analyzers.csproj \
  --configuration Release --output artifacts/packages
```

In your gRPC client project's `.csproj`, add:

```xml
<ItemGroup>
  <PackageReference Include="GrpcSafety.Analyzers"
                    Version="0.1.0-alpha.4"
                    PrivateAssets="all" />
</ItemGroup>
```

Keep your existing gRPC and generated-client references. Restore and build from
this repository's root, replacing the client project path:

```sh
dotnet restore /path/to/Client.csproj \
  --source "$PWD/artifacts/packages" --source https://api.nuget.org/v3/index.json
dotnet build /path/to/Client.csproj --no-restore
```

## Configure warnings

All rules default to Warning. Add these settings to your `.editorconfig`:

```editorconfig
[*.cs]
dotnet_diagnostic.GRPC001.severity = warning
dotnet_diagnostic.GRPC002.severity = warning
dotnet_diagnostic.GRPC003.severity = warning
dotnet_diagnostic.GRPC004.severity = warning
grpc_safety.strict = false
```

Use `error` to fail the build or `none` to disable a rule. Disabling GRPC002 or
GRPC004 does not reveal a lower-priority warning suppressed at the same call.

### Strict mode

By default, an unresolved client registration produces no deadline warning.
`grpc_safety.strict = true` assumes that such a client has no central deadline
mechanism. A call without an explicit deadline then produces GRPC002 inside an
RPC handler or GRPC001 elsewhere.

Strict mode can warn when protection is configured in another project. It does
not turn unknown deadlines, retry policies, callbacks, or interceptors into
confirmed violations.

## Deadline examples

The examples use generated `Greeter.GreeterClient`, `HelloRequest`, and
`HelloReply` types. Import their namespace, `System`, `Grpc.Core`, and
`Grpc.Net.Client`; handler examples also need `System.Threading.Tasks`.

### GRPC001: no deadline

```csharp
GrpcChannel channel = GrpcChannel.ForAddress("https://localhost");
Greeter.GreeterClient client = new Greeter.GreeterClient(channel);
client.SayHello(new HelloRequest()); // GRPC001
```

Set a deadline when making the call:

```csharp
client.SayHello(new HelloRequest(), deadline: DateTime.UtcNow.AddSeconds(5));
```

This supplies a runtime deadline. The preview treats `DateTime.UtcNow.AddSeconds(5)`
as `Unknown`, so it stops warning but does not verify that value. It can resolve
finite constant-argument `DateTime` constructors and supported expressions based
on a handler's `context.Deadline`.

An omitted deadline, `null`, or `DateTime.MaxValue` counts as no deadline. Empty
`CallOptions` also provide none. Deadlines passed through `CallOptions` and
`.WithDeadline(...)` are supported; `WithDeadline` replaces the previous value.

### GRPC002: missing deadline propagation

This handler makes an outgoing call without carrying over the incoming deadline:

```csharp
public sealed class ForwardingService : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        Greeter.GreeterClient client = new Greeter.GreeterClient(
            GrpcChannel.ForAddress("https://localhost"));
        client.SayHello(request); // GRPC002
        return Task.FromResult(new HelloReply());
    }
}
```

Pass the parent deadline:

```csharp
client.SayHello(request, deadline: context.Deadline);
```

For a constructor-injected client, you can configure deadline propagation with
`Microsoft.Extensions.DependencyInjection`:

```csharp
services.AddGrpcClient<Greeter.GreeterClient>(options =>
    options.Address = new Uri("https://localhost"))
    .EnableCallContextPropagation();
```

GRPC002 checks generated service RPC overrides, including nested lambdas and
local functions. Separate helper methods and other application layers are outside
its scope. A resolvable finite deadline of the call's own also satisfies the rule.

## Retry and hedging examples

Retries repeat failed calls; hedging can run overlapping attempts. Both need a
total time limit and a service contract that permits repetition.

### GRPC003: potentially unsafe retries

GRPC003 first estimates potential idempotency from the RPC name using
case-sensitive prefixes:

| RPC name starts with | Inferred behavior |
|----------------------|-------------------|
| `Get`, `List`, `Find` | Presumed safe to repeat. |
| `Create`, `Delete`, `Increment`, `Charge`, `Append` | Potentially unsafe to repeat. |
| Any other prefix | Idempotency must be established by the `.proto` contract. |

For example, `GetOrder` is presumed safe, while `ChargeCard` and an undeclared
`SayHello` produce a warning. An explicit `idempotency_level` takes precedence
over the name: `IDEMPOTENT` or `NO_SIDE_EFFECTS` prevents GRPC003 even for
`ChargeCard`; `IDEMPOTENCY_UNKNOWN` produces a warning even for `GetOrder`.
These are estimates of possible risk, not proof that an RPC implementation is
safe or faulty.

When repetition may be unsafe or idempotency remains unconfirmed, the default
Warning message is:

```text
GRPC003: Retry may be unsafe because RPC idempotency is not guaranteed.
```

For an explicit service/method selector, name-based analysis works without
`.proto` files. Add the contract to `AdditionalFiles` so declarations can override
the name and service-only selectors can enumerate their RPCs. A `Protobuf` item
used for code generation does not automatically supply it to the analyzer:

```xml
<ItemGroup>
  <AdditionalFiles Include="Protos/**/*.proto" />
</ItemGroup>
```

For example, this contract has no idempotency declaration:

```protobuf
syntax = "proto3";
package example;
message HelloRequest {}
message HelloReply {}
service Greeter {
  rpc SayHello (HelloRequest) returns (HelloReply);
}
```

With this contract and generated client source in the client project, the
following configuration and call produce GRPC003 and GRPC004. Also import
`Grpc.Net.Client.Configuration`:

```csharp
MethodConfig methodConfig = new MethodConfig // GRPC003
{
    Names = { new MethodName { Service = "example.Greeter", Method = "SayHello" } },
    RetryPolicy = new RetryPolicy
    {
        MaxAttempts = 3,
        InitialBackoff = TimeSpan.FromSeconds(1),
        MaxBackoff = TimeSpan.FromSeconds(5),
        BackoffMultiplier = 2,
        RetryableStatusCodes = { StatusCode.Unavailable }
    }
};
GrpcChannel channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
{
    ServiceConfig = new ServiceConfig { MethodConfigs = { methodConfig } }
});
Greeter.GreeterClient client = new Greeter.GreeterClient(channel);
client.SayHello(new HelloRequest()); // GRPC004
```

If the service contract permits repeating the operation, declare it in the RPC
block:

```protobuf
rpc SayHello (HelloRequest) returns (HelloReply) {
  option idempotency_level = IDEMPOTENT;
}
```

`NO_SIDE_EFFECTS` is also accepted. Declare either value only when the service
contract permits repeating the operation. A declaration prevents GRPC003; a
deadline alone does not. The analyzer evaluates names and declarations without
checking server behavior or request deduplication. If repetition is unsafe,
disable retry or hedging.

Selectors use the **proto package and service name**, regardless of
`csharp_namespace`. A service-only selector checks its RPCs, excluding methods
overridden by a more specific configuration. If the service cannot be resolved,
idempotency remains unconfirmed and GRPC003 warns. For an explicit service/method
selector, missing or unresolved contracts fall back to the method name.
`MethodName.Default` is not checked by GRPC003 because the channel's complete
method set is unknown.

### GRPC004: retries without an overall deadline

The call above has no deadline for its combined attempts. Pass a deadline as in
the GRPC001 example, inherit `context.Deadline` in a handler, or use configured
deadline propagation. `HedgingPolicy` is checked the same way as `RetryPolicy`.

Both retry rules account for `MaxRetryAttempts` on the channel: the default cap
is 5, `null` removes the cap, and 1 prevents multiple attempts. With a cap of 1,
a call without a deadline can still produce GRPC001 or GRPC002.

A method-specific configuration overrides a service-wide configuration, which
overrides `MethodName.Default`. A more specific configuration without a confirmed
retry policy does not fall back to a broader retry policy.

GRPC004 does not require `.proto` files. A default policy can apply directly to
a known call. Specific policies require a resolvable RPC service and method name,
as described below.

## Analysis limits

- **Client setup:** supported forms are `GrpcChannel.ForAddress` with a constant
  string address and literal channel options, and one unnamed `AddGrpcClient<T>`
  registration in the current project. `ConfigureChannel` supports literal
  `ServiceConfig`, `MethodConfig`, `RetryPolicy`, and `HedgingPolicy` initializers.
- **Local values and DI:** stable client, channel, deadline, and `CallOptions`
  aliases are supported, along with constructor-injected clients and readonly
  fields assigned from those parameters. A local is stable when it is not
  reassigned or passed by reference. Configuration objects can be inline literals
  or stable literal locals with a single use; mutated, shared, or escaped
  configurations remain `Unknown`.
- **Unresolved setup:** runtime JSON configuration, factories/interceptors/callbacks
  the analyzer cannot resolve, and named or duplicate registrations remain
  `Unknown`, including in strict mode.
- **RPC matching for GRPC004:** specific service/method selectors need generated
  client bodies visible in the current compilation, or a literal
  `Method<TRequest, TResponse>` passed to `CallInvoker`. If the client is supplied
  only as a DLL, specific matching stays `Unknown`. A default policy still works
  when there are no specific selectors that could override it.
- **Proto parsing for GRPC003:** comments, packages, services, unary/streaming RPCs,
  and idempotency options are supported. Unrelated RPC options do not override an
  idempotency declaration. Unsupported or duplicate `idempotency_level` options
  cannot establish idempotency and produce a warning. Unmatched or ambiguous
  contracts, unsupported service options, and malformed or unreadable additional
  proto files prevent declaration resolution. Explicit method selectors then
  fall back to the RPC name; an unknown name still produces a warning.
- **Scope:** generated client calls and the five standard `CallInvoker` methods
  are analyzed. Generated call sites are excluded. `CancellationToken` checks and
  code fixes are not included.

Unknown retry evidence does not suppress a separately confirmed GRPC001 or
GRPC002 violation. Validation against a corpus of public gRPC examples is still
pending.

## Development

Run the test suite from the repository root with the .NET 10 SDK:

```sh
dotnet test GrpcSafety.Analyzers.slnx --configuration Release
```

## License

[MIT](https://github.com/danieloileinik/grpc-safety-analyzers/blob/main/LICENSE).
