import { useState, useEffect } from "react";
import { toast } from "@/shared/utils/toast";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import { Card } from "@/shared/components/ui/card";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/shared/components/ui/tabs";
import {
  Mail,
  Save,
  Eye,
  Code,
  Sparkles,
  CheckCircle,
  AlertCircle,
  Copy,
  Send,
  ChevronRight,
  Home,
} from "lucide-react";
import { Link } from "react-router";
import {
  useEmailTemplates,
  useEmailTemplate,
  useCreateEmailTemplate,
  useUpdateEmailTemplate,
  useSendTestEmail,
} from '../hooks/use-email-templates';
import type { EmailTemplateDto } from '../types/email-template.types';

const defaultTemplates: Record<string, {
  id: string; name: string; subject: string; htmlContent: string;
  textContent: string; variables: string[]; description: string;
}> = {
  "connection-test": {
    id: "connection-test",
    name: "Connection Test",
    subject: "CalluApp - SMTP Connection Test",
    description: "Test email to verify SMTP configuration",
    variables: ["organization_name", "test_date", "smtp_host"],
    htmlContent: `<!DOCTYPE html>
<html>
<head>
  <style>
    body { font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif; margin: 0; padding: 0; background-color: #0F172A; }
    .container { max-width: 600px; margin: 40px auto; background: linear-gradient(135deg, #1E293B 0%, #0F172A 100%); border-radius: 16px; overflow: hidden; border: 1px solid #334155; }
    .header { background: linear-gradient(135deg, #3E7BFA 0%, #2563EB 100%); padding: 40px 30px; text-align: center; }
    .header h1 { color: #FFFFFF; margin: 0; font-size: 28px; font-weight: 700; }
    .content { padding: 40px 30px; color: #E2E8F0; }
    .content h2 { color: #FFFFFF; font-size: 20px; margin-bottom: 16px; }
    .content p { line-height: 1.6; margin-bottom: 16px; color: #CBD5E1; }
    .info-box { background: #334155; border-left: 4px solid #3E7BFA; padding: 16px; border-radius: 8px; margin: 24px 0; }
    .info-box strong { color: #3E7BFA; display: block; margin-bottom: 8px; }
    .footer { background: #1E293B; padding: 24px 30px; text-align: center; border-top: 1px solid #334155; }
    .footer p { margin: 0; font-size: 14px; color: #94A3B8; }
    .button { display: inline-block; padding: 12px 24px; background: linear-gradient(135deg, #3E7BFA 0%, #2563EB 100%); color: #FFFFFF; text-decoration: none; border-radius: 8px; font-weight: 600; margin: 16px 0; }
  </style>
</head>
<body>
  <div class="container">
    <div class="header">
      <h1>🔔 CalluApp</h1>
    </div>
    <div class="content">
      <h2>SMTP Connection Successful!</h2>
      <p>This is a test email to confirm that your email configuration is working correctly.</p>
      
      <div class="info-box">
        <strong>Test Details</strong>
        <p style="margin: 0;">Organization: {{organization_name}}</p>
        <p style="margin: 0;">Test Date: {{test_date}}</p>
        <p style="margin: 0;">SMTP Server: {{smtp_host}}</p>
      </div>

      <p>Your CalluApp instance is now ready to send incident notifications, invitations, and alerts.</p>
      
      <a href="#" class="button">Go to Dashboard</a>
    </div>
    <div class="footer">
      <p>© 2024 CalluApp. All rights reserved.</p>
    </div>
  </div>
</body>
</html>`,
    textContent: `CalluApp - SMTP Connection Test

SMTP Connection Successful!

This is a test email to confirm that your email configuration is working correctly.

Test Details:
- Organization: {{organization_name}}
- Test Date: {{test_date}}
- SMTP Server: {{smtp_host}}

Your CalluApp instance is now ready to send incident notifications, invitations, and alerts.

© 2024 CalluApp. All rights reserved.`,
  },
  invitation: {
    id: "invitation",
    name: "User Invitation",
    subject: "You've been invited to join {{organization_name}} on CalluApp",
    description: "Email sent when inviting new users to the organization",
    variables: [
      "organization_name",
      "inviter_name",
      "invitee_email",
      "invitation_link",
      "expiry_date",
    ],
    htmlContent: `<!DOCTYPE html>
<html>
<head>
  <style>
    body { font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif; margin: 0; padding: 0; background-color: #0F172A; }
    .container { max-width: 600px; margin: 40px auto; background: linear-gradient(135deg, #1E293B 0%, #0F172A 100%); border-radius: 16px; overflow: hidden; border: 1px solid #334155; }
    .header { background: linear-gradient(135deg, #3E7BFA 0%, #2563EB 100%); padding: 40px 30px; text-align: center; }
    .header h1 { color: #FFFFFF; margin: 0; font-size: 28px; font-weight: 700; }
    .content { padding: 40px 30px; color: #E2E8F0; }
    .content h2 { color: #FFFFFF; font-size: 20px; margin-bottom: 16px; }
    .content p { line-height: 1.6; margin-bottom: 16px; color: #CBD5E1; }
    .highlight-box { background: linear-gradient(135deg, #A855F7 0%, #7C3AED 100%); padding: 24px; border-radius: 12px; text-align: center; margin: 24px 0; }
    .highlight-box h3 { color: #FFFFFF; margin: 0 0 8px 0; font-size: 18px; }
    .highlight-box p { color: #E9D5FF; margin: 0; font-size: 14px; }
    .button { display: inline-block; padding: 14px 32px; background: linear-gradient(135deg, #22C55E 0%, #16A34A 100%); color: #FFFFFF; text-decoration: none; border-radius: 8px; font-weight: 600; margin: 16px 0; font-size: 16px; }
    .expiry { background: #FEF3C7; color: #92400E; padding: 12px; border-radius: 8px; text-align: center; font-size: 14px; margin: 24px 0; }
    .footer { background: #1E293B; padding: 24px 30px; text-align: center; border-top: 1px solid #334155; }
    .footer p { margin: 0; font-size: 14px; color: #94A3B8; }
  </style>
</head>
<body>
  <div class="container">
    <div class="header">
      <h1>🔔 CalluApp</h1>
    </div>
    <div class="content">
      <h2>Welcome to the Team!</h2>
      <p>Hi there! 👋</p>
      <p><strong>{{inviter_name}}</strong> has invited you to join <strong>{{organization_name}}</strong> on CalluApp.</p>
      
      <div class="highlight-box">
        <h3>Your Account</h3>
        <p>{{invitee_email}}</p>
      </div>

      <p>CalluApp helps your team respond to incidents faster with intelligent alerting, on-call scheduling, and seamless integrations.</p>

      <center>
        <a href="{{invitation_link}}" class="button">Accept Invitation & Set Password</a>
      </center>

      <div class="expiry">
        ⏰ This invitation expires on {{expiry_date}}
      </div>

      <p style="font-size: 14px; color: #94A3B8;">If you weren't expecting this invitation, you can safely ignore this email.</p>
    </div>
    <div class="footer">
      <p>© 2024 CalluApp. All rights reserved.</p>
    </div>
  </div>
</body>
</html>`,
    textContent: `CalluApp - You've been invited!

Welcome to the Team!

{{inviter_name}} has invited you to join {{organization_name}} on CalluApp.

Your Account: {{invitee_email}}

CalluApp helps your team respond to incidents faster with intelligent alerting, on-call scheduling, and seamless integrations.

Accept your invitation and set your password here:
{{invitation_link}}

⏰ This invitation expires on {{expiry_date}}

If you weren't expecting this invitation, you can safely ignore this email.

© 2024 CalluApp. All rights reserved.`,
  },
  "password-reset": {
    id: "password-reset",
    name: "Password Reset",
    subject: "Reset your CalluApp password",
    description: "Email sent when users request a password reset",
    variables: ["user_name", "reset_link", "expiry_minutes", "request_ip"],
    htmlContent: `<!DOCTYPE html>
<html>
<head>
  <style>
    body { font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif; margin: 0; padding: 0; background-color: #0F172A; }
    .container { max-width: 600px; margin: 40px auto; background: linear-gradient(135deg, #1E293B 0%, #0F172A 100%); border-radius: 16px; overflow: hidden; border: 1px solid #334155; }
    .header { background: linear-gradient(135deg, #FB923C 0%, #F97316 100%); padding: 40px 30px; text-align: center; }
    .header h1 { color: #FFFFFF; margin: 0; font-size: 28px; font-weight: 700; }
    .content { padding: 40px 30px; color: #E2E8F0; }
    .content h2 { color: #FFFFFF; font-size: 20px; margin-bottom: 16px; }
    .content p { line-height: 1.6; margin-bottom: 16px; color: #CBD5E1; }
    .button { display: inline-block; padding: 14px 32px; background: linear-gradient(135deg, #FB923C 0%, #F97316 100%); color: #FFFFFF; text-decoration: none; border-radius: 8px; font-weight: 600; margin: 16px 0; font-size: 16px; }
    .security-notice { background: #450A0A; border-left: 4px solid #EF4444; padding: 16px; border-radius: 8px; margin: 24px 0; color: #FCA5A5; }
    .security-notice strong { color: #FEE2E2; display: block; margin-bottom: 8px; }
    .code-box { background: #334155; padding: 16px; border-radius: 8px; font-family: 'Courier New', monospace; font-size: 14px; margin: 16px 0; color: #94A3B8; }
    .footer { background: #1E293B; padding: 24px 30px; text-align: center; border-top: 1px solid #334155; }
    .footer p { margin: 0; font-size: 14px; color: #94A3B8; }
  </style>
</head>
<body>
  <div class="container">
    <div class="header">
      <h1>🔐 CalluApp</h1>
    </div>
    <div class="content">
      <h2>Password Reset Request</h2>
      <p>Hi {{user_name}},</p>
      <p>We received a request to reset your password. Click the button below to choose a new password:</p>

      <center>
        <a href="{{reset_link}}" class="button">Reset Password</a>
      </center>

      <p style="font-size: 14px; color: #94A3B8;">This link will expire in {{expiry_minutes}} minutes for security reasons.</p>

      <div class="security-notice">
        <strong>⚠️ Security Notice</strong>
        <p style="margin: 0;">If you didn't request this password reset, please ignore this email. Your password will remain unchanged.</p>
        <p style="margin: 8px 0 0 0; font-size: 12px;">Request originated from IP: {{request_ip}}</p>
      </div>

      <p style="font-size: 14px; color: #94A3B8;">If the button doesn't work, copy and paste this link into your browser:</p>
      <div class="code-box">{{reset_link}}</div>
    </div>
    <div class="footer">
      <p>© 2024 CalluApp. All rights reserved.</p>
    </div>
  </div>
</body>
</html>`,
    textContent: `CalluApp - Password Reset Request

Hi {{user_name}},

We received a request to reset your password. Click the link below to choose a new password:

{{reset_link}}

This link will expire in {{expiry_minutes}} minutes for security reasons.

⚠️ Security Notice
If you didn't request this password reset, please ignore this email. Your password will remain unchanged.

Request originated from IP: {{request_ip}}

© 2024 CalluApp. All rights reserved.`,
  },
  "on-call-notification": {
    id: "on-call-notification",
    name: "On-Call Notification",
    subject: "🚨 New Incident: {{incident_title}}",
    description: "Email notification for new incidents",
    variables: [
      "incident_title",
      "incident_description",
      "incident_severity",
      "service_name",
      "incident_link",
      "acknowledge_link",
      "on_call_user",
    ],
    htmlContent: `<!DOCTYPE html>
<html>
<head>
  <style>
    body { font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif; margin: 0; padding: 0; background-color: #0F172A; }
    .container { max-width: 600px; margin: 40px auto; background: linear-gradient(135deg, #1E293B 0%, #0F172A 100%); border-radius: 16px; overflow: hidden; border: 1px solid #334155; }
    .header { background: linear-gradient(135deg, #FF4D4D 0%, #DC2626 100%); padding: 40px 30px; text-align: center; }
    .header h1 { color: #FFFFFF; margin: 0; font-size: 28px; font-weight: 700; }
    .alert-badge { display: inline-block; padding: 8px 16px; background: #FEE2E2; color: #991B1B; border-radius: 20px; font-size: 12px; font-weight: 700; text-transform: uppercase; margin-top: 12px; }
    .content { padding: 40px 30px; color: #E2E8F0; }
    .incident-card { background: #334155; border-left: 6px solid #FF4D4D; padding: 24px; border-radius: 12px; margin: 24px 0; }
    .incident-card h2 { color: #FFFFFF; margin: 0 0 16px 0; font-size: 22px; }
    .incident-card p { margin: 8px 0; line-height: 1.6; color: #CBD5E1; }
    .info-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin: 24px 0; }
    .info-item { background: #1E293B; padding: 16px; border-radius: 8px; border: 1px solid #334155; }
    .info-item .label { font-size: 12px; color: #94A3B8; text-transform: uppercase; margin-bottom: 4px; }
    .info-item .value { font-size: 16px; color: #FFFFFF; font-weight: 600; }
    .button-group { margin: 24px 0; }
    .button-primary { display: inline-block; padding: 14px 32px; background: linear-gradient(135deg, #22C55E 0%, #16A34A 100%); color: #FFFFFF; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px; margin-right: 12px; }
    .button-secondary { display: inline-block; padding: 14px 32px; background: #334155; color: #FFFFFF; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 16px; }
    .footer { background: #1E293B; padding: 24px 30px; text-align: center; border-top: 1px solid #334155; }
    .footer p { margin: 0; font-size: 14px; color: #94A3B8; }
  </style>
</head>
<body>
  <div class="container">
    <div class="header">
      <h1>🚨 New Incident</h1>
      <span class="alert-badge">{{incident_severity}}</span>
    </div>
    <div class="content">
      <div class="incident-card">
        <h2>{{incident_title}}</h2>
        <p>{{incident_description}}</p>
      </div>

      <div class="info-grid">
        <div class="info-item">
          <div class="label">Service</div>
          <div class="value">{{service_name}}</div>
        </div>
        <div class="info-item">
          <div class="label">On-Call</div>
          <div class="value">{{on_call_user}}</div>
        </div>
      </div>

      <div class="button-group">
        <a href="{{acknowledge_link}}" class="button-primary">✓ Acknowledge Incident</a>
        <a href="{{incident_link}}" class="button-secondary">View Details</a>
      </div>

      <p style="font-size: 14px; color: #94A3B8; margin-top: 24px;">
        ⏰ Please respond as soon as possible. If you don't acknowledge within the configured timeframe, this incident will be escalated.
      </p>
    </div>
    <div class="footer">
      <p>© 2024 CalluApp. All rights reserved.</p>
    </div>
  </div>
</body>
</html>`,
    textContent: `🚨 CalluApp - New Incident

{{incident_title}}
Severity: {{incident_severity}}

Description:
{{incident_description}}

Service: {{service_name}}
On-Call: {{on_call_user}}

Actions:
- Acknowledge: {{acknowledge_link}}
- View Details: {{incident_link}}

⏰ Please respond as soon as possible. If you don't acknowledge within the configured timeframe, this incident will be escalated.

© 2024 CalluApp. All rights reserved.`,
  },
};

