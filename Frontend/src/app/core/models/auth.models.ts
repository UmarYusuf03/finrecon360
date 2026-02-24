import { UserRole } from '../constants/roles.enum';

/**
 * User Model
 * Represents a user in the FinRecon360 system.
 */
export interface User {
    id: string;
    email: string;
    fullName: string;
    role: UserRole;
    department?: string;
    isActive: boolean;
    createdAt: Date;
    lastLogin?: Date;
}

/**
 * Authentication Response
 * Returned from the backend after successful login.
 * Contains JWT token and user information.
 */
export interface AuthResponse {
    token: string;
    refreshToken: string;
    user: User;
    expiresAt: Date;
}

/**
 * Login Request
 * Data sent to the backend for authentication.
 */
export interface LoginRequest {
    email: string;
    password: string;
}

/**
 * Register Request
 * Data sent to the backend for user registration.
 */
export interface RegisterRequest {
    email: string;
    password: string;
    fullName: string;
    country: string;
    dateOfBirth: string;
    gender: string;
}

/**
 * Token Refresh Request
 * Used to refresh an expired access token.
 */
export interface TokenRefreshRequest {
    refreshToken: string;
}

/**
 * Password Reset Request
 * Used for forgot password flow.
 */
export interface PasswordResetRequest {
    email: string;
}

/**
 * Password Reset Confirm
 * Used to set a new password with a reset token.
 */
export interface PasswordResetConfirm {
    token: string;
    newPassword: string;
}
