using System.Data;
using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Persistence;

public sealed class EfSimulationJobRepository :
    ISimulationJobRepository
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
            claimed_at_utc = CURRENT_TIMESTAMP
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
            job.failure_message;
        """;

    private const string CompleteRunningJobSql =
        """
        UPDATE simulation_jobs
        SET
            status = @completed_status,
            simulation_id = @simulation_id,
            finished_at_utc = CURRENT_TIMESTAMP,
            failure_message = NULL
        WHERE id = @id
          AND status = @running_status
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
            failure_message;
        """;

    private const string FailRunningJobSql =
        """
        UPDATE simulation_jobs
        SET
            status = @failed_status,
            simulation_id = NULL,
            finished_at_utc = CURRENT_TIMESTAMP,
            failure_message = @failure_message
        WHERE id = @id
          AND status = @running_status
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
            failure_message;
        """;

    private readonly SingularFlowDbContext _context;

    public EfSimulationJobRepository(
        SingularFlowDbContext context)
    {
        _context = context;
    }

    public async Task<SimulationJobResult> CreateAsync(
        RunSimulationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        SimulationJobEntity job = new()
        {
            SingularTime = request.SingularTime,
            ConcentrationExponent =
                request.ConcentrationExponent,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            SampleCount = request.SampleCount,
            SamplingMode = request.SamplingMode,
            Status = SimulationJobStatus.Pending,
            ClaimedAtUtc = null
        };

        _context.SimulationJobs.Add(job);

        await _context.SaveChangesAsync(
            cancellationToken);

        return Map(job);
    }

    public async Task<SimulationJobResult?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        SimulationJobEntity? job =
            await _context.SimulationJobs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity => entity.Id == id,
                    cancellationToken);

        return job is null
            ? null
            : Map(job);
    }

    public Task<SimulationJobResult?> TryCompleteAsync(
        Guid jobId,
        Guid simulationId,
        CancellationToken cancellationToken)
    {
        return ExecuteTransitionAsync(
            CompleteRunningJobSql,
            jobId,
            nameof(SimulationJobStatus.Completed),
            simulationId,
            failureMessage: null,
            cancellationToken);
    }

    public Task<SimulationJobResult?> TryFailAsync(
        Guid jobId,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureMessage);

        if (failureMessage.Length >
            SimulationJobResult.MaximumFailureMessageLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureMessage),
                $"Failure message must not exceed " +
                $"{SimulationJobResult.MaximumFailureMessageLength} " +
                "characters.");
        }

        return ExecuteTransitionAsync(
            FailRunningJobSql,
            jobId,
            nameof(SimulationJobStatus.Failed),
            simulationId: null,
            failureMessage: failureMessage,
            cancellationToken: cancellationToken);
    }

    public async Task<SimulationJobResult?> ClaimNextPendingAsync(
        CancellationToken cancellationToken)
    {
        DbConnection connection =
            _context.Database.GetDbConnection();
        IDbContextTransaction? currentTransaction =
            _context.Database.CurrentTransaction;
        bool openedConnection =
            connection.State != ConnectionState.Open;
        Exception? operationException = null;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using DbCommand command =
                connection.CreateCommand();

            command.CommandText = ClaimNextPendingSql;

            if (currentTransaction is not null)
            {
                command.Transaction =
                    currentTransaction.GetDbTransaction();
            }

            AddStringParameter(
                command,
                "pending_status",
                nameof(SimulationJobStatus.Pending));
            AddStringParameter(
                command,
                "running_status",
                nameof(SimulationJobStatus.Running));

            await using DbDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            SimulationJobResult result = Map(reader);

            if (await reader.ReadAsync(cancellationToken))
            {
                throw new DataException(
                    "Claim command returned more than one simulation job.");
            }

            return result;
        }
        catch (Exception exception)
        {
            operationException = exception;
            throw;
        }
        finally
        {
            if (openedConnection &&
                currentTransaction is null &&
                connection.State != ConnectionState.Closed)
            {
                try
                {
                    await connection.CloseAsync();
                }
                catch when (operationException is not null)
                {
                }
            }
        }
    }

    private static SimulationJobResult Map(
        SimulationJobEntity job)
    {
        RunSimulationRequest request = new(
            SingularTime: job.SingularTime,
            ConcentrationExponent:
                job.ConcentrationExponent,
            StartTime: job.StartTime,
            EndTime: job.EndTime,
            SampleCount: job.SampleCount,
            SamplingMode: job.SamplingMode);

        return new SimulationJobResult(
            Id: job.Id,
            Request: request,
            Status: job.Status,
            CreatedAtUtc: job.CreatedAtUtc,
            ClaimedAtUtc: job.ClaimedAtUtc,
            SimulationId: job.SimulationId,
            FinishedAtUtc: job.FinishedAtUtc,
            FailureMessage: job.FailureMessage);
    }

    private static SimulationJobResult Map(
        DbDataReader reader)
    {
        SamplingMode samplingMode = ParseEnum<SamplingMode>(
            reader.GetString(6),
            "sampling_mode");
        SimulationJobStatus status =
            ParseEnum<SimulationJobStatus>(
                reader.GetString(7),
                "status");
        DateTimeOffset? claimedAtUtc = reader.IsDBNull(9)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(9);

        if (claimedAtUtc is null)
        {
            throw new DataException(
                "Claimed simulation job has a null claimed_at_utc value.");
        }

        RunSimulationRequest request = new(
            SingularTime: reader.GetDouble(1),
            ConcentrationExponent: reader.GetDouble(2),
            StartTime: reader.GetDouble(3),
            EndTime: reader.GetDouble(4),
            SampleCount: reader.GetInt32(5),
            SamplingMode: samplingMode);

        return new SimulationJobResult(
            Id: reader.GetGuid(0),
            Request: request,
            Status: status,
            CreatedAtUtc:
                reader.GetFieldValue<DateTimeOffset>(8),
            ClaimedAtUtc: claimedAtUtc,
            SimulationId: reader.IsDBNull(10)
                ? null
                : reader.GetGuid(10),
            FinishedAtUtc: reader.IsDBNull(11)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(11),
            FailureMessage: reader.IsDBNull(12)
                ? null
                : reader.GetString(12));
    }

    private async Task<SimulationJobResult?> ExecuteTransitionAsync(
        string commandText,
        Guid jobId,
        string terminalStatus,
        Guid? simulationId,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        DbConnection connection =
            _context.Database.GetDbConnection();
        IDbContextTransaction? currentTransaction =
            _context.Database.CurrentTransaction;
        bool openedConnection =
            connection.State != ConnectionState.Open;
        Exception? operationException = null;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using DbCommand command =
                connection.CreateCommand();

            command.CommandText = commandText;

            if (currentTransaction is not null)
            {
                command.Transaction =
                    currentTransaction.GetDbTransaction();
            }

            AddParameter(command, "id", DbType.Guid, jobId);
            AddStringParameter(
                command,
                "running_status",
                nameof(SimulationJobStatus.Running));

            if (terminalStatus ==
                nameof(SimulationJobStatus.Completed))
            {
                Guid completedSimulationId =
                    simulationId ??
                    throw new InvalidOperationException(
                        "A completed job requires a simulation ID.");
                AddStringParameter(
                    command,
                    "completed_status",
                    terminalStatus);
                AddParameter(
                    command,
                    "simulation_id",
                    DbType.Guid,
                    completedSimulationId);
            }
            else
            {
                string failedMessage =
                    failureMessage ??
                    throw new InvalidOperationException(
                        "A failed job requires a failure message.");
                AddStringParameter(
                    command,
                    "failed_status",
                    terminalStatus);
                DbParameter failureParameter =
                    AddParameter(
                        command,
                        "failure_message",
                        DbType.String,
                        failedMessage);
                failureParameter.Size =
                    SimulationJobResult.MaximumFailureMessageLength;
            }

            await using DbDataReader reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            SimulationJobResult result = Map(reader);

            if (await reader.ReadAsync(cancellationToken))
            {
                throw new DataException(
                    "Terminal transition returned more than one simulation job.");
            }

            return result;
        }
        catch (Exception exception)
        {
            operationException = exception;
            throw;
        }
        finally
        {
            if (openedConnection &&
                currentTransaction is null &&
                connection.State != ConnectionState.Closed)
            {
                try
                {
                    await connection.CloseAsync();
                }
                catch when (operationException is not null)
                {
                }
            }
        }
    }

    private static DbParameter AddParameter(
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

        return parameter;
    }

    private static void AddStringParameter(
        DbCommand command,
        string name,
        string value)
    {
        DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = DbType.String;
        parameter.Value = value;

        command.Parameters.Add(parameter);
    }

    private static TEnum ParseEnum<TEnum>(
        string value,
        string columnName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse(
                value,
                ignoreCase: false,
                out TEnum result) &&
            Enum.IsDefined(result) &&
            string.Equals(
                Enum.GetName(result),
                value,
                StringComparison.Ordinal))
        {
            return result;
        }

        throw new DataException(
            $"Column '{columnName}' contains invalid " +
            $"{typeof(TEnum).Name} value '{value}'.");
    }
}