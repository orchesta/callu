/**
 * Callu incident call scenario. DTMF commands:
 *   1 → acknowledge, 2 → escalate, * → repeat, 999 → start conference (also acknowledges).
 * CALLU_API_URL / CALLU_API_KEY are injected by the backend during provisioning.
 */

var CALLU_API_URL = "{{CALLU_API_URL}}";
var CALLU_API_KEY = "{{CALLU_API_KEY}}";

var incidentData = {};
var callbackUrl = "";
var acknowledged = false;
var conferenceRequested = false;
var duration = 0;
var durationInterval;
var outboundCall;
var dtmfBuffer = "";
var dtmfTimeout = null;
var silenceRepromptCount = 0;
var silenceRepromptTimeout = null;
var SILENCE_TIMEOUT_MS = 15000;
var MAX_REPROMPTS = 2;
/** One UUID per VoxEngine session — correlates callbacks to a single CallLog row. */
var callSessionId = "";

function generateCallSessionId() {
    return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, function (c) {
        var r = (Math.random() * 16) | 0;
        var v = c === "x" ? r : (r & 0x3) | 0x8;
        return v.toString(16);
    });
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

// Playback state. We attach a one-shot PlaybackFinished listener to each Player
// returned by say(), rather than a shared call-level FIFO queue. When a DTMF
// keypress interrupts an in-progress prompt and starts a new one, the two players
// overlap; a shared FIFO desyncs (the superseded prompt's finish doesn't deliver a
// call-level PlaybackFinished) and drops a completion handler — which is exactly how
// the post-acknowledge hangup got lost, leaving the call open. Per-player listeners
// plus a generation token keep each prompt self-contained and cleanly interruptible.
var _currentPlayer = null;
var _playbackGeneration = 0;

// Cancels whatever prompt is currently playing and invalidates its remaining
// segments so its onComplete does NOT fire. Used when a keypress interrupts.
function stopCurrentPlayback() {
    _playbackGeneration++;
    if (_currentPlayer) {
        try { _currentPlayer.stop(); } catch (err) { /* already stopped */ }
        _currentPlayer = null;
    }
}

// True only when a segment carries speakable content. A lone "." left between an
// empty title and description makes Azure TTS fail with "Invalid data found when
// processing input" — skip those instead of emitting a failed playback.
function hasSpeakableContent(s) {
    return /[a-zA-Z0-9À-￿]/.test(s);
}

// English fallback messages — used when the backend didn't supply tts_messages.
var FALLBACK_MESSAGES = {
    "incident_message": "Alert from {service}. There is a {severity_text} issue. {title}. {description}.",
    "dtmf_prompt": "Press 1 to acknowledge, 2 to escalate, star to repeat, or 999 for video conference.",
    "ack_confirm": "Got it. The incident has been acknowledged. Thank you.",
    "escalation_confirm": "Understood. Escalation has been initiated.",
    "invalid_key": "Sorry, that key is not valid. Please try again.",
    "conference_wait": "Setting up a video conference for you. Please hold on.",
    "conference_success": "Video conference is ready. The link has been sent to {count} people. You may join now.",
    "conference_fail": "Sorry, we could not create the conference. Please try again later.",
    "conference_duplicate": "A video conference has already been requested."
};

