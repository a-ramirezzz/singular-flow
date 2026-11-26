namespace SingularFlow.Application.SimulationJobs;

public interface ISimulationJobFailureClassifier
{
    SimulationJobFailureClassification Classify(
        Exception exception,
        SimulationJobFailurePhase phase);
}