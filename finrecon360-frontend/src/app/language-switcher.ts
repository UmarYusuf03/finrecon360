import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateService } from '@ngx-translate/core';
import { MatMenuModule } from '@angular/material/menu';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-language-switcher',
  standalone: true,
  imports: [CommonModule, MatMenuModule, MatButtonModule, MatIconModule],
  template: `
    <button mat-button [matMenuTriggerFor]="menu" class="lang-btn">
      <mat-icon>language</mat-icon>
      <span class="current-lang">{{ currentLang.toUpperCase() }}</span>
      <mat-icon>arrow_drop_down</mat-icon>
    </button>

    <mat-menu #menu="matMenu">
      <button mat-menu-item (click)="switchLanguage('en')">
        <span>🇺🇸 English</span>
      </button>
      <button mat-menu-item (click)="switchLanguage('si')">
        <span>🇱🇰 Sinhala (සිංහල)</span>
      </button>
      <button mat-menu-item (click)="switchLanguage('ta')">
        <span>🇱🇰 Tamil (தமிழ்)</span>
      </button>
    </mat-menu>
  `,
  styles: [`
    .lang-btn {
      color: #536471;
      font-weight: 500;
      border: 1px solid #e1e8ed;
      border-radius: 20px;
      padding: 0 12px;
      height: 36px;
    }
    .current-lang {
      margin: 0 4px;
      font-size: 13px;
    }
  `]
})
export class LanguageSwitcherComponent {
  currentLang = 'en';

  constructor(private translate: TranslateService) {
    this.currentLang = translate.currentLang || 'en';
  }

  switchLanguage(lang: string) {
    this.translate.use(lang);
    this.currentLang = lang;
  }
}