export interface LanguageOption {
  code: string;
  label: string;
}

// Central list keeps language support consistent across app components.
export const SUPPORTED_LANGUAGES: LanguageOption[] = [
  { code: 'en', label: 'English' },
  { code: 'si', label: 'සිංහල' },
  { code: 'ta', label: 'தமிழ்' },
];
