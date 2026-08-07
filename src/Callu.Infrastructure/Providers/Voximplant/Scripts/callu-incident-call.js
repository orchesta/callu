/** Callu incident call scenario. DTMF: 1 acknowledge, 2 escalate, * repeat, 9 start conference.
 *  CALLU_API_URL / CALLU_API_KEY are injected by the backend during provisioning. */

var CALLU_API_URL = "{{CALLU_API_URL}}";
var CALLU_API_KEY = "{{CALLU_API_KEY}}";

// Contract this script speaks with the API (headers it sends, endpoints it calls). The backend
// reads this marker back out of the provisioned scenario to tell a current script apart from one
// left behind by an older Callu — a pre-1.4 script sends no X-Call-Token, so every status callback
// it makes is refused with 401. Keep in sync with VoximplantProviderLifecycle.ScriptContractVersion.
var CALLU_SCRIPT_CONTRACT = "1.6";

// A status callback the backend never acknowledged is a call that reached someone but left no
// trace — the incident stays unacknowledged and the responder gets dialled again. Retry the
// transient failures; auth failures cannot be retried into success.
var CALLBACK_MAX_ATTEMPTS = 3;
var CALLBACK_TIMEOUT_SECONDS = 5;
var CALLBACK_BACKOFF_MS = [500, 1000];

var incidentData = {};
var callbackUrl = "";
var acknowledged = false;
var conferenceRequested = false;
var duration = 0;
var durationInterval;
var outboundCall;
var dtmfTimeout = null;
var silenceRepromptCount = 0;
var silenceRepromptTimeout = null;
var SILENCE_TIMEOUT_MS = 15000;
var MAX_REPROMPTS = 2;
/** A terminal outcome (voicemail, timeout, …) was already reported; the disconnect our own hangup
 *  causes must not report "no_answer" on top of it and overwrite the real outcome. */
var terminalStatusSent = false;
/** The responder can hang up while a status callback is still being delivered; there is then no
 *  call left to speak the confirmation on. */
var callDisconnected = false;
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
    // Per-call bearer from the call-data response. The scenario key is shared and sits in this
    // file in plaintext; this token is what tells the backend WHICH incident we may report on.
    if (incidentData.callback_token) {
        base["X-Call-Token"] = incidentData.callback_token;
    }
    if (extra) {
        for (var k in extra) if (extra.hasOwnProperty(k)) base[k] = extra[k];
    }
    return base;
}

// Playback state: a one-shot listener per Player plus a generation token, not a shared call-level
// FIFO — interrupted prompts overlap, and a shared queue desyncs and drops a completion handler.
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
    "dtmf_prompt": "Press 1 to acknowledge, 2 to escalate, star to repeat, or 9 for video conference.",
    "ack_confirm": "Got it. The incident has been acknowledged. Thank you.",
    "escalation_confirm": "Understood. Escalation has been initiated.",
    "ack_failed": "Sorry, the acknowledgement could not be registered. The incident is still open. Please acknowledge it in Callu.",
    "escalation_failed": "Sorry, the escalation request could not be registered. The incident is still open. Please escalate it in Callu.",
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
            terminalStatusSent = true;
            notifyBackend("timeout", { duration: duration });
            outboundCall.hangup();
        }
    }, 1000);

    startDTMFMode();
}

function onDisconnected(e) {
    Logger.write("Call disconnected. Duration: " + duration + "s");
    callDisconnected = true;
    clearInterval(durationInterval);
    cancelSilenceReprompt();

    // Our own hangup after a voicemail or a duration cap lands here too. Reporting "no_answer" on
    // top of the status we just sent would overwrite the real outcome on the call log.
    if (!acknowledged && !terminalStatusSent) {
        notifyBackend("no_answer", { duration: duration });
    }

    terminateWhenIdle();
}

function onFailed(e) {
    Logger.write("Call failed: " + e.code + " - " + e.reason);
    callDisconnected = true;
    terminalStatusSent = true;
    notifyBackend("failed", { code: e.code, reason: e.reason });
    terminateWhenIdle();
}

function onVoicemailDetected(e) {
    if (!acknowledged) {
        Logger.write("Voicemail detected — terminating");
        terminalStatusSent = true;
        notifyBackend("voicemail");
        outboundCall.hangup();
    }
}

// ═══════════════════════════════════════════════════════
//  MODE: Built-in TTS + DTMF (Default — No API key needed)
// ═══════════════════════════════════════════════════════