export function EmailTemplates() {
  const { data: apiTemplates } = useEmailTemplates();
  const updateMutation = useUpdateEmailTemplate();
  const createMutation = useCreateEmailTemplate();
  const sendTestMutation = useSendTestEmail();

  const [selectedTemplate, setSelectedTemplate] = useState<string>("connection-test");
  const [activeTab, setActiveTab] = useState<"html" | "text" | "preview">("html");
  const [showSuccess, setShowSuccess] = useState(false);
  const [testEmail, setTestEmail] = useState("");

  const selectedApiId = (apiTemplates ?? []).find(
    (t: EmailTemplateDto) => t.key === selectedTemplate || t.id === selectedTemplate
  )?.id ?? '';
  const { data: templateDetail } = useEmailTemplate(selectedApiId);

  const fallback = defaultTemplates[selectedTemplate];
  const [subject, setSubject] = useState(fallback?.subject ?? '');
  const [htmlContent, setHtmlContent] = useState(fallback?.htmlContent ?? '');
  const [textContent, setTextContent] = useState(fallback?.textContent ?? '');

  useEffect(() => {
    if (templateDetail) {
      setSubject(templateDetail.subject);
      setHtmlContent(templateDetail.htmlBody);
      setTextContent(templateDetail.plainTextBody ?? '');
    } else if (fallback) {
      setSubject(fallback.subject);
      setHtmlContent(fallback.htmlContent);
      setTextContent(fallback.textContent);
    }
  }, [templateDetail, selectedTemplate, fallback]);

  const templateList: { id: string; key: string; name: string; description: string; variables: string[] }[] =
    apiTemplates && apiTemplates.length > 0
      ? (apiTemplates as EmailTemplateDto[]).map(t => ({
        id: t.id,
        key: t.key,
        name: t.name,
        description: (templateDetail && templateDetail.id === t.id ? templateDetail.description : '') ?? '',
        variables: [],
      }))
      : Object.values(defaultTemplates).map(t => ({
        id: t.id,
        key: t.id,
        name: t.name,
        description: t.description,
        variables: t.variables,
      }));

  const currentVariables = fallback?.variables ?? [];

  const handleTemplateChange = (templateKeyOrId: string) => {
    setSelectedTemplate(templateKeyOrId);
    setShowSuccess(false);
  };

  const handleSave = async () => {
    const targetId = selectedApiId || selectedTemplate;
    try {
      await updateMutation.mutateAsync({
        id: targetId,
        subject,
        htmlBody: htmlContent,
        plainTextBody: textContent,
      });
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('email.saveFailed'));
      return;
    }
    setShowSuccess(true);
    setTimeout(() => setShowSuccess(false), 3000);
  };

  const handleSendTest = async () => {
    if (!testEmail) return;

    const isUuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(selectedApiId);

    if (!isUuid) {
      const fallbackData = defaultTemplates[selectedTemplate];
      if (!fallbackData) {
        toast.error(t("email.toastSelectTemplate"));
        return;
      }
      try {
        toast.info(t("email.toastSavingFirst"));
        const created = await createMutation.mutateAsync({
          key: fallbackData.id,
          name: fallbackData.name,
          subject: subject,
          htmlBody: htmlContent,
          plainTextBody: textContent,
          description: fallbackData.description,
        });
        await sendTestMutation.mutateAsync({ id: created.id, email: testEmail });
        toast.success(t("email.toastTestSent"));
      } catch (err) {
        toast.error(err instanceof Error ? err.message : t('email.testFailed'));
      }
      return;
    }

    try {
      await sendTestMutation.mutateAsync({ id: selectedApiId, email: testEmail });
      toast.success(t("email.toastTestSent"));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('email.testFailed'));
    }
  };

  const copyVariable = (variable: string) => {
    navigator.clipboard.writeText(`{{${variable}}}`);
  };

  const isSaving = updateMutation.isPending;
  const isSendingTest = sendTestMutation.isPending;

  return (
    <div className="p-6 space-y-6">
      <nav className="flex items-center gap-2 text-sm">
        <Link
          to="/dashboard"
          className="text-muted-foreground hover:text-foreground transition-colors"
        >
          <Home className="w-4 h-4" />
        </Link>
        <ChevronRight className="w-4 h-4 text-muted-foreground" />
        <Link
          to="/settings"
          className="text-muted-foreground hover:text-foreground transition-colors"
        >
          {t('nav.settings')}
        </Link>
        <ChevronRight className="w-4 h-4 text-muted-foreground" />
        <span className="text-foreground font-medium">{t('email.title')}</span>
      </nav>

      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 style={{ fontSize: "1.875rem", fontWeight: 600 }}>
            {t('email.title')}
          </h1>
          <p
            style={{
              fontSize: "0.875rem",
              color: "#94A3B8",
              marginTop: "0.25rem",
            }}
          >
            {t('email.description')}
          </p>
        </div>
        <Button
          onClick={handleSave}
          disabled={isSaving}
          className="bg-brand-500 hover:bg-brand-600 text-white"
        >
          {isSaving ? (
            <>
              <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
              {t('common.saving')}
            </>
          ) : (
            <>
              <Save className="w-4 h-4 mr-2" />
              {t('common.saveChanges')}
            </>
          )}
        </Button>
      </div>

      {showSuccess && (
        <div className="p-4 rounded-lg bg-success-500/10 border border-success-500/20 flex items-center gap-3">
          <CheckCircle className="w-5 h-5 text-success-500 flex-shrink-0" />
          <div>
            <p style={{ fontSize: "0.875rem", fontWeight: 600, color: "#22C55E" }}>
              {t('email.savedSuccess')}
            </p>
            <p
              style={{
                fontSize: "0.8125rem",
                color: "#94A3B8",
                marginTop: "0.25rem",
              }}
            >
              {t('email.savedDesc')}
            </p>
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-4 gap-6">
        <div className="lg:col-span-1 space-y-4">
          <Card className="p-4 bg-card/80 backdrop-blur-sm border-border">
            <div className="flex items-center gap-2 mb-4">
              <Mail className="w-4 h-4 text-brand-500" />
              <h3 style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                {t('email.templateLibrary')}
              </h3>
            </div>

            <div className="space-y-2">
              {templateList.map((template) => (
                <button
                  key={template.id}
                  onClick={() => handleTemplateChange(template.key || template.id)}
                  className={`w-full text-left p-3 rounded-lg transition-all ${(selectedTemplate === template.key || selectedTemplate === template.id)
                    ? "bg-brand-500/10 border-2 border-brand-500"
                    : "bg-surface-light/20 border-2 border-transparent hover:border-border-light"
                    }`}
                >
                  <p style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.25rem" }}>
                    {template.name}
                  </p>
                  <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                    {template.description}
                  </p>
                </button>
              ))}
            </div>
          </Card>

          <Card className="p-4 bg-card/80 backdrop-blur-sm border-border">
            <div className="flex items-center gap-2 mb-4">
              <Sparkles className="w-4 h-4 text-purple-500" />
              <h3 style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                {t('email.availableVariables')}
              </h3>
            </div>

            <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginBottom: "1rem" }}>
              {t('email.clickToCopy')}
            </p>

            <div className="space-y-2">
              {currentVariables.map((variable: string) => (
                <button
                  key={variable}
                  onClick={() => copyVariable(variable)}
                  className="w-full flex items-center justify-between p-2 rounded-lg bg-surface-light/20 hover:bg-brand-500/10 transition-colors group"
                >
                  <span
                    style={{ fontSize: "0.75rem" }}
                    className="font-mono text-muted-foreground group-hover:text-brand-500"
                  >
                    {`{{${variable}}}`}
                  </span>
                  <Copy className="w-3 h-3 text-muted-foreground group-hover:text-brand-500" />
                </button>
              ))}
            </div>

            <div className="mt-4 p-3 rounded-lg bg-brand-500/5 border border-brand-500/20">
              <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                {t('email.variableNote')}
              </p>
            </div>
          </Card>
        </div>

        <div className="lg:col-span-3 space-y-6">
          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <label
              style={{
                fontSize: "0.875rem",
                fontWeight: 600,
                marginBottom: "0.5rem",
                display: "block",
              }}
            >
              {t('email.subject')}
            </label>
            <Input
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              placeholder={t("email.subjectPlaceholder")}
              className="bg-input-background font-medium"
            />
            <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.5rem" }}>
              {t('email.subjectVariableHint')}
            </p>
          </Card>

          <Card className="bg-card/80 backdrop-blur-sm border-border overflow-hidden">
            <Tabs
              value={activeTab}
              onValueChange={(v) => setActiveTab(v as "html" | "text" | "preview")}
            >
              <div className="border-b border-border px-6 pt-6 pb-6">
                <TabsList className="bg-surface-light/20">
                  <TabsTrigger value="html" className="data-[state=active]:bg-brand-500 data-[state=active]:text-white">
                    <Code className="w-4 h-4 mr-2" />
                    {t('email.htmlVersion')}
                  </TabsTrigger>
                  <TabsTrigger value="text" className="data-[state=active]:bg-brand-500 data-[state=active]:text-white">
                    <Mail className="w-4 h-4 mr-2" />
                    {t('email.plainTextVersion')}
                  </TabsTrigger>
                  <TabsTrigger value="preview" className="data-[state=active]:bg-brand-500 data-[state=active]:text-white">
                    <Eye className="w-4 h-4 mr-2" />
                    Preview
                  </TabsTrigger>
                </TabsList>
              </div>

              <TabsContent value="html" className="p-6 m-0">
                <Textarea
                  value={htmlContent}
                  onChange={(e) => setHtmlContent(e.target.value)}
                  rows={24}
                  className="bg-muted/10 font-mono text-xs resize-none"
                  style={{ minHeight: "500px" }}
                />
                <div className="mt-4 p-3 rounded-lg bg-purple-500/5 border border-purple-500/20 flex items-start gap-2">
                  <AlertCircle className="w-4 h-4 text-purple-500 flex-shrink-0 mt-0.5" />
                  <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                    {t('email.htmlNote')}
                  </p>
                </div>
              </TabsContent>

              <TabsContent value="text" className="p-6 m-0">
                <Textarea
                  value={textContent}
                  onChange={(e) => setTextContent(e.target.value)}
                  rows={24}
                  className="bg-muted/10 font-mono text-xs resize-none"
                  style={{ minHeight: "500px" }}
                />
                <div className="mt-4 p-3 rounded-lg bg-brand-500/5 border border-brand-500/20 flex items-start gap-2">
                  <AlertCircle className="w-4 h-4 text-brand-500 flex-shrink-0 mt-0.5" />
                  <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                    {t('email.plainTextNote')}
                  </p>
                </div>
              </TabsContent>

              <TabsContent value="preview" className="p-6 m-0">
                <div className="w-full bg-white rounded-md border border-border overflow-hidden" style={{ minHeight: "500px" }}>
                  <iframe 
                    title={t("email.previewDialogTitle")}
                    srcDoc={htmlContent}
                    sandbox=""
                    className="w-full"
                    style={{ minHeight: "500px", border: "none" }}
                  />
                </div>
              </TabsContent>
            </Tabs>
          </Card>

          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <div className="flex items-center gap-2 mb-4">
              <Send className="w-4 h-4 text-success-500" />
              <h3 style={{ fontSize: "1.0625rem", fontWeight: 600 }}>
                {t('email.sendTestEmail')}
              </h3>
            </div>

            <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginBottom: "1rem" }}>
              {t('email.previewInbox')}
            </p>

            <div className="flex gap-3">
              <Input
                type="email"
                placeholder={t("email.emailPlaceholder")}
                value={testEmail}
                onChange={(e) => setTestEmail(e.target.value)}
                className="bg-input-background flex-1"
              />
              <Button
                onClick={handleSendTest}
                disabled={!testEmail || isSendingTest}
                className="bg-success-600 hover:bg-success-700 text-white"
              >
                {isSendingTest ? (
                  <>
                    <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                    {t('email.sending')}
                  </>
                ) : (
                  <>
                    <Send className="w-4 h-4 mr-2" />
                    {t('email.sendTest')}
                  </>
                )}
              </Button>
            </div>

            <div className="mt-4 p-3 rounded-lg bg-warning-500/5 border border-warning-500/20 flex items-start gap-2">
              <AlertCircle className="w-4 h-4 text-warning-500 flex-shrink-0 mt-0.5" />
              <p style={{ fontSize: "0.75rem", color: "#94A3B8" }}>
                {t('email.smtpNote')}
              </p>
            </div>
          </Card>
        </div>
      </div>
    </div>
  );
}