// Keep keys in sync with NEURAL_VOICE_MAP — each voice language needs its labels here.
var SEVERITY_LABELS = {
    "tr-TR": { "critical": "acil", "high": "yüksek öncelikli", "warning": "uyarı", "info": "bilgilendirme", "low": "düşük öncelikli" },
    "en-US": { "critical": "critical", "high": "high priority", "warning": "warning", "info": "informational", "low": "low priority" },
    "en-GB": { "critical": "critical", "high": "high priority", "warning": "warning", "info": "informational", "low": "low priority" },
    "de-DE": { "critical": "kritisch", "high": "hohe Priorität", "warning": "Warnung", "info": "Information", "low": "niedrige Priorität" },
    "fr-FR": { "critical": "critique", "high": "priorité élevée", "warning": "avertissement", "info": "information", "low": "priorité basse" },
    "es-ES": { "critical": "crítico", "high": "alta prioridad", "warning": "advertencia", "info": "informativo", "low": "baja prioridad" },
    "it-IT": { "critical": "critico", "high": "alta priorità", "warning": "avviso", "info": "informativo", "low": "bassa priorità" },
    "pt-BR": { "critical": "crítico", "high": "alta prioridade", "warning": "alerta", "info": "informativo", "low": "baixa prioridade" },
    "ru-RU": { "critical": "критический", "high": "высокий приоритет", "warning": "предупреждение", "info": "информация", "low": "низкий приоритет" },
    "ja-JP": { "critical": "緊急", "high": "高優先度", "warning": "警告", "info": "情報", "low": "低優先度" },
    "ko-KR": { "critical": "긴급", "high": "높은 우선순위", "warning": "경고", "info": "정보", "low": "낮은 우선순위" },
    "zh-CN": { "critical": "紧急", "high": "高优先级", "warning": "警告", "info": "信息", "low": "低优先级" }
};

function resolveSeverityText(severity) {
    var lang = incidentData.language || "tr-TR";
    var labels = SEVERITY_LABELS[lang] || SEVERITY_LABELS["tr-TR"];
    return labels[severity] || severity;
}

// Plays a TTS string segment-by-segment so each <lang> block can use its own voice.
// Azure's <voice> tag rejects mixed-language content when wrapped inline, but
// back-to-back separate say() calls with different voices work fine.
function playSSML(call, text, options, onComplete) {
    if (!text) {
        if (onComplete) onComplete();
        return;
    }
    
    var segments = [];
    var defaultVoice = options.voice;
    var regex = /(?:<lang xml:lang="([^"]+)">([^<]+)<\/lang>)/g;
    var lastIndex = 0;
    var match;

    function pushSegment(raw, voice) {
        var trimmed = raw.trim();
        if (trimmed.length > 0 && hasSpeakableContent(trimmed)) {
            segments.push({ text: trimmed, voice: voice });
        }
    }

    while ((match = regex.exec(text)) !== null) {
        pushSegment(text.substring(lastIndex, match.index), defaultVoice);
        pushSegment(match[2], NEURAL_VOICE_MAP[match[1]] || defaultVoice);
        lastIndex = regex.lastIndex;
    }

    if (lastIndex < text.length) {
        pushSegment(text.substring(lastIndex), defaultVoice);
    }

    if (segments.length === 0) {
        if (onComplete) onComplete();
        return;
    }

    // Claim a generation. If a keypress interrupts (stopCurrentPlayback) or a newer
    // prompt starts, this sequence becomes stale and silently abandons — so a
    // superseded prompt never fires its onComplete or advances to its next segment.
    var myGeneration = ++_playbackGeneration;
    var index = 0;

    function playNext() {
        if (myGeneration !== _playbackGeneration) return; // interrupted — abandon
        if (index >= segments.length) {
            _currentPlayer = null;
            if (onComplete) onComplete();
            return;
        }
        var seg = segments[index++];
        var player = VoxEngine.createTTSPlayer(seg.text, { voice: seg.voice });
        _currentPlayer = player;
        player.addEventListener(PlayerEvents.PlaybackFinished, function handler() {
            player.removeEventListener(PlayerEvents.PlaybackFinished, handler);
            playNext();
        });
        player.sendMediaTo(call);
    }

    playNext();
}

// Resolves a TTS key, preferring backend-supplied messages over the English fallbacks.
function msg(key, vars) {
    var text = (incidentData.tts_messages && incidentData.tts_messages[key]) ||
        FALLBACK_MESSAGES[key] || key;
    if (vars) {
        for (var k in vars) {
            if (vars.hasOwnProperty(k)) {
                text = text.split("{" + k + "}").join(vars[k] || "");
            }
        }
    }
    return text;
}

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

