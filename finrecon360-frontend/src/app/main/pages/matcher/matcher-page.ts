import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  selector: 'app-matcher-page',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  templateUrl: './matcher-page.html',
  styleUrls: ['./matcher-page.scss'],
})
export class MatcherPageComponent {}
