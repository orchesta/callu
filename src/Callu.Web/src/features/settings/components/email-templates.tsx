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
  Plus,
  Trash2,
} from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import { Link } from "react-router";
import {
  useEmailTemplates,
  useEmailTemplate,
  useCreateEmailTemplate,
  useDeleteEmailTemplate,
  useUpdateEmailTemplate,
  useSendTestEmail,
} from '../hooks/use-email-templates';
import type { EmailTemplateDto } from '../types/email-template.types';

export function EmailTemplates() {
  // Templates are seeded server-side and the editor's changes affect the REAL
  // outgoing emails (DB row first, built-in file template as fallback).
  const { data: apiTemplates } = useEmailTemplates();
  const updateMutation = useUpdateEmailTemplate();
  const sendTestMutation = useSendTestEmail();
  const createMutation = useCreateEmailTemplate();
  const deleteMutation = useDeleteEmailTemplate();

  const [selectedTemplate, setSelectedTemplate] = useState<string>("");
  const [activeTab, setActiveTab] = useState<"html" | "text" | "preview">("html");
  const [showSuccess, setShowSuccess] = useState(false);
  const [testEmail, setTestEmail] = useState("");
  const [isCreating, setIsCreating] = useState(false);
  const [draft, setDraft] = useState({ name: "", key: "", subject: "", htmlBody: "" });
  const [deleting, setDeleting] = useState<EmailTemplateDto | null>(null);

  // Auto-select the first template once the list arrives.
  useEffect(() => {
    if (!selectedTemplate && apiTemplates && apiTemplates.length > 0) {
      setSelectedTemplate(apiTemplates[0].key);
    }
  }, [apiTemplates, selectedTemplate]);

  const selectedApiId = (apiTemplates ?? []).find(
    (t: EmailTemplateDto) => t.key === selectedTemplate || t.id === selectedTemplate
  )?.id ?? '';
  const selectedRow = (apiTemplates ?? []).find((item: EmailTemplateDto) => item.id === selectedApiId) ?? null;
  const { data: templateDetail } = useEmailTemplate(selectedApiId);

  const [subject, setSubject] = useState('');
  const [htmlContent, setHtmlContent] = useState('');
  const [textContent, setTextContent] = useState('');

  useEffect(() => {
    if (templateDetail) {
      setSubject(templateDetail.subject);
      setHtmlContent(templateDetail.htmlBody);
      setTextContent(templateDetail.plainTextBody ?? '');
    }
  }, [templateDetail, selectedTemplate]);

  // The editor shows the body being edited, not the saved one, so the substitution happens here.
  // [Name] matches what the test email puts in, so the two previews agree.
  const previewHtml = (templateDetail?.variables ?? []).reduce(
    (html, name) => html.split(`{{${name}}}`).join(`[${name}]`),
    htmlContent,
  );

  const templateList: { id: string; key: string; name: string; description: string }[] =
    (apiTemplates ?? []).map((item: EmailTemplateDto) => ({
      id: item.id,
      key: item.key,
      name: item.name,
      description: (templateDetail && templateDetail.id === item.id ? templateDetail.description : '') ?? '',
    }));

  // Variable hints come from the REAL template content (server-extracted), not a hardcoded list.
  const currentVariables = templateDetail?.variables ?? [];

  const handleTemplateChange = (templateKeyOrId: string) => {
    setSelectedTemplate(templateKeyOrId);
    setShowSuccess(false);
  };

  const handleSave = async () => {
    if (!selectedApiId) return;
    try {
      await updateMutation.mutateAsync({
        id: selectedApiId,
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
    if (!testEmail || !selectedApiId) return;

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
        <div className="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          className="bg-input-background"
          onClick={() => { setDraft({ name: "", key: "", subject: "", htmlBody: "" }); setIsCreating(true); }}
        >
          <Plus className="w-4 h-4 mr-2" />
          {t('email.newTemplate')}
        </Button>
        <Button
          variant="ghost"
          className="text-error-400"
          disabled={!selectedRow || selectedRow.isSystem}
          title={selectedRow?.isSystem ? t('email.systemLocked') : undefined}
          aria-label={t('email.deleteAria', { name: selectedRow?.name ?? '' })}
          onClick={() => selectedRow && setDeleting(selectedRow)}
        >
          <Trash2 className="w-4 h-4" />
        </Button>
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
                <p className="mb-3 text-xs text-muted-foreground">{t('email.previewNote')}</p>
                <div className="w-full bg-white rounded-md border border-border overflow-hidden" style={{ minHeight: "500px" }}>
                  <iframe 
                    title={t("email.previewDialogTitle")}
                    srcDoc={previewHtml}
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

      <Dialog open={isCreating} onOpenChange={setIsCreating}>
        <DialogContent className="bg-card border-border sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>{t('email.newTemplateTitle')}</DialogTitle>
            <DialogDescription>{t('email.newTemplateDescription')}</DialogDescription>
          </DialogHeader>

          <div className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <div>
                <label htmlFor="et-name" className="mb-2 block text-xs font-semibold text-muted-foreground">
                  {t('email.fieldName')}
                </label>
                <Input
                  id="et-name"
                  value={draft.name}
                  onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))}
                  className="bg-input-background"
                />
              </div>
              <div>
                <label htmlFor="et-key" className="mb-2 block text-xs font-semibold text-muted-foreground">
                  {t('email.fieldKey')}
                </label>
                <Input
                  id="et-key"
                  value={draft.key}
                  onChange={(e) => setDraft((d) => ({ ...d, key: e.target.value.trim().toLowerCase() }))}
                  className="bg-input-background font-mono"
                />
                <p className="mt-1 text-xs text-muted-foreground">{t('email.fieldKeyHint')}</p>
              </div>
            </div>
            <div>
              <label htmlFor="et-subject" className="mb-2 block text-xs font-semibold text-muted-foreground">
                {t('email.fieldSubject')}
              </label>
              <Input
                id="et-subject"
                value={draft.subject}
                onChange={(e) => setDraft((d) => ({ ...d, subject: e.target.value }))}
                className="bg-input-background"
              />
            </div>
            <div>
              <label htmlFor="et-body" className="mb-2 block text-xs font-semibold text-muted-foreground">
                {t('email.fieldBody')}
              </label>
              <Textarea
                id="et-body"
                rows={10}
                value={draft.htmlBody}
                onChange={(e) => setDraft((d) => ({ ...d, htmlBody: e.target.value }))}
                className="bg-input-background font-mono text-xs"
              />
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" className="bg-input-background" onClick={() => setIsCreating(false)}>
              {t('common.cancel')}
            </Button>
            <Button
              disabled={!draft.name.trim() || !draft.key.trim() || !draft.subject.trim()
                || !draft.htmlBody.trim() || createMutation.isPending}
              onClick={() =>
                createMutation.mutate(draft, {
                  onSuccess: (created) => {
                    setIsCreating(false);
                    if (created?.key) handleTemplateChange(created.key);
                  },
                })
              }
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {createMutation.isPending ? t('common.creating') : t('email.create')}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <DeleteConfirmDialog
        open={!!deleting}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={t('email.deleteTitle')}
        message={t('email.deleteMsg', { name: deleting?.name ?? '' })}
        warning={t('email.deleteWarn')}
        isLoading={deleteMutation.isPending}
        onConfirm={() => {
          if (!deleting) return;
          deleteMutation.mutate(deleting.id, {
            onSuccess: () => setSelectedTemplate(''),
            onSettled: () => setDeleting(null),
          });
        }}
      />
    </div>
  );
}