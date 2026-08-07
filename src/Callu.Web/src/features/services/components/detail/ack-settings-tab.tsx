import type { Dispatch, SetStateAction } from "react";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import { Card } from "@/shared/components/ui/card";
import { Switch } from "@/shared/components/ui/switch";
import { TabsContent } from "@/shared/components/ui/tabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { Plus, X } from "lucide-react";
import type { AckHeader } from "../../utils/ack-headers";

/** Fully controlled by ServiceDetail, so its one Save button sends the ack config with the rest of the
 * service; the setters keep their dispatch signatures because a functional updater is used. */
export interface AckSettingsTabProps {
  formId: string;
  ackEnabled: boolean;
  setAckEnabled: Dispatch<SetStateAction<boolean>>;
  ackUrl: string;
  setAckUrl: Dispatch<SetStateAction<string>>;
  ackHttpMethod: string;
  setAckHttpMethod: Dispatch<SetStateAction<string>>;
  ackContentType: string;
  setAckContentType: Dispatch<SetStateAction<string>>;
  ackHeaders: AckHeader[];
  setAckHeaders: Dispatch<SetStateAction<AckHeader[]>>;
  ackPayloadTemplate: string;
  setAckPayloadTemplate: Dispatch<SetStateAction<string>>;
}

export function AckSettingsTab({
  formId,
  ackEnabled,
  setAckEnabled,
  ackUrl,
  setAckUrl,
  ackHttpMethod,
  setAckHttpMethod,
  ackContentType,
  setAckContentType,
  ackHeaders,
  setAckHeaders,
  ackPayloadTemplate,
  setAckPayloadTemplate,
}: AckSettingsTabProps) {
  return (
          <TabsContent value="ack-settings" className="space-y-6">
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
              <div className="lg:col-span-2 space-y-6">
                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <div className="flex items-center justify-between mb-6">
                    <div>
                      <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("services.ackTitle")}</h3>
                      <p style={{ fontSize: "0.8125rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                        {t("services.ackSubtitle")}
                      </p>
                    </div>
                    <Switch checked={ackEnabled} onCheckedChange={setAckEnabled} />
                  </div>

                  {ackEnabled && (
                    <div className="space-y-4">
                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                          Callback URL
                        </label>
                        <Input
                          value={ackUrl}
                          onChange={(e) => setAckUrl(e.target.value)}
                          placeholder={t("services.ackCallbackUrlPlaceholder")}
                          className="bg-input-background font-mono text-sm"
                        />
                      </div>

                      <div className="grid grid-cols-2 gap-4">
                        <div>
                          <label htmlFor={`${formId}-ack-http-method`} style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                            HTTP Method
                          </label>
                          <Select value={ackHttpMethod} onValueChange={setAckHttpMethod}>
                            <SelectTrigger id={`${formId}-ack-http-method`} className="bg-input-background"><SelectValue /></SelectTrigger>
                            <SelectContent>
                              <SelectItem value="POST">POST</SelectItem>
                              <SelectItem value="PUT">PUT</SelectItem>
                              <SelectItem value="PATCH">PATCH</SelectItem>
                            </SelectContent>
                          </Select>
                        </div>
                        <div>
                          <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                            Content-Type
                          </label>
                          <Input
                            value={ackContentType}
                            onChange={(e) => setAckContentType(e.target.value)}
                            className="bg-input-background font-mono text-sm"
                          />
                        </div>
                      </div>

                      <div>
                        <div className="flex items-center justify-between mb-2">
                          <label style={{ fontSize: "0.875rem", fontWeight: 600 }}>{t("services.ackCustomHeaders")}</label>
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            className="bg-input-background"
                            onClick={() => setAckHeaders([...ackHeaders, { key: "", value: "" }])}
                          >
                            <Plus className="w-3 h-3 mr-1" /> Add Header
                          </Button>
                        </div>
                        {ackHeaders.length > 0 ? (
                          <div className="space-y-2">
                            {ackHeaders.map((h, i) => (
                              <div key={i} className="flex gap-2">
                                <Input
                                  value={h.key}
                                  onChange={(e) => {
                                    const next = [...ackHeaders];
                                    next[i] = { ...next[i], key: e.target.value };
                                    setAckHeaders(next);
                                  }}
                                  placeholder={t("services.headerNamePlaceholder")}
                                  className="bg-input-background font-mono text-sm flex-1"
                                />
                                <Input
                                  value={h.value}
                                  onChange={(e) => {
                                    const next = [...ackHeaders];
                                    next[i] = { ...next[i], value: e.target.value };
                                    setAckHeaders(next);
                                  }}
                                  placeholder={t("services.headerValuePlaceholder")}
                                  className="bg-input-background font-mono text-sm flex-1"
                                />
                                <Button
                                  type="button"
                                  variant="ghost"
                                  size="icon"
                                  className="text-error-500 hover:bg-error-500/10 flex-shrink-0"
                                  onClick={() => setAckHeaders(ackHeaders.filter((_, j) => j !== i))}
                                >
                                  <X className="w-4 h-4" />
                                </Button>
                              </div>
                            ))}
                          </div>
                        ) : (
                          <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                            {t("services.ackHeadersHint")}
                          </p>
                        )}
                      </div>

                      <div>
                        <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                          {t("services.ackPayloadTemplateLabel")}
                        </label>
                        <Textarea
                          value={ackPayloadTemplate}
                          onChange={(e) => setAckPayloadTemplate(e.target.value)}
                          rows={12}
                          className="bg-input-background resize-none font-mono text-sm"
                          placeholder={`{\n  "action": "{{ ack_type }}",\n  "incident_id": "{{ incident.external_id }}",\n  "title": "{{ incident.title }}",\n  "status": "{{ incident.status }}"\n}`}
                        />
                      </div>
                    </div>
                  )}
                </Card>
              </div>

              <div className="space-y-6">
                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>{t("services.templateVariables")}</h3>
                  <div className="space-y-4">
                    <div>
                      <p style={{ fontSize: "0.8125rem", fontWeight: 600, color: "#94A3B8", marginBottom: "0.5rem" }}>{t("services.varGroupIncident")}</p>
                      <div className="space-y-1">
                        {["incident.id", "incident.title", "incident.description", "incident.severity", "incident.status", "incident.external_id", "incident.started_at", "incident.resolved_at"].map(v => (
                          <button
                            key={v}
                            type="button"
                            className="block w-full text-left px-2 py-1 rounded text-xs font-mono hover:bg-brand-500/10 text-muted-foreground hover:text-brand-500 transition-colors"
                            onClick={() => setAckPayloadTemplate(prev => prev + `{{ ${v} }}`)}
                          >
                            {`{{ ${v} }}`}
                          </button>
                        ))}
                      </div>
                    </div>
                    <div>
                      <p style={{ fontSize: "0.8125rem", fontWeight: 600, color: "#94A3B8", marginBottom: "0.5rem" }}>{t("services.varGroupService")}</p>
                      <div className="space-y-1">
                        {["service.id", "service.name"].map(v => (
                          <button
                            key={v}
                            type="button"
                            className="block w-full text-left px-2 py-1 rounded text-xs font-mono hover:bg-brand-500/10 text-muted-foreground hover:text-brand-500 transition-colors"
                            onClick={() => setAckPayloadTemplate(prev => prev + `{{ ${v} }}`)}
                          >
                            {`{{ ${v} }}`}
                          </button>
                        ))}
                      </div>
                    </div>
                    <div>
                      <p style={{ fontSize: "0.8125rem", fontWeight: 600, color: "#94A3B8", marginBottom: "0.5rem" }}>{t("services.varGroupAckType")}</p>
                      <div className="space-y-1">
                        <button
                          type="button"
                          className="block w-full text-left px-2 py-1 rounded text-xs font-mono hover:bg-brand-500/10 text-muted-foreground hover:text-brand-500 transition-colors"
                          onClick={() => setAckPayloadTemplate(prev => prev + '{{ ack_type }}')}
                        >
                          {'{{ ack_type }}'}
                        </button>
                        <p style={{ fontSize: "0.6875rem", color: "#64748b", paddingLeft: "0.5rem" }}>{t("services.ackTypeHint")}</p>
                      </div>
                    </div>
                  </div>
                </Card>

                <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                  <h3 style={{ fontSize: "1.125rem", fontWeight: 600, marginBottom: "1rem" }}>{t("services.exampleTemplate")}</h3>
                  <pre className="p-3 rounded-lg bg-surface-light/20 border border-border text-xs font-mono overflow-x-auto whitespace-pre">{`{
  "action": "{{ ack_type }}",
  "alert_id": "{{ incident.external_id }}",
  "title": "{{ incident.title }}",
  "status": "{{ incident.status }}",
  "resolved_at": "{{ incident.resolved_at }}"
}`}</pre>
                </Card>
              </div>
            </div>
          </TabsContent>
  );
}
