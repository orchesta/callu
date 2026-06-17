import { useState, useEffect } from "react";
import { useParams, useNavigate, useSearchParams, Link } from "react-router";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  ChevronRight,
  Home,
  Save,
  X,
  AlignLeft,
  Search,
  Info,
  AlertCircle,
  CheckCircle,
  Zap,
  Plus,
  Trash2,
  ArrowRight,
  Sparkles,
  Loader2,
} from "lucide-react";
import {
  useWebhookTemplate,
  useCreateWebhookTemplate,
  useUpdateWebhookTemplate,
  useCreateWebhookTemplateFromCapture,
} from "@/features/settings/hooks/use-webhook-templates";
import { useService } from "../hooks/use-services";
import { useCapture } from "../hooks/use-captures";
import { onLocaleChange, t } from "@/shared/locales/i18n";

const samplePayloadTemplate = `{
  "status": "firing",
  "alerts": [
    {
      "status": "firing",
      "labels": {
        "alertname": "HighErrorRate",
        "severity": "critical",
        "service": "payment-api"
      },
      "annotations": {
        "summary": "High error rate detected",
        "description": "Error rate is above 5% for the last 5 minutes"
      }
    }
  ],
  "externalURL": "https://prometheus.example.com"
}`;

interface ParsedField {
  path: string;
  value: string;
  type: string;
}

interface SeverityMapping {
  id: string;
  sourceValue: string;
  targetSeverity: "critical" | "high" | "medium" | "low";
}

function incidentSeverityKey(sev: "critical" | "high" | "medium" | "low"): string {
  switch (sev) {
    case "critical":
      return "incidents.critical";
    case "high":
      return "incidents.high";
    case "medium":
      return "incidents.medium";
    case "low":
      return "incidents.low";
    default:
      return "incidents.medium";
  }
}

