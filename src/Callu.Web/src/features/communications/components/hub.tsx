/** Composes the provider, SIP trunk and TTS sections, each of which owns its own list, modal and form
 * state; only the shared delete confirmation lives here. */

import { useState } from "react";
import { toast } from "@/shared/utils/toast";
import { t } from "@/shared/locales/i18n";
import { Button } from "@/shared/components/ui/button";
import { LoadingState } from "@/shared/components/loading-state";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/shared/components/ui/dialog";
import { AlertCircle, Trash2 } from "lucide-react";
import {
  useCommunicationProviders,
  useCreateProvider,
  useUpdateProvider,
  useDeleteProvider,
  useSipTrunks,
  useCreateSipTrunk,
  useUpdateSipTrunk,
  useDeleteSipTrunk,
  useTtsTemplates,
  useSaveTtsTemplate,
  useDeleteTtsTemplate,
} from "../../settings/hooks/use-communications";
import { ProviderSection } from "./ProviderSection";
import { CapabilityRoutingSection } from "./CapabilityRoutingSection";
import { SipTrunkSection } from "./SipTrunkSection";
import { TtsTemplateSection } from "./TtsTemplateSection";
import { TtsPreviewSection } from "./TtsPreviewSection";

export interface DeleteTarget {
  type: "provider" | "sip" | "tts";
  id: string;
  name: string;
}

export function CommunicationsHub() {
  const { data: apiProviders, isLoading: isLoadingProviders } = useCommunicationProviders();
  const { data: apiSipTrunks, isLoading: isLoadingSip } = useSipTrunks();
  const { data: apiTtsTemplates, isLoading: isLoadingTts } = useTtsTemplates();

  const createProviderMutation = useCreateProvider();
  const updateProviderMutation = useUpdateProvider();
  const deleteProviderMutation = useDeleteProvider();
  const createSipTrunkMutation = useCreateSipTrunk();
  const updateSipTrunkMutation = useUpdateSipTrunk();
  const deleteSipTrunkMutation = useDeleteSipTrunk();
  const saveTtsTemplateMutation = useSaveTtsTemplate();
  const deleteTtsTemplateMutation = useDeleteTtsTemplate();


  const providers = apiProviders ?? [];
  const sipTrunks = apiSipTrunks ?? [];
  const ttsTemplates = apiTtsTemplates ?? [];

  const isSaving =
    createProviderMutation.isPending ||
    updateProviderMutation.isPending ||
    createSipTrunkMutation.isPending ||
    updateSipTrunkMutation.isPending ||
    saveTtsTemplateMutation.isPending;

  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<DeleteTarget | null>(null);

  if (isLoadingProviders || isLoadingSip || isLoadingTts) {
    return <LoadingState message={t("communications.hubLoading")} />;
  }

  const handleRequestDelete = (target: DeleteTarget) => {
    setDeleteTarget(target);
    setIsDeleteModalOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (!deleteTarget) return;
    try {
      if (deleteTarget.type === "provider") {
        await deleteProviderMutation.mutateAsync(deleteTarget.id);
        toast.success(t("providers.deleted"));
      } else if (deleteTarget.type === "sip") {
        await deleteSipTrunkMutation.mutateAsync(deleteTarget.id);
        toast.success(t("sipTrunk.deleted"));
      } else if (deleteTarget.type === "tts") {
        await deleteTtsTemplateMutation.mutateAsync(deleteTarget.id);
        toast.success(t("tts.deleted"));
      }
      setIsDeleteModalOpen(false);
      setDeleteTarget(null);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t("providers.deleteFailed"));
    }
  };

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 style={{ fontSize: "1.875rem", fontWeight: 600 }}>
          Communications Hub
        </h1>
        <p style={{ fontSize: "0.875rem", color: "#94A3B8", marginTop: "0.5rem" }}>
          Configure voice, SMS providers and SIP trunks for incident alerting
        </p>
      </div>

      {/* Who carries a channel is one decision, so the providers and the routing that picks between
          them stay in one column; the carrier and what it says sit beside them rather than below. */}
      <div className="grid gap-6 xl:grid-cols-2 xl:items-start">
        <div className="space-y-6">
          <ProviderSection
            providers={providers}
            sipTrunks={sipTrunks}
            createProviderMutation={createProviderMutation}
            updateProviderMutation={updateProviderMutation}
            isSaving={isSaving}
            onRequestDelete={handleRequestDelete}
          />

          <CapabilityRoutingSection />
        </div>

        <div className="space-y-6">
          <SipTrunkSection
            sipTrunks={sipTrunks}
            createSipTrunkMutation={createSipTrunkMutation}
            updateSipTrunkMutation={updateSipTrunkMutation}
            isSaving={isSaving}
            onRequestDelete={handleRequestDelete}
          />

          <TtsTemplateSection
            ttsTemplates={ttsTemplates}
            saveTtsTemplateMutation={saveTtsTemplateMutation}
            isSaving={isSaving}
            onRequestDelete={handleRequestDelete}
          />
        </div>
      </div>

      {/* Full width: the segments are rows of text and a language, and they read badly in half. */}
      <TtsPreviewSection />

      <Dialog open={isDeleteModalOpen} onOpenChange={setIsDeleteModalOpen}>
        <DialogContent className="bg-card border-border sm:max-w-[500px]">
          <DialogHeader>
            <DialogTitle style={{ fontSize: "1.5rem", fontWeight: 600 }}>
              {deleteTarget?.type === "provider"
                ? t("communications.deleteProviderTitle")
                : deleteTarget?.type === "sip"
                  ? t("communications.deleteSipTitle")
                  : t("communications.deleteTtsTitle")}
            </DialogTitle>
            <DialogDescription style={{ fontSize: "0.875rem", color: "#94A3B8" }}>
              {t("communications.deleteDialogLong")}
            </DialogDescription>
          </DialogHeader>
          <div className="py-4">
            <div className="flex gap-3">
              <div className="w-10 h-10 rounded-full bg-error-500/10 flex items-center justify-center flex-shrink-0">
                <AlertCircle className="w-5 h-5 text-error-500" />
              </div>
              <div>
                <p style={{ fontSize: "0.875rem", marginBottom: "0.5rem" }}>
                  {t("communications.deleteNamedConfirm", { name: deleteTarget?.name ?? "" })}
                </p>
                <p style={{ fontSize: "0.8125rem", color: "#94A3B8" }}>
                  {t("communications.deleteDialogShort")}
                </p>
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setIsDeleteModalOpen(false)}
              className="bg-input-background"
            >
              {t("common.cancel")}
            </Button>
            <Button
              className="bg-error-500 hover:bg-error-600 text-white"
              onClick={handleConfirmDelete}
            >
              <Trash2 className="w-4 h-4 mr-2" />
              {t("common.delete")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

export default CommunicationsHub;