import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';

import { ReportSnapshotDto } from '../../../../core/admin-rbac/reconciliation-kpi.service';

@Component({
  selector: 'app-kpi-summary-cards',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule],
  templateUrl: './kpi-summary-cards.html',
  styleUrls: ['./kpi-summary-cards.scss'],
})
export class KpiSummaryCardsComponent {
  @Input() snapshot: ReportSnapshotDto | null = null;
}
