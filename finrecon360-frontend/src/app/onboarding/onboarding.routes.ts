import type { Routes } from '@angular/router';

export const onboardingRoutes: Routes = [
  {
    path: 'subscribe',
    loadComponent: () => import('./pages/subscribe/subscribe').then((m) => m.OnboardingSubscribeComponent),
  },
];
