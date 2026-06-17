/**
 * Callu conference scenario — video + HD audio.
 * CALLU_API_URL / CALLU_API_KEY are injected by the backend at provisioning time.
 * Recording is disabled until we settle on a storage + retention plan.
 */

require(Modules.Conference);

// Placeholders — replaced by the backend during provisioning.
var CALLU_API_URL = "{{CALLU_API_URL}}";
var CALLU_API_KEY = "{{CALLU_API_KEY}}";

var conf = null;
var participants = [];
var confData = {};
var callbackUrl = "";
var maxParticipants = 12;
var confLanguage = "tr-TR";

var NEURAL_VOICE_MAP = {
    "tr-TR": VoiceList.Microsoft.Neural.tr_TR_AhmetNeural,
    "en-US": VoiceList.Microsoft.Neural.en_US_GuyNeural,
    "en-GB": VoiceList.Microsoft.Neural.en_GB_RyanNeural,
    "de-DE": VoiceList.Microsoft.Neural.de_DE_ConradNeural,
    "fr-FR": VoiceList.Microsoft.Neural.fr_FR_HenriNeural,
    "es-ES": VoiceList.Microsoft.Neural.es_ES_AlvaroNeural,
    "it-IT": VoiceList.Microsoft.Neural.it_IT_DiegoNeural,
    "pt-BR": VoiceList.Microsoft.Neural.pt_BR_AntonioNeural,
    "ru-RU": VoiceList.Microsoft.Neural.ru_RU_DmitryNeural,
    "ja-JP": VoiceList.Microsoft.Neural.ja_JP_KeitaNeural,
    "ko-KR": VoiceList.Microsoft.Neural.ko_KR_InJoonNeural,
    "zh-CN": VoiceList.Microsoft.Neural.zh_CN_YunxiNeural
};

function resolveVoice() {
    return NEURAL_VOICE_MAP[confLanguage] || NEURAL_VOICE_MAP["tr-TR"];
}

// Nonce for the backend's replay guard (5-min seen-set).
function generateNonce() {
    return Math.floor(Date.now()).toString(36) + "-" +
        Math.random().toString(36).slice(2, 10) +
        Math.random().toString(36).slice(2, 10);
}

function authHeaders(extra) {
    var base = {
        "X-Scenario-Key": CALLU_API_KEY,
        "X-Timestamp": String(Math.floor(Date.now() / 1000)),
        "X-Nonce": generateNonce()
    };
    if (extra) {
        for (var k in extra) if (extra.hasOwnProperty(k)) base[k] = extra[k];
    }
    return base;
}

VoxEngine.addEventListener(AppEvents.Started, async function (e) {
    callbackUrl = CALLU_API_URL + "/api/voximplant/callback";

    // Web SDK callConference ships identifiers through customData
    // ({ incident_id, conference_id, participant_token }). StartConference callers
    // send call_token instead — handled further down.
    var customData = {};
    try {
        customData = JSON.parse(VoxEngine.customData() || "{}");
    } catch (err) {
        Logger.write("WARN: customData parse failed: " + err);
        customData = {};
    }

    confData = {
        incident_id: customData.incident_id || "",
        conference_id: customData.conference_id || "",
        participant_token: customData.participant_token || ""
    };

    // Back-compat: legacy server-initiated flow passes call_token and we fetch the
    // full config from the backend. Kept for StartConference callers.
    var callToken = customData.call_token;
    if (callToken) {
        var fetchUrl = CALLU_API_URL + "/api/voximplant/call-data/" + callToken;
        try {
            var res = await Net.httpRequestAsync(fetchUrl, {
                method: "GET",
                headers: authHeaders({ "Content-Type": "application/json" })
            });
            if (res && res.code === 200) {
                try {
                    var parsed = JSON.parse(res.text);
                    confData.incident_id = parsed.incident_id || confData.incident_id;
                    confData.conference_id = parsed.conference_id || confData.conference_id;
                    maxParticipants = parsed.max_participants || maxParticipants;
                    confLanguage = parsed.language || confLanguage;
                    Logger.write("Conference config loaded: " + (confData.conference_id || "unknown"));
                } catch (err) {
                    Logger.write("WARN: Failed to parse config: " + err);
                }
            } else {
                Logger.write("WARN: Failed to fetch config: HTTP " + (res ? res.code : "null"));
            }
        } catch (fetchErr) {
            Logger.write("ERROR: Fetch failed: " + fetchErr);
        }
    } else {
        Logger.write("Conference started via Web SDK. incident_id=" + (confData.incident_id || "(missing)"));
    }
});

