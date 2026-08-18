import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  selector: 'app-admin-financial-reports-shell',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, RouterOutlet, TranslateModule],
  templateUrl: './admin-financial-reports-shell.html',
  styleUrls: ['./admin-financial-reports.scss'],
})
export class AdminFinancialReportsShellComponent {}
