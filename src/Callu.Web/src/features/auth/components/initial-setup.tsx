import { Fragment, useState, useEffect } from 'react';
import { useNavigate } from 'react-router';
import { authService } from '@/shared/auth/auth.service';
import { API_URL } from '@/shared/config';
import { Button } from '@/shared/components/ui/button';
import { Input } from '@/shared/components/ui/input';
import { Label } from '@/shared/components/ui/label';
import { Alert, AlertDescription } from '@/shared/components/ui/alert';
import {
  User,
  Mail,
  Lock,
  Globe,
  CheckCircle,
  ArrowRight,
  ArrowLeft,
  Eye,
  EyeOff,
  AlertCircle,
  Sparkles,
  Check,
  Search,
  ChevronDown,
} from 'lucide-react';
import { t } from '@/shared/locales/i18n';

interface SetupData {
  name: string;
  email: string;
  password: string;
  confirmPassword: string;
  defaultTimezone: string;
}

interface TimezoneOption {
  value: string;
  label: string;
}

const passwordRequirements = [
  { id: 'length', label: 'auth.passwordReqMinChars', test: (pw: string) => pw.length >= 12 },
  { id: 'uppercase', label: 'auth.passwordReqUppercase', test: (pw: string) => /[A-Z]/.test(pw) },
  { id: 'lowercase', label: 'auth.passwordReqLowercase', test: (pw: string) => /[a-z]/.test(pw) },
  { id: 'number', label: 'auth.passwordReqNumber', test: (pw: string) => /\d/.test(pw) },
  { id: 'special', label: 'auth.passwordReqSpecial', test: (pw: string) => /[^A-Za-z0-9]/.test(pw) },
  { id: 'unique', label: 'auth.passwordReqUnique', test: (pw: string) => new Set(pw).size >= 4 },
];

