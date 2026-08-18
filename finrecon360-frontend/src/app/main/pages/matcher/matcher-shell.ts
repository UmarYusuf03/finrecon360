import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  selector: 'app-matcher-shell',
  standalone: true,
  imports: [CommonModule, MatIconModule, RouterLink, RouterLinkActive, RouterOutlet, TranslateModule],
  templateUrl: './matcher-shell.html',
  styleUrls: ['./matcher-shell.scss'],
})
export class MatcherShellComponent {}