VoxEngine.addEventListener(AppEvents.CallAlerting, function (e) {
    var call = e.call;
    // e.scheme carries the inbound call's SDP; Conference.add needs it to forward
    // the right media tracks.
    var callScheme = e.scheme;

    if (participants.length >= maxParticipants) {
        Logger.write("Max participants reached, rejecting call");
        call.answer();
        call.addEventListener(CallEvents.Connected, function () {
            var msg = confLanguage.startsWith("tr") ?
                "Konferans dolu. Lütfen daha sonra tekrar deneyin." :
                "Conference is full. Please try again later.";
            call.say(msg, { voice: resolveVoice() });
            call.addEventListener(CallEvents.PlaybackFinished, function () {
                call.hangup();
            });
        });
        return;
    }

    // Video is enabled via the routing-rule's video_conference flag at provisioning —
    // createConference only takes hd_audio here.
    if (conf === null) {
        try {
            conf = VoxEngine.createConference({ hd_audio: true });
            notifyBackend("conference_started");
        } catch (createErr) {
            Logger.write("ERROR: createConference failed: " + createErr);
            call.reject(486);
            return;
        }
    }

    call.answer();

    call.addEventListener(CallEvents.Connected, function () {
        var participantId = call.callerid() || "unknown-" + participants.length;
        var displayName = call.displayName() || participantId;

        Logger.write("Participant joined: " + displayName);

        participants.push({
            call: call,
            id: participantId,
            displayName: displayName,
            joinedAt: new Date().toISOString()
        });

        // Conference.add forwards both audio and video; sendMediaBetween would drop video.
        // https://voximplant.com/docs/guides/conferences/howto
        try {
            conf.add({
                call: call,
                mode: "FORWARD",
                direction: "BOTH",
                scheme: callScheme
            });
        } catch (addErr) {
            Logger.write("Conference.add failed: " + addErr + " — falling back to audio-only bridge");
            VoxEngine.sendMediaBetween(call, conf);
        }

        notifyBackend("participant_joined", {
            participant_id: participantId,
            display_name: displayName,
            total_participants: participants.length
        });
    });

    call.addEventListener(CallEvents.Disconnected, function () {
        var participantId = call.callerid() || "unknown";
        var displayName = call.displayName() || participantId;

        Logger.write("Participant left: " + displayName);

        participants = participants.filter(function (p) { return p.call !== call; });

        notifyBackend("participant_left", {
            participant_id: participantId,
            display_name: displayName,
            total_participants: participants.length
        });

        // Auto-terminate 5 s after the last participant leaves (covers refreshes).
        if (participants.length === 0) {
            Logger.write("Conference empty — terminating in 5s");
            setTimeout(function () {
                if (participants.length === 0) {
                    notifyBackend("conference_ended");
                    VoxEngine.terminate();
                }
            }, 5000);
        }
    });
});

function notifyBackend(status, data) {
    if (!callbackUrl) return;

    // incident_id is required; without it the backend drops the callback.
    var payload = JSON.stringify({
        incident_id: confData.incident_id || "",
        conference_id: confData.conference_id || "",
        status: status,
        total_participants: participants.length,
        data: data || {}
    });

    (async function () {
        try {
            var res = await Net.httpRequestAsync(callbackUrl, {
                method: "POST",
                headers: authHeaders({ "Content-Type": "application/json" }),
                postData: payload
            });
            Logger.write("Callback [" + status + "]: " + (res ? res.code : "null"));
        } catch (err) {
            Logger.write("Callback error [" + status + "]: " + err);
        }
    })();
}
