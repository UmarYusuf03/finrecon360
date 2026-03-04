import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  selector: 'app-not-authorized',
  standalone: true,
  imports: [CommonModule, RouterLink, TranslateModule],
  templateUrl: './not-authorized.html',
  styleUrls: ['./not-authorized.scss'],
})
export class NotAuthorizedComponent {}
