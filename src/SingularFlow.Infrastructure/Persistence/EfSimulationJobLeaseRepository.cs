using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;

namespace SingularFlow.Infrastructure.Persistence;

public sealed class EfSimulationJobLeaseRepository :
    ISimulationJobLeaseRepository
{
    private const string ClaimNextPendingSql =
        """
        WITH next_job AS
        (
            SELECT id
            FROM simulation_jobs
            WHERE status = @pending_status
            ORDER BY created_at_utc ASC, id ASC
            FOR UPDATE SKIP LOCKED
            LIMIT 1
        )
        UPDATE simulation_jobs AS job
        SET
            status = @running_status,
            claimed_at_utc = CURRENT_TIMESTAMP,
            claim_token = gen_random_uuid(),
            lease_expires_at_utc = CURRENT_TIMESTAMP + @lease_duration
        FROM next_job
        WHERE job.id = next_job.id
        RETURNING
            job.id,
            job.singular_time,
            job.concentration_exponent,
            job.start_time,
            job.end_time,
            job.sample_count,
            job.sampling_mode,
            job.status,
            job.created_at_utc,
            job.claimed_at_utc,
            job.simulation_id,
            job.finished_at_utc,
            job.failure_message,
            job.claim_token,
            job.lease_expires_at_utc;
        """;

    private const string RenewLeaseSql =
        """
        UPDATE simulation_jobs
        SET lease_expires_at_utc = CURRENT_TIMESTAMP + @lease_duration
        WHERE id = @job_id
          AND status = @running_status
          AND claim_token = @claim_token
          AND lease_expires_at_utc >= CURRENT_TIMESTAMP
        RETURNING lease_expires_at_utc;
        """;

    private const string RecoverExpiredSql =
        """
        WITH expired AS
        (
            SELECT id, lease_expires_at_utc
            FROM simulation_jobs
            WHERE status = @running_status
              AND lease_expires_at_utc < CURRENT_TIMESTAMP
            ORDER BY lease_expires_at_utc ASC, id ASC
            FOR UPDATE SKIP LOCKED
            LIMIT @batch_size
        ),
        recovered AS
        (
            UPDATE simulation_jobs AS job
            SET
                status = @pending_status,
                claimed_at_utc = NULL,
                claim_token = NULL,
                lease_expires_at_utc = NULL
            FROM expired
            WHERE job.id = expired.id
            RETURNING job.id
        )
        SELECT expired.id
        FROM expired
        INNER JOIN recovered ON recovered.id = expired.id
        ORDER BY expired.lease_expires_at_utc ASC, expired.id ASC;
        """;

    private readonly SingularFlowDbContext _context;

    public EfSimulationJobLeaseRepository(
        SingularFlowDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public Task<SimulationJobClaim?> ClaimNextPendingAsync(
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ValidateLeaseDuration(leaseDuration);

        return ExecuteReaderAsync<SimulationJobClaim?>(
            ClaimNextPendingSql,
            command =>
            {
                AddStringParameter(command, "pending_status", nameof(SimulationJobStatus.Pending));
                AddStringParameter(command, "running_status", nameof(SimulationJobStatus.Running));
                AddIntervalParameter(command, "lease_duration", leaseDuration);
            },
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }

                SimulationJobClaim result = MapClaim(reader);
                if (await reader.ReadAsync(cancellationToken))
                {
                    throw new DataException(
                        "Lease claim returned more than one simulation job.");
                }

                return result;
            },
            cancellationToken);
    }

    public Task<DateTimeOffset?> TryRenewLeaseAsync(
        Guid jobId,
        Guid claimToken,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ValidateLeaseDuration(leaseDuration);

        return ExecuteReaderAsync<DateTimeOffset?>(
            RenewLeaseSql,
            command =>
            {
                AddParameter(command, "job_id", DbType.Guid, jobId);
                AddStringParameter(command, "running_status", nameof(SimulationJobStatus.Running));
                AddParameter(command, "claim_token", DbType.Guid, claimToken);
                AddIntervalParameter(command, "lease_duration", leaseDuration);
            },
            async reader =>
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }

                DateTimeOffset result = reader.GetFieldValue<DateTimeOffset>(0);
                if (await reader.ReadAsync(cancellationToken))
                {
                    throw new DataException(
                        "Lease renewal returned more than one simulation job.");
                }

                return result;
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> RecoverExpiredAsync(
        int batchSize,
        CancellationToken cancellationToken)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        return ExecuteReaderAsync<IReadOnlyList<Guid>>(
            RecoverExpiredSql,
            command =>
            {
                AddStringParameter(command, "running_status", nameof(SimulationJobStatus.Running));
                AddStringParameter(command, "pending_status", nameof(SimulationJobStatus.Pending));
                AddParameter(command, "batch_size", DbType.Int32, batchSize);
            },
            async reader =>
            {
                List<Guid> recovered = [];
                while (await reader.ReadAsync(cancellationToken))
                {
                    recovered.Add(reader.GetGuid(0));
                }

                return recovered;
            },
            cancellationToken);
    }

    private async Task<TResult> ExecuteReaderAsync<TResult>(
        string commandText,
        Action<DbCommand> configure,
        Func<DbDataReader, Task<TResult>> read,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _context.Database.GetDbConnection();
        IDbContextTransaction? transaction = _context.Database.CurrentTransaction;
        bool openedConnection = connection.State != ConnectionState.Open;
        DbCommand? command = null;
        DbDataReader? reader = null;
        TResult? result = default;
        ExceptionDispatchInfo? primaryException = null;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync(cancellationToken);
            }

            command = connection.CreateCommand();
            command.CommandText = commandText;
            if (transaction is not null)
            {
                command.Transaction = transaction.GetDbTransaction();
            }

            configure(command);
            reader = await command.ExecuteReaderAsync(cancellationToken);
            result = await read(reader);
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
        return result!;
    }

    private static SimulationJobClaim MapClaim(DbDataReader reader)
    {
        SamplingMode samplingMode = ParseEnum<SamplingMode>(reader.GetString(6), "sampling_mode");
        SimulationJobStatus status = ParseEnum<SimulationJobStatus>(reader.GetString(7), "status");
        DateTimeOffset? claimedAtUtc = reader.IsDBNull(9)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(9);
        Guid claimToken = reader.IsDBNull(13) ? Guid.Empty : reader.GetGuid(13);
        DateTimeOffset? leaseExpiresAtUtc = reader.IsDBNull(14)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(14);
        Guid? simulationId = reader.IsDBNull(10) ? null : reader.GetGuid(10);
        DateTimeOffset? finishedAtUtc = reader.IsDBNull(11)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(11);
        string? failureMessage = reader.IsDBNull(12) ? null : reader.GetString(12);

        if (status != SimulationJobStatus.Running ||
            claimedAtUtc is null ||
            claimToken == Guid.Empty ||
            leaseExpiresAtUtc is null ||
            leaseExpiresAtUtc <= claimedAtUtc ||
            simulationId is not null ||
            finishedAtUtc is not null ||
            failureMessage is not null)
        {
            throw new DataException(
                "Claimed simulation job violates lease invariants.");
        }

        RunSimulationRequest request = new(
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.GetDouble(3),
            reader.GetDouble(4),
            reader.GetInt32(5),
            samplingMode);
        SimulationJobResult job = new(
            reader.GetGuid(0),
            request,
            status,
            reader.GetFieldValue<DateTimeOffset>(8),
            claimedAtUtc,
            simulationId,
            finishedAtUtc,
            failureMessage);

        return new SimulationJobClaim(job, claimToken, leaseExpiresAtUtc.Value);
    }

    private static void ValidateLeaseDuration(TimeSpan leaseDuration)
    {
        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }
    }

    private static void AddIntervalParameter(
        DbCommand command,
        string name,
        TimeSpan value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void AddStringParameter(DbCommand command, string name, string value) =>
        AddParameter(command, name, DbType.String, value);

    private static void AddParameter(
        DbCommand command,
        string name,
        DbType type,
        object value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value;
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