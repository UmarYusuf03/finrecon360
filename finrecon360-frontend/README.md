# Finrecon360Frontend

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 20.3.10.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Backend Dependency Notes

For local development, frontend expects backend auth and tenant context.

- Standard path: tenant registration -> system admin approval -> onboarding -> Stripe checkout.
- Temporary backend bypass is available (development-only) to seed an active tenant admin without Stripe. When enabled in backend `.env`, you can login with `TEMP_BYPASS_TENANT_ADMIN_EMAIL` / `TEMP_BYPASS_TENANT_ADMIN_PASSWORD`.
- Public tenant registration now uses the same auth theme as user registration and requires:
  - Business name
  - Tenant admin email
  - Phone number
  - Business registration number
  - Business type dropdown (`Vehicle Rental`, `Accommodation`)

## Admin Ownership Split

- System admin screens and APIs (control-plane): tenant registrations, tenants, plans, enforcement.
- Tenant admin screens and APIs (tenant DB): users, roles, permissions, components, actions.
- User enforcement calls are system-admin only and use backend route `POST /api/system/enforcement/tenants/{tenantId}/users/{userId}/{action}`.
- Tenant-admin user deactivate/activate is tenant-membership state only (tenant DB) and is not global enforcement.
- The enforcement UI requires both `tenantId` and `userId` to avoid cross-tenant enforcement ambiguity.
- Roles screen lists system roles and custom roles together, with system roles shown first.
- User role edit UI blocks removing `ADMIN` from the last tenant admin in a tenant.

Disable this bypass once Stripe onboarding is configured by setting `TEMP_BYPASS_SEED_TENANT_ADMIN=false` in backend `.env`.

## Temporary Tenant-Status Access Gate Toggle

RBAC (roles/permissions) remains enforced in frontend guards.

For local pre-Stripe testing, tenant-status blocking is temporarily disabled in:
- `src/app/core/auth/access.guard.ts` (`enforceTenantActiveStatus = false`)

To roll back to strict paid-only tenant access later:
1. Set `enforceTenantActiveStatus = true` in `src/app/core/auth/access.guard.ts`.
2. Restart frontend test/dev server.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Karma](https://karma-runner.github.io) test runner, use the following command:

```bash
ng test
```

For CI/headless local runs:

```bash
ng test --watch=false --browsers=ChromeHeadless
```

Test environment behavior:
- Unit tests use `src/environments/environment.test.ts` via Angular `test.fileReplacements`.
- `mockApi` is intentionally `true` in test runs so service specs execute against in-memory/mock paths.
- Backend flags like `TEMP_BYPASS_SEED_TENANT_ADMIN` do not control frontend unit test behavior.

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