export function WebhookTemplateEditor() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const captureId = searchParams.get("captureId");
  const isEditing = !!id && !captureId;

  const { data: service } = useService(id!);
  const templateId = isEditing ? (service?.webhookTemplateId ?? "") : "";
  const { data: existingTemplate } = useWebhookTemplate(templateId);
  const { data: captureData } = useCapture(captureId ?? "");
  const createTemplate = useCreateWebhookTemplate();
  const updateTemplate = useUpdateWebhookTemplate();
  const createFromCapture = useCreateWebhookTemplateFromCapture();

  const [templateName, setTemplateName] = useState("");
  const [description, setDescription] = useState("");
  const [dataLanguage, setDataLanguage] = useState("en-US");
  const [samplePayload, setSamplePayload] = useState(samplePayloadTemplate);
  const [parsedFields, setParsedFields] = useState<ParsedField[]>([]);
  const [parseError, setParseError] = useState("");

  const [titleMapping, setTitleMapping] = useState("$.alerts[0].annotations.summary");
  const [descriptionMapping, setDescriptionMapping] = useState("$.alerts[0].annotations.description");
  const [severityFieldMapping, setSeverityFieldMapping] = useState("$.alerts[0].labels.severity");
  const [externalIdMapping, setExternalIdMapping] = useState("");
  const [stateFieldMapping, setStateFieldMapping] = useState("$.status");
  const [openStateValue, setOpenStateValue] = useState("firing");
  const [resolvedStateValue, setResolvedStateValue] = useState("resolved");

  const [severityMappings, setSeverityMappings] = useState<SeverityMapping[]>([
    { id: "1", sourceValue: "critical", targetSeverity: "critical" },
    { id: "2", sourceValue: "warning", targetSeverity: "high" },
    { id: "3", sourceValue: "info", targetSeverity: "low" },
  ]);

  const [showSuccess, setShowSuccess] = useState(false);
  const [isManualTitle, setIsManualTitle] = useState(false);
  const [isManualDescription, setIsManualDescription] = useState(false);
  const [isManualSeverity, setIsManualSeverity] = useState(false);
  const [isManualExternalId, setIsManualExternalId] = useState(false);
  const [isManualState, setIsManualState] = useState(false);

  const [previewTitle, setPreviewTitle] = useState(() => t("webhookTemplate.previewTitleDefault"));
  const [previewDescription, setPreviewDescription] = useState(() => t("webhookTemplate.previewDescriptionDefault"));
  const [previewSeverity, setPreviewSeverity] = useState<"critical" | "high" | "medium" | "low">("critical");
  const [previewOpenState, setPreviewOpenState] = useState<"open" | "resolved">("open");

  const [i18nTick, setI18nTick] = useState(0);
  useEffect(() => onLocaleChange(() => setI18nTick((n) => n + 1)), []);

  useEffect(() => {
    if (captureId) {
      setTemplateName(t("webhookTemplate.capturedNameDefault"));
      setDescription(t("webhookTemplate.capturedDescDefault"));
    }
  }, [captureId, i18nTick]);

  useEffect(() => {
    if (captureData?.body) {
      try {
        const parsed = JSON.parse(captureData.body);
        setSamplePayload(JSON.stringify(parsed, null, 2));
      } catch {
        setSamplePayload(captureData.body);
      }
    }
  }, [captureData]);

  useEffect(() => {
    if (existingTemplate) {
      setTemplateName(existingTemplate.name);
      setDescription(existingTemplate.description ?? "");
      setDataLanguage(existingTemplate.dataLanguage ?? "en-US");
      if (existingTemplate.samplePayload) {
        setSamplePayload(existingTemplate.samplePayload);
      }
      try {
        const fm = JSON.parse(existingTemplate.fieldMappings || "{}");
        if (fm.title) setTitleMapping(fm.title);
        if (fm.description) setDescriptionMapping(fm.description);
        if (fm.severity) setSeverityFieldMapping(fm.severity);
        if (fm.externalId) setExternalIdMapping(fm.externalId);
      } catch { /* empty */ }
      try {
        const sm = JSON.parse(existingTemplate.stateMapping || "{}");
        if (sm.stateField) setStateFieldMapping(sm.stateField);
        if (sm.openValue) setOpenStateValue(sm.openValue);
        if (sm.resolvedValue) setResolvedStateValue(sm.resolvedValue);
        if (sm.severityMappings) setSeverityMappings(sm.severityMappings);
      } catch { /* empty */ }
    }
  }, [existingTemplate]);

  const handleFormatJSON = () => {
    try {
      const parsed = JSON.parse(samplePayload);
      const formatted = JSON.stringify(parsed, null, 2);
      setSamplePayload(formatted);
      setParseError("");
    } catch {
      setParseError(t("webhookTemplate.invalidJson"));
    }
  };

  const handleParseJSON = () => {
    try {
      const parsed = JSON.parse(samplePayload) as Record<string, unknown>;
      const fields = extractFields(parsed);
      setParsedFields(fields);
      setParseError("");

      updatePreview(parsed);
    } catch {
      setParseError(t("webhookTemplate.parseFailed"));
      setParsedFields([]);
    }
  };

  const extractFields = (obj: unknown, prefix = "$"): ParsedField[] => {
    const fields: ParsedField[] = [];

    const traverse = (current: unknown, path: string) => {
      if (current === null || current === undefined) return;

      if (Array.isArray(current)) {
        current.forEach((item: unknown, index: number) => {
          traverse(item, `${path}[${index}]`);
        });
      } else if (typeof current === "object") {
        const record = current as Record<string, unknown>;
        Object.keys(record).forEach((key) => {
          const newPath = `${path}.${key}`;
          const val = record[key];
          if (typeof val === "object" && val !== null) {
            traverse(val, newPath);
          } else {
            fields.push({
              path: newPath,
              value: String(val),
              type: typeof val,
            });
          }
        });
      }
    };

    traverse(obj, prefix);
    return fields;
  };

  const resolveJsonPath = (obj: Record<string, unknown>, mapping: string): unknown => {
    const segments = mapping.replace("$.", "").split(".");
    let value: unknown = obj;
    for (const segment of segments) {
      if (value === null || typeof value !== "object") return undefined;
      const record = value as Record<string, unknown>;
      const arrayMatch = segment.match(/(.+)\[(\d+)\]/);
      if (arrayMatch) {
        const arr = record[arrayMatch[1]];
        value = Array.isArray(arr) ? arr[parseInt(arrayMatch[2])] : undefined;
      } else {
        value = record[segment];
      }
    }
    return value;
  };

  const updatePreview = (parsed: Record<string, unknown>) => {
    try {
      const titleValue = resolveJsonPath(parsed, titleMapping);
      setPreviewTitle(String(titleValue) || t("webhookTemplate.previewNoTitle"));
    } catch {
      setPreviewTitle(t("webhookTemplate.previewNoTitle"));
    }

    try {
      const descValue = resolveJsonPath(parsed, descriptionMapping);
      setPreviewDescription(String(descValue) || t("webhookTemplate.previewNoDescription"));
    } catch {
      setPreviewDescription(t("webhookTemplate.previewNoDescription"));
    }

    try {
      const sevValue = resolveJsonPath(parsed, severityFieldMapping);

      const mapping = severityMappings.find(m => m.sourceValue === sevValue);
      if (mapping) {
        setPreviewSeverity(mapping.targetSeverity);
      }
    } catch {
      /* empty */
    }

    try {
      const stateValue = resolveJsonPath(parsed, stateFieldMapping);
      setPreviewOpenState(stateValue === openStateValue ? "open" : "resolved");
    } catch {
      setPreviewOpenState("open");
    }
  };

  useEffect(() => {
    try {
      const parsed = JSON.parse(samplePayload) as Record<string, unknown>;
      updatePreview(parsed);
    } catch {
      /* empty */
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [titleMapping, descriptionMapping, severityFieldMapping, stateFieldMapping, openStateValue, resolvedStateValue, severityMappings, samplePayload, i18nTick]);

  const isSaving = createTemplate.isPending || updateTemplate.isPending || createFromCapture.isPending;

  const handleSave = () => {
    const fieldMappings = JSON.stringify({
      title: titleMapping,
      description: descriptionMapping,
      severity: severityFieldMapping,
      externalId: externalIdMapping || undefined,
    });
    const stateMapping = JSON.stringify({
      stateField: stateFieldMapping,
      openValue: openStateValue,
      resolvedValue: resolvedStateValue,
      severityMappings: severityMappings,
    });

    const successHandler = () => {
      setShowSuccess(true);
      setTimeout(() => navigate(`/services/${id}`), 1000);
    };

    if (captureId) {
      createFromCapture.mutate(
        {
          captureId,
          name: templateName,
          description: description || undefined,
          fieldMappings,
          stateMapping,
          samplePayload: samplePayload || undefined,
          dataLanguage,
        },
        { onSuccess: successHandler },
      );
    } else if (isEditing && templateId) {
      updateTemplate.mutate(
        {
          id: templateId,
          name: templateName,
          description: description || undefined,
          fieldMappings,
          stateMapping,
          samplePayload: samplePayload || undefined,
          dataLanguage,
        },
        { onSuccess: successHandler },
      );
    } else {
      createTemplate.mutate(
        {
          name: templateName,
          description: description || undefined,
          fieldMappings,
          stateMapping,
          samplePayload: samplePayload || undefined,
          dataLanguage,
        },
        { onSuccess: successHandler },
      );
    }
  };

  const addSeverityMapping = () => {
    setSeverityMappings([
      ...severityMappings,
      {
        id: Date.now().toString(),
        sourceValue: "",
        targetSeverity: "medium",
      },
    ]);
  };

  const removeSeverityMapping = (id: string) => {
    setSeverityMappings(severityMappings.filter((m) => m.id !== id));
  };

  const updateSeverityMapping = (id: string, field: keyof SeverityMapping, value: string) => {
    setSeverityMappings(
      severityMappings.map((m) =>
        m.id === id ? { ...m, [field]: value } : m
      )
    );
  };

  const getSeverityColor = (severity: string) => {
    switch (severity) {
      case "critical":
        return "bg-error-500/10 text-error-500 border-error-500/20";
      case "high":
        return "bg-warning-500/10 text-warning-500 border-warning-500/20";
      case "medium":
        return "bg-blue-400/10 text-blue-400 border-blue-400/20";
      case "low":
        return "bg-gray-400/10 text-gray-400 border-gray-400/20";
      default:
        return "bg-muted/10 text-muted-foreground border-muted/20";
    }
  };

  const isValid = templateName.trim().length > 0 && titleMapping.length > 0;

  return (
    <div className="p-6 space-y-6">
      <nav className="flex items-center gap-2 text-sm">
        <Link to="/dashboard" className="text-muted-foreground hover:text-foreground transition-colors">
          <Home className="w-4 h-4" />
        </Link>
        <ChevronRight className="w-4 h-4 text-muted-foreground" />
        <Link to="/services" className="text-muted-foreground hover:text-foreground transition-colors">
          {t("services.title")}
        </Link>
        <ChevronRight className="w-4 h-4 text-muted-foreground" />
        <Link to={`/services/${id}`} className="text-muted-foreground hover:text-foreground transition-colors">
          {service?.name ?? t("services.breadcrumbServiceFallback")}
        </Link>
        <ChevronRight className="w-4 h-4 text-muted-foreground" />
        <span className="text-foreground font-medium">{t("webhookTemplate.breadcrumbTemplate")}</span>
      </nav>

      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 style={{ fontSize: '1.875rem', fontWeight: 600 }}>
            {captureId ? t("webhookTemplate.pageTitleFromCapture") : t("webhookTemplate.pageTitleEdit")}
          </h1>
          <p style={{ fontSize: '0.875rem', color: '#94A3B8', marginTop: '0.25rem' }}>
            {t("webhookTemplate.pageSubtitle")}
          </p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            onClick={() => navigate(`/services/${id}`)}
            className="bg-input-background"
          >
            <X className="w-4 h-4 mr-2" />
            {t("common.cancel")}
          </Button>
          <Button
            onClick={handleSave}
            disabled={!isValid || isSaving}
            className="bg-brand-500 hover:bg-brand-600 text-white"
          >
            {isSaving ? (
              <>
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                {t("common.saving")}
              </>
            ) : (
              <>
                <Save className="w-4 h-4 mr-2" />
                {t("webhookTemplate.saveTemplate")}
              </>
            )}
          </Button>
        </div>
      </div>

      {showSuccess && (
        <div className="p-4 rounded-lg bg-success-500/10 border border-success-500/20 flex items-center gap-3">
          <CheckCircle className="w-5 h-5 text-success-500 flex-shrink-0" />
          <div>
            <p style={{ fontSize: '0.875rem', fontWeight: 600, color: '#22C55E' }}>
              {t("webhookTemplate.successTitle")}
            </p>
            <p style={{ fontSize: '0.8125rem', color: '#94A3B8', marginTop: '0.25rem' }}>
              {t("webhookTemplate.successRedirect")}
            </p>
          </div>
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-5 gap-6">
        <div className="lg:col-span-2 space-y-6">
          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <h3 style={{ fontSize: '1.125rem', fontWeight: 600, marginBottom: '1rem' }}>
              {t("webhookTemplate.cardTemplateInfo")}
            </h3>

            <div className="space-y-4">
              <div>
                <label style={{ fontSize: '0.875rem', fontWeight: 600, marginBottom: '0.5rem', display: 'block' }}>
                  {t("webhookTemplate.labelTemplateName")} <span className="text-error-500">*</span>
                </label>
                <Input
                  placeholder={t("webhookTemplate.nameExample")}
                  value={templateName}
                  onChange={(e) => setTemplateName(e.target.value)}
                  className="bg-input-background"
                />
              </div>

              <div>
                <label style={{ fontSize: '0.875rem', fontWeight: 600, marginBottom: '0.5rem', display: 'block' }}>
                  {t("webhookTemplate.labelDescription")}
                </label>
                <Textarea
                  placeholder={t("webhookTemplate.descriptionPlaceholder")}
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  rows={2}
                  className="bg-input-background resize-none"
                />
              </div>

              <div>
                <label style={{ fontSize: '0.875rem', fontWeight: 600, marginBottom: '0.5rem', display: 'block' }}>
                  {t("webhookTemplate.labelTtsLanguage")}
                </label>
                <Select value={dataLanguage} onValueChange={setDataLanguage}>
                  <SelectTrigger className="bg-input-background">
                    <SelectValue placeholder={t("webhookTemplate.selectLanguage")} />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="tr-TR">Turkish (TR)</SelectItem>
                    <SelectItem value="en-US">English (US)</SelectItem>
                    <SelectItem value="en-GB">English (UK)</SelectItem>
                    <SelectItem value="de-DE">German (DE)</SelectItem>
                    <SelectItem value="fr-FR">French (FR)</SelectItem>
                    <SelectItem value="es-ES">Spanish (ES)</SelectItem>
                    <SelectItem value="it-IT">Italian (IT)</SelectItem>
                    <SelectItem value="pt-BR">Portuguese (BR)</SelectItem>
                    <SelectItem value="ru-RU">Russian (RU)</SelectItem>
                    <SelectItem value="ja-JP">Japanese (JP)</SelectItem>
                    <SelectItem value="ko-KR">Korean (KR)</SelectItem>
                    <SelectItem value="zh-CN">Chinese (CN)</SelectItem>
                  </SelectContent>
                </Select>
                <p className="text-xs text-muted-foreground mt-1">
                  {t("webhookTemplate.ttsLanguageHint")}
                </p>
              </div>

              {captureId && (
                <Badge className="bg-brand-500/10 text-brand-500 border-brand-500/20 border">
                  <Sparkles className="w-3 h-3 mr-1" />
                  {t("webhookTemplate.badgeFromCapture")}
                </Badge>
              )}
            </div>
          </Card>

          <Card className="p-6 bg-gradient-to-br from-brand-500/5 to-transparent border-brand-500/20">
            <div className="flex items-start gap-3 mb-4">
              <div className="w-8 h-8 rounded-full bg-brand-500/10 flex items-center justify-center flex-shrink-0">
                <Info className="w-4 h-4 text-brand-500" />
              </div>
              <div>
                <h4 style={{ fontSize: '0.9375rem', fontWeight: 600, marginBottom: '0.5rem' }}>
                  {t("webhookTemplate.quickStartTitle")}
                </h4>
                <div className="space-y-3">
                  <div className="flex gap-3">
                    <div className="w-6 h-6 rounded-full bg-brand-500 text-white flex items-center justify-center flex-shrink-0 text-xs font-bold">
                      1
                    </div>
                    <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>
                      {t("webhookTemplate.quickStartStep1")}
                    </p>
                  </div>
                  <div className="flex gap-3">
                    <div className="w-6 h-6 rounded-full bg-brand-500 text-white flex items-center justify-center flex-shrink-0 text-xs font-bold">
                      2
                    </div>
                    <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>
                      {t("webhookTemplate.quickStartStep2")}
                    </p>
                  </div>
                  <div className="flex gap-3">
                    <div className="w-6 h-6 rounded-full bg-brand-500 text-white flex items-center justify-center flex-shrink-0 text-xs font-bold">
                      3
                    </div>
                    <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>
                      {t("webhookTemplate.quickStartStep3")}
                    </p>
                  </div>
                </div>
              </div>
            </div>
          </Card>

          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <div className="flex items-center justify-between mb-4">
              <h3 style={{ fontSize: '1.125rem', fontWeight: 600 }}>
                {t("webhookTemplate.samplePayloadTitle")}
              </h3>
              <div className="flex gap-2">
                <Button
                  size="sm"
                  variant="outline"
                  onClick={handleFormatJSON}
                  className="bg-input-background"
                >
                  <AlignLeft className="w-4 h-4 mr-2" />
                  {t("webhookTemplate.formatJson")}
                </Button>
                <Button
                  size="sm"
                  onClick={handleParseJSON}
                  className="bg-success-600 hover:bg-success-700 text-white"
                >
                  <Search className="w-4 h-4 mr-2" />
                  {t("webhookTemplate.parseJson")}
                </Button>
              </div>
            </div>

            <Textarea
              value={samplePayload}
              onChange={(e) => setSamplePayload(e.target.value)}
              rows={20}
              className="bg-muted/10 font-mono text-xs resize-y"
              style={{ minHeight: '320px' }}
              placeholder={t("webhookTemplate.pasteJsonPlaceholder")}
            />

            {parseError ? (
              <div className="mt-3 p-3 rounded-lg bg-error-500/10 border border-error-500/20 flex items-center gap-2">
                <AlertCircle className="w-4 h-4 text-error-500 flex-shrink-0" />
                <p style={{ fontSize: '0.8125rem', color: '#FF4D4D' }}>
                  {parseError}
                </p>
              </div>
            ) : parsedFields.length > 0 ? (
              <div className="mt-3 p-3 rounded-lg bg-success-500/10 border border-success-500/20 flex items-center gap-2">
                <CheckCircle className="w-4 h-4 text-success-500 flex-shrink-0" />
                <p style={{ fontSize: '0.8125rem', color: '#22C55E' }}>
                  {t("webhookTemplate.fieldsDiscovered", { count: parsedFields.length })}
                </p>
              </div>
            ) : (
              <div className="mt-3 p-3 rounded-lg bg-muted/10 border border-border flex items-center gap-2">
                <Info className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                <p style={{ fontSize: '0.8125rem', color: '#94A3B8' }}>
                  {t("webhookTemplate.pasteSampleHint")}
                </p>
              </div>
            )}
          </Card>
        </div>

        <div className="lg:col-span-3 space-y-6">
          <Card className="p-6 bg-card/80 backdrop-blur-sm border-2 border-brand-200">
            <div className="flex items-center gap-2 mb-4">
              <div className="w-2 h-2 bg-brand-500 rounded-full animate-pulse" />
              <h3 style={{ fontSize: '1.125rem', fontWeight: 600 }}>
                {t("webhookTemplate.livePreview")}
              </h3>
            </div>

            <div className="p-5 rounded-lg bg-gradient-to-br from-brand-500/5 to-transparent border border-brand-500/20">
              <div className="flex items-start gap-3 mb-4">
                <div className="w-10 h-10 rounded-lg bg-error-500/10 flex items-center justify-center flex-shrink-0">
                  <Zap className="w-5 h-5 text-error-500" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 mb-2">
                    <h4 style={{ fontSize: '1.0625rem', fontWeight: 600 }} className="truncate">
                      {previewTitle}
                    </h4>
                    <Badge className={`${getSeverityColor(previewSeverity)} border text-xs`}>
                      {t(incidentSeverityKey(previewSeverity))}
                    </Badge>
                  </div>
                  <p style={{ fontSize: '0.875rem', color: '#94A3B8' }} className="line-clamp-2">
                    {previewDescription}
                  </p>
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3 pt-4 border-t border-border">
                <div>
                  <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '0.25rem' }}>
                    {t("webhookTemplate.previewStatusLabel")}
                  </p>
                  <Badge className={`${previewOpenState === "open" ? "bg-warning-500/10 text-warning-500 border-warning-500/20" : "bg-success-500/10 text-success-500 border-success-500/20"} border text-xs`}>
                    {previewOpenState === "open" ? t("webhookTemplate.previewStateOpen") : t("webhookTemplate.previewStateResolved")}
                  </Badge>
                </div>
                <div>
                  <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginBottom: '0.25rem' }}>
                    {t("webhookTemplate.previewSeverityLabel")}
                  </p>
                  <Badge className={`${getSeverityColor(previewSeverity)} border text-xs`}>
                    {t(incidentSeverityKey(previewSeverity))}
                  </Badge>
                </div>
              </div>
            </div>

            <p style={{ fontSize: '0.75rem', color: '#94A3B8', marginTop: '1rem', textAlign: 'center' }}>
              {t("webhookTemplate.previewHint")}
            </p>
          </Card>

          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <h3 style={{ fontSize: '1.125rem', fontWeight: 600, marginBottom: '1rem' }}>
              {t("webhookTemplate.fieldMappingsTitle")}
            </h3>

            <div className="space-y-5">
              <div>
                <div className="flex items-center justify-between mb-2">
                  <label style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                    {t("webhookTemplate.mappingTitle")} <span className="text-error-500">*</span>
                  </label>
                  {parsedFields.length > 0 && (
                    <Button
                      size="sm"
                      variant="ghost"
                      onClick={() => setIsManualTitle(!isManualTitle)}
                      className="text-xs h-6"
                    >
                      {isManualTitle ? t("webhookTemplate.useDropdown") : t("webhookTemplate.enterManually")}
                    </Button>
                  )}
                </div>
                {isManualTitle || parsedFields.length === 0 ? (
                  <Input
                    placeholder={t("webhookTemplate.pathSummary")}
                    value={titleMapping}
                    onChange={(e) => setTitleMapping(e.target.value)}
                    className="bg-input-background font-mono text-sm"
                  />
                ) : (
                  <Select value={titleMapping} onValueChange={setTitleMapping}>
                    <SelectTrigger className="bg-input-background font-mono text-sm">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {parsedFields.map((field) => (
                        <SelectItem key={field.path} value={field.path} className="font-mono text-xs">
                          {field.path}
                          <span className="text-muted-foreground ml-2">({field.value})</span>
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </div>

              <div>
                <div className="flex items-center justify-between mb-2">
                  <label style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                    {t("webhookTemplate.mappingDescription")}
                  </label>
                  {parsedFields.length > 0 && (
                    <Button
                      size="sm"
                      variant="ghost"
                      onClick={() => setIsManualDescription(!isManualDescription)}
                      className="text-xs h-6"
                    >
                      {isManualDescription ? t("webhookTemplate.useDropdown") : t("webhookTemplate.enterManually")}
                    </Button>
                  )}
                </div>
                {isManualDescription || parsedFields.length === 0 ? (
                  <Input
                    placeholder={t("webhookTemplate.pathDescription")}
                    value={descriptionMapping}
                    onChange={(e) => setDescriptionMapping(e.target.value)}
                    className="bg-input-background font-mono text-sm"
                  />
                ) : (
                  <Select value={descriptionMapping} onValueChange={setDescriptionMapping}>
                    <SelectTrigger className="bg-input-background font-mono text-sm">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {parsedFields.map((field) => (
                        <SelectItem key={field.path} value={field.path} className="font-mono text-xs">
                          {field.path}
                          <span className="text-muted-foreground ml-2">({field.value})</span>
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <div className="flex items-center justify-between mb-2">
                    <label style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                      {t("webhookTemplate.severityField")}
                    </label>
                    {parsedFields.length > 0 && (
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => setIsManualSeverity(!isManualSeverity)}
                        className="text-xs h-6"
                      >
                        {isManualSeverity ? t("webhookTemplate.toggleDropdown") : t("webhookTemplate.toggleManual")}
                      </Button>
                    )}
                  </div>
                  {isManualSeverity || parsedFields.length === 0 ? (
                    <Input
                      placeholder={t("webhookTemplate.pathSeverity")}
                      value={severityFieldMapping}
                      onChange={(e) => setSeverityFieldMapping(e.target.value)}
                      className="bg-input-background font-mono text-sm"
                    />
                  ) : (
                    <Select value={severityFieldMapping} onValueChange={setSeverityFieldMapping}>
                      <SelectTrigger className="bg-input-background font-mono text-sm">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {parsedFields.map((field) => (
                          <SelectItem key={field.path} value={field.path} className="font-mono text-xs">
                            {field.path.split(".").pop()}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                </div>

                <div>
                  <div className="flex items-center justify-between mb-2">
                    <label style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                      {t("webhookTemplate.externalIdField")}
                    </label>
                    {parsedFields.length > 0 && (
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => setIsManualExternalId(!isManualExternalId)}
                        className="text-xs h-6"
                      >
                        {isManualExternalId ? t("webhookTemplate.toggleDropdown") : t("webhookTemplate.toggleManual")}
                      </Button>
                    )}
                  </div>
                  {isManualExternalId || parsedFields.length === 0 ? (
                    <Input
                      placeholder={t("webhookTemplate.pathAlertId")}
                      value={externalIdMapping}
                      onChange={(e) => setExternalIdMapping(e.target.value)}
                      className="bg-input-background font-mono text-sm"
                    />
                  ) : (
                    <Select value={externalIdMapping} onValueChange={setExternalIdMapping}>
                      <SelectTrigger className="bg-input-background font-mono text-sm">
                        <SelectValue placeholder={t("webhookTemplate.selectOptional")} />
                      </SelectTrigger>
                      <SelectContent>
                        {parsedFields.map((field) => (
                          <SelectItem key={field.path} value={field.path} className="font-mono text-xs">
                            {field.path.split(".").pop()}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                </div>
              </div>
            </div>
          </Card>

          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <h3 style={{ fontSize: '1.125rem', fontWeight: 600, marginBottom: '1rem' }}>
              {t("webhookTemplate.stateMappingTitle")}
            </h3>

            <div className="space-y-4">
              <div>
                <div className="flex items-center justify-between mb-2">
                  <label style={{ fontSize: '0.875rem', fontWeight: 600 }}>
                    {t("webhookTemplate.stateField")}
                  </label>
                  {parsedFields.length > 0 && (
                    <Button
                      size="sm"
                      variant="ghost"
                      onClick={() => setIsManualState(!isManualState)}
                      className="text-xs h-6"
                    >
                      {isManualState ? t("webhookTemplate.useDropdown") : t("webhookTemplate.enterManually")}
                    </Button>
                  )}
                </div>
                {isManualState || parsedFields.length === 0 ? (
                  <Input
                    placeholder={t("webhookTemplate.pathStatus")}
                    value={stateFieldMapping}
                    onChange={(e) => setStateFieldMapping(e.target.value)}
                    className="bg-input-background font-mono text-sm"
                  />
                ) : (
                  <Select value={stateFieldMapping} onValueChange={setStateFieldMapping}>
                    <SelectTrigger className="bg-input-background font-mono text-sm">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {parsedFields.map((field) => (
                        <SelectItem key={field.path} value={field.path} className="font-mono text-xs">
                          {field.path}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <label style={{ fontSize: '0.875rem', fontWeight: 600, marginBottom: '0.5rem', display: 'block' }}>
                    {t("webhookTemplate.openValue")}
                  </label>
                  <div className="p-3 rounded-lg bg-success-500/5 border border-success-500/20">
                    <Input
                      placeholder={t("webhookTemplate.statusFiring")}
                      value={openStateValue}
                      onChange={(e) => setOpenStateValue(e.target.value)}
                      className="bg-input-background font-mono text-sm"
                    />
                  </div>
                </div>
                <div>
                  <label style={{ fontSize: '0.875rem', fontWeight: 600, marginBottom: '0.5rem', display: 'block' }}>
                    {t("webhookTemplate.resolvedValue")}
                  </label>
                  <div className="p-3 rounded-lg bg-muted/10 border border-border">
                    <Input
                      placeholder={t("webhookTemplate.statusResolved")}
                      value={resolvedStateValue}
                      onChange={(e) => setResolvedStateValue(e.target.value)}
                      className="bg-input-background font-mono text-sm"
                    />
                  </div>
                </div>
              </div>
            </div>
          </Card>

          <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
            <div className="flex items-center justify-between mb-4">
              <h3 style={{ fontSize: '1.125rem', fontWeight: 600 }}>
                {t("webhookTemplate.severityMappingsTitle")}
              </h3>
              <Button
                size="sm"
                variant="outline"
                onClick={addSeverityMapping}
                className="bg-input-background"
              >
                <Plus className="w-4 h-4 mr-2" />
                {t("webhookTemplate.addMapping")}
              </Button>
            </div>

            <div className="space-y-3">
              {severityMappings.map((mapping) => (
                <div
                  key={mapping.id}
                  className="flex items-center gap-3 p-3 rounded-lg bg-surface-light/20 border border-border"
                >
                  <div className="flex-1">
                    <Input
                      placeholder={t("webhookTemplate.sourceValueExample")}
                      value={mapping.sourceValue}
                      onChange={(e) =>
                        updateSeverityMapping(mapping.id, "sourceValue", e.target.value)
                      }
                      className="bg-input-background font-mono text-sm"
                    />
                  </div>
                  <ArrowRight className="w-4 h-4 text-muted-foreground flex-shrink-0" />
                  <div className="flex-1">
                    <Select
                      value={mapping.targetSeverity}
                      onValueChange={(value: "critical" | "high" | "medium" | "low") =>
                        updateSeverityMapping(mapping.id, "targetSeverity", value)
                      }
                    >
                      <SelectTrigger className="bg-input-background">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="critical">
                          <Badge className={getSeverityColor("critical")}>{t("incidents.critical")}</Badge>
                        </SelectItem>
                        <SelectItem value="high">
                          <Badge className={getSeverityColor("high")}>{t("incidents.high")}</Badge>
                        </SelectItem>
                        <SelectItem value="medium">
                          <Badge className={getSeverityColor("medium")}>{t("incidents.medium")}</Badge>
                        </SelectItem>
                        <SelectItem value="low">
                          <Badge className={getSeverityColor("low")}>{t("incidents.low")}</Badge>
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                  <Button
                    size="sm"
                    variant="ghost"
                    onClick={() => removeSeverityMapping(mapping.id)}
                    className="text-error-500 hover:bg-error-500/10"
                  >
                    <Trash2 className="w-4 h-4" />
                  </Button>
                </div>
              ))}
            </div>

            {severityMappings.length === 0 && (
              <div className="text-center py-6">
                <p style={{ fontSize: '0.875rem', color: '#94A3B8' }}>
                  {t("webhookTemplate.noSeverityMappings")}
                </p>
              </div>
            )}
          </Card>

          {!isValid && templateName.length > 0 && (
            <div className="p-4 rounded-lg bg-error-500/10 border border-error-500/20 flex items-start gap-3">
              <AlertCircle className="w-5 h-5 text-error-500 flex-shrink-0 mt-0.5" />
              <div>
                <p style={{ fontSize: '0.875rem', fontWeight: 600, color: '#FF4D4D' }}>
                  {t("webhookTemplate.validationErrorTitle")}
                </p>
                <p style={{ fontSize: '0.8125rem', color: '#94A3B8', marginTop: '0.25rem' }}>
                  {t("webhookTemplate.validationErrorBody")}
                </p>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}