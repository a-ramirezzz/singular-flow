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
            job.claimed_at_utc;
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
            ClaimedAtUtc: job.ClaimedAtUtc);
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

        if (reader.IsDBNull(9))
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
            ClaimedAtUtc:
                reader.GetFieldValue<DateTimeOffset>(9));
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