using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Configuration;

internal sealed class ChannelConfigurationReader(GrpcSymbols symbols)
{
    private readonly LiteralConfigurationReader _literals = new(symbols);
    private int? _maxRetryAttempts = 5;
    private ImmutableArray<MethodRetryConfiguration> _methods = [];

    public ChannelRetryConfiguration Configuration => new(_methods, _maxRetryAttempts);

    public bool ReadOptions(IOperation operation, CancellationToken cancellationToken)
    {
        if (IsNull(operation)) return true;
        var options = _literals.Read(operation, "Grpc.Net.Client.GrpcChannelOptions", cancellationToken);
        return options is not null && options.Collections.IsEmpty
                                   && options.Properties.All(property => Apply(
                                       property.Key,
                                       property.Value,
                                       cancellationToken));
    }

    public bool ReadCallback(IInvocationOperation invocation, CancellationToken cancellationToken)
    {
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter?.Type.TypeKind is not TypeKind.Delegate) continue;
            var operation = DeadlineResolver.Unwrap(argument.Value)!;
            if (operation is IDelegateCreationOperation delegateCreation) operation = delegateCreation.Target;
            if (operation is not IAnonymousFunctionOperation lambda) return false;
            foreach (var statement in lambda.Body.Operations.Where(statement =>
                         statement is not IReturnOperation { ReturnedValue: null }))
                if (statement is not IExpressionStatementOperation
                    {
                        Operation: ISimpleAssignmentOperation
                        {
                            Target: IPropertyReferenceOperation property
                        } assignment
                    }
                    || !GrpcSymbols.Is(property.Property.ContainingType, symbols.ChannelOptions)
                    || property.Instance is not IParameterReferenceOperation parameter
                    || !lambda.Symbol.Parameters.Contains(parameter.Parameter, SymbolEqualityComparer.Default)
                    || !Apply(property.Property.Name, assignment.Value, cancellationToken))
                    return false;
        }

        return true;
    }

    private bool Apply(string property, IOperation value, CancellationToken cancellationToken)
    {
        if (property == "MaxRetryAttempts")
        {
            if (IsNull(value))
            {
                _maxRetryAttempts = null;
                return true;
            }

            if (Constant(value) is not int attempts || attempts < 1) return false;
            _maxRetryAttempts = attempts;
            return true;
        }

        if (property != "ServiceConfig")
            return property is "MaxReceiveMessageSize" or "MaxSendMessageSize" or "MaxRetryBufferSize"
                       or "MaxRetryBufferPerCallSize" or "DisposeHttpClient" or "ThrowOperationCanceledOnCancellation"
                   && DeadlineResolver.Unwrap(value)!.ConstantValue.HasValue;
        if (IsNull(value))
        {
            _methods = [];
            return true;
        }

        var methods = ReadServiceConfig(value, cancellationToken);
        if (methods is null) return false;
        _methods = methods.Value;
        return true;
    }

    private ImmutableArray<MethodRetryConfiguration>? ReadServiceConfig(IOperation operation,
        CancellationToken cancellationToken)
    {
        var service = _literals.Read(operation, "Grpc.Net.Client.Configuration.ServiceConfig", cancellationToken);
        if (service is null || !service.Properties.IsEmpty ||
            service.Collections.Keys.Any(key => key != "MethodConfigs")) return null;
        var methods = ImmutableArray.CreateBuilder<MethodRetryConfiguration>();
        var selectors = new HashSet<(string?, string?)>();
        foreach (var method in Collection(service, "MethodConfigs")
                     .Select(item => _literals.Read(
                         item,
                         "Grpc.Net.Client.Configuration.MethodConfig",
                         cancellationToken)))
        {
            if (method is null || method.Properties.Keys.Any(key => key is not ("RetryPolicy" or "HedgingPolicy"))
                               || method.Collections.Keys.Any(key => key != "Names")) return null;
            var names = ImmutableArray.CreateBuilder<MethodNamePattern>();
            foreach (var pattern in Collection(method, "Names").Select(name => ReadName(name, cancellationToken)))
            {
                if (pattern is null || !selectors.Add((pattern.Value.Service, pattern.Value.Method))) return null;
                names.Add(pattern.Value);
            }

            var retry = Property(method, "RetryPolicy");
            var hedging = Property(method, "HedgingPolicy");
            var hasRetry = retry is not null && !IsNull(retry);
            var hasHedging = hedging is not null && !IsNull(hedging);
            var attempts = hasRetry && hasHedging ? null
                : hasRetry ? ReadAttempts(retry!, false, cancellationToken)
                : hasHedging ? ReadAttempts(hedging!, true, cancellationToken) : null;
            methods.Add(
                new MethodRetryConfiguration(method.Creation.Syntax.GetLocation(), names.ToImmutable(), attempts));
        }

        return methods.ToImmutable();
    }

    private MethodNamePattern? ReadName(IOperation operation, CancellationToken cancellationToken)
    {
        operation = DeadlineResolver.Unwrap(operation)!;
        if (operation is IFieldReferenceOperation { Field.Name: "Default" } field
            && field.Field.ContainingType.ToDisplayString() == "Grpc.Net.Client.Configuration.MethodName")
            return new MethodNamePattern(null, null);
        var name = _literals.Read(operation, "Grpc.Net.Client.Configuration.MethodName", cancellationToken);
        if (name is null || !name.Collections.IsEmpty ||
            name.Properties.Keys.Any(key => key is not ("Service" or "Method"))) return null;
        string? service = null;
        string? method = null;
        if ((Property(name, "Service") is { } serviceValue && !ReadString(serviceValue, out service))
            || (Property(name, "Method") is { } methodValue && !ReadString(methodValue, out method))) return null;
        if (service is "" || method is "" || (method is not null && service is null)) return null;
        return new MethodNamePattern(service, method);
    }

    private int? ReadAttempts(IOperation operation, bool hedging, CancellationToken cancellationToken)
    {
        var policy = _literals.Read(
            operation,
            "Grpc.Net.Client.Configuration." + (hedging ? "HedgingPolicy" : "RetryPolicy"),
            cancellationToken);
        if (policy is null || Constant(Property(policy, "MaxAttempts")) is not int attempts || attempts < 2)
            return null;
        if (hedging)
        {
            if (policy.Properties.Keys.Any(key => key is not ("MaxAttempts" or "HedgingDelay"))
                || policy.Collections.Keys.Any(key => key != "NonFatalStatusCodes")
                || (Property(policy, "HedgingDelay") is { } delay && !IsNull(delay) && Duration(delay) is not >= 0)
                || !ValidStatusCodes(Collection(policy, "NonFatalStatusCodes"))) return null;
        }
        else if (policy.Properties.Keys.Any(key =>
                     key is not ("MaxAttempts" or "InitialBackoff" or "MaxBackoff" or "BackoffMultiplier"))
                 || policy.Collections.Keys.Any(key => key != "RetryableStatusCodes")
                 || Duration(Property(policy, "InitialBackoff")) is not > 0
                 || Duration(Property(policy, "MaxBackoff")) is not > 0
                 || Number(Property(policy, "BackoffMultiplier")) is not > 0
                 || Collection(policy, "RetryableStatusCodes").IsEmpty
                 || !ValidStatusCodes(Collection(policy, "RetryableStatusCodes")))
        {
            return null;
        }

        return attempts;
    }

    private double? Duration(IOperation? operation)
    {
        operation = DeadlineResolver.Unwrap(operation);
        if (operation is IFieldReferenceOperation { Field.Name: "Zero" } field
            && GrpcSymbols.Is(field.Field.ContainingType, symbols.TimeSpan)) return 0;
        if (operation is IInvocationOperation invocation && GrpcSymbols.Is(
                                                             invocation.TargetMethod.ContainingType,
                                                             symbols.TimeSpan)
                                                         && invocation.TargetMethod.IsStatic &&
                                                         invocation.Arguments.Length == 1
                                                         && invocation.TargetMethod.Name is "FromDays" or "FromHours"
                                                             or "FromMinutes" or "FromSeconds"
                                                             or "FromMilliseconds" or "FromMicroseconds" or "FromTicks")
        {
            var value = Number(invocation.Arguments[0].Value);
            double factor = invocation.TargetMethod.Name switch
            {
                "FromDays" => TimeSpan.TicksPerDay, "FromHours" => TimeSpan.TicksPerHour,
                "FromMinutes" => TimeSpan.TicksPerMinute, "FromSeconds" => TimeSpan.TicksPerSecond,
                "FromMilliseconds" => TimeSpan.TicksPerMillisecond, "FromMicroseconds" => 10, _ => 1
            };
            var ticks = value * factor;
            return ticks is >= long.MinValue and < long.MaxValue ? Math.Truncate(ticks.Value) : null;
        }

        if (operation is IObjectCreationOperation { Arguments.Length: 1, Initializer: null } creation
            && GrpcSymbols.Is(creation.Type, symbols.TimeSpan) &&
            Constant(creation.Arguments[0].Value) is long ticksValue)
            return ticksValue;
        return null;
    }

    private static bool ValidStatusCodes(ImmutableArray<IOperation> codes)
    {
        return codes.All(code => Constant(code) is int and >= 0 and <= 16);
    }

    private static bool ReadString(IOperation operation, out string? value)
    {
        operation = DeadlineResolver.Unwrap(operation)!;
        value = operation.ConstantValue.HasValue ? operation.ConstantValue.Value as string : null;
        return operation.ConstantValue is { HasValue: true, Value: null or string };
    }

    private static bool IsNull(IOperation operation)
    {
        return DeadlineResolver.Unwrap(operation)!.ConstantValue is { HasValue: true, Value: null };
    }

    private static object? Constant(IOperation? operation)
    {
        return DeadlineResolver.Unwrap(operation)?.ConstantValue is { HasValue: true } constant ? constant.Value : null;
    }

    private static double? Number(IOperation? operation)
    {
        return Constant(operation) switch
        {
            int value => value, long value => value,
            float value when !float.IsInfinity(value) && !float.IsNaN(value) => value,
            double value when !double.IsInfinity(value) && !double.IsNaN(value) => value, _ => null
        };
    }

    private static IOperation? Property(LiteralConfiguration configuration, string name)
    {
        return configuration.Properties.TryGetValue(name, out var value) ? value : null;
    }

    private static ImmutableArray<IOperation> Collection(LiteralConfiguration configuration, string name)
    {
        return configuration.Collections.TryGetValue(name, out var values) ? values : [];
    }
}
