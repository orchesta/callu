/** The sentinel the select uses for "no pin", because a Select value cannot be null. */
export const UNROUTED = "__auto__";

/** Keyed by the CommunicationCapability member name, which is what the API serialises. */
export const CAPABILITY_LABEL_KEYS: Record<string, string> = {
  VoiceCalls: "providers.channelVoice",
  Sms: "providers.channelSms",
  WhatsApp: "providers.channelWhatsApp",
  VideoConference: "providers.channelVideo",
};