export function InitialSetup() {
  const navigate = useNavigate();
  const [currentStep, setCurrentStep] = useState(1);
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState('');
  const [timezones, setTimezones] = useState<TimezoneOption[]>([]);
  const [, setTimezonesLoading] = useState(true);
  const [tzSearch, setTzSearch] = useState('');
  const [tzDropdownOpen, setTzDropdownOpen] = useState(false);

  const [setupData, setSetupData] = useState<SetupData>({
    name: '',
    email: '',
    password: '',
    confirmPassword: '',
    defaultTimezone: 'Europe/Istanbul',
  });

  const totalSteps = 3;

  const updateField = (field: keyof SetupData, value: string) => {
    setSetupData((prev) => ({ ...prev, [field]: value }));
    setError('');
  };

  const validateStep = (step: number): boolean => {
    switch (step) {
      case 1:
        if (!setupData.name.trim()) {
          setError(t('initialSetup.nameRequired'));
          return false;
        }
        if (!setupData.email.trim() || !/\S+@\S+\.\S+/.test(setupData.email)) {
          setError(t('initialSetup.validEmailRequired'));
          return false;
        }
        return true;
      case 2: {
        const allRequirementsMet = passwordRequirements.every((req) =>
          req.test(setupData.password)
        );
        if (!allRequirementsMet) {
          setError(t('auth.meetAllPasswordRequirements'));
          return false;
        }
        if (setupData.password !== setupData.confirmPassword) {
          setError(t('initialSetup.passwordsDoNotMatch'));
          return false;
        }
        return true;
      }
      default:
        return true;
    }
  };

  const handleNext = () => {
    if (validateStep(currentStep)) {
      setCurrentStep((prev) => Math.min(prev + 1, totalSteps));
    }
  };

  const handleBack = () => {
    setCurrentStep((prev) => Math.max(prev - 1, 1));
    setError('');
  };

  useEffect(() => {
    (async () => {
      try {
        const res = await fetch(`${API_URL}/api/v1/setup/status`);
        if (res.ok) {
          const json = await res.json();
          const setupRequired = json.data?.setupRequired ?? json.setupRequired;
          if (!setupRequired) {
            navigate('/login');
          }
        }
      } catch {
        /* empty */
      }
    })();
  }, [navigate]);

  useEffect(() => {
    (async () => {
      try {
        const res = await fetch(`${API_URL}/api/v1/settings/localization/timezones`);
        if (res.ok) {
          const json = await res.json();
          const tzList = json.data ?? json;
          const mapped = (Array.isArray(tzList) ? tzList : []).map((tz: { id: string; displayName: string }) => ({
            value: tz.id,
            label: tz.displayName,
          }));
          setTimezones(mapped);
        }
      } catch {
        setTimezones([{ value: 'Europe/Istanbul', label: '(UTC+03:00) Istanbul' }]);
      } finally {
        setTimezonesLoading(false);
      }
    })();
  }, []);

  const handleSubmit = async () => {
    if (!validateStep(2)) return;

    setIsSubmitting(true);
    setError('');

    try {
      const response = await fetch(`${API_URL}/api/v1/setup/initial`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: setupData.email,
          password: setupData.password,
          name: setupData.name,
          defaultTimezone: setupData.defaultTimezone,
        }),
      });

      if (!response.ok) {
        const body = await response.json().catch(() => null);
        throw new Error(body?.message || t('initialSetup.setupFailed'));
      }

      await authService.login(setupData.email, setupData.password);

      setCurrentStep(3);
      setIsSubmitting(false);

      setTimeout(() => {
        navigate('/dashboard');
      }, 2000);
    } catch (err: unknown) {
      setIsSubmitting(false);
      setError(err instanceof Error ? err.message : t('initialSetup.setupFailed'));
    }
  };

  const getPasswordRequirementStatus = (requirement: typeof passwordRequirements[0]) => {
    if (!setupData.password) return 'idle';
    return requirement.test(setupData.password) ? 'valid' : 'invalid';
  };

  return (
    <div className="min-h-screen flex items-center justify-center p-4 bg-background">
      <div className="absolute inset-0 overflow-hidden pointer-events-none">
        <div className="absolute top-20 left-10 w-72 h-72 bg-brand-500/5 rounded-full blur-3xl" />
        <div className="absolute bottom-20 right-10 w-96 h-96 bg-purple-500/5 rounded-full blur-3xl" />
      </div>

      <div className="relative w-full max-w-2xl">
        <div className="text-center mb-8">
          <div className="inline-flex items-center gap-3 mb-6">
            <img src="/callu-logo.png" alt="Callu" className="h-12 w-auto" />
          </div>
          <h1 style={{ fontSize: '1.875rem', fontWeight: 600, lineHeight: 1.3 }}>
            {t('initialSetup.welcomeToCalluApp')}
          </h1>
          <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginTop: '0.5rem' }}>
            {t('initialSetup.setupDescription')}
          </p>
        </div>

        <div className="mb-8">
          <div className="flex items-start">
            {[
              { step: 1, label: t('initialSetup.stepAdminAccount') },
              { step: 2, label: t('initialSetup.stepSettings') },
              { step: 3, label: t('initialSetup.stepComplete') },
            ].map(({ step, label }, idx, arr) => (
              <Fragment key={step}>
                <div className="flex flex-col items-center min-w-[88px]">
                  <div
                    className={`w-10 h-10 rounded-full flex items-center justify-center font-semibold transition-all ${step < currentStep
                      ? 'bg-brand-500 text-white'
                      : step === currentStep
                        ? 'bg-brand-500 text-white ring-4 ring-brand-500/20'
                        : 'bg-surface-light/20 text-muted-foreground'
                      }`}
                    style={{ fontSize: '0.875rem' }}
                  >
                    {step < currentStep ? <Check className="w-5 h-5" /> : step}
                  </div>
                  <span
                    className={`mt-2 text-xs text-center transition-colors ${step <= currentStep ? 'text-foreground font-medium' : 'text-muted-foreground'
                      }`}
                  >
                    {label}
                  </span>
                </div>
                {idx < arr.length - 1 && (
                  <div
                    className={`flex-1 h-1 rounded-full transition-all ${step < currentStep ? 'bg-brand-500' : 'bg-surface-light/20'
                      }`}
                    style={{ marginTop: '18px' }}
                  />
                )}
              </Fragment>
            ))}
          </div>
        </div>

        {error && (
          <Alert className="border-error-500 bg-error-500/10 mb-6">
            <AlertCircle className="h-4 w-4 text-error-500" />
            <AlertDescription className="text-error-500">{error}</AlertDescription>
          </Alert>
        )}

        <div className="bg-card/80 backdrop-blur-sm border border-border rounded-xl p-8 shadow-xl">
          {currentStep === 1 && (
            <div className="space-y-6">
              <div className="text-center mb-6">
                <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-brand-500/10 mb-4">
                  <User className="w-8 h-8 text-brand-500" />
                </div>
                <h2 style={{ fontSize: '1.5rem', fontWeight: 600, marginBottom: '0.5rem' }}>
                  {t('initialSetup.adminAccount')}
                </h2>
                <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
                  {t('initialSetup.createAdminAccount')}
                </p>
              </div>

              <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="name" style={{ fontSize: '0.875rem', fontWeight: 500 }}>
                    {t('initialSetup.fullName')}
                  </Label>
                  <div className="relative">
                    <User className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-muted-foreground z-10 pointer-events-none" />
                    <Input
                      id="name"
                      placeholder={t("auth.fullNamePlaceholder")}
                      value={setupData.name}
                      onChange={(e) => updateField('name', e.target.value)}
                      className="pl-10 bg-input-background backdrop-blur-sm border-border"
                      style={{ fontSize: '0.875rem' }}
                    />
                  </div>
                </div>

                <div className="space-y-2">
                  <Label htmlFor="email" style={{ fontSize: '0.875rem', fontWeight: 500 }}>
                    {t('auth.emailAddress')} *
                  </Label>
                  <div className="relative">
                    <Mail className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-muted-foreground z-10 pointer-events-none" />
                    <Input
                      id="email"
                      type="email"
                      placeholder={t("initialSetup.adminEmailPlaceholder")}
                      value={setupData.email}
                      onChange={(e) => updateField('email', e.target.value)}
                      className="pl-10 bg-input-background backdrop-blur-sm border-border"
                      style={{ fontSize: '0.875rem' }}
                    />
                  </div>
                </div>
              </div>
            </div>
          )}

          {currentStep === 2 && (
            <div className="space-y-6">
              <div className="text-center mb-6">
                <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-brand-500/10 mb-4">
                  <Lock className="w-8 h-8 text-brand-500" />
                </div>
                <h2 style={{ fontSize: '1.5rem', fontWeight: 600, marginBottom: '0.5rem' }}>
                  {t('initialSetup.securityAndSettings')}
                </h2>
                <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
                  {t('initialSetup.setPasswordAndPrefs')}
                </p>
              </div>

              <div className="space-y-4">
                <div className="space-y-2">
                  <Label htmlFor="password" style={{ fontSize: '0.875rem', fontWeight: 500 }}>
                    {t('auth.password')} *
                  </Label>
                  <div className="relative">
                    <Lock className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-muted-foreground z-10 pointer-events-none" />
                    <Input
                      id="password"
                      type={showPassword ? 'text' : 'password'}
                      placeholder={t("auth.passwordMaskedPlaceholder")}
                      value={setupData.password}
                      onChange={(e) => updateField('password', e.target.value)}
                      className="pl-10 pr-10 bg-input-background backdrop-blur-sm border-border"
                      style={{ fontSize: '0.875rem' }}
                    />
                    <button
                      type="button"
                      onClick={() => setShowPassword(!showPassword)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                    >
                      {showPassword ? <EyeOff className="h-5 w-5" /> : <Eye className="h-5 w-5" />}
                    </button>
                  </div>
                </div>

                <div className="space-y-2">
                  <Label
                    htmlFor="confirmPassword"
                    style={{ fontSize: '0.875rem', fontWeight: 500 }}
                  >
                    {t('auth.confirmPassword')} *
                  </Label>
                  <div className="relative">
                    <Lock className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-muted-foreground z-10 pointer-events-none" />
                    <Input
                      id="confirmPassword"
                      type={showConfirmPassword ? 'text' : 'password'}
                      placeholder={t("auth.passwordMaskedPlaceholder")}
                      value={setupData.confirmPassword}
                      onChange={(e) => updateField('confirmPassword', e.target.value)}
                      className="pl-10 pr-10 bg-input-background backdrop-blur-sm border-border"
                      style={{ fontSize: '0.875rem' }}
                    />
                    <button
                      type="button"
                      onClick={() => setShowConfirmPassword(!showConfirmPassword)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                    >
                      {showConfirmPassword ? (
                        <EyeOff className="h-5 w-5" />
                      ) : (
                        <Eye className="h-5 h-5" />
                      )}
                    </button>
                  </div>
                </div>

                {setupData.password && (
                  <div className="p-4 rounded-lg bg-surface-light/20 border border-border-light">
                    <p
                      style={{
                        fontSize: '0.75rem',
                        fontWeight: 600,
                        marginBottom: '0.75rem',
                        color: '#94A3B8',
                        textTransform: 'uppercase',
                        letterSpacing: '0.05em',
                      }}
                    >
                      {t('auth.passwordRequirements')}
                    </p>
                    <div className="grid grid-cols-2 gap-2">
                      {passwordRequirements.map((req) => {
                        const status = getPasswordRequirementStatus(req);
                        return (
                          <div key={req.id} className="flex items-center gap-2">
                            {status === 'valid' ? (
                              <CheckCircle className="w-4 h-4 text-success-500 flex-shrink-0" />
                            ) : (
                              <div className="w-4 h-4 rounded-full border-2 border-muted/30 flex-shrink-0" />
                            )}
                            <span
                              style={{
                                fontSize: '0.75rem',
                                color: status === 'valid' ? '#22C55E' : '#94A3B8',
                              }}
                            >
                              {t(req.label)}
                            </span>
                          </div>
                        );
                      })}
                    </div>
                  </div>
                )}

                <div className="space-y-2">
                  <Label htmlFor="timezone" style={{ fontSize: '0.875rem', fontWeight: 500 }}>
                    {t('initialSetup.defaultTimezone')}
                  </Label>
                  <div className="relative">
                    <Globe className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-muted-foreground pointer-events-none z-10" />
                    <button
                      type="button"
                      onClick={() => setTzDropdownOpen(!tzDropdownOpen)}
                      className="w-full flex items-center justify-between pl-10 pr-3 py-2 rounded-md bg-input-background backdrop-blur-sm border border-border text-left hover:border-brand-500/50 transition-colors"
                      style={{ fontSize: '0.875rem', minHeight: '40px' }}
                    >
                      <span className={setupData.defaultTimezone ? '' : 'text-muted-foreground'}>
                        {timezones.find(tz => tz.value === setupData.defaultTimezone)?.label || t('initialSetup.selectTimezone')}
                      </span>
                      <ChevronDown className={`w-4 h-4 text-muted-foreground transition-transform ${tzDropdownOpen ? 'rotate-180' : ''}`} />
                    </button>

                    {tzDropdownOpen && (
                      <div className="absolute z-50 mt-1 w-full max-h-64 bg-card border border-border rounded-lg shadow-xl overflow-hidden">
                        <div className="sticky top-0 bg-card p-2 border-b border-border">
                          <div className="relative">
                            <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                            <input
                              type="text"
                              placeholder={t('initialSetup.searchTimezones')}
                              value={tzSearch}
                              onChange={(e) => setTzSearch(e.target.value)}
                              className="w-full pl-8 pr-3 py-1.5 rounded-md bg-input-background border border-border text-foreground placeholder:text-muted-foreground focus:outline-none focus:ring-1 focus:ring-brand-500"
                              style={{ fontSize: '0.8125rem' }}
                              autoFocus
                            />
                          </div>
                        </div>
                        <div className="overflow-y-auto max-h-48">
                          {timezones
                            .filter(tz => tz.label.toLowerCase().includes(tzSearch.toLowerCase()) || tz.value.toLowerCase().includes(tzSearch.toLowerCase()))
                            .map((tz) => (
                              <button
                                key={tz.value}
                                type="button"
                                onClick={() => {
                                  updateField('defaultTimezone', tz.value);
                                  setTzDropdownOpen(false);
                                  setTzSearch('');
                                }}
                                className={`w-full text-left px-3 py-2 hover:bg-brand-500/10 transition-colors ${setupData.defaultTimezone === tz.value ? 'bg-brand-500/10 text-brand-500' : 'text-foreground'
                                  }`}
                                style={{ fontSize: '0.8125rem' }}
                              >
                                {tz.label}
                              </button>
                            ))}
                          {timezones.filter(tz => tz.label.toLowerCase().includes(tzSearch.toLowerCase()) || tz.value.toLowerCase().includes(tzSearch.toLowerCase())).length === 0 && (
                            <div className="px-3 py-4 text-center text-muted-foreground" style={{ fontSize: '0.8125rem' }}>
                              {t('initialSetup.noTimezonesFound')}
                            </div>
                          )}
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              </div>
            </div>
          )}

          {currentStep === 3 && (
            <div className="text-center py-8">
              <div className="inline-flex items-center justify-center w-20 h-20 rounded-full bg-success-500/10 mb-6 animate-pulse">
                <CheckCircle className="w-10 h-10 text-success-500" />
              </div>
              <h2 style={{ fontSize: '1.875rem', fontWeight: 600, marginBottom: '0.75rem' }}>
                {t('initialSetup.setupComplete')}
              </h2>
              <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginBottom: '1.5rem' }}>
                {t('initialSetup.workspaceReady')}
              </p>

              <div className="p-6 rounded-lg bg-brand-500/5 border border-brand-500/20 text-left max-w-md mx-auto">
                <div className="flex items-start gap-3 mb-4">
                  <Sparkles className="w-5 h-5 text-brand-500 flex-shrink-0 mt-0.5" />
                  <div>
                    <p style={{ fontSize: '0.875rem', fontWeight: 600, marginBottom: '0.25rem' }}>
                      {t('initialSetup.whatsNext')}
                    </p>
                    <p style={{ fontSize: '0.75rem', color: '#94A3B8' }}>
                      {t('initialSetup.startConfiguring')}
                    </p>
                  </div>
                </div>
                <ul className="space-y-2 text-sm text-muted-foreground">
                  <li className="flex items-center gap-2">
                    <CheckCircle className="w-4 h-4 text-success-500 flex-shrink-0" />
                    <span style={{ fontSize: '0.8125rem' }}>{t('initialSetup.addTeamMembers')}</span>
                  </li>
                  <li className="flex items-center gap-2">
                    <CheckCircle className="w-4 h-4 text-success-500 flex-shrink-0" />
                    <span style={{ fontSize: '0.8125rem' }}>{t('initialSetup.connectServices')}</span>
                  </li>
                  <li className="flex items-center gap-2">
                    <CheckCircle className="w-4 h-4 text-success-500 flex-shrink-0" />
                    <span style={{ fontSize: '0.8125rem' }}>{t('initialSetup.configureEscalations')}</span>
                  </li>
                </ul>
              </div>

              <div className="mt-6">
                <div className="w-48 h-2 bg-surface-light/20 rounded-full mx-auto overflow-hidden">
                  <div className="h-full bg-brand-500 rounded-full animate-[loading_2s_ease-in-out]" />
                </div>
              </div>
            </div>
          )}

          {currentStep < 3 && (
            <div className="flex items-center justify-between mt-8 pt-6 border-t border-border">
              <Button
                variant="outline"
                onClick={handleBack}
                disabled={currentStep === 1}
                className="bg-input-background"
              >
                <ArrowLeft className="w-4 h-4 mr-2" />
                {t('common.back')}
              </Button>

              {currentStep < 2 ? (
                <Button
                  onClick={handleNext}
                  className="bg-brand-500 hover:bg-brand-600 text-white"
                >
                  {t('common.continue')}
                  <ArrowRight className="w-4 h-4 ml-2" />
                </Button>
              ) : (
                <Button
                  onClick={handleSubmit}
                  disabled={isSubmitting}
                  className="bg-brand-500 hover:bg-brand-600 text-white min-w-[140px]"
                >
                  {isSubmitting ? (
                    <>
                      <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                      {t('initialSetup.settingUp')}
                    </>
                  ) : (
                    <>
                      {t('initialSetup.completeSetup')}
                      <CheckCircle className="w-4 h-4 ml-2" />
                    </>
                  )}
                </Button>
              )}
            </div>
          )}
        </div>

        <div className="text-center mt-6">
          <p style={{ fontSize: '0.75rem', color: '#94A3B8' }}>
            {t('initialSetup.byCompletingSetup')}{' '}
            <a
              href="/legal/terms"
              target="_blank"
              rel="noopener noreferrer"
              className="text-brand-500 hover:text-brand-400 transition-colors"
            >
              {t('initialSetup.termsOfService')}
            </a>{' '}
            {t('common.and')}{' '}
            <a
              href="/legal/privacy"
              target="_blank"
              rel="noopener noreferrer"
              className="text-brand-500 hover:text-brand-400 transition-colors"
            >
              {t('initialSetup.privacyPolicy')}
            </a>
          </p>
        </div>
      </div>

      <style>{`
        @keyframes loading {
          0% { width: 0%; }
          100% { width: 100%; }
        }
      `}</style>
    </div>
  );
}
