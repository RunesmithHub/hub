namespace RunesmithHub.Protocol.Resolution;

/// <summary>What resolution decided: removals first, then installs and updates with dependencies before their dependents.</summary>
public sealed record InstallPlan(IReadOnlyList<PlanOperation> Operations)
{
    /// <summary>Gets the operations that change something.</summary>
    public IEnumerable<PlanOperation> Changes => Operations.Where(operation => operation.Kind != OperationKind.Keep);

    public bool HasChanges => Changes.Any();

    public PlanOperation? Find(string pluginId) => Operations.FirstOrDefault(operation => operation.PluginId == pluginId);
}
