import { useState, useEffect, useId } from 'react';
import { toast } from '@/shared/utils/toast';
import { t } from '@/shared/locales/i18n';
import { Button } from '@/shared/components/ui/button';
import { Input } from '@/shared/components/ui/input';
import { Label } from '@/shared/components/ui/label';
import { Switch } from '@/shared/components/ui/switch';
import { Badge } from '@/shared/components/ui/badge';
import { Textarea } from '@/shared/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/shared/components/ui/select';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from '@/shared/components/ui/dialog';
import { Loader2, Save, Search, Zap } from 'lucide-react';
import { useTestHealthCheck, useSniffHealthCheck } from '../hooks/use-status-pages';
import type { StatusPageComponentDto, HealthCheckSnifferResultDto } from '../types/status-page.types';

export interface HealthCheckPayload {
  componentId: string;
  healthCheckEnabled: boolean;
  healthCheckUrl?: string;
  healthCheckHttpMethod: string;
  healthCheckIntervalSeconds: number;
  healthCheckTimeoutSeconds: number;
  healthCheckHeaders: string;
  healthCheckBody: string;
  healthCheckContentType: string;
  healthCheckFieldMappings?: string;
  healthCheckStateMapping?: string;
}

export interface HealthCheckDialogProps {
  /** The component being configured; null closes the dialog. */
  component: StatusPageComponentDto | null;
  onClose: () => void;
  onSave: (payload: HealthCheckPayload) => void;
  /**
   * Owned by the parent because the same update-component mutation also drives the components
   * card — giving this dialog its own instance would silently decouple the two.
   */
  isSaving: boolean;
}

