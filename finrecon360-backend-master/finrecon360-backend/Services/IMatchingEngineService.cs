using finrecon360_backend.Dtos.Reconciliation;

namespace finrecon360_backend.Services
{
    public interface IMatchingEngineService
    {
        Task<MatchingSummaryResponse> RunAutomatedMatchingAsync(Guid bankStatementImportId, Guid currentUserId);

        Task<ConfirmMatchesResponse> ConfirmMatchesAsync(List<Guid> matchGroupIds, Guid currentUserId);

        Task<IReadOnlyList<MatchGroupDto>> GetProposedMatchGroupsAsync(Guid effectiveTenantId);

        Task<IReadOnlyList<BankStatementLineDto>> GetExceptionsAsync(Guid effectiveTenantId);
    }
}