// progressivePlayback sends audio as soon as the first chunk is synthesized —
// shaves perceived latency off long prompts.
function resolveVoiceOptions() {
    var lang = incidentData.language || "tr-TR";
    var voice = NEURAL_VOICE_MAP[lang] || VoiceList.Microsoft.Neural.tr_TR_AhmetNeural;
    return {
        voice: voice,
        ttsOptions: { rate: "slow", progressivePlayback: true }
    };
}

VoxEngine.addEventListener(AppEvents.Started, async function (e) {
    var customData = {};
    try {
        customData = JSON.parse(VoxEngine.customData() || "{}");
    } catch (err) {
        Logger.write("ERROR: Failed to parse customData: " + err);
        VoxEngine.terminate();
        return;
    }

    var callToken = customData.call_token;
    if (!callToken) {
        Logger.write("ERROR: No call_token in customData");
        VoxEngine.terminate();
        return;
    }

    var fetchUrl = CALLU_API_URL + "/api/voximplant/call-data/" + callToken;
    Logger.write("Fetching call data: " + fetchUrl);

    try {
        var res = await Net.httpRequestAsync(fetchUrl, {
            method: "GET",
            headers: authHeaders({ "Content-Type": "application/json" })
        });

        Logger.write("Fetch response. Code: " + (res ? res.code : "null"));

        if (!res || res.code !== 200) {
            Logger.write("ERROR: Bad response. Code: " + (res ? res.code : "null") + " | Body: " + (res && res.text ? res.text.substring(0, 500) : "(empty)"));
            VoxEngine.terminate();
            return;
        }

        incidentData = JSON.parse(res.text);
        Logger.write("Call data loaded. incident_id=" + incidentData.incident_id + " | phone=" + incidentData.phone);
        callbackUrl = CALLU_API_URL + "/api/voximplant/callback";
        startCall();
    } catch (fetchErr) {
        Logger.write("ERROR: Fetch failed: " + fetchErr);
        VoxEngine.terminate();
    }
});

// ═══════════════════════════════════════════════════════
//  Start Call (after data is fetched)
// ═══════════════════════════════════════════════════════

function startCall() {
    callSessionId = generateCallSessionId();
    Logger.write("call_session_id=" + callSessionId);

    // country_code and phone are stored separately and concatenated here for the SIP URI.
    var fullNumber = (incidentData.country_code || "") + incidentData.phone;
    var sipUri = "sip:" + fullNumber + "@" + incidentData.sip_server;
    Logger.write("SIP URI: " + sipUri);

    outboundCall = VoxEngine.callSIP(sipUri, {
        callerid: incidentData.caller_id || "",
        displayName: "Callu Alert",
        password: incidentData.sip_password || "",
        authUser: incidentData.sip_username || ""
    });

    outboundCall.addEventListener(CallEvents.Connected, onConnected);
    outboundCall.addEventListener(CallEvents.Disconnected, onDisconnected);
    outboundCall.addEventListener(CallEvents.Failed, onFailed);

    // AudioStarted can fire again on SIP renegotiation; running the 30-second voicemail
    // scan twice would misclassify live callers, so guard with a one-shot flag.
    var voicemailScanStarted = false;
    outboundCall.addEventListener(CallEvents.AudioStarted, function () {
        if (voicemailScanStarted) return;
        voicemailScanStarted = true;
        outboundCall.detectVoicemailTone(30);
    });
    outboundCall.addEventListener(CallEvents.VoicemailToneDetected, onVoicemailDetected);
    outboundCall.addEventListener(CallEvents.VoicemailPromptDetected, onVoicemailDetected);

    notifyBackend("alerting");
}

