import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({
  selector: 'app-payment-result',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './payment-result.html',
  styleUrls: ['./payment-result.scss'],
})
export class PaymentResultComponent implements OnInit {
  status: 'success' | 'cancel' | 'pending' = 'pending';
  orderId: string | null = null;
  loading = true;
  message: string | null = null;

  constructor(
    private route: ActivatedRoute,
    private router: Router
  ) {}

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      this.orderId = params['order_id'] || null;

      // Check URL path for status
      const path = this.route.snapshot.url[0]?.path;
      if (path === 'payment-success') {
        this.status = 'success';
        this.message = params['message'] || 'Your payment has been processed successfully!';
      } else if (path === 'payment-cancel') {
        this.status = 'cancel';
        this.message = params['message'] || 'Payment was cancelled. You can try again when ready.';
      } else {
        // Fallback: check status parameter
        const statusParam = params['status'];
        if (statusParam === 'success') {
          this.status = 'success';
        } else if (statusParam === 'cancel') {
          this.status = 'cancel';
        }
      }

      this.loading = false;
    });
  }

  goToLogin(): void {
    this.router.navigateByUrl('/auth/login');
  }

  goToDashboard(): void {
    this.router.navigateByUrl('/app/dashboard');
  }

  goToProfile(): void {
    this.router.navigateByUrl('/app/profile');
  }

  retryPayment(): void {
    this.router.navigateByUrl('/app/profile');
  }
}
