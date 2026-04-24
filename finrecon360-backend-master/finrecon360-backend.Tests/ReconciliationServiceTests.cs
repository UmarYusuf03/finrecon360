using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reconciliation;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace finrecon360_backend.Tests
{
    public class ReconciliationServiceTests
    {
        [Fact]
        public async Task CreateRunAsync_Creates_Run_Successfully()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            await using var dbContext = CreateDbContext(tenantId);

            var service = new ReconciliationService(
                dbContext,
                NullLogger<ReconciliationService>.Instance,
                new FakeTenantContext(tenantId));

            var request = new CreateReconciliationRunRequest
            {
                BankAccountId = Guid.NewGuid()
            };

            var currentUserId = Guid.NewGuid();

            // Act
            var response = await service.CreateRunAsync(request, currentUserId);

            // Assert
            Assert.NotEqual(Guid.Empty, response.ReconciliationRunId);
            Assert.Equal(request.BankAccountId, response.BankAccountId);
            Assert.Equal(ReconciliationRunStatus.InProgress.ToString(), response.Status);
            Assert.Equal(0, response.TotalMatchesProposed);
            Assert.Equal(0, response.TotalMatchesConfirmed);
            Assert.Equal(0, response.TotalExceptions);

            var saved = await dbContext.ReconciliationRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == response.ReconciliationRunId);

            Assert.NotNull(saved);
            Assert.Equal(currentUserId, saved!.CreatedBy);
            Assert.Equal(request.BankAccountId, saved.BankAccountId);
            Assert.Equal(ReconciliationRunStatus.InProgress, saved.Status);
            Assert.Equal(tenantId, saved.TenantId);
        }

        [Fact]
        public async Task GetRunsAsync_Applies_Pagination_And_Order()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            await using var dbContext = CreateDbContext(tenantId);

            var baseDate = DateTime.UtcNow.Date;

            // Seed five runs with increasing run dates (newest has highest day offset).
            for (var i = 0; i < 5; i++)
            {
                dbContext.ReconciliationRuns.Add(new ReconciliationRun
                {
                    Id = Guid.NewGuid(),
                    BankAccountId = Guid.NewGuid(),
                    RunDate = baseDate.AddDays(i),
                    Status = ReconciliationRunStatus.InProgress,
                    TotalMatchesProposed = i,
                    TenantId = tenantId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = Guid.NewGuid()
                });
            }

            await dbContext.SaveChangesAsync();

            var service = new ReconciliationService(
                dbContext,
                NullLogger<ReconciliationService>.Instance,
                new FakeTenantContext(tenantId));

            // Act: page 2, size 2 on 5 total items (ordered descending by RunDate).
            var page = await service.GetRunsAsync(pageNumber: 2, pageSize: 2);

            // Assert
            Assert.Equal(5, page.TotalCount);
            Assert.Equal(2, page.PageNumber);
            Assert.Equal(2, page.PageSize);
            Assert.Equal(2, page.Items.Count());

            var ordered = page.Items.ToList();
            Assert.True(ordered[0].RunDate >= ordered[1].RunDate);

            // Expected dates on page 2:
            // Desc order: day4, day3, day2, day1, day0 => page2(size2) => day2, day1
            Assert.Equal(baseDate.AddDays(2), ordered[0].RunDate);
            Assert.Equal(baseDate.AddDays(1), ordered[1].RunDate);
        }

        [Fact]
        public async Task GetRunByIdAsync_Returns_Null_When_Not_Found()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            await using var dbContext = CreateDbContext(tenantId);

            var service = new ReconciliationService(
                dbContext,
                NullLogger<ReconciliationService>.Instance,
                new FakeTenantContext(tenantId));

            // Act
            var result = await service.GetRunByIdAsync(Guid.NewGuid());

            // Assert
            Assert.Null(result);
        }

        private static AppDbContext CreateDbContext(Guid tenantId)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"ReconciliationServiceTests-{Guid.NewGuid()}")
                .Options;

            var services = new ServiceCollection();
            services.AddSingleton<ITenantContext>(new FakeTenantContext(tenantId));
            var serviceProvider = services.BuildServiceProvider();

            return new AppDbContext(options, serviceProvider);
        }

        private sealed class FakeTenantContext : ITenantContext
        {
            private readonly TenantResolution _resolution;

            public FakeTenantContext(Guid tenantId)
            {
                _resolution = new TenantResolution(tenantId, TenantStatus.Active, "Test Tenant");
            }

            public Task<TenantResolution?> ResolveAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult<TenantResolution?>(_resolution);
            }
        }
    }
}