export function HealthCheckDialog({ component, onClose, onSave, isSaving }: HealthCheckDialogProps) {
  const formId = useId();
  const testHealthCheckMutation = useTestHealthCheck();
  const sniffHealthCheckMutation = useSniffHealthCheck();

  const [hcEnabled, setHcEnabled] = useState(false);
  const [hcUrl, setHcUrl] = useState('');
  const [hcMethod, setHcMethod] = useState('GET');
  const [hcInterval, setHcInterval] = useState(60);
  const [hcTimeout, setHcTimeout] = useState(10);
  const [hcHeaders, setHcHeaders] = useState('');
  const [hcBody, setHcBody] = useState('');
  const [hcContentType, setHcContentType] = useState('application/json');
  const [hcFieldMappings, setHcFieldMappings] = useState('');
  const [hcStateMapping, setHcStateMapping] = useState('');
  const [snifferResult, setSnifferResult] = useState<HealthCheckSnifferResultDto | null>(null);

  // Re-seeds every time a component is handed over, so reopening after an abandoned edit shows
  // what is stored rather than the discarded draft.
  useEffect(() => {
    if (!component) return;
    setHcEnabled(component.healthCheckEnabled);
    setHcUrl(component.healthCheckUrl ?? '');
    setHcMethod(component.healthCheckHttpMethod ?? 'GET');
    setHcInterval(component.healthCheckIntervalSeconds ?? 60);
    setHcTimeout(component.healthCheckTimeoutSeconds ?? 10);
    setHcHeaders(component.healthCheckHeaders ?? '');
    setHcBody(component.healthCheckBody ?? '');
    setHcContentType(component.healthCheckContentType ?? 'application/json');
    setHcFieldMappings(component.healthCheckFieldMappings ?? '');
    setHcStateMapping(component.healthCheckStateMapping ?? '');
    setSnifferResult(null);
  }, [component]);

  const handleSaveHealthCheck = () => {
    if (!component) return;
    onSave({
      componentId: component.id,
      healthCheckEnabled: hcEnabled,
      healthCheckUrl: hcUrl || undefined,
      healthCheckHttpMethod: hcMethod,
      healthCheckIntervalSeconds: hcInterval,
      healthCheckTimeoutSeconds: hcTimeout,
      healthCheckHeaders: hcHeaders,
      healthCheckBody: hcBody,
      healthCheckContentType: hcContentType,
      healthCheckFieldMappings: hcFieldMappings || undefined,
      healthCheckStateMapping: hcStateMapping || undefined,
    });
  };

  const handleTestHealthCheck = () => {
    if (!component) return;
    testHealthCheckMutation.mutate(component.id, {
      onSuccess: (result) => {
        toast.success(
          t("statusPage.toastHealthCheckOk", {
            status: result.status,
            responseMs: String(result.responseMs),
          }),
        );
      },
      onError: () => toast.error(t("statusPage.toastHealthCheckTestFailed")),
    });
  };

  const handleSniff = () => {
    if (!component) return;
    sniffHealthCheckMutation.mutate(component.id, {
      onSuccess: (result) => {
        setSnifferResult(result);
        toast.success(
          t("statusPage.toastSnifferOk", { code: String(result.httpStatusCode) }),
        );
      },
      onError: () => toast.error(t("statusPage.toastSnifferFailed")),
    });
  };

  return (
      <Dialog open={!!component} onOpenChange={(open) => !open && onClose()}>
        <DialogContent className="bg-card border-border sm:max-w-[600px] max-h-[85vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle style={{ fontSize: '1.25rem', fontWeight: 600 }}>
              {component
                ? t('statusPage.healthCheckDialogTitle', { name: component.name })
                : t('statusPage.enableHealthCheck')}
            </DialogTitle>
          </DialogHeader>

          <div className="space-y-4 py-2">
            <div className="flex items-center justify-between p-3 rounded-lg bg-surface-light/20 border border-border">
              <div>
                <p style={{ fontSize: '0.875rem', fontWeight: 600 }}>{t('statusPage.enableHealthCheck')}</p>
                <p style={{ fontSize: '0.75rem', color: '#94A3B8' }}>{t('statusPage.healthCheckProbeHint')}</p>
              </div>
              <Switch checked={hcEnabled} onCheckedChange={setHcEnabled} />
            </div>

            {hcEnabled && (
              <>
                <div className="grid grid-cols-3 gap-3">
                  <div className="col-span-2">
                    <Label>{t('statusPage.labelUrl')}</Label>
                    <Input
                      value={hcUrl}
                      onChange={(e) => setHcUrl(e.target.value)}
                      placeholder={t("statusPage.healthCheckUrlPlaceholder")}
                      className="bg-input-background mt-1"
                    />
                  </div>
                  <div>
                    <Label htmlFor={`${formId}-hc-method`}>{t('statusPage.labelMethod')}</Label>
                    <Select value={hcMethod} onValueChange={setHcMethod}>
                      <SelectTrigger id={`${formId}-hc-method`} className="bg-input-background mt-1">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="GET">GET</SelectItem>
                        <SelectItem value="POST">POST</SelectItem>
                        <SelectItem value="HEAD">HEAD</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>

                <div className="grid grid-cols-2 gap-3">
                  <div>
                    <Label>{t('statusPage.labelIntervalSeconds')}</Label>
                    <Input
                      type="number"
                      value={hcInterval}
                      onChange={(e) => setHcInterval(Number(e.target.value))}
                      min={10}
                      className="bg-input-background mt-1"
                    />
                  </div>
                  <div>
                    <Label>{t('statusPage.labelTimeoutSeconds')}</Label>
                    <Input
                      type="number"
                      value={hcTimeout}
                      onChange={(e) => setHcTimeout(Number(e.target.value))}
                      min={1}
                      max={60}
                      className="bg-input-background mt-1"
                    />
                  </div>
                </div>

                <div>
                  <Label>{t('statusPage.labelCustomHeadersJson')}</Label>
                  <Textarea
                    value={hcHeaders}
                    onChange={(e) => setHcHeaders(e.target.value)}
                    placeholder={t('statusPage.healthCheckHeadersPlaceholder')}
                    rows={2}
                    className="bg-input-background mt-1 resize-none font-mono text-xs"
                  />
                </div>

                {hcMethod === 'POST' && (
                  <div className="grid grid-cols-3 gap-3">
                    <div className="col-span-2">
                      <Label>{t('statusPage.labelRequestBody')}</Label>
                      <Textarea
                        value={hcBody}
                        onChange={(e) => setHcBody(e.target.value)}
                        placeholder={t('statusPage.healthCheckBodyPlaceholder')}
                        rows={2}
                        className="bg-input-background mt-1 resize-none font-mono text-xs"
                      />
                    </div>
                    <div>
                      <Label>{t('statusPage.labelContentType')}</Label>
                      <Input
                        value={hcContentType}
                        onChange={(e) => setHcContentType(e.target.value)}
                        placeholder="application/json"
                        className="bg-input-background mt-1 font-mono text-xs"
                      />
                    </div>
                  </div>
                )}

                <div className="p-4 rounded-lg border border-border bg-surface-light/10 space-y-3">
                  <div className="flex items-center gap-2">
                    <Search className="w-4 h-4 text-brand-500" />
                    <p style={{ fontSize: '0.875rem', fontWeight: 600 }}>{t('statusPage.responseTemplate')}</p>
                  </div>
                  <p style={{ fontSize: '0.75rem', color: '#94A3B8' }}>
                    {t('statusPage.healthCheckResponseMapHint')}
                  </p>

                  <div>
                    <Label>{t('statusPage.labelStateMappingJson')}</Label>
                    <Textarea
                      value={hcStateMapping}
                      onChange={(e) => setHcStateMapping(e.target.value)}
                      placeholder={t('statusPage.healthCheckStateMappingPlaceholder')}
                      rows={2}
                      className="bg-input-background mt-1 resize-none font-mono text-xs"
                    />
                  </div>

                  <div>
                    <Label>{t('statusPage.labelFieldMappingsJson')}</Label>
                    <Textarea
                      value={hcFieldMappings}
                      onChange={(e) => setHcFieldMappings(e.target.value)}
                      placeholder={t('statusPage.healthCheckFieldMappingsPlaceholder')}
                      rows={2}
                      className="bg-input-background mt-1 resize-none font-mono text-xs"
                    />
                  </div>
                </div>

                <div className="flex gap-2">
                  <Button
                    variant="outline"
                    className="flex-1 bg-input-background"
                    onClick={handleTestHealthCheck}
                    disabled={testHealthCheckMutation.isPending}
                  >
                    {testHealthCheckMutation.isPending ? (
                      <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                    ) : (
                      <Zap className="w-4 h-4 mr-2" />
                    )}
                    {t('statusPage.testHealthCheck')}
                  </Button>
                  <Button
                    variant="outline"
                    className="flex-1 bg-input-background"
                    onClick={handleSniff}
                    disabled={sniffHealthCheckMutation.isPending || !hcUrl}
                  >
                    {sniffHealthCheckMutation.isPending ? (
                      <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                    ) : (
                      <Search className="w-4 h-4 mr-2" />
                    )}
                    {t('statusPage.sniffResponse')}
                  </Button>
                </div>

                {snifferResult && (
                  <div className="p-3 rounded-lg bg-surface-light/10 border border-border space-y-2">
                    <div className="flex items-center justify-between">
                      <Badge variant="outline">
                        HTTP {snifferResult.httpStatusCode} · {snifferResult.responseMs}ms
                      </Badge>
                      <span style={{ fontSize: '0.75rem', color: '#94A3B8' }}>
                        {snifferResult.contentType}
                      </span>
                    </div>
                    <pre className="text-xs p-2 rounded bg-background/50 border border-border overflow-x-auto max-h-40 overflow-y-auto font-mono">
                      {snifferResult.responseBody
                        ? (() => { try { return JSON.stringify(JSON.parse(snifferResult.responseBody), null, 2); } catch { return snifferResult.responseBody; } })()
                        : t('statusPage.noResponseBody')}
                    </pre>
                  </div>
                )}

                {component?.healthCheckSampleResponse && !snifferResult && (
                  <div className="p-3 rounded-lg bg-surface-light/10 border border-border">
                    <p style={{ fontSize: '0.75rem', fontWeight: 600, marginBottom: '0.5rem' }}>
                      {t('statusPage.lastCapturedResponse')}
                    </p>
                    <pre className="text-xs p-2 rounded bg-background/50 border border-border overflow-x-auto max-h-32 overflow-y-auto font-mono">
                      {(() => { try { return JSON.stringify(JSON.parse(component.healthCheckSampleResponse), null, 2); } catch { return component.healthCheckSampleResponse; } })()}
                    </pre>
                  </div>
                )}
              </>
            )}
          </div>

          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => onClose()}
              className="bg-input-background"
            >
              {t('common.cancel')}
            </Button>
            <Button
              onClick={handleSaveHealthCheck}
              disabled={isSaving}
              className="bg-brand-500 hover:bg-brand-600 text-white"
            >
              {isSaving ? (
                <>
                  <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                  {t('common.saving')}
                </>
              ) : (
                <>
                  <Save className="w-4 h-4 mr-2" />
                  {t('statusPage.saveHealthCheck')}
                </>
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
  );
}
