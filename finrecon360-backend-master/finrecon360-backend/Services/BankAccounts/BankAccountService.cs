using finrecon360_backend.Data;
using finrecon360_backend.Dtos.BankAccounts;
using finrecon360_backend.Models;
using Microsoft.EntityFrameworkCore;

namespace finrecon360_backend.Services.BankAccounts
{
    public class BankAccountService
    {
        public async Task<BankAccountResponse> CreateAsync(TenantDbContext tenantDb, CreateBankAccountRequest request, CancellationToken ct)
        {
            var accountNumber = NormalizeAccountNumber(request.AccountNumber);

            var exists = await tenantDb.BankAccounts
                .AsNoTracking()
                .AnyAsync(x => x.AccountNumber == accountNumber, ct);

            if (exists)
            {
                throw new InvalidOperationException("A bank account with this account number already exists.");
            }

            var entity = new BankAccount
            {
                BankAccountId = Guid.NewGuid(),
                BankName = request.BankName.Trim(),
                AccountName = request.AccountName.Trim(),
                AccountNumber = accountNumber,
                Currency = request.Currency.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = null
            };

            tenantDb.BankAccounts.Add(entity);
            await tenantDb.SaveChangesAsync(ct);

            return Map(entity);
        }

        public async Task<List<BankAccountResponse>> GetAllAsync(TenantDbContext tenantDb, CancellationToken ct)
        {
            var items = await tenantDb.BankAccounts
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);

            return items.Select(Map).ToList();
        }

        public async Task<BankAccountResponse?> GetByIdAsync(TenantDbContext tenantDb, Guid id, CancellationToken ct)
        {
            var entity = await tenantDb.BankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.BankAccountId == id, ct);

            return entity == null ? null : Map(entity);
        }

        public async Task<bool> UpdateAsync(TenantDbContext tenantDb, Guid id, UpdateBankAccountRequest request, CancellationToken ct)
        {
            var entity = await tenantDb.BankAccounts
                .FirstOrDefaultAsync(x => x.BankAccountId == id, ct);

            if (entity == null)
            {
                return false;
            }

            if (request.AccountNumber != null)
            {
                var accountNumber = NormalizeAccountNumber(request.AccountNumber);

                if (!string.Equals(entity.AccountNumber, accountNumber, StringComparison.Ordinal))
                {
                    var duplicateExists = await tenantDb.BankAccounts
                        .AsNoTracking()
                        .AnyAsync(x => x.BankAccountId != id && x.AccountNumber == accountNumber, ct);

                    if (duplicateExists)
                    {
                        throw new InvalidOperationException("A bank account with this account number already exists.");
                    }

                    entity.AccountNumber = accountNumber;
                }
            }

            if (request.BankName != null)
            {
                entity.BankName = request.BankName.Trim();
            }

            if (request.AccountName != null)
            {
                entity.AccountName = request.AccountName.Trim();
            }

            if (request.Currency != null)
            {
                entity.Currency = request.Currency.Trim();
            }

            if (request.IsActive.HasValue)
            {
                entity.IsActive = request.IsActive.Value;
            }

            entity.UpdatedAt = DateTime.UtcNow;

            await tenantDb.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeactivateAsync(TenantDbContext tenantDb, Guid id, CancellationToken ct)
        {
            var entity = await tenantDb.BankAccounts
                .FirstOrDefaultAsync(x => x.BankAccountId == id, ct);

            if (entity == null)
            {
                return false;
            }

            entity.IsActive = false;
            entity.UpdatedAt = DateTime.UtcNow;

            await tenantDb.SaveChangesAsync(ct);
            return true;
        }

        private static string NormalizeAccountNumber(string value) =>
            value.Trim().ToUpperInvariant();

        private static BankAccountResponse Map(BankAccount entity) =>
            new(
                entity.BankAccountId,
                entity.BankName,
                entity.AccountName,
                entity.AccountNumber,
                entity.Currency,
                entity.IsActive,
                entity.CreatedAt,
                entity.UpdatedAt);
    }
}
