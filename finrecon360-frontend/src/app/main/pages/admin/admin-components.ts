import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule } from '@ngx-translate/core';

import { AdminComponentService } from '../../../core/admin-rbac/admin-component.service';
import { AppComponentResource } from '../../../core/admin-rbac/models';
import { HasPermissionDirective } from '../../../core/auth/has-permission.directive';

@Component({
  selector: 'app-admin-components',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    TranslateModule,
    HasPermissionDirective,
  ],
  templateUrl: './admin-components.html',
  styleUrls: ['./admin-components.scss'],
})
export class AdminComponentsComponent implements OnInit {
  displayedColumns = ['code', 'name', 'route', 'category', 'status', 'actions'];
  components: AppComponentResource[] = [];
  form!: FormGroup;
  editingId: string | null = null;

  constructor(
    private adminComponentService: AdminComponentService,
    private fb: FormBuilder,
    private dialog: MatDialog
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      code: ['', Validators.required],
      name: ['', Validators.required],
      routePath: ['', Validators.required],
      category: [''],
      description: [''],
    });

    this.adminComponentService.getComponents().subscribe((components) => (this.components = components));
  }

  openAdd(dialogTemplate: any): void {
    this.editingId = null;
    this.form.reset();
    this.dialog.open(dialogTemplate);
  }

  openEdit(component: AppComponentResource, dialogTemplate: any): void {
    this.editingId = component.id;
    this.form.patchValue({
      code: component.code,
      name: component.name,
      routePath: component.routePath,
      category: component.category,
      description: component.description,
    });
    this.dialog.open(dialogTemplate);
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const payload = this.form.value;
    if (this.editingId) {
      this.adminComponentService.updateComponent(this.editingId, payload).subscribe();
    } else {
      this.adminComponentService.createComponent(payload).subscribe();
    }
    this.dialog.closeAll();
  }

  toggleActive(component: AppComponentResource): void {
    if (component.isActive) {
      this.adminComponentService.deactivateComponent(component.id).subscribe();
    } else {
      this.adminComponentService.reactivateComponent(component.id).subscribe();
    }
  }
}
