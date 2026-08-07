import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Card } from "@/shared/components/ui/card";
import { TabsContent } from "@/shared/components/ui/tabs";
import { Plus, Link2, X } from "lucide-react";
import type { ServiceDependencyDto } from "../../types/service.types";
import { getCriticalityBadge } from "../../utils/service-badges";

export interface DependenciesTabProps {
  dependencies: ServiceDependencyDto[] | undefined;
  onAddClick: () => void;
  onRemove: (depId: string) => void;
}

export function DependenciesTab({ dependencies, onAddClick, onRemove }: DependenciesTabProps) {
  return (
          <TabsContent value="dependencies" className="space-y-6">
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
              <div className="flex items-center justify-between mb-4">
                <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("services.serviceDependencies")}</h3>
                <Button onClick={() => onAddClick()} className="bg-brand-500 hover:bg-brand-600 text-white">
                  <Plus className="w-4 h-4 mr-2" />
                  {t("services.addDependency")}
                </Button>
              </div>

              {(dependencies ?? []).length > 0 ? (
                <div className="space-y-3">
                  {(dependencies ?? []).map((dep: ServiceDependencyDto) => (
                    <div
                      key={dep.id}
                      className="flex items-center justify-between p-4 rounded-lg bg-surface-light/20 border border-border hover:border-border-light transition-colors"
                    >
                      <div className="flex items-center gap-3 flex-1 min-w-0">
                        <div className="w-10 h-10 rounded-lg bg-brand-500/10 flex items-center justify-center flex-shrink-0">
                          <Link2 className="w-5 h-5 text-brand-500" />
                        </div>
                        <div className="flex-1 min-w-0">
                          <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>
                            {dep.dependsOnServiceName}
                          </p>
                          {dep.description && (
                            <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }} className="truncate">
                              {dep.description}
                            </p>
                          )}
                        </div>
                      </div>
                      <div className="flex items-center gap-2 flex-shrink-0">
                        <Badge className="bg-muted/10 text-muted-foreground border-muted/20 border text-xs">
                          {dep.type}
                        </Badge>
                        <Badge className={`${getCriticalityBadge(dep.criticality)} border text-xs`}>
                          {dep.criticality}
                        </Badge>
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => onRemove(dep.id)}
                          className="text-error-500 hover:bg-error-500/10"
                        >
                          <X className="w-4 h-4" />
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                <div className="text-center py-12">
                  <Link2 className="w-12 h-12 text-muted-foreground mx-auto mb-3 opacity-50" />
                  <p style={{ fontSize: "0.9375rem", fontWeight: 600, marginBottom: "0.5rem" }}>
                    {t("services.noDependencies")}
                  </p>
                  <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                    {t("services.noDependenciesHint")}
                  </p>
                </div>
              )}
            </Card>
          </TabsContent>
  );
}