function startDTMFMode() {
    Logger.write("Mode: Built-in TTS + DTMF");

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

    if (e.tone === "9") {
        if (dtmfTimeout) clearTimeout(dtmfTimeout);
        requestConference();
        return;
    }

    if (dtmfTimeout) clearTimeout(dtmfTimeout);

    switch (e.tone) {
        // The confirmation waits for the backend's answer; acknowledged stays true either way because
        // the responder did pick up, so the disconnect must not report "no_answer".
        case "1":
            acknowledged = true;
            terminalStatusSent = true;
            notifyBackend("acknowledged", { method: "dtmf", dtmf: "1" }, function (ok) {
                announceOutcome(ok, "ack_confirm", "ack_failed", voiceOptions);
            });
            break;
        // Accepting the keypress is not the same as paging somebody: escalation_paged is the backend's
        // answer to "did anyone's phone actually ring?", and only that answer may be confirmed aloud.
        case "2":
            acknowledged = true;
            terminalStatusSent = true;
            notifyBackend("escalated", { method: "dtmf", dtmf: "2" }, function (ok, code, body) {
                var pagedSomeone = ok && !!body && body.escalation_paged === true;
                if (ok && !pagedSomeone) {
                    Logger.write("Escalation was recorded but paged NOBODY — playing the honest prompt");
                }
                announceOutcome(pagedSomeone, "escalation_confirm", "escalation_failed", voiceOptions);
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


// Speaks the outcome the backend actually recorded, then ends the call. The responder may have hung
// up while the status was still being delivered — there is nothing left to speak on then, and the
// outcome is already in the log.
function announceOutcome(delivered, successKey, failureKey, voiceOptions) {
    if (callDisconnected) {
        Logger.write("Status settled after the call ended (delivered=" + delivered + ")");
        return;
    }

    playSSML(outboundCall, msg(delivered ? successKey : failureKey), voiceOptions, function () {
        outboundCall.hangup();
    });
}

function requestConference() {
    var voiceOptions = resolveVoiceOptions();

    if (conferenceRequested) {
        playSSML(outboundCall, msg("conference_duplicate"), voiceOptions);
        return;
    }

    conferenceRequested = true;
    Logger.write("Conference requested via 9 DTMF");

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
                // Requesting a conference is active engagement, so acknowledge and hang up this leg —
                // the responder joins the video via the link, not this call.
                acknowledged = true;
                terminalStatusSent = true;
                Logger.write("Conference room created: " + result.room_id + " | " + count + " participants");
                // The announcement below is true regardless: the room exists and the invitations went
                // out from the conference-room endpoint. Only the incident's acknowledgement rides on
                // this callback, and the hangup no longer cuts its retries short.
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

function delay(ms) {
    return new Promise(function (resolve) { setTimeout(resolve, ms); });
}

// VoxEngine.terminate() kills the session and every request still in flight with it, which is how
// the status callback that a hangup triggers used to get lost. Terminate only once the callbacks
// have settled — each is bounded by CALLBACK_TIMEOUT_SECONDS and the attempt count, and the hard
// deadline below makes sure a wedged request can never hold the session open forever.
var pendingCallbacks = 0;
var terminateRequested = false;
var TERMINATE_DEADLINE_MS = 30000;

function terminateWhenIdle() {
    if (terminateRequested) return;
    terminateRequested = true;

    if (pendingCallbacks === 0) {
        VoxEngine.terminate();
        return;
    }

    setTimeout(function () {
        Logger.write("Terminate deadline reached with " + pendingCallbacks + " callback(s) unfinished");
        VoxEngine.terminate();
    }, TERMINATE_DEADLINE_MS);
}

function onCallbackSettled() {
    pendingCallbacks--;
    if (terminateRequested && pendingCallbacks === 0) VoxEngine.terminate();
}

// A code the backend can still turn into a success on a later attempt. 401/403 mean this scenario
// is not authorised (stale script, rotated key) and 4xx means the request itself is wrong — both
// stay wrong however often we send them.
function isRetryableCallbackCode(code) {
    return !code || code <= 0 || code >= 500 || code === 408 || code === 429;
}

// The parsed JSON body of a callback response, or null. The body carries what the callback ACHIEVED
// (escalation_paged) as opposed to whether it was accepted (the status code) — see the "2" branch.
function parseCallbackBody(res) {
    if (!res || !res.text) return null;
    try {
        return JSON.parse(res.text);
    } catch (err) {
        Logger.write("Callback response body was not JSON: " + err);
        return null;
    }
}

// onResult(ok, code, body) — ok is true only when the backend accepted the status, so any caller that
// tells the responder something must wait for it. `body` carries the finer answer.
function notifyBackend(status, data, onResult) {
    if (!callbackUrl) {
        if (onResult) onResult(false, 0, null);
        return Promise.resolve(false);
    }

    var payload = JSON.stringify({
        incident_id: incidentData.incident_id || "",
        attempt_id: incidentData.attempt_id || "",
        call_session_id: callSessionId || "",
        status: status,
        duration: duration,
        data: Object.assign({ phone: incidentData.phone || "" }, data || {})
    });

    pendingCallbacks++;

    return (async function () {
        var lastCode = 0;

        for (var attempt = 1; attempt <= CALLBACK_MAX_ATTEMPTS; attempt++) {
            try {
                // Fresh headers per attempt on purpose: the backend's replay guard burns the nonce
                // of every request it sees, so a retry that reused them would be rejected as a replay.
                var res = await Net.httpRequestAsync(callbackUrl, {
                    method: "POST",
                    headers: authHeaders({ "Content-Type": "application/json" }),
                    postData: payload,
                    timeout: CALLBACK_TIMEOUT_SECONDS
                });
                lastCode = res && res.code ? res.code : 0;
            } catch (err) {
                lastCode = 0;
                Logger.write("Callback error [" + status + "] attempt " + attempt + ": " + err);
            }

            if (lastCode >= 200 && lastCode < 300) {
                Logger.write("Callback [" + status + "]: " + lastCode);
                // onResult first: it may still need a live session to speak on. Settling can
                // terminate the session when a hangup is already waiting on this callback.
                if (onResult) onResult(true, lastCode, parseCallbackBody(res));
                onCallbackSettled();
                return true;
            }

            Logger.write("Callback [" + status + "] attempt " + attempt + " failed: " + lastCode);

            if (!isRetryableCallbackCode(lastCode)) break;
            if (attempt < CALLBACK_MAX_ATTEMPTS) await delay(CALLBACK_BACKOFF_MS[attempt - 1] || 1000);
        }

        Logger.write("Callback [" + status + "] GAVE UP. Last code: " + lastCode +
            (lastCode === 401 || lastCode === 403
                ? " — this scenario is not authorised; re-provision it from Callu (Settings → Communications)."
                : ""));
        if (onResult) onResult(false, lastCode, null);
        onCallbackSettled();
        return false;
    })();
}
