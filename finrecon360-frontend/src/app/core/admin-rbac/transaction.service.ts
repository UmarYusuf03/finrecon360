import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { tap } from 'rxjs/operators';

import { API_BASE_URL, USE_MOCK_API } from '../constants/api.constants';
import {
  ApproveTransactionRequest,
  CreateTransactionRequest,
  RejectTransactionRequest,
  Transaction,
  TransactionStateHistory,
} from './models';

@Injectable({
  providedIn: 'root',
})
export class TransactionService {
  private readonly baseUrl = `${API_BASE_URL}/api/admin/transactions`;
  private readonly transactionsSubject = new BehaviorSubject<Transaction[]>([]);

  constructor(private http: HttpClient) {}

  getAll(): Observable<Transaction[]> {
    if (USE_MOCK_API) {
      return this.transactionsSubject.asObservable();
    }

    return this.http.get<Transaction[]>(this.baseUrl).pipe(
      tap((transactions) => this.transactionsSubject.next(transactions)),
    );
  }

  getById(id: string): Observable<Transaction> {
    if (USE_MOCK_API) {
      return of(this.transactionsSubject.value.find((item) => item.transactionId === id) as Transaction);
    }

    return this.http.get<Transaction>(`${this.baseUrl}/${id}`);
  }

  create(data: CreateTransactionRequest): Observable<Transaction> {
    if (USE_MOCK_API) {
      const created: Transaction = {
        transactionId: `txn-${Date.now()}`,
        amount: data.amount,
        transactionDate: data.transactionDate,
        description: data.description,
        bankAccountId: data.bankAccountId ?? null,
        transactionType: data.transactionType,
        paymentMethod: data.paymentMethod,
        transactionState: 'Pending',
        createdByUserId: null,
        approvedAt: null,
        approvedByUserId: null,
        rejectedAt: null,
        rejectedByUserId: null,
        rejectionReason: null,
        createdAt: new Date().toISOString(),
        updatedAt: null,
      };
      this.transactionsSubject.next([created, ...this.transactionsSubject.value]);
      return of(created);
    }

    return this.http.post<Transaction>(this.baseUrl, data).pipe(
      tap((created) => this.transactionsSubject.next([created, ...this.transactionsSubject.value])),
    );
  }

  approve(id: string, data: ApproveTransactionRequest): Observable<Transaction> {
    if (USE_MOCK_API) {
      return this.updateMockState(id, data.note ?? null, 'approve');
    }

    return this.http.post<Transaction>(`${this.baseUrl}/${id}/approve`, data);
  }

  reject(id: string, data: RejectTransactionRequest): Observable<Transaction> {
    if (USE_MOCK_API) {
      return this.updateMockState(id, data.reason, 'reject');
    }

    return this.http.post<Transaction>(`${this.baseUrl}/${id}/reject`, data);
  }

  getJournalReady(): Observable<Transaction[]> {
    if (USE_MOCK_API) {
      // Mirrors the backend queue: NeedsBankMatch transactions are not journal-ready yet.
      return of(
        this.transactionsSubject.value
          .filter((item) => item.transactionState === 'JournalReady')
          .sort((left, right) =>
            left.transactionDate.localeCompare(right.transactionDate) ||
            left.createdAt.localeCompare(right.createdAt),
          ),
      );
    }

    return this.http.get<Transaction[]>(`${this.baseUrl}/journal-ready`);
  }

  getNeedsBankMatch(): Observable<Transaction[]> {
    if (USE_MOCK_API) {
      // Mirrors the backend handoff queue for future matcher/reconciliation work.
      return of(
        this.transactionsSubject.value
          .filter((item) => item.transactionState === 'NeedsBankMatch')
          .sort((left, right) =>
            left.transactionDate.localeCompare(right.transactionDate) ||
            left.createdAt.localeCompare(right.createdAt),
          ),
      );
    }

    return this.http.get<Transaction[]>(`${this.baseUrl}/needs-bank-match`);
  }

  getHistory(id: string): Observable<TransactionStateHistory[]> {
    if (USE_MOCK_API) {
      const transaction = this.transactionsSubject.value.find((item) => item.transactionId === id);
      return of(transaction ? [{
        transactionStateHistoryId: `${id}-history-created`,
        transactionId: id,
        fromState: 'Pending',
        toState: transaction.transactionState,
        changedByUserId: transaction.createdByUserId ?? null,
        changedAt: transaction.updatedAt ?? transaction.createdAt,
        note: transaction.transactionState === 'Pending' ? 'Transaction created' : null,
      }] : []);
    }

    return this.http.get<TransactionStateHistory[]>(`${this.baseUrl}/${id}/history`);
  }

  private updateMockState(
    id: string,
    note: string | null,
    action: 'approve' | 'reject',
  ): Observable<Transaction> {
    const now = new Date().toISOString();
    let updated!: Transaction;

    this.transactionsSubject.next(
      this.transactionsSubject.value.map((transaction) => {
        if (transaction.transactionId !== id) {
          return transaction;
        }

        const nextState = action === 'reject'
          ? 'Rejected'
          // Card cash-outs wait for bank matching before they can enter the journal queue.
          : transaction.transactionType === 'CashOut' && transaction.paymentMethod === 'Card'
            ? 'NeedsBankMatch'
            : 'JournalReady';

        updated = {
          ...transaction,
          transactionState: nextState,
          approvedAt: action === 'approve' ? now : transaction.approvedAt,
          rejectedAt: action === 'reject' ? now : transaction.rejectedAt,
          rejectionReason: action === 'reject' ? note : transaction.rejectionReason,
          updatedAt: now,
        };

        return updated;
      }),
    );

    return of(updated);
  }
}
