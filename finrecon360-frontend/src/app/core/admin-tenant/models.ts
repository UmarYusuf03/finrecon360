export interface TenantRegistrationSummary {
  id: string;
  businessName: string;
  adminEmail: string;
  phoneNumber?: string;
  businessRegistrationNumber?: string;
  businessType?: string;
  status: string;
  submittedAt: string;
}

export interface TenantRegistrationApprovalResult {
  requestId: string;
  adminEmail: string;
  onboardingLink?: string | null;
  emailSent: boolean;
  emailError?: string | null;
}

export interface TenantSummary {
  id: string;
  name: string;
  status: string;
  createdAt: string;
  currentPlan?: string | null;
}

export interface TenantAdmin {
  userId: string;
  email: string;
  displayName: string;
  status: string;
}

export interface TenantDetail {
  id: string;
  name: string;
  status: string;
  createdAt: string;
  activatedAt?: string | null;
  primaryDomain?: string | null;
  currentSubscription?: {
    subscriptionId: string;
    planCode: string;
    planName: string;
    status: string;
    periodStart?: string | null;
    periodEnd?: string | null;
  } | null;
  admins: TenantAdmin[];
}

export interface PlanSummary {
  id: string;
  code: string;
  name: string;
  priceCents: number;
  currency: string;
  durationDays: number;
  maxUsers: number;
  maxAccounts: number;
  isActive: boolean;
}
