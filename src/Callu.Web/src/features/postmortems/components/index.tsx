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
    FileText,
    Pencil,
    Trash2,
    Send,
    CheckCircle2,
    Clock,
    Undo2,
    Lock,
    Eye,
} from "lucide-react";
import { LoadingState } from "@/shared/components/loading-state";
import { PageHeader } from "@/shared/components/page-header";
import { EmptyState } from "@/shared/components/empty-state";
import { ErrorState } from "@/shared/components/error-state";
import {
    usePostmortems,
    useCreatePostmortem,
    useUpdatePostmortem,
    useSubmitPostmortem,
    useRejectPostmortem,
    usePublishPostmortem,
    useLockPostmortem,
    useDeletePostmortem,
} from "../hooks/use-postmortems";
import type {
    PostmortemDto,
    PostmortemActionItemDto,
    CreatePostmortemRequest,
} from "../types/postmortems.types";

const STATUS_STYLE: Record<string, string> = {
    Draft: "bg-yellow-500/10 text-yellow-400 border-yellow-500/20",
    InReview: "bg-blue-500/10 text-blue-400 border-blue-500/20",
    Published: "bg-green-500/10 text-green-400 border-green-500/20",
    Locked: "bg-gray-500/10 text-gray-400 border-gray-500/20",
};