function onConnected(e) {
    Logger.write("Call connected");
    notifyBackend("connected");

    // Cap the outbound leg at 2 minutes so a stuck call doesn't hold the line.
    durationInterval = setInterval(function () {
        duration++;
        if (duration >= 120) {
            Logger.write("Max duration reached, terminating");
            notifyBackend("timeout", { duration: duration });
            outboundCall.hangup();
        }
    }, 1000);

    startDTMFMode();
}

function onDisconnected(e) {
    Logger.write("Call disconnected. Duration: " + duration + "s");
    clearInterval(durationInterval);
    cancelSilenceReprompt();

    if (!acknowledged) {
        notifyBackend("no_answer", { duration: duration });
    }

    VoxEngine.terminate();
}

function onFailed(e) {
    Logger.write("Call failed: " + e.code + " - " + e.reason);
    notifyBackend("failed", { code: e.code, reason: e.reason });
    VoxEngine.terminate();
}

function onVoicemailDetected(e) {
    if (!acknowledged) {
        Logger.write("Voicemail detected — terminating");
        notifyBackend("voicemail");
        outboundCall.hangup();
    }
}

// ═══════════════════════════════════════════════════════
//  MODE: Built-in TTS + DTMF (Default — No API key needed)
// ═══════════════════════════════════════════════════════

function startDTMFMode() {
    Logger.write("Mode: Built-in TTS + DTMF");

    var lang = incidentData.language || "tr-TR";
    var voiceOptions = resolveVoiceOptions();

    outboundCall.handleTones(true);
    outboundCall.addEventListener(CallEvents.ToneReceived, onDTMFReceived);

    var rawSeverity = incidentData.severity || "critical";
    var incidentMsg = msg("incident_message", {
        severity: rawSeverity,
        severity_text: resolveSeverityText(rawSeverity),
        title: incidentData.title || "",
        service: incidentData.service_name || "",
        description: incidentData.description || ""
    });
    playSSML(outboundCall, incidentMsg, voiceOptions, function() {
        if (!acknowledged) {
            playSSML(outboundCall, msg("dtmf_prompt"), voiceOptions, scheduleSilenceReprompt);
        }
    });
}

function scheduleSilenceReprompt() {
    if (acknowledged) return;
    if (silenceRepromptTimeout) clearTimeout(silenceRepromptTimeout);
    silenceRepromptTimeout = setTimeout(function () {
        if (acknowledged) return;
        if (silenceRepromptCount >= MAX_REPROMPTS) {
            Logger.write("Silence timeout — no DTMF after " + (MAX_REPROMPTS + 1) + " prompts, hanging up");
            notifyBackend("silence_timeout", { reprompts: silenceRepromptCount });
            outboundCall.hangup();
            return;
        }
        silenceRepromptCount++;
        Logger.write("Silence reprompt #" + silenceRepromptCount);
        playSSML(outboundCall, msg("dtmf_prompt"), resolveVoiceOptions(), scheduleSilenceReprompt);
    }, SILENCE_TIMEOUT_MS);
}

function cancelSilenceReprompt() {
    if (silenceRepromptTimeout) {
        clearTimeout(silenceRepromptTimeout);
        silenceRepromptTimeout = null;
    }
}

