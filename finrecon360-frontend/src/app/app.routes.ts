import type { Routes } from '@angular/router';
import { authRoutes } from './auth/auth.routes';
import { mainRoutes } from './main/main.routes';
import { onboardingRoutes } from './onboarding/onboarding.routes';
import { publicRoutes } from './public/public.routes';

export const routes: Routes = [
  // Default: go to login
  {
    path: '',
    redirectTo: 'auth/login',
    pathMatch: 'full',
  },

  // Public / authentication area
  {
    path: 'auth',
    children: authRoutes,
  },

  {
    path: 'public',
    children: publicRoutes,
  },

  {
    path: 'onboarding',
    children: onboardingRoutes,
  },

  // Main application area after login
  {
    path: 'app',
    children: mainRoutes,
  },

  // Fallback
  {
    path: '**',
    redirectTo: 'auth/login',
  },
];
