export interface UserProfileDetails {
  displayName: string;
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string | null;
  roles: string[];
  preferredLanguage: string;
  timeZone: string;
  emailNotifications: boolean;
  hasProfileImage: boolean;
}
