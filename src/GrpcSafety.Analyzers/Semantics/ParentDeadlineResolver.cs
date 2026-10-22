using System;
using System.Globalization;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Semantics;

internal sealed class ParentDeadlineResolver(GrpcSymbols symbols, LocalValueResolver locals)
{
    public bool IsInherited(IOperation operation,
        IParameterSymbol context,
        CancellationToken cancellationToken,
        int depth = 0)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > 32) return false;
        operation = locals.Resolve(operation, cancellationToken);

        if (operation is IPropertyReferenceOperation { Property.Name: "Deadline", Instance: { } receiver } property
            && GrpcSymbols.Is(property.Property.ContainingType, symbols.ServerCallContext)
            && locals.Resolve(receiver, cancellationToken) is IParameterReferenceOperation parameter
            && SymbolEqualityComparer.Default.Equals(parameter.Parameter, context)
            && locals.IsStable(context, cancellationToken))
            return true;

        if (operation is IInvocationOperation { Instance: { } instance, Arguments.Length: 1 } invocation
            && invocation.TargetMethod.ContainingType.SpecialType is SpecialType.System_DateTime
            && invocation.Type?.SpecialType is SpecialType.System_DateTime
            && IsInherited(instance, context, cancellationToken, depth + 1))
        {
            var argument = locals.Resolve(invocation.Arguments[0].Value, cancellationToken);
            if (invocation.TargetMethod.Name is "AddTicks" or "AddMilliseconds" or "AddSeconds"
                or "AddMinutes" or "AddHours" or "AddDays" or "AddMonths" or "AddYears")
                return NumericSign(argument) is <= 0;

            var sign = TimeSpanSign(argument, cancellationToken);
            return invocation.TargetMethod.Name switch
            {
                "Add" => sign is <= 0,
                "Subtract" => sign is >= 0,
                _ => false
            };
        }

        if (operation is IBinaryOperation { Type.SpecialType: SpecialType.System_DateTime } binary
            && binary.OperatorMethod?.ContainingType.SpecialType is SpecialType.System_DateTime
            && IsInherited(binary.LeftOperand, context, cancellationToken, depth + 1))
        {
            var sign = TimeSpanSign(binary.RightOperand, cancellationToken);
            return binary.OperatorKind switch
            {
                BinaryOperatorKind.Add => sign is <= 0,
                BinaryOperatorKind.Subtract => sign is >= 0,
                _ => false
            };
        }

        if (operation is not IConditionalOperation
            {
                Condition: IBinaryOperation { IsLifted: false } comparison, WhenFalse: { } whenFalse
            } conditional
            || comparison.OperatorMethod?.ContainingType.SpecialType is not SpecialType.System_DateTime) return false;
        var ascending =
            comparison.OperatorKind is BinaryOperatorKind.LessThan or BinaryOperatorKind.LessThanOrEqual;
        var descending =
            comparison.OperatorKind is BinaryOperatorKind.GreaterThan or BinaryOperatorKind.GreaterThanOrEqual;
        if (!ascending && !descending) return false;
        var minimum = ascending ? comparison.LeftOperand : comparison.RightOperand;
        var maximum = ascending ? comparison.RightOperand : comparison.LeftOperand;
        return SameStableValue(minimum, conditional.WhenTrue, context, cancellationToken)
               && SameStableValue(maximum, whenFalse, context, cancellationToken)
               && (IsInherited(comparison.LeftOperand, context, cancellationToken, depth + 1)
                   || IsInherited(comparison.RightOperand, context, cancellationToken, depth + 1));
    }

    private bool SameStableValue(IOperation first,
        IOperation second,
        IParameterSymbol context,
        CancellationToken cancellationToken)
    {
        first = DeadlineResolver.Unwrap(first)!;
        second = DeadlineResolver.Unwrap(second)!;
        if (first is ILocalReferenceOperation leftLocal && second is ILocalReferenceOperation rightLocal)
            return SymbolEqualityComparer.Default.Equals(leftLocal.Local, rightLocal.Local)
                   && locals.IsStable(leftLocal.Local, cancellationToken);

        if (first is IParameterReferenceOperation leftParameter &&
            second is IParameterReferenceOperation rightParameter)
            return SymbolEqualityComparer.Default.Equals(leftParameter.Parameter, rightParameter.Parameter)
                   && locals.IsStable(leftParameter.Parameter, cancellationToken);

        if (first.ConstantValue.HasValue && second.ConstantValue.HasValue)
            return Equals(first.ConstantValue.Value, second.ConstantValue.Value)
                   && SymbolEqualityComparer.Default.Equals(first.Type, second.Type);

        return IsParentProperty(first, context, cancellationToken) &&
               IsParentProperty(second, context, cancellationToken);
    }

    private bool IsParentProperty(IOperation operation, IParameterSymbol context, CancellationToken cancellationToken)
    {
        return operation is IPropertyReferenceOperation { Property.Name: "Deadline" }
               && IsInherited(operation, context, cancellationToken);
    }

    private int? TimeSpanSign(IOperation operation, CancellationToken cancellationToken)
    {
        operation = locals.Resolve(operation, cancellationToken);
        if (!GrpcSymbols.Is(operation.Type, symbols.TimeSpan)) return null;
        if (operation is IDefaultValueOperation
            or IFieldReferenceOperation { Field.Name: "Zero" })
            return 0;

        if (operation is IInvocationOperation { TargetMethod.IsStatic: true, Arguments.Length: 1 } invocation
            && GrpcSymbols.Is(invocation.TargetMethod.ContainingType, symbols.TimeSpan)
            && invocation.TargetMethod.Name is "FromTicks" or "FromMilliseconds" or "FromSeconds"
                or "FromMinutes" or "FromHours" or "FromDays" or "FromMicroseconds")
            return NumericSign(locals.Resolve(invocation.Arguments[0].Value, cancellationToken));

        if (operation is IObjectCreationOperation { Arguments.Length: 1 } creation)
            return NumericSign(locals.Resolve(creation.Arguments[0].Value, cancellationToken));

        return null;
    }

    private static int? NumericSign(IOperation operation)
    {
        if (operation.ConstantValue is not { HasValue: true, Value: { } value }) return null;
        if (value is not (sbyte or byte or short or ushort or int or uint or long or ulong or float or double
            or decimal))
            return null;
        var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return double.IsNaN(number) || double.IsInfinity(number) ? null : Math.Sign(number);
    }
}
