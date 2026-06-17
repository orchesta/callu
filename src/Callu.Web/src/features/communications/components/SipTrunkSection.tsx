/**
 * SIP Trunk Section — manages SIP trunk list + create/edit modal.
 * Extracted from CommunicationsHub for SRP.
 */

import { useState } from "react";
import { toast } from "@/shared/utils/toast";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Card } from "@/shared/components/ui/card";
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
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogFooter,
} from "@/shared/components/ui/dialog";
import {
    Network,
    Plus,
    Edit,
    Trash2,
    Save,
} from "lucide-react";
import type { SipTrunkDto } from "../../settings/types/communications.types";
import type { useCreateSipTrunk, useUpdateSipTrunk } from "../../settings/hooks/use-communications";

interface SipTrunkSectionProps {
    sipTrunks: SipTrunkDto[];
    createSipTrunkMutation: ReturnType<typeof useCreateSipTrunk>;
    updateSipTrunkMutation: ReturnType<typeof useUpdateSipTrunk>;
    isSaving: boolean;
    onRequestDelete: (target: { type: "sip"; id: string; name: string }) => void;
}

export function SipTrunkSection({
    sipTrunks,
    createSipTrunkMutation,
    updateSipTrunkMutation,
    isSaving,
    onRequestDelete,
}: SipTrunkSectionProps) {
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingSip, setEditingSip] = useState<SipTrunkDto | null>(null);

    const [formData, setFormData] = useState({
        name: "",
        server: "",
        port: 5060,
        username: "",
        password: "",
        callerId: "",
        useTls: false,
        useTcp: false,
    });

    const handleCreate = () => {
        setEditingSip(null);
        setFormData({ name: "", server: "", port: 5060, username: "", password: "", callerId: "", useTls: false, useTcp: false });
        setIsModalOpen(true);
    };

    const handleEdit = (trunk: SipTrunkDto) => {
        setEditingSip(trunk);
        setFormData({
            name: trunk.name,
            server: trunk.server,
            port: trunk.port || 5060,
            username: trunk.username || "",
            password: "",
            callerId: trunk.callerId || "",
            useTls: trunk.useTls || false,
            useTcp: trunk.useTcp || false,
        });
        setIsModalOpen(true);
    };

    const handleSave = async () => {
        try {
            if (editingSip) {
                await updateSipTrunkMutation.mutateAsync({
                    id: editingSip.id,
                    name: formData.name,
                    server: formData.server,
                    port: formData.port,
                    username: formData.username,
                    password: formData.password || undefined,
                    callerId: formData.callerId,
                    useTls: formData.useTls,
                    useTcp: formData.useTcp,
                });
                toast.success(t("sipTrunk.updated"));
            } else {
                await createSipTrunkMutation.mutateAsync({
                    name: formData.name,
                    server: formData.server,
                    port: formData.port,
                    username: formData.username,
                    password: formData.password,
                    callerId: formData.callerId,
                    useTls: formData.useTls,
                    useTcp: formData.useTcp,
                });
                toast.success(t("sipTrunk.created"));
            }
            setIsModalOpen(false);
            setFormData({ name: "", server: "", port: 5060, username: "", password: "", callerId: "", useTls: false, useTcp: false });
        } catch (err) {
            toast.error(err instanceof Error ? err.message : t("sipTrunk.saveFailed"));
        }
    };

    return (
        <>
            <Card className="p-6 bg-card/80 backdrop-blur-sm border-border">
                <div className="flex items-start gap-4 mb-6">
                    <div className="w-10 h-10 rounded-lg bg-emerald-500/10 flex items-center justify-center flex-shrink-0">
                        <Network className="w-5 h-5 text-emerald-500" />
                    </div>
                    <div className="flex-1">
                        <h3 style={{ fontSize: "1.125rem", fontWeight: 600 }}>{t("sipTrunk.title")}</h3>
                        <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                            {t("sipTrunk.description")}
                        </p>
                    </div>
                    <Button
                        onClick={handleCreate}
                        variant="outline"
                        className="bg-input-background"
                    >
                        <Plus className="w-4 h-4 mr-2" />
                        {t("sipTrunk.addTrunk")}
                    </Button>
                </div>

                {sipTrunks.length > 0 ? (
                    <div className="space-y-3">
                        {sipTrunks.map((trunk) => (
                            <div
                                key={trunk.id}
                                className="p-4 rounded-lg border border-border bg-surface-light/20 hover:border-border-light transition-all flex items-center gap-4"
                            >
                                <div className="w-10 h-10 rounded-lg bg-emerald-500/10 flex items-center justify-center flex-shrink-0">
                                    <Network className="w-5 h-5 text-emerald-500" />
                                </div>
                                <div className="flex-1 min-w-0">
                                    <p style={{ fontSize: "0.9375rem", fontWeight: 600 }}>{trunk.name}</p>
                                    <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                                        {trunk.server}:{trunk.port} •{" "}
                                        {trunk.useTls ? "TLS" : trunk.useTcp ? "TCP" : "UDP"}
                                        {trunk.callerId && ` • ${trunk.callerId}`}
                                    </p>
                                </div>
                                <div className="flex gap-2">
                                    <Button size="sm" variant="ghost" onClick={() => handleEdit(trunk)}>
                                        <Edit className="w-4 h-4" />
                                    </Button>
                                    <Button
                                        size="sm"
                                        variant="ghost"
                                        onClick={() =>
                                            onRequestDelete({ type: "sip", id: trunk.id, name: trunk.name })
                                        }
                                        className="text-error-500 hover:bg-error-500/10"
                                    >
                                        <Trash2 className="w-4 h-4" />
                                    </Button>
                                </div>
                            </div>
                        ))}
                    </div>
                ) : (
                    <div className="text-center py-8">
                        <Network className="w-10 h-10 text-muted-foreground mx-auto mb-3 opacity-50" />
                        <p style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.25rem" }}>
                            {t("sipTrunk.noTrunks")}
                        </p>
                        <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                            {t("sipTrunk.noTrunksDesc")}
                        </p>
                    </div>
                )}
            </Card>

            <Dialog open={isModalOpen} onOpenChange={setIsModalOpen}>
                <DialogContent className="bg-card border-border sm:max-w-[600px]">
                    <DialogHeader>
                        <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
                            {editingSip ? t("sipTrunk.editTrunk") : t("sipTrunk.addSipTrunk")}
                        </DialogTitle>
                        <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
                            {t("sipTrunk.configureSip")}
                        </DialogDescription>
                    </DialogHeader>

                    <div className="space-y-5 py-4">
                        <div>
                            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                {t("common.name")} <span className="text-error-500">*</span>
                            </label>
                            <Input
                                placeholder={t("sipTrunk.displayNameExamplePlaceholder")}
                                value={formData.name}
                                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                                className="bg-input-background"
                            />
                        </div>

                        <div className="grid grid-cols-3 gap-4">
                            <div className="col-span-2">
                                <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                    {t("sipTrunk.server")}
                                </label>
                                <Input
                                    placeholder={t("sipTrunk.serverHostPlaceholder")}
                                    value={formData.server}
                                    onChange={(e) => setFormData({ ...formData, server: e.target.value })}
                                    className="bg-input-background"
                                />
                            </div>
                            <div>
                                <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                    {t("sipTrunk.port")}
                                </label>
                                <Input
                                    placeholder={t("sipTrunk.portPlaceholder5060")}
                                    value={formData.port}
                                    onChange={(e) => setFormData({ ...formData, port: parseInt(e.target.value) || 0 })}
                                    className="bg-input-background"
                                />
                            </div>
                        </div>

                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                    {t("sipTrunk.username")}
                                </label>
                                <Input
                                    placeholder={t("sipTrunk.usernameExamplePlaceholder")}
                                    value={formData.username}
                                    onChange={(e) => setFormData({ ...formData, username: e.target.value })}
                                    className="bg-input-background"
                                />
                            </div>
                            <div>
                                <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                    {t("sipTrunk.password")}
                                </label>
                                <Input
                                    type="password"
                                    value={formData.password}
                                    onChange={(e) => setFormData({ ...formData, password: e.target.value })}
                                    className="bg-input-background"
                                />
                            </div>
                        </div>

                        <div>
                            <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                {t("sipTrunk.callerId")} <span className="text-error-500">*</span>
                            </label>
                            <Input
                                placeholder={t("sipTrunk.callerIdExamplePlaceholder")}
                                value={formData.callerId}
                                onChange={(e) => setFormData({ ...formData, callerId: e.target.value })}
                                className="bg-input-background"
                            />
                            <p style={{ fontSize: "0.75rem", color: "#94A3B8", marginTop: "0.25rem" }}>
                                {t("sipTrunk.callerIdHint")}
                            </p>
                        </div>

                        <div className="grid grid-cols-2 gap-4">
                            <div>
                                <label style={{ fontSize: "0.875rem", fontWeight: 600, marginBottom: "0.5rem", display: "block" }}>
                                    {t("sipTrunk.transport")}
                                </label>
                                <Select
                                    value={formData.useTls ? "TLS" : formData.useTcp ? "TCP" : "UDP"}
                                    onValueChange={(value) =>
                                        setFormData({
                                            ...formData,
                                            useTls: value === "TLS",
                                            useTcp: value === "TCP",
                                        })
                                    }
                                >
                                    <SelectTrigger className="bg-input-background">
                                        <SelectValue />
                                    </SelectTrigger>
                                    <SelectContent>
                                        <SelectItem value="UDP">UDP</SelectItem>
                                        <SelectItem value="TCP">TCP</SelectItem>
                                        <SelectItem value="TLS">TLS (sips:)</SelectItem>
                                    </SelectContent>
                                </Select>
                            </div>
                        </div>
                    </div>

                    <DialogFooter>
                        <Button variant="outline" onClick={() => setIsModalOpen(false)} className="bg-input-background">
                            {t("common.cancel")}
                        </Button>
                        <Button
                            disabled={!formData.name || !formData.server || !formData.callerId || isSaving}
                            className="bg-brand-500 hover:bg-brand-600 text-white"
                            onClick={handleSave}
                        >
                            {isSaving ? (
                                <>
                                    <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin mr-2" />
                                    {t("common.saving")}
                                </>
                            ) : (
                                <>
                                    <Save className="w-4 h-4 mr-2" />
                                    {editingSip ? t("sipTrunk.updateSipTrunk") : t("sipTrunk.createSipTrunk")}
                                </>
                            )}
                        </Button>
                    </DialogFooter>
                </DialogContent>
            </Dialog>
        </>
    );
}
