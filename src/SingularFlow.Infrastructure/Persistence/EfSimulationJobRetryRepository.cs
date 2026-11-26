using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

using NpgsqlTypes;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Infrastructure.Persistence;

public sealed class EfSimulationJobRetryRepository :
    ISimulationJobRetryRepository
{
    private const string RecordRetryableFailureSql =
        """
        UPDATE simulation_jobs
        SET
            status = CASE
                WHEN attempt_count < max_attempts
                    THEN @pending_status
                ELSE @failed_status
            END,
            claimed_at_utc = CASE
                WHEN attempt_count < max_attempts
                    THEN NULL
                ELSE claimed_at_utc
            END,
            claim_token = NULL,
            lease_expires_at_utc = NULL,
            next_attempt_at_utc = CASE
                WHEN attempt_count < max_attempts
                    THEN CURRENT_TIMESTAMP + @retry_delay
                ELSE NULL
            END,
            finished_at_utc = CASE
                WHEN attempt_count < max_attempts
                    THEN NULL
                ELSE CURRENT_TIMESTAMP
            END,
            simulation_id = NULL,
            failure_message = CASE
                WHEN attempt_count < max_attempts
                    THEN NULL
                ELSE @safe_failure_message
            END,
            last_failed_at_utc = CURRENT_TIMESTAMP,
            last_failure_message = @safe_failure_message
        WHERE id = @job_id
          AND status = @running_status
          AND claim_token = @claim_token
          AND lease_expires_at_utc > CURRENT_TIMESTAMP
        RETURNING
            id,
            singular_time,
            concentration_exponent,
            start_time,
            end_time,
            sample_count,
            sampling_mode,
            status,
            created_at_utc,
            claimed_at_utc,
            simulation_id,
            finished_at_utc,
            failure_message,
            attempt_count,
            max_attempts,
            next_attempt_at_utc,
            last_failed_at_utc,
            last_failure_message;
        """;

    private readonly SingularFlowDbContext _dbContext;

    public EfSimulationJobRetryRepository(
        SingularFlowDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _dbContext = dbContext;
    }

    public async Task<SimulationJobRetryDecision?> TryRecordRetryableFailureAsync(
        Guid jobId,
        Guid claimToken,
        string safeFailureMessage,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        Validate(safeFailureMessage, retryDelay);

        DbConnection connection = _dbContext.Database.GetDbConnection();
        IDbContextTransaction? transaction =
            _dbContext.Database.CurrentTransaction;
        bool openedConnection = connection.State != ConnectionState.Open;
        DbCommand? command = null;
        DbDataReader? reader = null;
        SimulationJobRetryDecision? result = null;
        ExceptionDispatchInfo? primaryException = null;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync(cancellationToken);
            }

            command = connection.CreateCommand();
            command.CommandText = RecordRetryableFailureSql;
            if (transaction is not null)
            {
                command.Transaction = transaction.GetDbTransaction();
            }

            AddParameter(command, "job_id", NpgsqlDbType.Uuid, jobId);
            AddParameter(command, "claim_token", NpgsqlDbType.Uuid, claimToken);
            AddParameter(
                command,
                "safe_failure_message",
                NpgsqlDbType.Varchar,
                safeFailureMessage,
                SimulationJobResult.MaximumFailureMessageLength);
            AddParameter(command, "retry_delay", NpgsqlDbType.Interval, retryDelay);
            AddParameter(command, "pending_status", NpgsqlDbType.Varchar, nameof(SimulationJobStatus.Pending));
            AddParameter(command, "failed_status", NpgsqlDbType.Varchar, nameof(SimulationJobStatus.Failed));
            AddParameter(command, "running_status", NpgsqlDbType.Varchar, nameof(SimulationJobStatus.Running));

            reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                SimulationJobResult job = Map(reader);
                result = job.Status switch
                {
                    SimulationJobStatus.Pending =>
                        new SimulationJobRetryDecision.Scheduled(job),
                    SimulationJobStatus.Failed =>
                        new SimulationJobRetryDecision.Exhausted(job),
                    _ => throw new DataException(
                        "Retry transition returned an invalid simulation job status.")
                };

                if (await reader.ReadAsync(cancellationToken))
                {
                    throw new DataException(
                        "Retry transition returned more than one simulation job.");
                }
            }
        }
        catch (Exception exception)
        {
            primaryException = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            if (reader is not null)
            {
                try
                {
                    await reader.DisposeAsync();
                }
                catch (Exception exception)
                {
                    primaryException ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            if (command is not null)
            {
                try
                {
                    await command.DisposeAsync();
                }
                catch (Exception exception)
                {
                    primaryException ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            try
            {
                if (openedConnection &&
                    transaction is null &&
                    connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
            }
            catch (Exception exception)
            {
                primaryException ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        primaryException?.Throw();
        return result;
    }

    private static void Validate(
        string safeFailureMessage,
        TimeSpan retryDelay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeFailureMessage);
        if (safeFailureMessage.Length >
            SimulationJobResult.MaximumFailureMessageLength)
        {
            throw new ArgumentOutOfRangeException(nameof(safeFailureMessage));
        }

        if (retryDelay <= TimeSpan.Zero ||
            retryDelay > SimulationJobRetryPolicy.MaximumAllowedDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }
    }

    private static SimulationJobResult Map(DbDataReader reader)
    {
        SamplingMode samplingMode = ParseEnum<SamplingMode>(
            reader.GetString(6),
            "sampling_mode");
        SimulationJobStatus status = ParseEnum<SimulationJobStatus>(
            reader.GetString(7),
            "status");
        RunSimulationRequest request = new(
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.GetInt32(5),
            samplingMode);

        return new SimulationJobResult(
            reader.GetGuid(0),
            request,
            status,
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetGuid(10),
            reader.IsDBNull(11)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12) ? null : reader.GetString(12))
        {
            AttemptCount = reader.GetInt32(13),
            MaxAttempts = reader.GetInt32(14),
            NextAttemptAtUtc = reader.IsDBNull(15)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(15),
            LastFailedAtUtc = reader.IsDBNull(16)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(16),
            LastFailureMessage = reader.IsDBNull(17)
                ? null
                : reader.GetString(17)
        };
    }

    private static void AddParameter(
        DbCommand command,
        string name,
        NpgsqlDbType type,
        object value,
        int? size = null)
    {
        NpgsqlParameter parameter = new(name, type)
        {
            Value = value
        };
        if (size is not null)
        {
            parameter.Size = size.Value;
        }

        command.Parameters.Add(parameter);
    }

    private static TEnum ParseEnum<TEnum>(string value, string columnName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: false, out TEnum result) &&
            Enum.IsDefined(result) &&
            string.Equals(Enum.GetName(result), value, StringComparison.Ordinal))
        {
            return result;
        }

        throw new DataException(
            $"Column '{columnName}' contains invalid {typeof(TEnum).Name} value '{value}'.");
    }
}