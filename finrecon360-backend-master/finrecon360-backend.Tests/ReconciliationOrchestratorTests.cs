using finrecon360_backend.Services;

namespace finrecon360_backend.Tests;

public class ReconciliationOrchestratorTests
{
    private readonly ReconciliationOrchestrator _orchestrator = new();

    [Theory]
    [InlineData("POS", 1, "Operational Match")]
    [InlineData("ERP", 2, "Sync Audit")]
    [InlineData("GATEWAY", 2, "Settlement Match")]
    [InlineData("BANK", 3, "Collection Match")]
    public void TryBuildPlan_routes_known_source_types(string sourceType, int expectedSteps, string expectedEventName)
    {
        var found = _orchestrator.TryBuildPlan(sourceType, out var plan);

        Assert.True(found);
        Assert.Equal(sourceType, plan.SourceType);
        Assert.Equal(expectedSteps, plan.Steps.Count);
        Assert.Contains(plan.Steps, step => step.Event.ToString().Contains(expectedEventName.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase)
            || step.Purpose.Contains(expectedEventName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryBuildPlan_returns_false_for_unknown_source_type()
    {
        var found = _orchestrator.TryBuildPlan("CSV", out var plan);

        Assert.False(found);
        Assert.Empty(plan.Steps);
        Assert.Contains("Unclassified", plan.Summary, StringComparison.OrdinalIgnoreCase);
    }
}