export function PostmortemsList() {
    const { data: postmortems, isLoading, error } = usePostmortems();
    const createMutation = useCreatePostmortem();
    const updateMutation = useUpdatePostmortem();
    const submitMutation = useSubmitPostmortem();
    const rejectMutation = useRejectPostmortem();
    const publishMutation = usePublishPostmortem();
    const lockMutation = useLockPostmortem();
    const deleteMutation = useDeletePostmortem();

    const [isEditorOpen, setIsEditorOpen] = useState(false);
    const [editing, setEditing] = useState<PostmortemDto | null>(null);
    const [form, setForm] = useState<CreatePostmortemRequest>({
        title: "",
        content: "",
        rootCause: "",
        incidentId: "",
        actionItems: [],
    });

    const openCreate = () => {
        setEditing(null);
        setForm({ title: "", content: "", rootCause: "", incidentId: "", actionItems: [] });
        setIsEditorOpen(true);
    };

    const openEdit = (pm: PostmortemDto) => {
        setEditing(pm);
        setForm({
            title: pm.title,
            content: pm.content,
            rootCause: pm.rootCause ?? "",
            incidentId: pm.incidentId,
            actionItems: pm.actionItems,
        });
        setIsEditorOpen(true);
    };

    const handleSave = async () => {
        if (editing) {
            await updateMutation.mutateAsync({
                id: editing.id,
                data: { title: form.title, content: form.content, rootCause: form.rootCause, actionItems: form.actionItems },
            });
        } else {
            await createMutation.mutateAsync(form);
        }
        setIsEditorOpen(false);
    };

    const addActionItem = () => {
        setForm((f) => ({
            ...f,
            actionItems: [...f.actionItems, { description: "", isComplete: false }],
        }));
    };

    const updateActionItem = (idx: number, field: keyof PostmortemActionItemDto, value: string | boolean) => {
        setForm((f) => {
            const items = [...f.actionItems];
            items[idx] = { ...items[idx], [field]: value };
            return { ...f, actionItems: items };
        });
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
                title={t("postmortems.title")}
                subtitle={t("postmortems.subtitle")}
                action={
                    <Button onClick={openCreate}>
                        <Plus className="w-4 h-4 mr-2" /> {t("postmortems.newPostmortem")}
                    </Button>
                }
            />

            {!postmortems || postmortems.length === 0 ? (
                <EmptyState
                    icon={FileText}
                    title={t("postmortems.noPostmortems")}
                    description={t("postmortems.createFirstPostmortem")}
                />
            ) : (
                <div className="space-y-3">
                    {postmortems.map((pm) => (
                        <Card key={pm.id} className="p-5 bg-card/80 backdrop-blur-sm border-border hover:border-brand-500/30 transition-colors">
                            <div className="flex items-start justify-between">
                                <div className="flex-1">
                                    <div className="flex items-center gap-3 mb-2">
                                        <h3 style={{ fontSize: "1rem", fontWeight: 600 }}>{pm.title}</h3>
                                        <Badge className={`border text-xs ${STATUS_STYLE[pm.status] ?? ""}`}>
                                            {pm.status}
                                        </Badge>
                                    </div>
                                    {pm.incidentTitle && (
                                        <p style={{ fontSize: "0.75rem", color: "#64748B" }}>
                                            {t("postmortems.linkedTo")}: {pm.incidentTitle}
                                        </p>
                                    )}
                                    {pm.rootCause && (
                                        <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginTop: "0.5rem" }}>
                                            {t("postmortems.rootCause")}: {pm.rootCause.substring(0, 120)}{pm.rootCause.length > 120 ? "..." : ""}
                                        </p>
                                    )}
                                    <div className="flex items-center gap-4 mt-3">
                                        <span className="flex items-center gap-1 text-xs" style={{ color: "#64748B" }}>
                                            <Clock className="w-3 h-3" />
                                            {new Date(pm.createdAt).toLocaleDateString()}
                                        </span>
                                        <span className="flex items-center gap-1 text-xs" style={{ color: "#64748B" }}>
                                            <CheckCircle2 className="w-3 h-3" />
                                            {pm.actionItems.filter((a) => a.isComplete).length}/{pm.actionItems.length} {t("postmortems.actions")}
                                        </span>
                                    </div>
                                </div>
                                <div className="flex items-center gap-2">
                                    {pm.status === "Draft" && (
                                        <Button size="sm" variant="outline" onClick={() => submitMutation.mutate(pm.id)} disabled={submitMutation.isPending}>
                                            <Send className="w-3 h-3 mr-1" /> {t("postmortems.submit")}
                                        </Button>
                                    )}
                                    {pm.status === "InReview" && (
                                        <>
                                            <Button size="sm" variant="outline" onClick={() => rejectMutation.mutate(pm.id)} disabled={rejectMutation.isPending}>
                                                <Undo2 className="w-3 h-3 mr-1" /> {t("postmortems.reject")}
                                            </Button>
                                            <Button size="sm" variant="outline" onClick={() => publishMutation.mutate(pm.id)} disabled={publishMutation.isPending}>
                                                <Eye className="w-3 h-3 mr-1" /> {t("postmortems.publish")}
                                            </Button>
                                        </>
                                    )}
                                    {pm.status === "Published" && (
                                        <Button size="sm" variant="outline" onClick={() => lockMutation.mutate(pm.id)} disabled={lockMutation.isPending}>
                                            <Lock className="w-3 h-3 mr-1" /> {t("postmortems.lock")}
                                        </Button>
                                    )}
                                    {pm.status !== "Locked" && (
                                        <Button size="sm" variant="outline" onClick={() => openEdit(pm)}>
                                            <Pencil className="w-3 h-3" />
                                        </Button>
                                    )}
                                    {pm.status === "Draft" && (
                                        <Button size="sm" variant="outline" className="text-error-400 hover:text-error-500" onClick={() => deleteMutation.mutate(pm.id)}>
                                            <Trash2 className="w-3 h-3" />
                                        </Button>
                                    )}
                                </div>
                            </div>
                        </Card>
                    ))}
                </div>
            )}

            <Dialog open={isEditorOpen} onOpenChange={setIsEditorOpen}>
                <DialogContent className="max-w-2xl max-h-[85vh] overflow-y-auto">
                    <DialogHeader>
                        <DialogTitle>{editing ? t("postmortems.editPostmortem") : t("postmortems.newPostmortem")}</DialogTitle>
                    </DialogHeader>
                    <div className="space-y-4 py-4">
                        <Input
                            placeholder={t("postmortems.titlePlaceholder")}
                            value={form.title}
                            onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))}
                        />
                        {!editing && (
                            <Input
                                placeholder={t("postmortems.incidentIdPlaceholder")}
                                value={form.incidentId}
                                onChange={(e) => setForm((f) => ({ ...f, incidentId: e.target.value }))}
                            />
                        )}
                        <div>
                            <label style={{ fontSize: "0.75rem", fontWeight: 600, color: "#94A3B8" }}>{t("postmortems.rootCause")}</label>
                            <textarea
                                className="w-full mt-1 p-3 rounded-lg bg-input-background border border-border text-sm min-h-[80px] resize-y"
                                placeholder={t("postmortems.rootCausePlaceholder")}
                                value={form.rootCause ?? ""}
                                onChange={(e) => setForm((f) => ({ ...f, rootCause: e.target.value }))}
                            />
                        </div>
                        <div>
                            <label style={{ fontSize: "0.75rem", fontWeight: 600, color: "#94A3B8" }}>{t("postmortems.contentLabel")}</label>
                            <textarea
                                className="w-full mt-1 p-3 rounded-lg bg-input-background border border-border text-sm min-h-[200px] resize-y font-mono"
                                placeholder={t("postmortems.templatePlaceholder")}
                                value={form.content}
                                onChange={(e) => setForm((f) => ({ ...f, content: e.target.value }))}
                            />
                        </div>
                        <div>
                            <div className="flex items-center justify-between mb-2">
                                <label style={{ fontSize: "0.75rem", fontWeight: 600, color: "#94A3B8" }}>{t("postmortems.actionItems")}</label>
                                <Button size="sm" variant="outline" onClick={addActionItem}>
                                    <Plus className="w-3 h-3 mr-1" /> {t("postmortems.add")}
                                </Button>
                            </div>
                            {form.actionItems.map((item, i) => (
                                <div key={i} className="flex items-center gap-2 mb-2">
                                    <input
                                        type="checkbox"
                                        checked={item.isComplete}
                                        onChange={(e) => updateActionItem(i, "isComplete", e.target.checked)}
                                        className="rounded"
                                    />
                                    <Input
                                        className="flex-1"
                                        placeholder={t("postmortems.actionItemPlaceholder")}
                                        value={item.description}
                                        onChange={(e) => updateActionItem(i, "description", e.target.value)}
                                    />
                                </div>
                            ))}
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
