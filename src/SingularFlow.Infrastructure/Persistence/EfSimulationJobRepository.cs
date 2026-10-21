using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using SingularFlow.Application.SimulationJobs;
using SingularFlow.Application.Simulations;
using SingularFlow.Infrastructure.Persistence.Entities;

namespace SingularFlow.Infrastructure.Persistence;

public sealed class EfSimulationJobRepository :
    ISimulationJobRepository
{
    private const string CompleteLeasedRunningJobSql =
        """
        UPDATE simulation_jobs
        SET
            status = @completed_status,
            simulation_id = @simulation_id,
            finished_at_utc = CURRENT_TIMESTAMP,
            failure_message = NULL,
            claim_token = NULL,
            lease_expires_at_utc = NULL
        WHERE id = @id
          AND status = @running_status
          AND claim_token = @claim_token
          AND lease_expires_at_utc >= CURRENT_TIMESTAMP
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

    private const string FailLeasedRunningJobSql =
        """
        UPDATE simulation_jobs
        SET
            status = @failed_status,
            simulation_id = NULL,
            finished_at_utc = CURRENT_TIMESTAMP,
            failure_message = @failure_message,
            claim_token = NULL,
            lease_expires_at_utc = NULL
        WHERE id = @id
          AND status = @running_status
          AND claim_token = @claim_token
          AND lease_expires_at_utc >= CURRENT_TIMESTAMP
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
        Guid claimToken,
        Guid simulationId,
        CancellationToken cancellationToken)
    {
        return ExecuteTransitionAsync(
            CompleteLeasedRunningJobSql,
            jobId,
            nameof(SimulationJobStatus.Completed),
            simulationId,
            failureMessage: null,
            claimToken,
            cancellationToken);
    }

    public Task<SimulationJobResult?> TryFailAsync(
        Guid jobId,
        Guid claimToken,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        ValidateFailureMessage(failureMessage);

        return ExecuteTransitionAsync(
            FailLeasedRunningJobSql,
            jobId,
            nameof(SimulationJobStatus.Failed),
            simulationId: null,
            failureMessage,
            claimToken,
            cancellationToken);
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
        Guid? claimToken,
        CancellationToken cancellationToken)
    {
        DbConnection connection =
            _context.Database.GetDbConnection();
        IDbContextTransaction? currentTransaction =
            _context.Database.CurrentTransaction;
        bool openedConnection =
            connection.State != ConnectionState.Open;
        DbCommand? command = null;
        DbDataReader? reader = null;
        SimulationJobResult? result = null;
        ExceptionDispatchInfo? primaryException = null;

        try
        {
            if (openedConnection)
            {
                await connection.OpenAsync(cancellationToken);
            }

            command = connection.CreateCommand();

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

            if (claimToken is not null)
            {
                AddParameter(
                    command,
                    "claim_token",
                    DbType.Guid,
                    claimToken.Value);
            }

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

            reader = await command.ExecuteReaderAsync(
                    cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                result = Map(reader);

                if (await reader.ReadAsync(cancellationToken))
                {
                    throw new DataException(
                        "Terminal transition returned more than one simulation job.");
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
                    primaryException ??=
                        ExceptionDispatchInfo.Capture(exception);
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
                    primaryException ??=
                        ExceptionDispatchInfo.Capture(exception);
                }
            }

            try
            {
                if (openedConnection &&
                    currentTransaction is null &&
                    connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
            }
            catch (Exception exception)
            {
                primaryException ??=
                    ExceptionDispatchInfo.Capture(exception);
            }
        }

        primaryException?.Throw();
        return result;
    }

    private static void ValidateFailureMessage(
        string failureMessage)
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