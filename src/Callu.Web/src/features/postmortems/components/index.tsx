import { useEffect, useState } from "react";
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import { t } from "@/shared/locales/i18n";
import { DeleteConfirmDialog } from "@/shared/components/delete-confirm-dialog";
import { toast } from "@/shared/utils";
import { Card } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Input } from "@/shared/components/ui/input";
import {
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
} from "@/shared/components/ui/select";
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
import { useIncidents, useIncident } from "@/features/incidents/hooks/use-incidents";
import type {
    PostmortemDto,
    PostmortemActionItemDto,
    CreatePostmortemRequest,
} from "../types/postmortems.types";
import { dateLocale } from "@/shared/utils/datetime";

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

    const navigate = useNavigate();
    const location = useLocation();
    const { id: routeId } = useParams();
    const [searchParams] = useSearchParams();
    const isNewRoute = location.pathname === "/postmortems/new";
    const prefillIncidentId = searchParams.get("incidentId") ?? "";

    const [isEditorOpen, setIsEditorOpen] = useState(false);
    const [editing, setEditing] = useState<PostmortemDto | null>(null);
    const [viewing, setViewing] = useState<PostmortemDto | null>(null);
    const [deleting, setDeleting] = useState<PostmortemDto | null>(null);
    const [form, setForm] = useState<CreatePostmortemRequest>({
        title: "",
        content: "",
        rootCause: "",
        incidentId: "",
        actionItems: [],
    });

    const { data: incidentsPage } = useIncidents({ page: 1, pageSize: 50 });
    const incidentOptions = incidentsPage?.items ?? [];
    // Prefilled incident may be older than the first page — resolve its title separately.
    const { data: prefilledIncident } = useIncident(prefillIncidentId);

    // /postmortems/new (from incident detail): start with the create editor open.
    useEffect(() => {
        if (isNewRoute) {
            setEditing(null);
            setForm({ title: "", content: "", rootCause: "", incidentId: prefillIncidentId, actionItems: [] });
            setIsEditorOpen(true);
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [isNewRoute]);

    // /postmortems/:id (deep link): open the read-only viewer.
    useEffect(() => {
        if (!routeId || !postmortems || isEditorOpen) return;
        const pm = postmortems.find((p) => p.id === routeId);
        if (pm) {
            setViewing(pm);
        } else {
            toast.error(t("postmortems.notFound"));
            navigate("/postmortems", { replace: true });
        }
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [routeId, postmortems]);

    const closeEditor = (open: boolean) => {
        setIsEditorOpen(open);
        if (!open && (isNewRoute || routeId)) navigate("/postmortems", { replace: true });
    };

    const closeViewer = (open: boolean) => {
        if (!open) {
            setViewing(null);
            if (routeId) navigate("/postmortems", { replace: true });
        }
    };

    const openCreate = () => {
        setEditing(null);
        setForm({ title: "", content: "", rootCause: "", incidentId: "", actionItems: [] });
        setIsEditorOpen(true);
    };

    const openEdit = (pm: PostmortemDto) => {
        setViewing(null);
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
        closeEditor(false);
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
                                            {new Date(pm.createdAt).toLocaleDateString(dateLocale())}
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
                                    <Button size="sm" variant="outline" title={t("common.view")} onClick={() => setViewing(pm)}>
                                        <Eye className="w-3 h-3" />
                                    </Button>
                                    {pm.status !== "Locked" && (
                                        <Button size="sm" variant="outline" title={t("common.edit")} onClick={() => openEdit(pm)}>
                                            <Pencil className="w-3 h-3" />
                                        </Button>
                                    )}
                                    {pm.status === "Draft" && (
                                        <Button
                                            size="sm"
                                            variant="outline"
                                            className="text-error-400 hover:text-error-500"
                                            aria-label={t("postmortems.deleteAria").replace("{title}", pm.title)}
                                            onClick={() => setDeleting(pm)}
                                        >
                                            <Trash2 className="w-3 h-3" />
                                        </Button>
                                    )}
                                </div>
                            </div>
                        </Card>
                    ))}
                </div>
            )}

            <Dialog open={isEditorOpen} onOpenChange={closeEditor}>
                <DialogContent className="sm:max-w-2xl max-h-[85vh] overflow-y-auto">
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
                            <Select
                                value={form.incidentId || undefined}
                                onValueChange={(v) => setForm((f) => ({ ...f, incidentId: v }))}
                            >
                                <SelectTrigger className="bg-input-background" aria-label={t("postmortems.selectIncidentPlaceholder")}>
                                    <SelectValue placeholder={t("postmortems.selectIncidentPlaceholder")} />
                                </SelectTrigger>
                                <SelectContent>
                                    {form.incidentId && !incidentOptions.some((i) => i.id === form.incidentId) && (
                                        <SelectItem value={form.incidentId}>
                                            {prefilledIncident?.title ?? form.incidentId}
                                        </SelectItem>
                                    )}
                                    {incidentOptions.map((i) => (
                                        <SelectItem key={i.id} value={i.id}>
                                            {i.title}
                                        </SelectItem>
                                    ))}
                                </SelectContent>
                            </Select>
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
                        <Button variant="outline" onClick={() => closeEditor(false)}>{t("common.cancel")}</Button>
                        <Button
                            onClick={handleSave}
                            disabled={!form.title || (!editing && !form.incidentId) || createMutation.isPending || updateMutation.isPending}
                        >
                            {(createMutation.isPending || updateMutation.isPending) && <Loader2 className="w-4 h-4 mr-2 animate-spin" />}
                            {editing ? t("common.save") : t("common.create")}
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>

            <Dialog open={!!viewing} onOpenChange={closeViewer}>
                <DialogContent className="sm:max-w-2xl max-h-[85vh] overflow-y-auto">
                    <DialogHeader>
                        <DialogTitle className="flex items-center gap-3">
                            <span className="truncate">{viewing?.title}</span>
                            {viewing && (
                                <Badge className={`border text-xs shrink-0 ${STATUS_STYLE[viewing.status] ?? ""}`}>
                                    {viewing.status}
                                </Badge>
                            )}
                        </DialogTitle>
                    </DialogHeader>
                    {viewing && (
                        <div className="space-y-4 py-2">
                            {viewing.incidentTitle && (
                                <p style={{ fontSize: "0.8125rem", color: "#64748B" }}>
                                    {t("postmortems.linkedTo")}: {viewing.incidentTitle}
                                </p>
                            )}
                            {viewing.rootCause && (
                                <div>
                                    <label style={{ fontSize: "0.75rem", fontWeight: 600, color: "#94A3B8" }}>{t("postmortems.rootCause")}</label>
                                    <p className="mt-1 text-sm whitespace-pre-wrap">{viewing.rootCause}</p>
                                </div>
                            )}
                            <div>
                                <label style={{ fontSize: "0.75rem", fontWeight: 600, color: "#94A3B8" }}>{t("postmortems.contentLabel")}</label>
                                <pre className="mt-1 p-3 rounded-lg bg-input-background border border-border text-sm whitespace-pre-wrap font-mono">{viewing.content}</pre>
                            </div>
                            {viewing.actionItems.length > 0 && (
                                <div>
                                    <label style={{ fontSize: "0.75rem", fontWeight: 600, color: "#94A3B8" }}>{t("postmortems.actionItems")}</label>
                                    <div className="mt-1 space-y-1">
                                        {viewing.actionItems.map((item, i) => (
                                            <div key={i} className="flex items-center gap-2 text-sm">
                                                <input type="checkbox" checked={item.isComplete} disabled className="rounded" />
                                                <span className={item.isComplete ? "line-through text-muted-foreground" : ""}>{item.description}</span>
                                            </div>
                                        ))}
                                    </div>
                                </div>
                            )}
                        </div>
                    )}
                    <DialogFooter>
                        {viewing && viewing.status !== "Locked" && (
                            <Button variant="outline" onClick={() => openEdit(viewing)}>
                                <Pencil className="w-3 h-3 mr-1" /> {t("common.edit")}
                            </Button>
                        )}
                        <Button onClick={() => closeViewer(false)}>{t("common.close")}</Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>

            <DeleteConfirmDialog
                open={!!deleting}
                onOpenChange={(open) => !open && setDeleting(null)}
                title={t("postmortems.deleteTitle")}
                message={t("postmortems.deleteMsg").replace("{title}", deleting?.title ?? "")}
                isLoading={deleteMutation.isPending}
                onConfirm={() => {
                    if (!deleting) return;
                    deleteMutation.mutate(deleting.id, { onSettled: () => setDeleting(null) });
                }}
            />
        </div>
    );
}
