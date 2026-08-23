// src/app/shared/components/confirm-dialog/confirm-dialog.ts
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';

export type ConfirmDialogVariant = 'danger' | 'warning' | 'primary';

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  templateUrl: './confirm-dialog.html',
  styleUrls: ['./confirm-dialog.scss']
})
export class ConfirmDialogComponent {
  @Input() open = false;
  @Input() title = '';
  @Input() message = '';
  @Input() confirmLabel = '';
  @Input() cancelLabel = '';
  @Input() processingLabel = '';
  @Input() processing = false;
  @Input() variant: ConfirmDialogVariant = 'danger';

  @Output() confirmed = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<void>();

  get confirmButtonClass(): string {
    switch (this.variant) {
      case 'warning':
        return 'warning-btn';
      case 'primary':
        return 'primary-btn';
      default:
        return 'danger-btn';
    }
  }

  onCancel(): void {
    if (this.processing) return;
    this.cancelled.emit();
  }

  onConfirm(): void {
    if (this.processing) return;
    this.confirmed.emit();
  }
}
