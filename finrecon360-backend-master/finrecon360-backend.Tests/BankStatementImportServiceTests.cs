using System.Text;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reconciliation;
using finrecon360_backend.Models;
using finrecon360_backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace finrecon360_backend.Tests
{
    public class BankStatementImportServiceTests
    {
        [Fact]
        public async Task UploadStatementAsync_ParsesCsv_And_SavesToDatabase()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            await using var dbContext = CreateDbContext(tenantId);
            var tenantContext = new FakeTenantContext(tenantId);

            var service = new BankStatementImportService(
                dbContext,
                NullLogger<BankStatementImportService>.Instance,
                tenantContext);

            var csv = new StringBuilder()
                .AppendLine("Date,Description,Amount,Reference")
                .AppendLine("2026-04-01,Coffee,12.50,REF001")
                .AppendLine("2026-04-02,Lunch,25.00,REF002")
                .ToString();

            var csvBytes = Encoding.UTF8.GetBytes(csv);
            var stream = new MemoryStream(csvBytes);

            IFormFile file = new FormFile(stream, 0, csvBytes.Length, "file", "statement.csv")
            {
                Headers = new HeaderDictionary(),
                ContentType = "text/csv"
            };

            var request = new UploadStatementRequest
            {
                File = file,
                BankAccountId = Guid.NewGuid()
            };

            var currentUserId = Guid.NewGuid();

            // Act
            var response = await service.UploadStatementAsync(request, currentUserId);

            // Assert
            Assert.NotEqual(Guid.Empty, response.ImportId);
            Assert.Equal(request.BankAccountId, response.BankAccountId);
            Assert.Equal(2, response.TotalLinesImported);

            var savedImport = await dbContext.BankStatementImports
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == response.ImportId);

            Assert.NotNull(savedImport);
            Assert.Equal(tenantId, savedImport!.TenantId);
            Assert.Equal(currentUserId, savedImport.CreatedBy);
            Assert.Equal(2, savedImport.TotalRows);

            var savedLines = await dbContext.BankStatementLines
                .AsNoTracking()
                .Where(l => l.BankStatementImportId == response.ImportId)
                .OrderBy(l => l.TransactionDate)
                .ToListAsync();

            Assert.Equal(2, savedLines.Count);
            Assert.All(savedLines, line =>
            {
                Assert.Equal(tenantId, line.TenantId);
                Assert.False(line.IsReconciled);
            });
            Assert.Equal("Coffee", savedLines[0].Description);
            Assert.Equal(12.50m, savedLines[0].Amount);
            Assert.Equal("REF001", savedLines[0].ReferenceNumber);
        }

        [Fact]
        public async Task GetImportsAsync_ReturnsPaginatedResults()
        {
            // Arrange
            var tenantId = Guid.NewGuid();
            await using var dbContext = CreateDbContext(tenantId);
            var tenantContext = new FakeTenantContext(tenantId);

            var service = new BankStatementImportService(
                dbContext,
                NullLogger<BankStatementImportService>.Instance,
                tenantContext);

            var bankAccountId = Guid.NewGuid();
            var otherBankAccountId = Guid.NewGuid();
            var baseDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);

            for (var i = 0; i < 5; i++)
            {
                dbContext.BankStatementImports.Add(new BankStatementImport
                {
                    Id = Guid.NewGuid(),
                    BatchId = Guid.NewGuid(),
                    BankAccountId = bankAccountId,
                    FileName = $"statement-{i}.csv",
                    ImportDate = baseDate.AddDays(-i),
                    Status = BankStatementImportStatus.Parsed,
                    TotalRows = i + 1,
                    ValidRows = i + 1,
                    TenantId = tenantId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = Guid.NewGuid()
                });
            }

            // Extra row for another bank account to verify filtering.
            dbContext.BankStatementImports.Add(new BankStatementImport
            {
                Id = Guid.NewGuid(),
                BatchId = Guid.NewGuid(),
                BankAccountId = otherBankAccountId,
                FileName = "other.csv",
                ImportDate = baseDate,
                Status = BankStatementImportStatus.Parsed,
                TotalRows = 99,
                ValidRows = 99,
                TenantId = tenantId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = Guid.NewGuid()
            });

            await dbContext.SaveChangesAsync();

            // Act
            var page = await service.GetImportsAsync(bankAccountId, pageNumber: 2, pageSize: 2);

            // Assert
            Assert.Equal(5, page.TotalCount);
            Assert.Equal(2, page.PageNumber);
            Assert.Equal(2, page.PageSize);
            Assert.Equal(2, page.Items.Count());

            var items = page.Items.ToList();
            Assert.True(items[0].ImportDate >= items[1].ImportDate);

            // Ordered desc by date:
            // day0, day-1, day-2, day-3, day-4 => page 2 size 2 => day-2, day-3
            Assert.Equal(baseDate.AddDays(-2), items[0].ImportDate);
            Assert.Equal(baseDate.AddDays(-3), items[1].ImportDate);
        }

        private static AppDbContext CreateDbContext(Guid tenantId)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"BankStatementImportServiceTests-{Guid.NewGuid()}")
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