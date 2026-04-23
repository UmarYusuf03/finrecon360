import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { tap } from 'rxjs/operators';

import { API_BASE_URL, USE_MOCK_API } from '../constants/api.constants';
import { BankAccount } from './models';

@Injectable({
  providedIn: 'root',
})
export class BankAccountService {
  private readonly baseUrl = `${API_BASE_URL}/api/admin/bank-accounts`;
  private readonly mockAccounts: BankAccount[] = [
    {
      bankAccountId: 'bank-1',
      bankName: 'Mock National Bank',
      accountName: 'Operations Account',
      accountNumber: 'ACC-001',
      currency: 'USD',
      isActive: true,
      createdAt: new Date().toISOString(),
      updatedAt: null,
    },
  ];
  private readonly accountsSubject = new BehaviorSubject<BankAccount[]>(
    USE_MOCK_API ? this.mockAccounts : [],
  );

  constructor(private http: HttpClient) {}

  getAll(): Observable<BankAccount[]> {
    if (USE_MOCK_API) {
      return this.accountsSubject.asObservable();
    }

    return this.http.get<BankAccount[]>(this.baseUrl).pipe(
      tap((accounts) => this.accountsSubject.next(accounts)),
    );
  }

  getById(id: string): Observable<BankAccount> {
    if (USE_MOCK_API) {
      const account = this.accountsSubject.value.find((item) => item.bankAccountId === id);
      return of(account as BankAccount);
    }

    return this.http.get<BankAccount>(`${this.baseUrl}/${id}`);
  }

  create(data: Partial<BankAccount>): Observable<BankAccount> {
    if (USE_MOCK_API) {
      const created: BankAccount = {
        bankAccountId: `bank-${Date.now()}`,
        bankName: data.bankName ?? '',
        accountName: data.accountName ?? '',
        accountNumber: data.accountNumber ?? '',
        currency: data.currency ?? '',
        isActive: true,
        createdAt: new Date().toISOString(),
        updatedAt: null,
      };
      this.accountsSubject.next([...this.accountsSubject.value, created]);
      return of(created);
    }

    return this.http.post<BankAccount>(this.baseUrl, data).pipe(
      tap((created) => {
        this.accountsSubject.next([...this.accountsSubject.value, created]);
      }),
    );
  }

  update(id: string, data: Partial<BankAccount>): Observable<void> {
    if (USE_MOCK_API) {
      this.accountsSubject.next(
        this.accountsSubject.value.map((account) =>
          account.bankAccountId === id
            ? { ...account, ...data, updatedAt: new Date().toISOString() }
            : account,
        ),
      );
      return of(void 0);
    }

    return this.http.put<void>(`${this.baseUrl}/${id}`, data).pipe(
      tap(() => {
        this.accountsSubject.next(
          this.accountsSubject.value.map((account) =>
            account.bankAccountId === id
              ? { ...account, ...data, updatedAt: new Date().toISOString() }
              : account,
          ),
        );
      }),
    );
  }

  deactivate(id: string): Observable<void> {
    if (USE_MOCK_API) {
      this.accountsSubject.next(
        this.accountsSubject.value.map((account) =>
          account.bankAccountId === id
            ? { ...account, isActive: false, updatedAt: new Date().toISOString() }
            : account,
        ),
      );
      return of(void 0);
    }

    return this.http.delete<void>(`${this.baseUrl}/${id}`).pipe(
      tap(() => {
        this.accountsSubject.next(
          this.accountsSubject.value.map((account) =>
            account.bankAccountId === id
              ? { ...account, isActive: false, updatedAt: new Date().toISOString() }
              : account,
          ),
        );
      }),
    );
  }
}
