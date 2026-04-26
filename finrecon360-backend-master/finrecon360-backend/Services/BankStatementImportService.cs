using System.Globalization;
using finrecon360_backend.Data;
using finrecon360_backend.Dtos.Reconciliation;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services
{
    /// <summary>
    /// Service for uploading and querying bank statement imports.
    /// </summary>
    public class BankStatementImportService : IBankStatementImportService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<BankStatementImportService> _logger;
        private readonly ITenantContext _tenantContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="BankStatementImportService"/> class.
        /// </summary>
        /// <param name="dbContext">Application database context.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="tenantContext">Tenant context resolver.</param>
        public BankStatementImportService(
            AppDbContext dbContext,
            ILogger<BankStatementImportService> logger,
            ITenantContext tenantContext)
        {
            _dbContext = dbContext;
            _logger = logger;
            _tenantContext = tenantContext;
        }

        /// <summary>
        /// Uploads a statement CSV and stores parsed lines.
        /// </summary>
        /// <param name="request">Upload request containing the file and bank account id.</param>
        /// <param name="currentUserId">Current authenticated user id.</param>
        /// <returns>Created import response.</returns>
        public async Task<StatementImportResponse> UploadStatementAsync(UploadStatementRequest request, Guid currentUserId)
        {
            if (request.File == null || request.File.Length == 0)
            {
                throw new ArgumentException("Statement file is required.", nameof(request));
            }

            var tenantResolution = await _tenantContext.ResolveAsync();
            if (tenantResolution == null)
            {
                throw new InvalidOperationException("Unable to resolve tenant context.");
            }

            // Prefer "Completed" when enum contains it, otherwise use Parsed as the equivalent.
            var completedLikeStatus =
                Enum.TryParse<BankStatementImportStatus>("Completed", true, out var completedStatus)
                    ? completedStatus
                    : BankStatementImportStatus.Parsed;

            var now = DateTime.UtcNow;

            var import = new BankStatementImport
            {
                Id = Guid.NewGuid(),
                BatchId = Guid.NewGuid(),
                BankAccountId = request.BankAccountId,
                FileName = request.File.FileName,
                ImportDate = now,
                Status = completedLikeStatus,
                TotalRows = 0,
                ValidRows = 0,
                TenantId = tenantResolution.TenantId,
                CreatedAt = now,
                CreatedBy = currentUserId
            };

            _dbContext.BankStatementImports.Add(import);

            var importedLineCount = 0;
            var skippedLineCount = 0;

            using (var stream = request.File.OpenReadStream())
            using (var reader = new StreamReader(stream))
            {
                var isFirstLine = true;
                while (!reader.EndOfStream)
                {
                    var rawLine = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(rawLine))
                    {
                        continue;
                    }

                    // Skip header row.
                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        continue;
                    }

                    var parts = rawLine.Split(',');
                    if (parts.Length < 4)
                    {
                        skippedLineCount++;
                        continue;
                    }

                    var dateText = parts[0].Trim();
                    var description = parts[1].Trim();
                    var amountText = parts[2].Trim();
                    var reference = parts[3].Trim();

                    if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var transactionDate) &&
                        !DateTime.TryParse(dateText, out transactionDate))
                    {
                        skippedLineCount++;
                        continue;
                    }

                    if (!decimal.TryParse(amountText, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CultureInfo.InvariantCulture, out var amount) &&
                        !decimal.TryParse(amountText, out amount))
                    {
                        skippedLineCount++;
                        continue;
                    }

                    var line = new BankStatementLine
                    {
                        Id = Guid.NewGuid(),
                        BankStatementImportId = import.Id,
                        TransactionDate = transactionDate,
                        PostingDate = null,
                        Description = description,
                        Amount = amount,
                        ReferenceNumber = string.IsNullOrWhiteSpace(reference) ? null : reference,
                        IsReconciled = false,
                        TenantId = tenantResolution.TenantId,
                        CreatedAt = now,
                        CreatedBy = currentUserId
                    };

                    _dbContext.BankStatementLines.Add(line);
                    importedLineCount++;
                }
            }

            import.TotalRows = importedLineCount;
            import.ValidRows = importedLineCount;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Statement import created. ImportId={ImportId}, BankAccountId={BankAccountId}, ImportedLines={ImportedLines}, SkippedLines={SkippedLines}, UserId={UserId}",
                import.Id,
                import.BankAccountId,
                importedLineCount,
                skippedLineCount,
                currentUserId);

            return MapToResponse(import);
        }

        /// <summary>
        /// Gets paginated imports for a specific bank account.
        /// </summary>
        /// <param name="bankAccountId">Bank account id filter.</param>
        /// <param name="pageNumber">1-based page number.</param>
        /// <param name="pageSize">Page size.</param>
        /// <returns>Paginated import responses.</returns>
        public async Task<PaginatedResponse<StatementImportResponse>> GetImportsAsync(Guid bankAccountId, int pageNumber, int pageSize)
        {
            pageNumber = pageNumber < 1 ? 1 : pageNumber;
            pageSize = pageSize < 1 ? 20 : pageSize;

            var query = _dbContext.BankStatementImports
                .AsNoTracking()
                .Where(i => i.BankAccountId == bankAccountId);

            var totalCount = await query.CountAsync();

            var items = await query
                .OrderByDescending(i => i.ImportDate)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(i => new StatementImportResponse
                {
                    ImportId = i.Id,
                    ImportDate = i.ImportDate,
                    BankAccountId = i.BankAccountId,
                    Status = i.Status.ToString(),
                    TotalLinesImported = i.TotalRows
                })
                .ToListAsync();

            return new PaginatedResponse<StatementImportResponse>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        /// <summary>
        /// Gets a single import by id.
        /// </summary>
        /// <param name="id">Import id.</param>
        /// <returns>Mapped import response when found; otherwise null.</returns>
        public async Task<StatementImportResponse?> GetImportByIdAsync(Guid id)
        {
            return await _dbContext.BankStatementImports
                .AsNoTracking()
                .Where(i => i.Id == id)
                .Select(i => new StatementImportResponse
                {
                    ImportId = i.Id,
                    ImportDate = i.ImportDate,
                    BankAccountId = i.BankAccountId,
                    Status = i.Status.ToString(),
                    TotalLinesImported = i.TotalRows
                })
                .FirstOrDefaultAsync();
        }

        private static StatementImportResponse MapToResponse(BankStatementImport import)
        {
            return new StatementImportResponse
            {
                ImportId = import.Id,
                ImportDate = import.ImportDate,
                BankAccountId = import.BankAccountId,
                Status = import.Status.ToString(),
                TotalLinesImported = import.TotalRows
            };
        }
    }
}