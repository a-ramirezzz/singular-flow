namespace SingularFlow.Application.SimulationJobs;

public abstract record SimulationJobRetryDecision
{
    private SimulationJobRetryDecision()
    {
    }

    public sealed record Scheduled : SimulationJobRetryDecision
    {
        public Scheduled(SimulationJobResult job)
        {
            ArgumentNullException.ThrowIfNull(job);
            Job = job;
        }

        public SimulationJobResult Job { get; }
    }

    public sealed record Exhausted : SimulationJobRetryDecision
    {
        public Exhausted(SimulationJobResult job)
        {
            ArgumentNullException.ThrowIfNull(job);
            Job = job;
        }

        public SimulationJobResult Job { get; }
    }
}