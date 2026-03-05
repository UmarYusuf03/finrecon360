// src/app/shared/components/language-switcher/language-switcher.ts
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateService } from '@ngx-translate/core';

import { SUPPORTED_LANGUAGES, type LanguageOption } from '../../../core/constants/languages';

@Component({
  selector: 'app-language-switcher',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './language-switcher.html',
  styleUrls: ['./language-switcher.scss']
})
export class LanguageSwitcherComponent implements OnInit {
  languages: LanguageOption[] = SUPPORTED_LANGUAGES;

  currentLang = 'en';

  constructor(private translate: TranslateService) {}

  ngOnInit(): void {
    // Tell ngx-translate what languages exist
    this.translate.addLangs(this.languages.map(l => l.code));
    this.translate.setDefaultLang('en');

    const saved = localStorage.getItem('app_lang');
    const initial = saved && this.languages.some(l => l.code === saved)
      ? saved
      : 'en';

    this.currentLang = initial;
    this.translate.use(initial);
  }

  changeLanguage(lang: string): void {
    if (lang === this.currentLang) return;
    this.currentLang = lang;
    this.translate.use(lang);
    localStorage.setItem('app_lang', lang);
  }
}
