import { environment } from '../../../environments/environment';

/**
 * API Configuration Constants
 *
 * Centralized location for all API-related configuration.
 */

/**
 * Base URL for the backend API.
 * For production, override via environment files.
 */
export const API_BASE_URL = environment.apiBaseUrl;

/**
 * Toggle mock data vs backend API calls.
 */
export const USE_MOCK_API = environment.mockApi;

/**
 * API Endpoints for different modules in the FinRecon360 system.
 * Each endpoint is relative to the API_BASE_URL.
 */
export const API_ENDPOINTS = {
  AUTH: {
    LOGIN: '/api/auth/login',
    REGISTER: '/api/auth/register',
    VERIFY_EMAIL_LINK: '/api/auth/verify-email-link',
    REQUEST_PASSWORD_RESET_LINK: '/api/auth/request-password-reset-link',
    CONFIRM_PASSWORD_RESET_LINK: '/api/auth/confirm-password-reset-link',
    REQUEST_CHANGE_PASSWORD_LINK: '/api/auth/request-change-password-link',
    CONFIRM_CHANGE_PASSWORD_LINK: '/api/auth/confirm-change-password-link',
  },
  ME: '/api/me',
  USERS: {
    BASE: '/api/users',
    BY_ID: (id: string) => `/api/users/${id}`,
    PROFILE: '/api/users/profile',
    UPDATE_PROFILE: '/api/users/profile',
    BY_ROLE: (role: string) => `/api/users/by-role/${role}`,
  },
  MATCHER: {
    BASE: '/api/matcher',
    TRANSACTIONS: '/api/matcher/transactions',
    MATCH: '/api/matcher/match',
    UNMATCH: '/api/matcher/unmatch',
    RULES: '/api/matcher/rules',
  },
  BALANCER: {
    BASE: '/api/balancer',
    RECONCILE: '/api/balancer/reconcile',
    ACCOUNTS: '/api/balancer/accounts',
    DISCREPANCIES: '/api/balancer/discrepancies',
  },
  TASKS: {
    BASE: '/api/tasks',
    BY_ID: (id: string) => `/api/tasks/${id}`,
    ASSIGN: '/api/tasks/assign',
    COMPLETE: '/api/tasks/complete',
  },
  JOURNAL: {
    BASE: '/api/journal',
    ENTRIES: '/api/journal/entries',
    BY_ID: (id: string) => `/api/journal/entries/${id}`,
    POST: '/api/journal/post',
  },
  ANALYTICS: {
    DASHBOARD: '/api/analytics/dashboard',
    REPORTS: '/api/analytics/reports',
    METRICS: '/api/analytics/metrics',
  },
  ADMIN: {
    ROLES: '/api/admin/roles',
    PERMISSIONS: '/api/admin/permissions',
    ACTIONS: '/api/admin/actions',
    COMPONENTS: '/api/admin/components',
    USERS: '/api/admin/users',
  },
} as const;

/**
 * HTTP request timeout in milliseconds
 */
export const REQUEST_TIMEOUT = 30000; // 30 seconds
