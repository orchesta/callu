import { useState } from "react";
import { t } from "@/shared/locales/i18n";
import { Card } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Input } from "@/shared/components/ui/input";
import {
    Dialog,
    DialogContent,
    DialogHeader,
    DialogTitle,
    DialogFooter,
} from "@/shared/components/ui/dialog";
import {
    Loader2,
    Plus,
    BookOpen,
    Pencil,
    Trash2,
    Play,
    Tag,
    Clock,
    Hash,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { PageHeader } from "@/shared/components/page-header";
import { EmptyState } from "@/shared/components/empty-state";
import { ErrorState } from "@/shared/components/error-state";
import {
    useRunbooks,
    useCreateRunbook,
    useUpdateRunbook,
    useMarkRunbookUsed,
    useDeleteRunbook,
} from "../hooks/use-runbooks";
import type { RunbookDto, CreateRunbookRequest } from "../types/runbooks.types";
import { useServices } from "@/features/services/hooks/use-services";

export function RunbooksList() {
    const { data: runbooks, isLoading, error } = useRunbooks();
    const { data: services } = useServices();
    const createMutation = useCreateRunbook();
    const updateMutation = useUpdateRunbook();
    const markUsedMutation = useMarkRunbookUsed();
    const deleteMutation = useDeleteRunbook();

    const [isEditorOpen, setIsEditorOpen] = useState(false);
    const [editing, setEditing] = useState<RunbookDto | null>(null);
    const [tagInput, setTagInput] = useState("");
    const [form, setForm] = useState<CreateRunbookRequest>({
        title: "",
        description: "",
        content: "",
        tags: [],
    });

    const openCreate = () => {
        setEditing(null);
        setForm({ title: "", description: "", content: "", tags: [] });
        setTagInput("");
        setIsEditorOpen(true);
    };

    const openEdit = (rb: RunbookDto) => {
        setEditing(rb);
        setForm({
            title: rb.title,
            description: rb.description ?? "",
            content: rb.content,
            serviceId: rb.serviceId ?? undefined,
            tags: rb.tags,
        });
        setTagInput("");
        setIsEditorOpen(true);
    };

    const handleSave = async () => {
        if (editing) {
            await updateMutation.mutateAsync({
                id: editing.id,
                data: { title: form.title, description: form.description, content: form.content, serviceId: form.serviceId, tags: form.tags },
            });
        } else {
            await createMutation.mutateAsync(form);
        }
        setIsEditorOpen(false);
    };

    const addTag = () => {
        const tag = tagInput.trim();
        if (tag && !form.tags.includes(tag)) {
            setForm((f) => ({ ...f, tags: [...f.tags, tag] }));
            setTagInput("");
        }
    };

    const removeTag = (tag: string) => {
        setForm((f) => ({ ...f, tags: f.tags.filter((t) => t !== tag) }));
    };

    if (isLoading) {
        return <LoadingState />;
    }

    if (error) {
        return <ErrorState title={t("common.loadFailed")} message={error.message} />;
    }

    return (
        <div className="p-6 space-y-6">
            <PageHeader
                title={t("runbooks.pageTitle")}
                subtitle={t("runbooks.pageSubtitle")}
                action={
                    <Button onClick={openCreate}>
                        <Plus className="w-4 h-4 mr-2" /> {t("runbooks.newRunbook")}
                    </Button>
                }
            />

            {!runbooks || runbooks.length === 0 ? (
                <EmptyState
                    icon={BookOpen}
                    title={t("runbooks.noRunbooks")}
                    description={t("runbooks.createRunbooksHint")}
                />
            ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                    {runbooks.map((rb) => (
                        <Card key={rb.id} className="p-5 bg-card/80 backdrop-blur-sm border-border hover:border-brand-500/30 transition-colors flex flex-col">
                            <div className="flex-1">
                                <h3 style={{ fontSize: "1rem", fontWeight: 600, marginBottom: "0.25rem" }}>
                                    {rb.title}
                                </h3>
                                {rb.description && (
                                    <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginBottom: "0.75rem" }}>
                                        {rb.description.substring(0, 100)}{rb.description.length > 100 ? "..." : ""}
                                    </p>
                                )}
                                {rb.serviceName && (
                                    <p style={{ fontSize: "0.75rem", color: "#64748B" }}>
                                        {t("runbooks.service")}: {rb.serviceName}
                                    </p>
                                )}
                                {rb.tags.length > 0 && (
                                    <div className="flex flex-wrap gap-1 mt-2">
                                        {rb.tags.map((tag) => (
                                            <Badge key={tag} className="bg-brand-500/10 text-brand-400 border-brand-500/20 border text-xs">
                                                <Tag className="w-2.5 h-2.5 mr-1" />{tag}
                                            </Badge>
                                        ))}
                                    </div>
                                )}
                            </div>
                            <div className="flex items-center justify-between mt-4 pt-3 border-t border-border">
                                <div className="flex items-center gap-3 text-xs" style={{ color: "#64748B" }}>
                                    <span className="flex items-center gap-1">
                                        <Hash className="w-3 h-3" />{rb.usageCount} {t("runbooks.uses")}
                                    </span>
                                    {rb.lastUsedAt && (
                                        <span className="flex items-center gap-1">
                                            <Clock className="w-3 h-3" />
                                            {new Date(rb.lastUsedAt).toLocaleDateString()}
                                        </span>
                                    )}
                                </div>
                                <div className="flex items-center gap-1">
                                    <Button size="sm" variant="outline" onClick={() => markUsedMutation.mutate(rb.id)}>
                                        <Play className="w-3 h-3" />
                                    </Button>
                                    <Button size="sm" variant="outline" onClick={() => openEdit(rb)}>
                                        <Pencil className="w-3 h-3" />
                                    </Button>
                                    <Button size="sm" variant="outline" className="text-error-400" onClick={() => deleteMutation.mutate(rb.id)}>
                                        <Trash2 className="w-3 h-3" />
                                    </Button>
                                </div>
                            </div>
                        </Card>
                    ))}
                </div>
            )}

            <Dialog open={isEditorOpen} onOpenChange={setIsEditorOpen}>
                <DialogContent className="max-w-2xl max-h-[85vh] overflow-y-auto">
                    <DialogHeader>
                        <DialogTitle>{editing ? t("runbooks.editRunbook") : t("runbooks.newRunbook")}</DialogTitle>
                    </DialogHeader>
                    <div className="space-y-4 py-4">
                        <Input
                            placeholder={t("runbooks.titlePlaceholder")}
                            value={form.title}
                            onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))}
                        />
                        <Input
                            placeholder={t("runbooks.descriptionPlaceholder")}
                            value={form.description ?? ""}
                            onChange={(e) => setForm((f) => ({ ...f, description: e.target.value }))}
                        />
                        <div>
                            <label style={{ fontSize: "0.75rem", fontWeight: 600, color: "#94A3B8" }}>
                                {t("runbooks.serviceLabel")}
                            </label>
                            <select
                                className="w-full mt-1 p-2.5 rounded-lg bg-input-background border border-border text-sm"
                                value={form.serviceId ?? ""}
                                onChange={(e) => setForm((f) => ({ ...f, serviceId: e.target.value || undefined }))}
                            >
                                <option value="">{t("runbooks.serviceNone")}</option>
                                {services.map((s) => (
                                    <option key={s.id} value={s.id}>{s.name}</option>
                                ))}
                            </select>
                        </div>
                        <div>
                            <label style={{ fontSize: "0.75rem", fontWeight: 600, color: "#94A3B8" }}>
                                {t("runbooks.contentLabel")}
                            </label>
                            <textarea
                                className="w-full mt-1 p-3 rounded-lg bg-input-background border border-border text-sm min-h-[300px] resize-y font-mono"
                                placeholder={t("runbooks.contentPlaceholderExample")}
                                value={form.content}
                                onChange={(e) => setForm((f) => ({ ...f, content: e.target.value }))}
                            />
                        </div>
                        <div>
                            <label style={{ fontSize: "0.75rem", fontWeight: 600, color: "#94A3B8" }}>{t("runbooks.tags")}</label>
                            <div className="flex items-center gap-2 mt-1">
                                <Input
                                    placeholder={t("runbooks.addTagPlaceholder")}
                                    value={tagInput}
                                    onChange={(e) => setTagInput(e.target.value)}
                                    onKeyDown={(e) => e.key === "Enter" && (e.preventDefault(), addTag())}
                                />
                                <Button variant="outline" size="sm" onClick={addTag}>{t("runbooks.add")}</Button>
                            </div>
                            {form.tags.length > 0 && (
                                <div className="flex flex-wrap gap-1 mt-2">
                                    {form.tags.map((tag) => (
                                        <Badge
                                            key={tag}
                                            className="bg-brand-500/10 text-brand-400 border-brand-500/20 border text-xs cursor-pointer"
                                            onClick={() => removeTag(tag)}
                                        >
                                            {tag} ×
                                        </Badge>
                                    ))}
                                </div>
                            )}
                        </div>
                    </div>
                    <DialogFooter>
                        <Button variant="outline" onClick={() => setIsEditorOpen(false)}>{t("common.cancel")}</Button>
                        <Button onClick={handleSave} disabled={!form.title || createMutation.isPending || updateMutation.isPending}>
                            {(createMutation.isPending || updateMutation.isPending) && <Loader2 className="w-4 h-4 mr-2 animate-spin" />}
                            {editing ? t("common.save") : t("common.create")}
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </div>
    );
}
