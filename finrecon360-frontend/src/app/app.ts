import { Component, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TranslateService } from '@ngx-translate/core';

import { SUPPORTED_LANGUAGES } from './core/constants/languages';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  protected readonly title = signal('finrecon360');

  constructor(private translate: TranslateService) {
    this.configureLanguage();
  }

  private configureLanguage(): void {
    // Keep language bootstrap in one place so later IdentityServer profile info can override it safely.
    const available = SUPPORTED_LANGUAGES.map(lang => lang.code);
    this.translate.addLangs(available);
    this.translate.setDefaultLang('en');

    const saved = localStorage.getItem('app_lang');
    const initial = saved && available.includes(saved) ? saved : 'en';
    this.translate.use(initial);
  }
}
