using System.Reflection;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Application.Tests.SimulationJobs;

public sealed class SimulationJobRetryContractTests
{
    [Fact]
    public void SimulationJobResult_ExistingConstruction_UsesRetryDefaults()
    {
        SimulationJobResult result = CreateJob();

        Assert.Equal(0, result.AttemptCount);
        Assert.Equal(3, result.MaxAttempts);
        Assert.Null(result.NextAttemptAtUtc);
        Assert.Null(result.LastFailedAtUtc);
        Assert.Null(result.LastFailureMessage);
    }

    [Fact]
    public void SimulationJobResult_WithInitialization_PreservesRetryValues()
    {
        DateTimeOffset nextAttemptAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1);
        DateTimeOffset lastFailedAtUtc = DateTimeOffset.UnixEpoch;

        SimulationJobResult result = CreateJob() with
        {
            AttemptCount = 2,
            MaxAttempts = 5,
            NextAttemptAtUtc = nextAttemptAtUtc,
            LastFailedAtUtc = lastFailedAtUtc,
            LastFailureMessage = "Safe failure."
        };

        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(5, result.MaxAttempts);
        Assert.Equal(nextAttemptAtUtc, result.NextAttemptAtUtc);
        Assert.Equal(lastFailedAtUtc, result.LastFailedAtUtc);
        Assert.Equal("Safe failure.", result.LastFailureMessage);
    }

    [Fact]
    public void DefaultPolicy_HasExpectedConfigurationAndDelays()
    {
        SimulationJobRetryPolicy policy = SimulationJobRetryPolicy.Default;

        Assert.Equal(3, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), policy.BaseDelay);
        Assert.Equal(TimeSpan.FromMinutes(5), policy.MaximumDelay);
        Assert.Equal(TimeSpan.FromSeconds(5), policy.GetDelayAfterAttempt(1));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.GetDelayAfterAttempt(2));
        Assert.Equal(TimeSpan.FromSeconds(20), policy.GetDelayAfterAttempt(3));
    }

    [Theory]
    [InlineData(0, "maxAttempts")]
    [InlineData(11, "maxAttempts")]
    public void Policy_InvalidMaximumAttempts_ThrowsNamedException(
        int maxAttempts,
        string parameterName)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationJobRetryPolicy(
                maxAttempts,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2)));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Policy_InvalidBaseDelay_ThrowsNamedException(long ticks)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationJobRetryPolicy(
                3,
                TimeSpan.FromTicks(ticks),
                TimeSpan.FromSeconds(2)));

        Assert.Equal("baseDelay", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Policy_NonPositiveMaximumDelay_ThrowsNamedException(long ticks)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationJobRetryPolicy(
                3,
                TimeSpan.FromTicks(1),
                TimeSpan.FromTicks(ticks)));

        Assert.Equal("maximumDelay", exception.ParamName);
    }

    [Fact]
    public void Policy_MaximumBelowBase_ThrowsNamedException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationJobRetryPolicy(
                3,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(1)));

        Assert.Equal("maximumDelay", exception.ParamName);
    }

    [Fact]
    public void Policy_MaximumAboveAllowedLimit_ThrowsNamedException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new SimulationJobRetryPolicy(
                3,
                TimeSpan.FromSeconds(1),
                SimulationJobRetryPolicy.MaximumAllowedDelay.Add(TimeSpan.FromTicks(1))));

        Assert.Equal("maximumDelay", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Policy_SupportedAttemptCount_Succeeds(int attemptCount)
    {
        SimulationJobRetryPolicy policy = new(
            3,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1));

        TimeSpan delay = policy.GetDelayAfterAttempt(attemptCount);

        Assert.InRange(delay, policy.BaseDelay, policy.MaximumDelay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Policy_UnsupportedAttemptCount_ThrowsNamedException(int attemptCount)
    {
        SimulationJobRetryPolicy policy = new(
            3,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(1));

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => policy.GetDelayAfterAttempt(attemptCount));

        Assert.Equal("attemptCount", exception.ParamName);
    }

    [Theory]
    [InlineData(30, 24)]
    [InlineData(20, 20)]
    public void Policy_DelayForPersistedAttemptAboveConfiguredMaximum_Succeeds(
        int maximumDelaySeconds,
        int expectedDelaySeconds)
    {
        SimulationJobRetryPolicy policy = new(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromSeconds(3),
            maximumDelay: TimeSpan.FromSeconds(maximumDelaySeconds));

        TimeSpan delay = policy.GetDelayAfterAttempt(4);

        Assert.Equal(TimeSpan.FromSeconds(expectedDelaySeconds), delay);
    }

    [Fact]
    public void Policy_ExponentialDelay_SaturatesWithoutOverflow()
    {
        SimulationJobRetryPolicy policy = new(
            maxAttempts: 10,
            baseDelay: TimeSpan.FromMinutes(2),
            maximumDelay: TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(2), policy.GetDelayAfterAttempt(1));
        Assert.Equal(TimeSpan.FromMinutes(4), policy.GetDelayAfterAttempt(2));
        Assert.Equal(TimeSpan.FromMinutes(5), policy.GetDelayAfterAttempt(3));
        Assert.Equal(TimeSpan.FromMinutes(5), policy.GetDelayAfterAttempt(9));
        Assert.Equal(TimeSpan.FromMinutes(5), policy.GetDelayAfterAttempt(10));
    }

    [Fact]
    public void RetryDecision_CasesPreserveExactJobIdentity()
    {
        SimulationJobResult job = CreateJob();

        SimulationJobRetryDecision.Scheduled scheduled = new(job);
        SimulationJobRetryDecision.Exhausted exhausted = new(job);

        Assert.Same(job, scheduled.Job);
        Assert.Same(job, exhausted.Job);
    }

    [Theory]
    [InlineData(typeof(SimulationJobRetryDecision.Scheduled))]
    [InlineData(typeof(SimulationJobRetryDecision.Exhausted))]
    public void RetryDecision_CasesRejectNullJob(Type decisionType)
    {
        TargetInvocationException invocationException = Assert.Throws<TargetInvocationException>(
            () => Activator.CreateInstance(decisionType, new object?[] { null }));
        ArgumentNullException exception = Assert.IsType<ArgumentNullException>(
            invocationException.InnerException);

        Assert.Equal("job", exception.ParamName);
    }

    [Fact]
    public void RetryDecision_HierarchyIsEffectivelyClosed()
    {
        Type decisionType = typeof(SimulationJobRetryDecision);
        Type[] cases = decisionType.GetNestedTypes(BindingFlags.Public);
        ConstructorInfo[] externallyAccessibleConstructors = decisionType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => constructor.IsPublic ||
                constructor.IsFamily ||
                constructor.IsFamilyOrAssembly)
            .Where(constructor => constructor.GetParameters().Length == 0)
            .ToArray();

        Assert.True(decisionType.IsAbstract);
        Assert.Equal(
            new[] { "Exhausted", "Scheduled" },
            cases.Select(type => type.Name).OrderBy(name => name));
        Assert.All(cases, type => Assert.True(type.IsSealed));
        Assert.Empty(externallyAccessibleConstructors);
    }

    [Fact]
    public void FailureEnums_DefineExactCasesAndDefaultClassification()
    {
        Assert.Equal(
            new[] { "Execution", "Persistence" },
            Enum.GetNames<SimulationJobFailurePhase>());
        Assert.Equal(
            new[] { "NonRetryable", "Retryable" },
            Enum.GetNames<SimulationJobFailureClassification>());
        Assert.Equal(
            SimulationJobFailureClassification.NonRetryable,
            default(SimulationJobFailureClassification));
    }

    [Fact]
    public void FailureClassifier_DefinesExactProviderIndependentMethod()
    {
        MethodInfo method = Assert.Single(
            typeof(ISimulationJobFailureClassifier).GetMethods());

        Assert.Equal("Classify", method.Name);
        Assert.Equal(typeof(SimulationJobFailureClassification), method.ReturnType);
        Assert.Equal(
            new[] { typeof(Exception), typeof(SimulationJobFailurePhase) },
            method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.All(method.GetParameters(), parameter => Assert.False(parameter.IsOptional));
        AssertProviderIndependent(method);
    }

    [Fact]
    public void RetryRepository_DefinesExactFencedNullableMethod()
    {
        MethodInfo method = Assert.Single(
            typeof(ISimulationJobRetryRepository).GetMethods());
        ParameterInfo[] parameters = method.GetParameters();
        NullabilityInfo returnNullability = new NullabilityInfoContext()
            .Create(method.ReturnParameter);

        Assert.Equal("TryRecordRetryableFailureAsync", method.Name);
        Assert.Equal(typeof(Task<SimulationJobRetryDecision>), method.ReturnType);
        Assert.Equal(NullabilityState.Nullable, returnNullability.GenericTypeArguments[0].ReadState);
        Assert.Equal(
            new[]
            {
                typeof(Guid),
                typeof(Guid),
                typeof(string),
                typeof(TimeSpan),
                typeof(CancellationToken)
            },
            parameters.Select(parameter => parameter.ParameterType));
        Assert.Equal(
            new[]
            {
                "jobId",
                "claimToken",
                "safeFailureMessage",
                "retryDelay",
                "cancellationToken"
            },
            parameters.Select(parameter => parameter.Name));
        Assert.All(parameters, parameter => Assert.False(parameter.IsOptional));
        Assert.All(parameters, parameter => Assert.False(parameter.HasDefaultValue));
        AssertProviderIndependent(method);
    }

    private static SimulationJobResult CreateJob()
    {
        RunSimulationRequest request = new(
            SingularTime: 1.0,
            ConcentrationExponent: 0.005,
            StartTime: 0.0,
            EndTime: 0.8,
            SampleCount: 3,
            SamplingMode: SamplingMode.Uniform);

        return new SimulationJobResult(
            Id: Guid.Parse("04f20d94-4c27-4bc5-8111-c8b2e9b42429"),
            Request: request,
            Status: SimulationJobStatus.Pending,
            CreatedAtUtc: DateTimeOffset.UnixEpoch,
            ClaimedAtUtc: null,
            SimulationId: null,
            FinishedAtUtc: null,
            FailureMessage: null);
    }

    private static void AssertProviderIndependent(MethodInfo method)
    {
        IEnumerable<Type> contractTypes = method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType);

        Assert.DoesNotContain(
            contractTypes,
            type => type.Namespace is not null &&
                (type.Namespace.Contains("EntityFrameworkCore", StringComparison.Ordinal) ||
                 type.Namespace.Contains("Npgsql", StringComparison.Ordinal) ||
                 type.Namespace.Contains("AspNetCore", StringComparison.Ordinal) ||
                 type.Namespace.Contains("Infrastructure", StringComparison.Ordinal)));
    }
}