function onDTMFReceived(e) {
    var voiceOptions = resolveVoiceOptions();

    Logger.write("DTMF received: " + e.tone);
    cancelSilenceReprompt();
    stopCurrentPlayback(); // interrupt the in-flight prompt so the response plays cleanly

    // 9s buffer into a multi-digit command (999 = conference).
    if (e.tone === "9") {
        dtmfBuffer += e.tone;
        if (dtmfTimeout) clearTimeout(dtmfTimeout);

        if (dtmfBuffer === "999") {
            dtmfBuffer = "";
            requestConference();
            return;
        }

        // Incomplete "9" or "99" after 2 s — announce invalid and rearm silence reprompt.
        dtmfTimeout = setTimeout(function () {
            if (dtmfBuffer.length > 0) {
                Logger.write("Incomplete 9-prefix sequence: '" + dtmfBuffer + "' — treating as invalid");
                dtmfBuffer = "";
                playSSML(outboundCall, msg("invalid_key"), voiceOptions, scheduleSilenceReprompt);
            }
        }, 2000);
        return;
    }

    dtmfBuffer = "";
    if (dtmfTimeout) clearTimeout(dtmfTimeout);

    switch (e.tone) {
        case "1":
            acknowledged = true;
            notifyBackend("acknowledged", { method: "dtmf", dtmf: "1" });
            playSSML(outboundCall, msg("ack_confirm"), voiceOptions, function() {
                outboundCall.hangup();
            });
            break;
        case "2":
            acknowledged = true;
            notifyBackend("escalated", { method: "dtmf", dtmf: "2" });
            playSSML(outboundCall, msg("escalation_confirm"), voiceOptions, function() {
                outboundCall.hangup();
            });
            break;
        case "*":
            var repeatSeverity = incidentData.severity || "critical";
            playSSML(outboundCall, msg("incident_message", {
                severity: repeatSeverity,
                severity_text: resolveSeverityText(repeatSeverity),
                title: incidentData.title || "Unknown",
                service: incidentData.service_name || "Unknown",
                description: incidentData.description || "No details"
            }), voiceOptions, function () {
                if (!acknowledged) {
                    playSSML(outboundCall, msg("dtmf_prompt"), voiceOptions, scheduleSilenceReprompt);
                }
            });
            break;
        default:
            playSSML(outboundCall, msg("invalid_key"), voiceOptions, scheduleSilenceReprompt);
            break;
    }
}


function requestConference() {
    var voiceOptions = resolveVoiceOptions();

    if (conferenceRequested) {
        playSSML(outboundCall, msg("conference_duplicate"), voiceOptions);
        return;
    }

    conferenceRequested = true;
    Logger.write("Conference requested via 999 DTMF");

    playSSML(outboundCall, msg("conference_wait"), voiceOptions);

    (async function () {
        try {
            var conferenceUrl = CALLU_API_URL + "/api/voximplant/conference-room";
            var payload = JSON.stringify({ incidentId: incidentData.incident_id });

            var res = await Net.httpRequestAsync(conferenceUrl, {
                method: "POST",
                headers: authHeaders({ "Content-Type": "application/json" }),
                postData: payload
            });

            if (res && res.code === 200) {
                var result = JSON.parse(res.text);
                var count = result.participant_count || 0;
                // Requesting a conference is active engagement: acknowledge so the backend
                // stops escalation/retries (otherwise the responder keeps getting called),
                // then hang up this notification leg — the responder joins the video via the
                // link, not this phone call. acknowledged=true also stops onDisconnected from
                // reporting a spurious "no_answer".
                acknowledged = true;
                Logger.write("Conference room created: " + result.room_id + " | " + count + " participants");
                notifyBackend("conference_created", { room_id: result.room_id, participant_count: count });
                playSSML(outboundCall, msg("conference_success", { count: "" + count }), voiceOptions, function () {
                    outboundCall.hangup();
                });
            } else {
                Logger.write("ERROR: Conference creation failed: HTTP " + (res ? res.code : "null"));
                playSSML(outboundCall, msg("conference_fail"), voiceOptions);
                conferenceRequested = false;
            }
        } catch (err) {
            Logger.write("ERROR: Conference request error: " + err);
            playSSML(outboundCall, msg("conference_fail"), voiceOptions);
            conferenceRequested = false;
        }
    })();
}

function notifyBackend(status, data) {
    if (!callbackUrl) return;

    var payload = JSON.stringify({
        incident_id: incidentData.incident_id || "",
        call_session_id: callSessionId || "",
        status: status,
        duration: duration,
        data: Object.assign({ phone: incidentData.phone || "" }, data || {})
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
