using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Configuration;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class DiagnosticPriorityTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeadlinePropagationViolationWinsOverDeadlineAndRetry(bool retries)
    {
        // Arrange
        var retry = new RetrySafety(ProtectionState.Unprotected, retries);

        // Act
        var classification = CallSafetyClassifier.Classify(
            ProtectionState.Unprotected,
            ClientConfiguration.WithoutCentralDeadline,
            ProtectionState.Unprotected,
            retry,
            default);

        // Assert
        Assert.Equal(DiagnosticIds.MissingDeadlinePropagation, classification.Diagnostic?.Id);
    }

    [Fact]
    public void RetryWithoutDeadlineWinsOverMissingDeadline()
    {
        // Arrange
        var retry = new RetrySafety(ProtectionState.Unknown, true);

        // Act
        var classification = CallSafetyClassifier.Classify(
            ProtectionState.Unprotected,
            ClientConfiguration.WithoutCentralDeadline,
            ProtectionState.Unknown,
            retry,
            default);

        // Assert
        Assert.Equal(DiagnosticIds.RetryWithoutDeadline, classification.Diagnostic?.Id);
    }

    [Fact]
    public void RetryViolationRequiresConfirmedMultipleAttempts()
    {
        // Arrange
        var retry = new RetrySafety(ProtectionState.Unprotected, false);

        // Act
        var classification = CallSafetyClassifier.Classify(
            ProtectionState.Protected,
            ClientConfiguration.Unresolved,
            ProtectionState.Unknown,
            retry,
            default);

        // Assert
        Assert.Null(classification.Diagnostic);
    }

    [Fact]
    public void UnknownDeadlineDoesNotProveRetryWithoutOverallDeadline()
    {
        // Arrange
        var retry = new RetrySafety(ProtectionState.Unknown, true);

        // Act
        var classification = CallSafetyClassifier.Classify(
            ProtectionState.Unknown,
            ClientConfiguration.WithoutCentralDeadline,
            ProtectionState.Unknown,
            retry,
            default);

        // Assert
        Assert.Null(classification.Diagnostic);
    }

    [Fact]
    public void ConfirmedRetryAndMissingIdempotencyCanBeClassified()
    {
        // Arrange
        var retry = new RetrySafety(ProtectionState.Unprotected, true);

        // Act
        var classification = CallSafetyClassifier.Classify(
            ProtectionState.Protected,
            ClientConfiguration.Unresolved,
            ProtectionState.Unknown,
            retry,
            default);

        // Assert
        Assert.Equal(DiagnosticIds.UndeclaredIdempotency, classification.Diagnostic?.Id);
    }

    [Fact]
    public void FiniteDeadlineSatisfiesDeadlinePropagationRequirement()
    {
        // Arrange
        var configuration = ClientConfiguration.Unresolved;

        // Act
        var classification = CallSafetyClassifier.Classify(
            ProtectionState.Protected,
            configuration,
            ProtectionState.Unprotected,
            RetrySafety.Unknown,
            default);

        // Assert
        Assert.Equal(ProtectionState.Protected, classification.DeadlinePropagation);
        Assert.Null(classification.Diagnostic);
    }
}
