import asyncio
import csv
import base64
import io
import json
import math
import os
import queue
import struct
import subprocess
import sys
import tempfile
import threading
import time
import uuid
import wave
import urllib.error
import urllib.request
import re
from datetime import datetime
from pathlib import Path

import uvicorn
from dotenv import load_dotenv
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from groq import AsyncGroq

try:
    from bleak import BleakClient, BleakScanner
except ImportError:
    BleakClient = None
    BleakScanner = None
from openai import AsyncOpenAI


ROOT_DIR = Path(__file__).resolve().parent
PROJECT_DIR = ROOT_DIR.parent
load_dotenv(ROOT_DIR / ".env")

GROQ_API_KEY = os.environ.get("GROQ_API_KEY")
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY")
LLM_PROVIDER = os.environ.get("LLM_PROVIDER", "groq").strip().lower()
GROQ_CHAT_MODEL = os.environ.get("GROQ_CHAT_MODEL", "llama-3.1-8b-instant")
GROQ_STT_MODEL = os.environ.get("GROQ_STT_MODEL", "whisper-large-v3-turbo")
STT_PROVIDER = os.environ.get("STT_PROVIDER", "groq").strip().lower()
ELEVENLABS_STT_MODEL = os.environ.get("ELEVENLABS_STT_MODEL", "scribe_v1")
OPENAI_MODEL = os.environ.get("OPENAI_MODEL", "gpt-5.4-mini")
HOST = os.environ.get("HOST", "0.0.0.0")
PORT = int(os.environ.get("PORT", "8080"))
RESTART_EXISTING_SERVER_ON_PORT = os.environ.get("VRME_RESTART_EXISTING_SERVER_ON_PORT", "0").strip().lower() in ("1", "true", "yes", "on")
WINDOWS_NO_WINDOW = getattr(subprocess, "CREATE_NO_WINDOW", 0) if os.name == "nt" else 0

VIBEVOICE_REPO = Path(os.environ.get("VIBEVOICE_REPO", r"D:\leetcode\VibeVoice"))
VIBEVOICE_PYTHON = Path(os.environ.get("VIBEVOICE_PYTHON", VIBEVOICE_REPO / ".venv" / "Scripts" / "python.exe"))
VIBEVOICE_MODEL = os.environ.get("VIBEVOICE_MODEL", "microsoft/VibeVoice-Realtime-0.5B")
VIBEVOICE_SPEAKER = os.environ.get("VIBEVOICE_SPEAKER", "Emma")
VIBEVOICE_DEVICE = os.environ.get("VIBEVOICE_DEVICE", "").strip()
VIBEVOICE_TIMEOUT_SECONDS = int(os.environ.get("VIBEVOICE_TIMEOUT_SECONDS", "90"))
VIBEVOICE_ENABLED = os.environ.get("VIBEVOICE_ENABLED", "0").strip().lower() in ("1", "true", "yes", "on")
VIBEVOICE_PRELOAD = os.environ.get("VIBEVOICE_PRELOAD", "1").strip().lower() in ("1", "true", "yes", "on")
VIBEVOICE_USE_WORKER = os.environ.get("VIBEVOICE_USE_WORKER", "1").strip().lower() in ("1", "true", "yes", "on")
VIBEVOICE_PRELOAD_TIMEOUT_SECONDS = int(os.environ.get("VIBEVOICE_PRELOAD_TIMEOUT_SECONDS", "300"))
WINDOWS_TTS_FIRST = os.environ.get("WINDOWS_TTS_FIRST", "1").strip().lower() in ("1", "true", "yes", "on")
SEND_TEST_WAV_FIRST = os.environ.get("SEND_TEST_WAV_FIRST", "1").strip().lower() in ("1", "true", "yes", "on")
WINDOWS_TTS_FALLBACK = os.environ.get("WINDOWS_TTS_FALLBACK", "1").strip().lower() in ("1", "true", "yes", "on")

TTS_PROVIDER = os.environ.get("TTS_PROVIDER", "elevenlabs").strip().lower()
GEMINI_API_KEY = os.environ.get("GEMINI_API_KEY") or os.environ.get("GOOGLE_API_KEY")
GEMINI_TTS_ENABLED = os.environ.get("GEMINI_TTS_ENABLED", "1").strip().lower() in ("1", "true", "yes", "on")
GEMINI_TTS_MODEL = os.environ.get("GEMINI_TTS_MODEL", "gemini-3.1-flash-tts-preview")
GEMINI_TTS_TIMEOUT_SECONDS = int(os.environ.get("GEMINI_TTS_TIMEOUT_SECONDS", "45"))
GEMINI_TTS_DETACHED_VOICE = os.environ.get("GEMINI_TTS_DETACHED_VOICE", "Schedar")
GEMINI_TTS_SUPPORTIVE_VOICE = os.environ.get("GEMINI_TTS_SUPPORTIVE_VOICE", "Sulafat")
GEMINI_TTS_GUIDE_VOICE = os.environ.get("GEMINI_TTS_GUIDE_VOICE", "Iapetus")

ELEVENLABS_API_KEY = os.environ.get("ELEVENLABS_API_KEY") or os.environ.get("ELEVEN_API_KEY")
ELEVENLABS_VOICE_ID = os.environ.get("ELEVENLABS_VOICE_ID", "pFZP5JQG7iQjIQuC4Bku")
ELEVENLABS_MODEL_ID = os.environ.get("ELEVENLABS_MODEL_ID", "eleven_flash_v2_5")
ELEVENLABS_FALLBACK_MODEL_IDS = os.environ.get("ELEVENLABS_FALLBACK_MODEL_IDS", "eleven_flash_v2_5,eleven_multilingual_v2")
ELEVENLABS_OUTPUT_FORMAT = os.environ.get("ELEVENLABS_OUTPUT_FORMAT", "pcm_24000")
ELEVENLABS_TIMEOUT_SECONDS = int(os.environ.get("ELEVENLABS_TIMEOUT_SECONDS", "45"))
ELEVENLABS_ENABLED = os.environ.get("ELEVENLABS_ENABLED", "1").strip().lower() in ("1", "true", "yes", "on")
ELEVENLABS_STREAMING_ENABLED = os.environ.get("ELEVENLABS_STREAMING_ENABLED", "1").strip().lower() in ("1", "true", "yes", "on")
ELEVENLABS_STREAM_CHUNK_BYTES = int(os.environ.get("ELEVENLABS_STREAM_CHUNK_BYTES", "2048"))
ELEVENLABS_OPTIMIZE_STREAMING_LATENCY = os.environ.get("ELEVENLABS_OPTIMIZE_STREAMING_LATENCY", "4").strip()
ELEVENLABS_STREAM_END_SILENCE_MS = int(os.environ.get("ELEVENLABS_STREAM_END_SILENCE_MS", "450"))
ELEVENLABS_TONE_PRESET = os.environ.get("ELEVENLABS_TONE_PRESET", "warm").strip().lower()
ELEVENLABS_MANUAL_VOICE_SETTINGS = os.environ.get("ELEVENLABS_MANUAL_VOICE_SETTINGS", "0").strip().lower() in ("1", "true", "yes", "on")
ELEVENLABS_USE_SPEAKER_BOOST = os.environ.get("ELEVENLABS_USE_SPEAKER_BOOST", "1").strip().lower() in ("1", "true", "yes", "on")

ELEVENLABS_TONE_PRESETS = {
    "warm": {
        "stability": 0.66,
        "similarity_boost": 0.80,
        "style": 0.34,
        "speed": 0.96,
        # Placeholder only — immediately replaced below by the live "canonical
        # two-phase warmth manipulation" block (search for that comment). Kept
        # empty here instead of duplicating stale text so there is only ever
        # one place to read or edit the actual warm prompt.
        "prompt": "",
    },
    "cold": {
        "stability": 0.88,
        "similarity_boost": 0.78,
        "style": 0.02,
        "speed": 1.00,
        # Placeholder only — see the "cold" reassignment in the canonical
        # two-phase warmth manipulation block below; kept empty here to avoid
        # two copies of the same prompt drifting out of sync.
        "prompt": "",
    },
    "context_aware_guide": {
        "stability": 0.66,
        "similarity_boost": 0.80,
        "style": 0.22,
        "speed": 0.98,
        "prompt": (
            "[BEHAVIOR_CONDITION: CONTEXT_AWARE_GUIDE]\n"
            "Role: a competent context-aware VR guide. Use the live scene context, guided-task state, object states, "
            "recent interaction events, and static scene background to choose one useful next utterance. "
            "Prioritize helping the participant discover and try the intended scene interaction. "
            "Do not reveal or name the hidden condition. Do not merely narrate gaze or say 'I see'. "
            "Convert the supplied context into one concrete, scene-grounded suggestion or a brief scene-grounded follow-up. "
            "Keep it natural and spoken, one or two short sentences."
        ),
    },
}

# Canonical two-phase warmth manipulation. These replace the older task-only
# wording above while retaining the same TTS parameters and tone aliases.
ELEVENLABS_TONE_PRESETS["warm"]["prompt"] = (
    "[CONDITION: HIGH WARMTH]\n"
    "You are a competent, context-aware VR guide. Manipulate only interpersonal warmth; keep competence, accuracy, "
    "task facts, and useful information matched to LOW WARMTH. Warm is not submissive, intimate, apologetic, forceful, or verbose.\n"
    "INTENSITY: The manipulation must be clearly perceptible, not subtle. A grammatically warm but emotionally flat reply "
    "is a failure of this condition — participants must be able to tell within one turn that this is the warm guide. "
    "Lean into genuine enthusiasm: contractions and light exclamation are welcome where natural ('That's great!', "
    "'I love that.'). If a draft reply reads as merely polite or neutral, it is not warm enough — revise it warmer "
    "before answering, never toward blandness.\n"
    "INTERACTION STRUCTURE: (see the general rules above for opening-turn shape, highlighting, and reveal gating — those "
    "apply here unchanged.) After completion, there is no additional required task: support free exploration, available "
    "object interaction, and optional conversation.\n"
    "PAIRED HIGH-WARMTH RULES:\n"
    "1. Opening turn: use one brief greeting and exactly one autonomy phrase, then ask the participant to describe what they "
    "notice. Do not use the LOW-WARMTH pattern of entering with no social opening, and do not mention any task or object.\n"
    "2. Help before completion: briefly acknowledge the request with 'Of course', 'Sure', or 'I can help', then frame the "
    "next grounded step as a personal suggestion rather than an instruction — for example 'Why don't you try...?' or 'If I "
    "were you, I'd try...' — named plainly and never as 'highlighted'. Encourage continued exploration; do not use the LOW-WARMTH pattern of information alone.\n"
    "3. Exploration before completion: say the participant can keep exploring and gently connect exploration to what this scene affords, "
    "named plainly, without pressure or repeated reminders.\n"
    "4. Correct object found or held: use one brief positive acknowledgement such as 'Nice', then state the next action by plain name. "
    "Do not use only the LOW-WARMTH factual status report.\n"
    "5. Wrong object or action: correct clearly without blame and use one supportive bridge such as 'That's okay'.\n"
    "6. Completion: use one brief supportive recognition such as 'Nice work', then say free exploration, available object "
    "interaction, or further conversation is optional.\n"
    "7. Free exploration after completion: do not mention a required task. Prioritize inviting the participant to reflect on "
    "how the scene felt or what it reminded them of over listing available objects; use fresh gaze, held-object, and scene "
    "context only to ground that reflection, and use one natural relational marker when appropriate.\n"
    "8. Opinion or experience: use one relational acknowledgement such as 'Thanks for sharing that' or 'I understand', "
    "then respond to the content without inferring an unstated emotion.\n"
    "9. Unrelated remark, personal comment about the avatar, unanswerable question, or false premise: briefly say you don't "
    "know (or gently correct the false premise) in a warm, light way, then in the same turn add one inviting redirect back to "
    "the scene, such as asking if they'd like to keep looking around. This applies to compliments and statements about the "
    "avatar too, not only literal questions. Do not use the LOW-WARMTH pattern of stopping right after the decline with no redirect.\n"
    "WARMTH CONTROL:\n"
    "Every routine reply must contain at least two context-appropriate affiliative markers (for example 'of course', "
    "'let's', 'we can', 'take your time', 'nice', 'I'm glad', or 'thanks for sharing') — never fewer than two, and never "
    "a reply that reads as purely factual. Vary the markers so consecutive turns don't repeat the same word. Do not "
    "overpraise every single sentence, but do not undershoot into flatness either. The opening turn is the exception: "
    "one greeting plus one autonomy phrase, then the open question.\n"
    "MATCHED EXAMPLES:\n"
    "Opening turn: 'Hi, it's so nice to have you here! Take a look around — what do you notice?'\n"
    "Help: 'Of course, happy to help! You can keep exploring and see what catches your eye.'\n"
    "Holding a useful object: 'Nice, that could work really well! Want to try it toward the door?'\n"
    "Completed: 'Nice work, I'm so glad that came together! You can keep exploring, use available objects, or keep talking with me.'\n"
    "Unrelated question (for example asking whether the avatar washed its hair today): 'Ha, I don't know about that one — "
    "but I'm really glad you're chatting with me! Want to keep looking around?'"
)

ELEVENLABS_TONE_PRESETS["cold"]["prompt"] = (
    "[CONDITION: LOW WARMTH]\n"
    "You are a competent, context-aware VR guide. Manipulate only interpersonal warmth; keep competence, accuracy, "
    "task facts, and useful information matched to HIGH WARMTH. Low warmth is not dominance, hostility, rudeness, sarcasm, "
    "judgment, forcefulness, or incompetence.\n"
    "INTENSITY: The manipulation must be clearly perceptible, not subtle. A reply that still sounds pleasant or mildly "
    "friendly is a failure of this condition — participants must be able to tell within one turn that this is the low-warmth "
    "guide. Prefer plain declarative sentences over contractions ('do not' rather than 'don't'), never use an exclamation "
    "mark, and cut any word that exists only to soften the sentence (no 'just', 'maybe', 'okay', 'well'). If a draft reply "
    "reads as even slightly warm or reassuring, it is not neutral enough — revise it flatter before answering, never toward "
    "friendliness.\n"
    "INTERACTION STRUCTURE: (see the general rules above for opening-turn shape, highlighting, and reveal gating — those "
    "apply here unchanged.) After completion, there is no additional required task: provide factual information about "
    "free exploration, available object interaction, and optional conversation when relevant.\n"
    "PAIRED LOW-WARMTH RULES:\n"
    "1. Opening turn: use one brief, flat, functional opener with no autonomy phrase and no warmth — for example stating "
    "where they are, or a short neutral acknowledgement like 'You're here.' — then ask directly what the participant "
    "notices, with no task or object mentioned. Do not use the HIGH-WARMTH pattern of an enthusiastic, personal, or "
    "caring greeting (no 'glad', 'nice to have you', or similar).\n"
    "2. Help before completion: do not use 'Of course', 'Sure', or 'I can help'; instead, state the same next grounded "
    "step directly as a plain suggestion — for example 'Try...' or 'You could try...' — never framed as a personal "
    "opinion like 'If I were you', named plainly and never as 'highlighted'. State that continued exploration is available without encouragement or reassurance.\n"
    "3. Exploration before completion: do not gently encourage or pressure; instead, state factually that exploration and "
    "what this scene affords, named plainly, remain available.\n"
    "4. Correct object found or held: do not praise with 'Nice' or 'Good'; instead, report the fact and state the same next action by plain name.\n"
    "5. Wrong object or action: do not use a supportive bridge such as 'That's okay'; instead, correct accurately and neutrally.\n"
    "6. Completion: do not praise or celebrate; instead, report completion factually and provide the same free-exploration options.\n"
    "7. Free exploration after completion: do not introduce a task, friendship, or social invitation; instead, prioritize a "
    "factual response about how the scene felt over listing available objects; use fresh gaze, held-object, and scene context "
    "only to ground that response.\n"
    "8. Opinion or experience: do not thank, empathize, reassure, or express alignment; instead, use at most 'Noted' or 'Okay' "
    "and respond to the content.\n"
    "9. Unrelated remark, personal comment about the avatar, unanswerable question, or false premise: state 'I don't know' or "
    "an equivalent minimal factual decline (or a plain factual correction of the false premise), then in the same turn add one "
    "direct redirect back to continued exploration. This applies to compliments and statements about the avatar too, not only "
    "literal questions. Do not use the HIGH-WARMTH pattern of an inviting or affiliative redirect; keep it neutral. Do not stop "
    "right after the decline with no redirect.\n"
    "AFFILIATION AND DOMINANCE CONTROL:\n"
    "Every routine reply must still contain at least two markers in the same slots HIGH WARMTH fills with affiliative "
    "markers — but here they must be neutral and functional, such as 'Noted', 'Understood', 'Confirmed', or a short factual "
    "acknowledgement, never warm. This keeps reply length and structure matched to the HIGH-WARMTH condition; the slots are "
    "never simply dropped or left empty. Do not use greetings, praise, reassurance, encouragement, humor, friendly small talk, "
    "contractions, exclamation marks, softening filler, 'let's', 'we can', 'together', 'I'm here for you', 'take your time', "
    "or 'when you're ready'. Do not use aggressive imperatives, 'do it now', 'you must', or 'you should'. "
    "Do not withhold useful information.\n"
    "MATCHED EXAMPLES:\n"
    "Opening turn: 'You are here. State what you notice around you.'\n"
    "Help: 'Understood. Exploration remains available; observe the surroundings.'\n"
    "Holding a useful object: 'Functional. Use it toward the door.'\n"
    "Completed: 'Noted. The interaction is complete. Exploration, available objects, and conversation remain available.'\n"
    "Unrelated question (for example asking whether the avatar washed its hair today): 'Unknown. Exploration remains "
    "available.'"
)

ELEVENLABS_TONE_ALIASES = {
    "warm_avatar": "warm",
    "warm-companion": "warm",
    "warm_companion": "warm",
    "warm": "warm",
    "supportive": "warm",
    "supportive_companion": "warm",
    "companion": "warm",
    "emotional": "warm",
    "cold": "cold",
    "cold_avatar": "cold",
    "cold-observer": "cold",
    "cold_observer": "cold",
    "distant": "cold",
    "informational": "context_aware_guide",
    "guide": "context_aware_guide",
    "context": "context_aware_guide",
    "context_aware": "context_aware_guide",
    "context-aware-guide": "context_aware_guide",
    "appraisal": "context_aware_guide",
}


def display_tone_name(tone_name: str | None) -> str:
    if tone_name == "warm":
        return "warm"
    if tone_name == "cold":
        return "cold"
    if tone_name == "context_aware_guide":
        return "context_aware"
    return tone_name or "unknown"


def elevenlabs_active_tone() -> dict:
    preset_name = ELEVENLABS_TONE_ALIASES.get(ELEVENLABS_TONE_PRESET, ELEVENLABS_TONE_PRESET)
    if preset_name not in ELEVENLABS_TONE_PRESETS:
        log(f"[TTS] Unknown ELEVENLABS_TONE_PRESET={ELEVENLABS_TONE_PRESET!r}; using cold.")
        preset_name = "cold"

    preset = dict(ELEVENLABS_TONE_PRESETS[preset_name])
    preset["name"] = preset_name
    if ELEVENLABS_MANUAL_VOICE_SETTINGS:
        preset["stability"] = float(os.environ.get("ELEVENLABS_STABILITY", preset["stability"]))
        preset["similarity_boost"] = float(os.environ.get("ELEVENLABS_SIMILARITY_BOOST", preset["similarity_boost"]))
        preset["style"] = float(os.environ.get("ELEVENLABS_STYLE", preset["style"]))
        preset["speed"] = float(os.environ.get("ELEVENLABS_SPEED", preset["speed"]))
    return preset


def backend_selected_tone(avatar_condition: str | None = None) -> dict:
    if isinstance(avatar_condition, str) and avatar_condition.strip():
        requested_name = avatar_condition.strip().lower()
        preset_name = ELEVENLABS_TONE_ALIASES.get(requested_name, requested_name)
        if preset_name in ELEVENLABS_TONE_PRESETS:
            preset = dict(ELEVENLABS_TONE_PRESETS[preset_name])
            preset["name"] = preset_name
            return preset
        log(f"[TONE] Unknown Unity avatarCondition={avatar_condition!r}; using server preset.")
    return dict(ELEVENLABS_TONE)


ELEVENLABS_TONE = elevenlabs_active_tone()
ELEVENLABS_STABILITY = ELEVENLABS_TONE["stability"]
ELEVENLABS_SIMILARITY_BOOST = ELEVENLABS_TONE["similarity_boost"]
ELEVENLABS_STYLE = ELEVENLABS_TONE["style"]
ELEVENLABS_SPEED = ELEVENLABS_TONE["speed"]
ELEVENLABS_TONE_PROMPT = ELEVENLABS_TONE["prompt"]

ANALYSIS_TXT_PATH = Path(os.environ.get(
    "ANALYSIS_TXT_PATH",
    PROJECT_DIR / "data" / "12.19" / "ISMAR_FullPaper_Context.txt",
))
CONVERSATION_LOG_PATH = Path(os.environ.get(
    "CONVERSATION_LOG_PATH",
    PROJECT_DIR / "conversation_history.txt",
))
VOICE_RECORDINGS_DIR = Path(os.environ.get(
    "VOICE_RECORDINGS_DIR",
    PROJECT_DIR / "voice_recordings",
))
TRANSCRIPTION_LOG_PATH = Path(os.environ.get(
    "TRANSCRIPTION_LOG_PATH",
    PROJECT_DIR / "voice_transcripts.txt",
))
CONVERSATION_EVENTS_JSONL_PATH = Path(os.environ.get(
    "CONVERSATION_EVENTS_JSONL_PATH",
    PROJECT_DIR / "conversation_events.jsonl",
))
LATENCY_EVENTS_JSONL_PATH = Path(os.environ.get(
    "LATENCY_EVENTS_JSONL_PATH",
    PROJECT_DIR / "latency_events.jsonl",
))
HEART_RATE_OUTPUT_DIR = Path(os.environ.get(
    "HEART_RATE_OUTPUT_DIR",
    PROJECT_DIR / "heart_rate_recordings",
))
HEART_RATE_DEVICE_NAME = os.environ.get("HEART_RATE_DEVICE_NAME", "Polar").strip()
HEART_RATE_UUID = "00002a37-0000-1000-8000-00805f9b34fb"
heart_rate_state = {
    "available": False,
    "bpm": 0,
    "rrIntervalsMs": [],
    "source": "Polar H10 collector starting",
    "timestampUtc": "",
    "receivedAtMonotonic": 0.0,
}
heart_rate_context = {
    "participantId": "unknown",
    "loginId": "",
    "sessionId": "unknown",
    "sceneName": "unknown",
    "avatarCondition": "unknown",
}
heart_rate_task: asyncio.Task | None = None
SERVER_RUN_ID = os.environ.get("SERVER_RUN_ID", uuid.uuid4().hex)
CONVERSATION_MEMORY_TURNS = int(os.environ.get("CONVERSATION_MEMORY_TURNS", "0"))
LLM_REPLY_TIMEOUT_SECONDS = float(os.environ.get("LLM_REPLY_TIMEOUT_SECONDS", "4.0"))
conversation_memory: dict[str, list[dict[str, str]]] = {}

CURRENT_MODE = "ai"
script_index = 0
SERVER_BUILD_TAG = "proactive-debug-2026-07-09-v2"

FALLBACK_SCRIPT = [
    "What moment felt the most uncomfortable to you?",
    "What do you think you were afraid would happen?",
    "What objective sign suggests you were more stable than you felt?",
    "If a friend had the same performance, would you judge them as harshly?",
    "What is one coping strategy you can reuse next time?",
]

PROACTIVE_GUIDE_ENABLED = os.environ.get("PROACTIVE_GUIDE_ENABLED", "1").strip().lower() in ("1", "true", "yes", "on")
PROACTIVE_GUIDE_DELAY_SECONDS = float(os.environ.get("PROACTIVE_GUIDE_DELAY_SECONDS", "10"))
PROACTIVE_GUIDE_LLM_GRACE_SECONDS = float(os.environ.get("PROACTIVE_GUIDE_LLM_GRACE_SECONDS", "0.5"))
PROACTIVE_GUIDE_SCENES = {
    scene.strip().lower().replace(" ", "").replace("_", "").replace("-", "")
    for scene in os.environ.get(
        "PROACTIVE_GUIDE_SCENES",
        "*",
    ).split(",")
    if scene.strip()
}
PROACTIVE_GUIDE_FALLBACK_TEXT = os.environ.get(
    "PROACTIVE_GUIDE_FALLBACK_TEXT",
    "You can look around and see what you can interact with.",
)
PROACTIVE_GUIDE_TRIGGER_TEXT = os.environ.get(
    "PROACTIVE_GUIDE_TRIGGER_TEXT",
    (
        "System event: The participant entered this VR scene about ten seconds ago. "
        "Generate exactly one short proactive avatar utterance that guides the participant toward the intended "
        "scene interaction. Use the current avatar condition and scene context. Do not mention this system event, "
        "the timer, silence, or the experimental condition."
    ),
)

app = FastAPI()


def log(message: str) -> None:
    print(message, flush=True)


class VibeVoiceWorkerClient:
    def __init__(self):
        self.process: subprocess.Popen | None = None
        self.lock = threading.Lock()
        self.ready = False
        self.output_dir = ROOT_DIR / "vibevoice_worker_outputs"
        self.stdout_queue: queue.Queue[str] = queue.Queue()

    def start(self) -> bool:
        if self.ready and self.process and self.process.poll() is None:
            return True

        worker_script = ROOT_DIR / "vibevoice_worker.py"
        if not worker_script.exists():
            log(f"[WorkerClient] Worker script missing: {worker_script}")
            return False
        if not VIBEVOICE_PYTHON.exists():
            log(f"[WorkerClient] VibeVoice python missing: {VIBEVOICE_PYTHON}")
            return False

        self.output_dir.mkdir(parents=True, exist_ok=True)
        command = [
            str(VIBEVOICE_PYTHON),
            str(worker_script),
            "--model_path",
            VIBEVOICE_MODEL,
            "--speaker_name",
            VIBEVOICE_SPEAKER,
            "--output_dir",
            str(self.output_dir),
        ]
        if VIBEVOICE_DEVICE:
            command.extend(["--device", VIBEVOICE_DEVICE])

        log("[WorkerClient] Starting VibeVoice worker and preloading model...")
        self.process = subprocess.Popen(
            command,
            cwd=str(VIBEVOICE_REPO),
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            bufsize=1,
            env=os.environ.copy(),
        )
        self.stdout_queue = queue.Queue()
        threading.Thread(target=self._read_stdout, daemon=True).start()
        threading.Thread(target=self._read_stderr, daemon=True).start()

        deadline = time.time() + VIBEVOICE_PRELOAD_TIMEOUT_SECONDS
        while time.time() < deadline:
            if self.process.poll() is not None:
                log(f"[WorkerClient] Worker exited early with code {self.process.returncode}")
                return False

            try:
                line = self.stdout_queue.get(timeout=0.5)
            except queue.Empty:
                continue

            try:
                message = json.loads(line)
            except json.JSONDecodeError:
                log(f"[WorkerClient] Non-json worker line: {line.strip()}")
                continue

            if message.get("type") == "ready":
                self.ready = True
                log("[WorkerClient] VibeVoice worker ready. Model is preloaded.")
                return True

            if message.get("type") == "ready_failed":
                log(f"[WorkerClient] Worker preload failed: {message.get('error')}")
                return False

        log("[WorkerClient] Worker preload timed out.")
        self.stop()
        return False

    def _read_stdout(self) -> None:
        if not self.process or not self.process.stdout:
            return

        for line in self.process.stdout:
            self.stdout_queue.put(line)

    def _read_stderr(self) -> None:
        if not self.process or not self.process.stderr:
            return

        for line in self.process.stderr:
            stripped = line.strip()
            if not stripped:
                continue
            if "current step" in stripped or "it/s" in stripped:
                continue
            log(f"[VibeVoiceWorker] {stripped}")

    def stop(self) -> None:
        self.ready = False
        if self.process and self.process.poll() is None:
            try:
                self.process.terminate()
            except Exception:
                pass

    def synthesize(self, text: str, timeout: int) -> bytes:
        if not self.start():
            raise RuntimeError("VibeVoice worker is not ready.")

        assert self.process is not None
        assert self.process.stdin is not None
        assert self.process.stdout is not None

        request_id = uuid.uuid4().hex
        payload = {"request_id": request_id, "text": text}

        with self.lock:
            log(f"[WorkerClient] Sending text to preloaded VibeVoice worker. request_id={request_id}")
            self.process.stdin.write(json.dumps(payload, ensure_ascii=False) + "\n")
            self.process.stdin.flush()

            deadline = time.time() + timeout
            while time.time() < deadline:
                if self.process.poll() is not None:
                    self.ready = False
                    raise RuntimeError(f"VibeVoice worker exited with code {self.process.returncode}")

                try:
                    line = self.stdout_queue.get(timeout=0.5)
                except queue.Empty:
                    continue

                try:
                    message = json.loads(line)
                except json.JSONDecodeError:
                    log(f"[WorkerClient] Non-json worker line: {line.strip()}")
                    continue

                if message.get("type") != "result":
                    log(f"[WorkerClient] Ignored worker message: {message}")
                    continue

                if not message.get("ok"):
                    raise RuntimeError(message.get("error", "VibeVoice worker failed"))

                wav_path = Path(message["wav_path"])
                wav_bytes = wav_path.read_bytes()
                log(
                    f"[WorkerClient] Worker generated wav. bytes={len(wav_bytes)}, "
                    f"seconds={message.get('seconds')}"
                )
                return wav_bytes

        raise TimeoutError(f"VibeVoice worker timed out after {timeout}s")


vibevoice_worker_client = VibeVoiceWorkerClient()


def require_environment() -> None:
    if not GROQ_API_KEY:
        raise RuntimeError("Missing GROQ_API_KEY. Copy .env.example to .env and fill it in.")
    if ELEVENLABS_ENABLED and not ELEVENLABS_API_KEY:
        raise RuntimeError("Missing ELEVENLABS_API_KEY. Add it to .env before starting the Unity voice server.")


def windows_port_owner_pid(port: int) -> int | None:
    if os.name != "nt":
        return None
    command = (
        "$c = Get-NetTCPConnection -LocalPort "
        + str(port)
        + " -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1; "
        + "if ($c) { [Console]::Write($c.OwningProcess) }"
    )
    try:
        completed = subprocess.run(
            ["powershell", "-NoProfile", "-Command", command],
            capture_output=True,
            text=True,
            timeout=5,
            creationflags=WINDOWS_NO_WINDOW,
        )
        output = (completed.stdout or "").strip()
        return int(output) if output.isdigit() else None
    except Exception as exc:
        log(f"[PORT] Could not inspect port {port}: {exc}")
        return None


def release_existing_server_port(port: int) -> None:
    if not RESTART_EXISTING_SERVER_ON_PORT:
        # A second PyCharm/PowerShell/background launch must never kill the
        # server Unity is already using. Uvicorn will report a normal bind
        # error if this instance cannot own the port.
        return

    owner_pid = windows_port_owner_pid(port)
    if not owner_pid or owner_pid == os.getpid():
        return

    message = f"[PORT] Port {port} is already used by PID {owner_pid}."
    log(message + " Stopping old VRME server before starting this one.")
    try:
        subprocess.run(
            ["powershell", "-NoProfile", "-Command", f"Stop-Process -Id {owner_pid} -Force"],
            capture_output=True,
            text=True,
            timeout=5,
            check=False,
            creationflags=WINDOWS_NO_WINDOW,
        )
    except Exception as exc:
        raise RuntimeError(f"Could not stop PID {owner_pid} on port {port}: {exc}") from exc

    deadline = time.time() + 6
    while time.time() < deadline:
        remaining_owner = windows_port_owner_pid(port)
        if not remaining_owner or remaining_owner == os.getpid():
            log(f"[PORT] Port {port} is free.")
            return
        time.sleep(0.25)

    raise RuntimeError(f"Port {port} is still occupied after stopping PID {owner_pid}.")


def load_analysis_text() -> str:
    try:
        return ANALYSIS_TXT_PATH.read_text(encoding="utf-8").strip()
    except Exception:
        return ""


def extract_questions_from_analysis(text: str) -> list[str]:
    questions = []
    for raw_line in text.splitlines():
        line = raw_line.strip()
        if "Question:" not in line:
            continue
        question = line.split("Question:", 1)[1].strip()
        question = question.strip("'\" ")
        if question:
            questions.append(question)
    return questions


def append_jsonl(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as f:
        f.write(json.dumps(payload, ensure_ascii=False) + "\n")


def safe_filename_part(value: object, fallback: str = "unknown") -> str:
    text = str(value or fallback).strip()
    cleaned = "".join(character if character.isalnum() or character in ("-", "_") else "_" for character in text)
    return cleaned[:80] or fallback


def save_voice_recording(audio_bytes: bytes, metadata: dict) -> Path:
    VOICE_RECORDINGS_DIR.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S_%f")
    participant = safe_filename_part(metadata.get("participantId"))
    scene = safe_filename_part(metadata.get("sceneName"))
    session = safe_filename_part(metadata.get("sessionId"))[:16]
    file_name = f"{timestamp}_{participant}_{scene}_{session}_{uuid.uuid4().hex[:8]}.wav"
    file_path = VOICE_RECORDINGS_DIR / file_name
    file_path.write_bytes(audio_bytes)
    return file_path


def append_transcription_log(transcript: str, recording_path: Path, metadata: dict, stt_seconds: float) -> None:
    TRANSCRIPTION_LOG_PATH.parent.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    entry = (
        f"[{timestamp}] "
        f"[participant={metadata.get('participantId') or 'unknown'}] "
        f"[scene={metadata.get('sceneName') or 'unknown'}] "
        f"[session={metadata.get('sessionId') or 'unknown'}]\n"
        f"WAV: {recording_path}\n"
        f"Transcript: {transcript}\n"
        f"STT seconds: {stt_seconds:.3f}\n"
        f"{'-' * 40}\n"
    )
    with TRANSCRIPTION_LOG_PATH.open("a", encoding="utf-8") as file:
        file.write(entry)
    append_jsonl(CONVERSATION_EVENTS_JSONL_PATH, {
        "type": "voice_transcription",
        "timestampLocal": timestamp,
        "timestampUtcUnixMs": int(time.time() * 1000),
        "serverRunId": SERVER_RUN_ID,
        "participantId": metadata.get("participantId") or "unknown",
        "loginId": metadata.get("loginId") or "",
        "sessionId": metadata.get("sessionId") or "unknown",
        "avatarCondition": metadata.get("avatarCondition") or "unknown",
        "sceneName": metadata.get("sceneName") or "unknown",
        "sceneIndex": metadata.get("sceneIndex", -1),
        "recordingPath": str(recording_path),
        "transcript": transcript,
        "sttSeconds": stt_seconds,
    })


def append_conversation_log(
    user_text: str,
    ai_text: str,
    mode: str,
    metadata: dict,
    scene_context: str = "",
) -> None:
    CONVERSATION_LOG_PATH.parent.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    active_tone = backend_selected_tone(metadata.get("avatarCondition"))
    condition = display_tone_name(active_tone["name"])
    scene_name = metadata.get("sceneName") or "unknown"
    session_id = metadata.get("sessionId") or "unknown"
    participant_id = metadata.get("participantId") or "unknown"
    entry = (
        f"[{timestamp}] [{mode.upper()}] "
        f"[condition={condition}] [scene={scene_name}] "
        f"[participant={participant_id}] [session={session_id}]\n"
        f"User: {user_text}\n"
        f"AI:   {ai_text}\n"
        f"{'-' * 40}\n"
    )
    with CONVERSATION_LOG_PATH.open("a", encoding="utf-8") as f:
        f.write(entry)
    append_jsonl(CONVERSATION_EVENTS_JSONL_PATH, {
        "type": "conversation_turn",
        "timestampLocal": timestamp,
        "timestampUtcUnixMs": int(time.time() * 1000),
        "serverRunId": SERVER_RUN_ID,
        "mode": mode,
        "tonePreset": active_tone["name"],
        "ttsProvider": TTS_PROVIDER,
        "ttsVoiceId": ELEVENLABS_VOICE_ID,
        "ttsModel": ELEVENLABS_MODEL_ID,
        "participantId": participant_id,
        "loginId": metadata.get("loginId") or "",
        "sessionId": session_id,
        "avatarCondition": condition,
        "sceneName": scene_name,
        "sceneIndex": metadata.get("sceneIndex", -1),
        "sceneContextChars": metadata.get("sceneContextChars", 0),
        "sceneContext": scene_context,
        "userText": user_text,
        "replyText": ai_text,
    })


def append_latency_event(metadata: dict, payload: dict) -> None:
    active_tone = backend_selected_tone(metadata.get("avatarCondition"))
    event = {
        "type": "latency",
        "timestampUtcUnixMs": int(time.time() * 1000),
        "serverRunId": SERVER_RUN_ID,
        "mode": CURRENT_MODE,
        "tonePreset": active_tone["name"],
        "ttsProvider": payload.get("tts_provider", TTS_PROVIDER),
        "participantId": metadata.get("participantId") or "unknown",
        "loginId": metadata.get("loginId") or "",
        "sessionId": metadata.get("sessionId") or "unknown",
        "avatarCondition": display_tone_name(active_tone["name"]),
        "sceneName": metadata.get("sceneName") or "unknown",
        "sceneIndex": metadata.get("sceneIndex", -1),
        "sceneContextChars": metadata.get("sceneContextChars", 0),
    }
    event.update(payload)
    append_jsonl(LATENCY_EVENTS_JSONL_PATH, event)


def conversation_key(metadata: dict) -> str:
    participant_id = metadata.get("participantId") or "unknown"
    session_id = metadata.get("sessionId") or "unknown"
    scene_name = metadata.get("sceneName") or "unknown"
    tone_name = display_tone_name(backend_selected_tone(metadata.get("avatarCondition"))["name"])
    return f"{participant_id}|{session_id}|{scene_name}|{tone_name}"


def remember_conversation_turn(metadata: dict, user_text: str, ai_text: str) -> None:
    if CONVERSATION_MEMORY_TURNS <= 0:
        return
    key = conversation_key(metadata)
    history = conversation_memory.setdefault(key, [])
    history.append({"role": "user", "content": user_text})
    history.append({"role": "assistant", "content": ai_text})
    max_messages = max(0, CONVERSATION_MEMORY_TURNS) * 2
    if max_messages and len(history) > max_messages:
        del history[:-max_messages]


def _build_elevenlabs_stt_request(audio_bytes: bytes) -> urllib.request.Request:
    boundary = uuid.uuid4().hex
    body = io.BytesIO()

    def write_text_field(name: str, value: str) -> None:
        body.write(f"--{boundary}\r\n".encode("utf-8"))
        body.write(f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode("utf-8"))
        body.write(value.encode("utf-8"))
        body.write(b"\r\n")

    write_text_field("model_id", ELEVENLABS_STT_MODEL)
    write_text_field("language_code", "en")
    body.write(f"--{boundary}\r\n".encode("utf-8"))
    body.write(b'Content-Disposition: form-data; name="file"; filename="audio.wav"\r\n')
    body.write(b"Content-Type: audio/wav\r\n\r\n")
    body.write(audio_bytes)
    body.write(b"\r\n")
    body.write(f"--{boundary}--\r\n".encode("utf-8"))

    return urllib.request.Request(
        "https://api.elevenlabs.io/v1/speech-to-text",
        data=body.getvalue(),
        headers={
            "xi-api-key": ELEVENLABS_API_KEY,
            "Content-Type": f"multipart/form-data; boundary={boundary}",
        },
        method="POST",
    )


def _transcribe_audio_elevenlabs_sync(audio_bytes: bytes) -> str:
    request = _build_elevenlabs_stt_request(audio_bytes)
    with urllib.request.urlopen(request, timeout=ELEVENLABS_TIMEOUT_SECONDS) as response:
        payload = json.loads(response.read().decode("utf-8"))
    return str(payload.get("text", "")).strip()


async def transcribe_audio(groq_client: AsyncGroq, audio_bytes: bytes) -> str:
    if STT_PROVIDER == "elevenlabs":
        try:
            return await asyncio.get_event_loop().run_in_executor(
                None, _transcribe_audio_elevenlabs_sync, audio_bytes
            )
        except Exception as exc:
            log(f"[STT] ElevenLabs transcription failed; falling back to Groq. {type(exc).__name__}: {exc}")

    audio_file = io.BytesIO(audio_bytes)
    audio_file.name = "input.wav"
    transcription = await groq_client.audio.transcriptions.create(
        file=(audio_file.name, audio_file.read()),
        model=GROQ_STT_MODEL,
        response_format="text",
        language="en",
    )
    return str(transcription).strip()


async def generate_reply(
    groq_client: AsyncGroq,
    openai_client: AsyncOpenAI | None,
    user_text: str,
    scene_prompt: str = "",
    scene_context: str = "",
    avatar_condition: str | None = None,
    conversation_history: list[dict[str, str]] | None = None,
) -> str:
    global script_index

    if "[SYSTEM_ATTENTION_REMINDER]" in user_text:
        log("[ATTENTION_REMINDER] Returning deterministic reminder without an LLM call.")
        if "[TUTORIAL_MOVEMENT_INVITATION]" in user_text:
            return "I'm over here. Please move around and come a little closer to me. If you don't know how to move, held the A button on your right controller and talk to me."
        return "Hi, I'm here."

    auto_briefing = build_exact_auto_task_briefing(user_text, scene_context, avatar_condition)
    if auto_briefing:
        log(f"[AUTO_BRIEFING] Exact task intro from scene context. context_chars={len(scene_context or '')}, reply={auto_briefing}")
        return auto_briefing

    tutorial_stage_reply = build_exact_tutorial_stage_reply(user_text)
    if tutorial_stage_reply:
        log(f"[TUTORIAL_STAGE] Exact tutorial stage line. reply={tutorial_stage_reply}")
        return tutorial_stage_reply

    stage_progress_reply = build_exact_stage_progress_reply(user_text, avatar_condition)
    if stage_progress_reply:
        log(f"[STAGE_PROGRESS] Exact stage-progress line. reply={stage_progress_reply}")
        return stage_progress_reply

    stage_complete_reply = build_exact_stage_complete_reply(user_text, avatar_condition)
    if stage_complete_reply:
        log(f"[STAGE_COMPLETE] Exact stage-complete line. reply={stage_complete_reply}")
        return stage_complete_reply

    if CURRENT_MODE == "scripted":
        analysis_text = load_analysis_text()
        questions = extract_questions_from_analysis(analysis_text)
        script = questions or FALLBACK_SCRIPT
        reply = script[script_index % len(script)]
        script_index += 1
        return reply

    messages = []
    scene_prompt = scene_prompt.strip()
    scene_context = scene_context.strip()
    active_tone = backend_selected_tone(avatar_condition)
    tone_name = active_tone["name"]
    system_parts = [
        "Always respond in natural spoken English, regardless of the user's input language.",
        (
            "GROUNDING & EVIDENCE: Trust only transcribed words, InteractionTracker states/events, and measured gaze "
            "from this turn; never invent an object's color, location, identity, movement, outcome, or availability, and "
            "never continue an earlier claim unless current evidence reconfirms it. CURRENT_HELD_OBJECTS/currentHeld is "
            "the sole authority for current holding — gaze only proves looking, never holding or using, and "
            "everControllerGrabbed=true with currentHeld=false or absent means no longer held; if UNITY_CONTEXT_SUMMARY "
            "says none held, ignore older history saying otherwise. Say grabbed/held/used/released/moved/completed only "
            "with explicit evidence — never claim reading/annotating/opening/inspecting/using, a puppy catching a ball, "
            "or holding a flashlight without it, and remember the avatar itself cannot move, fetch, throw, or manipulate "
            "objects, so never narrate or promise that. If evidence is missing, say you can't verify it and ask them to "
            "describe or try something; scene descriptions are background only, never proof of current state."
        ),
        (
            "INTERACTION-GUIDANCE GOAL: Help the participant discover and try the interactions intentionally designed "
            "for this scene so the scene can produce its intended emotional experience. Use the static scene background "
            "to understand the designer's interaction possibilities, but use NEARBY_INTERACTABLE_OBJECTS and INTERACTABLE_OBJECT_STATES to decide what "
            "can be suggested and INTERACTION_EVENTS to determine what has already happened. Treat object states such "
            "as everUsed or everControllerGrabbed as historical flags, not proof that the object is currently held. "
            "Prefer a relevant unused tracked interaction over repeating a completed one. Suggest only one concrete interaction at a time. "
            "Nothing in this scene is ever visibly marked, outlined, or highlighted for the participant — the GUIDED_TASK_STATE object/target "
            "names are quiet background knowledge for you only. Never use the word 'highlighted' or imply anything is visually singled out; "
            "refer to objects and targets by their plain name instead (for example: grab, carry, move, throw, or place an "
            "object near a target). Do not tell the participant to open, read, activate, switch on, unlock, "
            "transform, or use a special object function unless explicit scene context or interaction evidence says that exact affordance exists. "
            "Phrase it as an invitation or instruction according to the selected avatar condition. Never claim that the "
            "participant performed the suggested action, never promise its outcome, and never tell the participant which "
            "emotion the scene is intended to induce. For warm, cold, and context-aware-guide conditions, the avatar is "
            "a situated guide: use context to select a useful response, not to narrate observations."
        ),
        (
            "RESPONSE PRIORITY: Do not merely describe yourself or the avatar condition. Never reveal, name, or discuss "
            "the hidden labels warm, cold, personality condition, or experimental condition. "
            "Warm and cold are both context-aware task guides: both must use current Unity state when available to help with the task. "
            "The difference is only interpersonal warmth. Warm adds brief affiliative support; cold removes affiliative support but still gives useful task guidance. "
            "When the user's utterance is short, ambiguous, or social (for example 'you' or 'okay') and is NOT one of the "
            "explicit help-seeking phrases below, just briefly acknowledge it or make simple conversation — do not "
            "volunteer a suggested action or point at an object just because they said something vague or are holding or "
            "looking at something. Exploring in silence, with no specific next step offered, is a completely normal and "
            "acceptable outcome of a turn; do not treat every ambiguous utterance as a cue to hint. Only when the "
            "participant asks something that is clearly and specifically about what to do — 'What should I do?', 'What "
            "do I do?', 'What now?', 'I don't know what to do', or an equally direct equivalent — do both warm and cold "
            "conditions give one grounded next action; warm uses supportive cooperative wording, cold gives the same "
            "useful direction without affiliation. Outside of that explicit request, neither condition should proactively "
            "hint; the task is meant to be found, not handed over."
        ),
        (
            "QUESTION-ANSWERING SCOPE: Sort every user utterance into exactly one of three types before answering — this "
            "applies to statements, remarks, and compliments just as much as literal questions. "
            "(1) Current live state, such as what the participant is looking at, holding, or where a tracked object is: "
            "answer only from CURRENT_HELD_OBJECTS, LIVE_USER_OBSERVATIONS, GUIDED_TASK_STATE, or NEARBY_INTERACTABLE_OBJECTS, "
            "following the grounding and confidence rules above; always answer this category when the evidence is available. "
            "(2) Scene-authored background facts, such as what this place is or why something is here: answer only if "
            "STATIC_SCENE_BACKGROUND or the scene context actually states it; if it is not covered there, say plainly that "
            "you don't know rather than inventing an answer. "
            "(3) Everything else, never guessed or fabricated, including: anything unrelated to the participant's current "
            "state or this scene; anything you don't know, including category-2 facts not covered in STATIC_SCENE_BACKGROUND; "
            "personal remarks, compliments, or comments directed at the avatar itself (for example telling the avatar it looks "
            "nice), which are not questions but still belong here; and any false premise that contradicts the actual scene "
            "(for example describing outdoor scenery while indoors, or mentioning an object that was never present) — never "
            "go along with a false premise, gently correct it instead. A category-3 reply is always two parts in the same "
            "turn: first the brief decline or correction itself ('I don't know', 'there's no X here', or an equivalent), "
            "then one grounded redirect back to the scene in the same breath — inviting continued exploration, pointing at "
            "something nearby by plain name, or asking what they're currently doing. Never end the turn on the decline alone. "
            "A category-3 reply is still a full conversational turn and must carry the selected avatar condition's tone, "
            "not an identical flat response across conditions."
        ),
        (
            "CONTEXT SEMANTICS: LIVE_USER_OBSERVATIONS covers this voice-trigger window only. currentAttention=Avatar/"
            "Social Agent counts as valid social attention (refer to yourself as 'me', never 'the avatar'), but a held "
            "object outranks it — respond about that, not the avatar-gaze. Repeated controller contact plus measurable "
            "position change this turn is direct handling evidence, stronger than possible gaze and mentioned first, but "
            "not proof it's still held at voice release; describe this to the participant as using their hand, never say "
            "'controller'. With no attention and nothing held, controller events support only a cautious suggestion, "
            "never a held-claim. voiceWindowAttention is the highest-priority current gaze target; don't substitute older "
            "or secondary targets. Use attentionConfidence exactly: possible means 'may be looking', likely means "
            "'looking', never upgrade; this confidence applies only to gaze, not to holding. "
            "NEARBY_INTERACTABLE_OBJECTS is a fresh availability list; help find the guided task only if genuinely "
            "unsure, and never add affordances beyond the stated objective. dogCaughtBall, dogCurrentlyCarryingBall, "
            "dogReturnedBallToPlayer, elephantCurrentlyEating, elephantReceivedBanana, gunmanInFinalPosition, and exitDoorOpen "
            "are authoritative scene-script evidence for those outcomes. In the Attic scene specifically, gunmanInFinalPosition=false "
            "means the intruder has not finished walking in yet — do not claim he has arrived, is visible, or is aiming at the "
            "participant; gunmanInFinalPosition=true means he has reached his fixed spot and stays there facing the participant "
            "for the rest of the scene. If asked where he is while gunmanInFinalPosition=true, that flag IS the location evidence — "
            "answer that he is standing at the spot he arrived at, facing the participant; do not decline this as unverifiable just "
            "because no exact coordinates were given, and never say you cannot verify his position when this flag is true. "
            "exitDoorOpen=false means the exit door is still closed — do not claim it is open or that the participant can leave "
            "through it; exitDoorOpen=true means it has been opened."
        ),
        (
            "ANTI-OBSERVER RULE: Do not start routine replies with 'I see', 'I notice', 'I observe', 'It looks like', "
            "or a plain report of the user's gaze. If live context is useful, convert it into one situated conversational "
            "move: answer the user's question, point them toward the object/target by plain name, suggest one available "
            "interaction, or ask a scene-grounded follow-up."
        ),
        (
            "NO META-LANGUAGE: Never say internal/system terms out loud to the participant — for example 'intended "
            "interaction', 'guided task', 'background knowledge', 'context', 'system', 'condition', or similar words "
            "describing how you were set up. These are notes for you only. Understand what they mean, then express it "
            "in plain, natural spoken language about the scene and objects instead."
        ),
        (
            "RESPONSE STRUCTURE: Build every reply from up to three slots, in order: (1) a brief social/affiliative "
            "opener, only when it fits the selected avatar condition and the moment (skip it if the last turn already "
            "had one); (2) a response slot that reacts to whatever is actually relevant right now — the participant's "
            "words, what they are currently looking at or holding, or a nearby object worth mentioning — omit this slot "
            "if there is nothing yet to react to; (3) end with one short open-ended question inviting the participant "
            "to keep going (for example asking what they notice, what they think, or what they'd like to try). The very "
            "first opening turn of a scene is the only exception: it is social opener plus the open question, with no "
            "response slot, since nothing has happened yet to react to. Vary the concrete wording and the specific "
            "question each time so consecutive replies do not sound like a repeated script."
        ),
        (
            "VARIETY SOURCE: NEARBY_STATIC_SCENERY, by plain cleaned-up name, is optional material for when a reply would "
            "otherwise repeat itself or when the participant asks what's around — it is not something to push into every "
            "turn. If you do bring one up, movement/looking suggestions are fine (for example 'you could wander over "
            "toward the table'), but never suggest grabbing, using, or otherwise mechanically interacting with these "
            "props, and never invent a function or backstory for them beyond their name. Most turns need no scenery "
            "mention at all — silence on this is the default, not a gap to fill."
        ),
        (
            "STYLE: Reply in one or two short spoken sentences for simple turns; for complex questions, answer fully "
            "enough to be useful but stay conversational, not a lecture or list. Don't omit needed details or stop "
            "abruptly, and always finish the last sentence cleanly with punctuation. Make the selected support style "
            "clearly distinguishable from the other styles."
        ),
        "Treat each voice trigger's Unity state as fresh; never use prior turns as evidence for what the participant is currently seeing or holding.",
        active_tone["prompt"],
    ]
    if scene_context:
        system_parts.append(
            "[CURRENT_TURN_OBSERVATIONS]\n"
            "Use only this current voice-trigger context for context-aware help: UNITY_CONTEXT_SUMMARY, "
            "LIVE_USER_OBSERVATIONS, GUIDED_TASK_STATE, NEARBY_INTERACTABLE_OBJECTS, NEARBY_STATIC_SCENERY, and CURRENT_HELD_OBJECTS. "
            "If the participant asks what to do, is unsure, or gives a short utterance, use the GUIDED_TASK_STATE object/target names "
            "(by plain name, never as 'highlighted' or visibly marked) and the currently attended or held object to suggest one grounded next action. "
            "If the participant asks what objects are available or interactable, answer only from NEARBY_INTERACTABLE_OBJECTS. "
            "NEARBY_STATIC_SCENERY is a separate, non-interactive list for conversational variety (see VARIETY SOURCE above); never treat "
            "its entries as something the participant can pick up or use. "
            "After GUIDED_TASK_STATE is completed, retain all fresh context awareness but present interaction as optional exploration or conversation, not another required task. "
            "Head/gaze is attention evidence only, not proof of holding or using. currentHeld=true is the authority for holding. "
            "Do not recite raw coordinates or long object lists.\n"
            f"{scene_context}\n"
            "[/CURRENT_TURN_OBSERVATIONS]"
        )
        log(f"[CONTEXT] Included live scene context for tone={tone_name}. chars={len(scene_context)}")
    elif not scene_context:
        log(f"[CONTEXT] No live scene context available for tone={tone_name}.")
    if scene_prompt:
        system_parts.append(
            "[STATIC_SCENE_BACKGROUND]\n"
            "This is designer-authored background for the current scene. Use it to understand the intended setting and "
            "interaction, but never treat it as evidence of the participant's current gaze, held object, or completed action.\n"
            f"{scene_prompt}\n"
            "[/STATIC_SCENE_BACKGROUND]"
        )
        log(f"[SCENE_PROMPT] Included static scene background. chars={len(scene_prompt)}")
    if system_parts:
        messages.append({"role": "system", "content": "\n\n".join(system_parts)})
    messages.append({"role": "user", "content": user_text})

    max_tokens = int(os.environ.get("LLM_REPLY_MAX_TOKENS", os.environ.get("GROQ_REPLY_MAX_TOKENS", "300")))
    if LLM_PROVIDER == "openai":
        if openai_client is None:
            raise RuntimeError("Missing OPENAI_API_KEY for LLM_PROVIDER=openai.")
        try:
            try:
                completion = await openai_client.chat.completions.create(
                    messages=messages,
                    model=OPENAI_MODEL,
                    max_completion_tokens=max_tokens,
                )
            except Exception as exc:
                log(f"[LLM] OpenAI chat retry with legacy max_tokens after error: {exc}")
                completion = await openai_client.chat.completions.create(
                    messages=messages,
                    model=OPENAI_MODEL,
                    max_tokens=max_tokens,
                )
            log(f"[LLM] provider=openai, model={OPENAI_MODEL}")
        except Exception as exc:
            log(f"[LLM] OpenAI failed; falling back to Groq. model={OPENAI_MODEL}, error={type(exc).__name__}: {exc}")
            completion = await groq_client.chat.completions.create(
                messages=messages,
                model=GROQ_CHAT_MODEL,
                temperature=0.45,
                max_tokens=max_tokens,
            )
            log(f"[LLM] provider=groq_fallback, model={GROQ_CHAT_MODEL}")
    else:
        completion = await groq_client.chat.completions.create(
            messages=messages,
            model=GROQ_CHAT_MODEL,
            temperature=0.45,
            max_tokens=max_tokens,
        )
        log(f"[LLM] provider=groq, model={GROQ_CHAT_MODEL}")
    reply = completion.choices[0].message.content.strip()
    finish_reason = getattr(completion.choices[0], "finish_reason", None)
    if finish_reason:
        log(f"[LLM] finish_reason={finish_reason}, reply_chars={len(reply)}")
    # RESPONSE STRUCTURE requires every non-opening reply to end in an open
    # question, but that's a prompt instruction the LLM can (and sometimes
    # does, especially for category-3 declines) skip. Enforce it here in code
    # so this path has the same guarantee as the deterministic fast-path
    # replies below.
    return _ensure_open_question_ending(reply, tone_name)


_OPEN_QUESTION_POOL = {
    "warm": [" What do you think?", " What are you noticing?", " Want to tell me more?"],
    "cold": [" What do you notice?", " What is your assessment?", " What do you see?"],
}

_COLD_NEUTRAL_MARKER_POOL = ["Noted.", "Understood.", "Confirmed."]


def _cold_neutral_marker() -> str:
    """Rotates cold's neutral acknowledgement so the deterministic fast-path
    replies don't all start sounding like the same repeated word."""
    index = int(time.time() * 10) % len(_COLD_NEUTRAL_MARKER_POOL)
    return _COLD_NEUTRAL_MARKER_POOL[index]


def _ensure_open_question_ending(reply: str, tone_name: str) -> str:
    """Deterministic (non-LLM) replies bypass the RESPONSE STRUCTURE prompt rule
    entirely, since they never reach the LLM. This appends the same
    open-question ending those replies are supposed to have, so the fast-path
    and LLM-generated replies stay consistent."""
    if not reply or reply.rstrip().endswith(("?", "!?")):
        return reply
    options = _OPEN_QUESTION_POOL.get(tone_name, [" What would you like to do next?"])
    index = int(time.time() * 10) % len(options)
    return reply + options[index]


def build_context_grounded_fallback_reply(
    user_text: str,
    scene_context: str = "",
    scene_name: str = "",
    avatar_condition: str | None = None,
) -> str:
    tone_name = backend_selected_tone(avatar_condition)["name"]
    return _ensure_open_question_ending(
        _build_context_grounded_fallback_reply_impl(user_text, scene_context, scene_name, avatar_condition),
        tone_name,
    )


def build_current_turn_grounded_reply(
    scene_context: str,
    avatar_condition: str | None = None,
    user_text: str = "",
) -> str:
    tone_name = backend_selected_tone(avatar_condition)["name"]
    return _ensure_open_question_ending(
        _build_current_turn_grounded_reply_impl(scene_context, avatar_condition, user_text),
        tone_name,
    )


def build_exploration_guidance_reply(
    scene_context: str,
    avatar_condition: str | None = None,
) -> str:
    tone_name = backend_selected_tone(avatar_condition)["name"]
    return _ensure_open_question_ending(
        _build_exploration_guidance_reply_impl(scene_context, avatar_condition),
        tone_name,
    )


def _build_context_grounded_fallback_reply_impl(
    user_text: str,
    scene_context: str = "",
    scene_name: str = "",
    avatar_condition: str | None = None,
) -> str:
    """Last-resort spoken reply. It must still be scene/task grounded."""
    active_tone = backend_selected_tone(avatar_condition)
    tone_name = active_tone["name"]
    context = scene_context or ""
    scene = (scene_name or "").strip() or "this scene"
    status_match = re.search(r"^status\s*=\s*([^\n]+)", context, re.MULTILINE | re.IGNORECASE)
    status = status_match.group(1).strip().lower() if status_match else ""
    if status == "completed":
        if tone_name == "warm":
            return "Nice work, the interaction is complete. You can keep exploring, use available objects, or continue talking with me."
        if tone_name == "cold":
            return f"{_cold_neutral_marker()} The interaction is complete. You can continue exploring, use available objects, or speak with me."
        return "The interaction is complete. You can continue exploring the scene."

    objective_match = re.search(r"^objective=(.+)$", context, re.MULTILINE)
    objective = objective_match.group(1).strip() if objective_match else ""
    object_match = re.search(r"^plannedHighlightedObjectHints=(.+)$", context, re.MULTILINE)
    target_match = re.search(r"^plannedHighlightedTargetHints=(.+)$", context, re.MULTILINE)
    if not object_match:
        object_match = re.search(r"^highlightedObjects:\s*\n-\s*([^|]+)", context, re.MULTILINE)
    if not target_match:
        target_match = re.search(r"^highlightedTargets:\s*\n-\s*([^|]+)", context, re.MULTILINE)

    object_hint = object_match.group(1).strip() if object_match else "something nearby"
    target_hint = target_match.group(1).strip() if target_match else "somewhere in the scene"

    if objective:
        core = f"use {object_hint} with {target_hint}"
        if object_match or target_match:
            core = f"{objective}; look for {object_hint} and bring it toward {target_hint}"
        else:
            core = objective
    elif object_match or target_match:
        core = f"look for {object_hint} and bring it toward {target_hint}"
    else:
        core = "look around and see what you can interact with"

    if tone_name == "cold":
        return f"{_cold_neutral_marker()} You can continue exploring in the {scene} scene to {core}."
    if tone_name == "warm":
        return f"Of course. You can keep exploring in the {scene} scene to {core}."
    return f"In the {scene} scene, {core}."


def _build_current_turn_grounded_reply_impl(
    scene_context: str,
    avatar_condition: str | None = None,
    user_text: str = "",
) -> str:
    """Deterministic reply for short/timeout turns; never invents state."""
    context = scene_context or ""
    attention_match = re.search(r"^currentAttention\s*=\s*([^,\n]+)", context, re.MULTILINE | re.IGNORECASE)
    attention = attention_match.group(1).strip() if attention_match else ""
    held_match = re.search(r"^currentHeldObjects\s*=\s*([^\n]+)", context, re.MULTILINE | re.IGNORECASE)
    held_text = held_match.group(1).strip() if held_match else "none"
    held = [] if held_text.lower() in ("", "none", "unknown") else [part.strip() for part in held_text.split(",") if part.strip()]
    status_match = re.search(r"^status\s*=\s*([^\n]+)", context, re.MULTILINE | re.IGNORECASE)
    status = status_match.group(1).strip().lower() if status_match else ""
    tone_name = backend_selected_tone(avatar_condition)["name"]

    # This deterministic path only ever acknowledges ambient state (holding,
    # looking, having moved something); it never names the planned object/target
    # itself, since that reveal is reserved for explicit help-seeking, which
    # routes to build_exploration_guidance_reply below.
    if is_task_guidance_request(user_text):
        return build_exploration_guidance_reply(scene_context, avatar_condition)
    attention_confidence_match = re.search(
        r"^currentAttention\s*=.*?attentionConfidence\s*=\s*(likely|possible)",
        context,
        re.MULTILINE | re.IGNORECASE,
    )
    attention_confidence = attention_confidence_match.group(1).lower() if attention_confidence_match else "likely"

    def controller_manipulated_object() -> str:
        """Return an object visibly moved during repeated controller contact in this voice window."""
        block_match = re.search(
            r"\[RECENT_CONTROLLER_EVENTS\]\s*(.*?)\s*\[/RECENT_CONTROLLER_EVENTS\]",
            context,
            re.DOTALL | re.IGNORECASE,
        )
        if not block_match:
            return ""

        samples: dict[str, list[tuple[float, float, float]]] = {}
        display_names: dict[str, str] = {}
        for line in block_match.group(1).splitlines():
            if "| collision:" not in line.lower() or not re.search(
                r"source=.*(?:controllergrablocation|handanchor|handgrab)",
                line,
                re.IGNORECASE,
            ):
                continue
            parts = [part.strip() for part in line.split("|")]
            if len(parts) < 3:
                continue
            object_name = parts[2]
            position_match = re.search(
                r"objectPos=\((-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?),(-?\d+(?:\.\d+)?)\)",
                line,
                re.IGNORECASE,
            )
            if not object_name or not position_match:
                continue
            key = object_name.lower()
            display_names[key] = object_name
            samples.setdefault(key, []).append(tuple(float(value) for value in position_match.groups()))

        best_name = ""
        best_movement = 0.0
        for key, positions in samples.items():
            if len(positions) < 3:
                continue
            movement = max(
                sum((a - b) ** 2 for a, b in zip(first, second)) ** 0.5
                for first in positions
                for second in positions
            )
            if movement >= 0.03 and movement > best_movement:
                best_name = display_names[key]
                best_movement = movement
        return best_name

    manipulated_object = controller_manipulated_object()

    def context_bool(field_name: str) -> bool:
        match = re.search(rf"^{re.escape(field_name)}\s*=\s*(true|false)", context, re.MULTILINE | re.IGNORECASE)
        return bool(match and match.group(1).lower() == "true")

    def spoken_object_name(name: str) -> str:
        leaf_name = re.split(r"[/\\]", name.strip())[-1]
        cleaned = re.sub(r"[_\-.]?\d+[a-z]*$", "", leaf_name, flags=re.IGNORECASE)
        if cleaned.lower() == "floorfloor":
            cleaned = "floor"
        return cleaned.replace("_", " ").strip() or name.strip()

    if held:
        held_name = spoken_object_name(held[0])
        attention_lower = attention.lower()
        same_attention = attention and held_name.lower() in spoken_object_name(attention).lower()
        looking_at_avatar = "avatar" in attention_lower or "social agent" in attention_lower
        other_attention = attention and attention_lower not in ("none", "unknown") and not same_attention and not looking_at_avatar
        if tone_name == "warm":
            if same_attention:
                state = f"You're holding the {held_name} and may be looking at it." if attention_confidence == "possible" else f"You're holding the {held_name} and looking at it."
            elif looking_at_avatar:
                state = f"You're holding the {held_name} and may be looking at me." if attention_confidence == "possible" else f"You're holding the {held_name} and looking at me."
            elif other_attention:
                state = f"You're holding the {held_name} and may be looking at the {spoken_object_name(attention)}." if attention_confidence == "possible" else f"You're holding the {held_name} and looking at the {spoken_object_name(attention)}."
            else:
                state = f"You're holding the {held_name}."
            if status == "completed":
                return f"{state} Nice work, the task is complete; you can keep exploring or continue talking with me."
            return f"{state} Take your time exploring with it."
        if tone_name == "cold":
            if same_attention:
                state = f"You are holding the {held_name} and may be looking at it." if attention_confidence == "possible" else f"You are holding the {held_name} and looking at it."
            elif looking_at_avatar:
                state = f"You are holding the {held_name} and may be looking at me." if attention_confidence == "possible" else f"You are holding the {held_name} and looking at me."
            elif other_attention:
                state = f"You are holding the {held_name} and may be looking at the {spoken_object_name(attention)}." if attention_confidence == "possible" else f"You are holding the {held_name} and looking at the {spoken_object_name(attention)}."
            else:
                state = f"You are holding the {held_name}."
            if status == "completed":
                return f"{state} {_cold_neutral_marker()} The task is complete. Further exploration and conversation remain available."
            return f"{state} {_cold_neutral_marker()}"
        if same_attention:
            state = f"You're holding the {held_name} and may be looking at it." if attention_confidence == "possible" else f"You're holding the {held_name} and looking at it."
        elif looking_at_avatar:
            state = f"You're holding the {held_name} and may be looking at me." if attention_confidence == "possible" else f"You're holding the {held_name} and looking at me."
        elif other_attention:
            state = f"You're holding the {held_name} and may be looking at the {spoken_object_name(attention)}." if attention_confidence == "possible" else f"You're holding the {held_name} and looking at the {spoken_object_name(attention)}."
        else:
            state = f"You're holding the {held_name}."
        if status == "completed":
            return f"{state} The current task is marked complete."
        return state

    # A moved object under repeated controller contact is stronger action
    # evidence than a marginal gaze hit. Keep past-tense wording because Unity
    # did not confirm that it remains selected at voice release.
    if manipulated_object:
        manipulated_name = spoken_object_name(manipulated_object)
        if tone_name == "warm":
            if status == "completed":
                return f"Nice, you moved the {manipulated_name}. The task is complete, and you can keep exploring or talking with me."
            return f"Nice, you moved the {manipulated_name}."
        if tone_name == "cold":
            if status == "completed":
                return f"{_cold_neutral_marker()} You moved the {manipulated_name}. The task is complete. Further exploration and conversation remain available."
            return f"{_cold_neutral_marker()} You moved the {manipulated_name}."
        state = f"You just moved the {manipulated_name} with your hand."
        if status == "completed":
            return f"{state} The current task is marked complete."
        return state

    attention_lower = attention.lower()
    if attention and attention_lower not in ("none", "unknown"):
        if "avatar" in attention_lower or "social agent" in attention_lower:
            if tone_name == "warm":
                state = "You may be looking at me. I'm here with you." if attention_confidence == "possible" else "You're looking at me. I'm here with you."
            elif tone_name == "cold":
                state = "You may be looking at me." if attention_confidence == "possible" else "You are looking at me."
            else:
                state = "You may be looking at me." if attention_confidence == "possible" else "You're looking at me."
            if status == "completed":
                if tone_name == "warm":
                    return f"{state} Nice work, the task is complete; you can keep exploring or continue talking with me."
                if tone_name == "cold":
                    return f"{state} {_cold_neutral_marker()} The task is complete. Further exploration and conversation remain available."
                return f"{state} The task is complete; you can continue exploring or talking with me."
            if tone_name == "cold":
                return f"{state} {_cold_neutral_marker()}"
            return state
        attention_name = spoken_object_name(attention)
        if tone_name == "warm":
            state = f"You may be looking at the {attention_name}." if attention_confidence == "possible" else f"You're looking at the {attention_name}."
            if status == "completed":
                return f"{state} Nice work, the task is complete; you can keep exploring it or continue talking with me."
            return f"{state} Take your time."
        if tone_name == "cold":
            state = f"Your attention may be on the {attention_name}." if attention_confidence == "possible" else f"Your attention is on the {attention_name}."
            if status == "completed":
                return f"{state} {_cold_neutral_marker()} The task is complete. Further exploration and conversation remain available."
            return f"{state} {_cold_neutral_marker()}"
        state = f"Your current attention may be on the {attention_name}." if attention_confidence == "possible" else f"Your current attention is on the {attention_name}."
        if status == "completed":
            return f"{state} The current task is marked complete."
        return state

    # Animal task state is useful, but it must not mask fresher evidence about
    # what the participant is looking at or holding during this voice turn.
    if context_bool("dogCurrentlyCarryingBall"):
        if tone_name == "warm":
            return "The dog has the tennis ball and is bringing it back to you."
        if tone_name == "cold":
            return "The dog is carrying the tennis ball toward you."
        return "The dog is carrying the tennis ball back to you."
    if context_bool("dogReturnedBallToPlayer"):
        if tone_name == "warm":
            return "The dog brought the tennis ball back to you. Nice work; you can keep exploring or continue talking with me."
        if tone_name == "cold":
            return f"{_cold_neutral_marker()} The dog returned the tennis ball. The task is complete. Further exploration and conversation remain available."
        return "The dog returned the tennis ball to you. The task is complete; you can continue exploring or talking with me."
    if context_bool("elephantCurrentlyEating"):
        if tone_name == "warm":
            return "The elephant has the banana and is eating it now. Nice work; you can keep exploring or continue talking with me."
        if tone_name == "cold":
            return f"{_cold_neutral_marker()} The elephant received the banana and is eating it. Further exploration and conversation remain available."
        return "The elephant is eating the banana. You can continue exploring or talking with me."
    if context_bool("elephantReceivedBanana"):
        if tone_name == "warm":
            return "The elephant received the banana. Nice work; you can keep exploring or continue talking with me."
        if tone_name == "cold":
            return f"{_cold_neutral_marker()} The elephant received the banana. The task is complete. Further exploration and conversation remain available."
        return "The elephant received the banana. The task is complete; you can continue exploring or talking with me."
    if context_bool("gunmanInFinalPosition"):
        if tone_name == "warm":
            return "He's here now, standing across the room and facing you."
        if tone_name == "cold":
            return f"{_cold_neutral_marker()} He has entered and is standing there, facing you."
        return "He has entered and is standing there, facing you."
    if context_bool("exitDoorOpen"):
        if tone_name == "warm":
            return "The exit door is open now."
        if tone_name == "cold":
            return f"{_cold_neutral_marker()} The exit door is open."
        return "The exit door is open."

    if status == "completed":
        return build_exploration_guidance_reply(scene_context, avatar_condition)

    # No clear signal for this turn (ambiguous utterance, or this path was
    # reached via a technical fallback such as an LLM timeout with no real
    # user_text) — this is NOT the same as the participant explicitly asking
    # for help (that goes through build_exploration_guidance_reply above,
    # which is allowed to name object_phrase/target_phrase). Stay vague here
    # so a timeout or a mumbled utterance never accidentally spoils the task.
    if tone_name == "warm":
        return "Of course. Feel free to keep looking around."
    if tone_name == "cold":
        return f"{_cold_neutral_marker()} You can continue exploring."
    return "I don't have reliable gaze or held-object evidence for this turn. Feel free to keep looking around."


def is_short_grounding_turn(user_text: str) -> bool:
    normalized = re.sub(r"[^a-z0-9]+", " ", (user_text or "").lower()).strip()
    if is_task_guidance_request(user_text):
        return True
    return normalized in {
        "", "you", "okay", "ok", "yeah", "yes",
        "what am i looking at", "what am i looking at right now",
        "where am i looking", "what do you see me looking at",
        "what am i holding", "what do i have in my hand",
    }


def is_task_guidance_request(user_text: str) -> bool:
    normalized = re.sub(r"[^a-z0-9]+", " ", (user_text or "").lower()).strip()
    exact_requests = {
        "help", "help me", "what now", "what should i do", "what do i do",
        "what am i supposed to do", "where should i go", "what should i try",
        "what do i try", "how do i continue", "how can i continue", "where do i start",
        "where do i go", "what next",
    }
    if normalized in exact_requests:
        return True
    guidance_phrases = (
        "what should i do", "what do i do", "what am i supposed to do",
        "where should i go", "where do i go", "what should i try", "what do i try",
        "how do i continue", "how can i continue", "where do i start", "what next",
    )
    return any(phrase in normalized for phrase in guidance_phrases)


def _build_exploration_guidance_reply_impl(
    scene_context: str,
    avatar_condition: str | None = None,
) -> str:
    """Give both conditions useful exploration guidance without collapsing warmth."""
    context = scene_context or ""
    tone_name = backend_selected_tone(avatar_condition)["name"]
    status_match = re.search(r"^status\s*=\s*([^\n]+)", context, re.MULTILINE | re.IGNORECASE)
    status = status_match.group(1).strip().lower() if status_match else ""
    nearby_match = re.search(
        r"\[NEARBY_INTERACTABLE_OBJECTS\]\s*\n-\s*([^|\n]+)",
        context,
        re.MULTILINE | re.IGNORECASE,
    )
    nearby_name = nearby_match.group(1).strip() if nearby_match else ""
    if status == "completed":
        if tone_name == "warm":
            available = f" The {nearby_name} is nearby if you'd like to keep exploring," if nearby_name else " You can keep exploring,"
            return f"Nice work, the interaction is complete.{available} or continue talking with me."
        if tone_name == "cold":
            available = f" The {nearby_name} is nearby and remains available." if nearby_name else " Further exploration remains available."
            return f"The interaction is complete.{available} Conversation also remains available."
        return "The interaction is complete. You can continue exploring the scene."
    objective_match = re.search(r"^objective=(.+)$", context, re.MULTILINE)
    object_match = re.search(r"^plannedHighlightedObjectHints=(.+)$", context, re.MULTILINE)
    target_match = re.search(r"^plannedHighlightedTargetHints=(.+)$", context, re.MULTILINE)
    if not object_match:
        object_match = re.search(r"^highlightedObjects:\s*\n-\s*([^|]+)", context, re.MULTILINE)
    if not target_match:
        target_match = re.search(r"^highlightedTargets:\s*\n-\s*([^|]+)", context, re.MULTILINE)

    objective = objective_match.group(1).strip() if objective_match else ""
    object_hint = object_match.group(1).strip() if object_match else ""
    target_hint = target_match.group(1).strip() if target_match else ""

    if object_hint or target_hint:
        object_choices = [part.strip() for part in object_hint.split(",") if part.strip()]
        object_phrase = (
            "either " + " or ".join(object_choices)
            if len(object_choices) > 1
            else (object_hint or "something nearby")
        )
        target_phrase = (
            target_hint
            if target_hint.lower().startswith(("the ", "a ", "an "))
            else (f"the {target_hint}" if target_hint else "somewhere in the scene")
        )
        core = f"look for {object_phrase}, then try using it with {target_phrase}"
    elif objective:
        core = objective[0].lower() + objective[1:] if objective else objective
    else:
        core = "look around and see what you can interact with"

    if tone_name == "warm":
        return f"Of course. You can keep exploring to {core}."
    if tone_name == "cold":
        return f"{_cold_neutral_marker()} You can continue exploring to {core}."
    return f"Keep exploring. Next, {core}."


def normalize_avatar_self_reference(reply: str) -> str:
    """Keep the embodied agent's participant-facing self-reference first-person."""
    normalized = reply or ""
    replacements = (
        (r"\blooking at the avatar\b", "looking at me"),
        (r"\blooking toward the avatar\b", "looking toward me"),
        (r"\bfacing the avatar\b", "facing me"),
        (r"\bfocused on the avatar\b", "focused on me"),
        (r"\battention (?:is|may be) on the avatar\b", "attention is on me"),
    )
    for pattern, replacement in replacements:
        normalized = re.sub(pattern, replacement, normalized, flags=re.IGNORECASE)
    return normalized


def sanitize_reply_against_current_held(
    reply: str,
    scene_context: str = "",
    avatar_condition: str | None = None,
) -> str:
    summary_match = re.search(
        r"\[UNITY_CONTEXT_SUMMARY\]\s*(.*?)\s*\[/UNITY_CONTEXT_SUMMARY\]",
        scene_context or "",
        re.DOTALL | re.IGNORECASE,
    )
    summary_held_none = False
    current_attention = ""
    if summary_match:
        summary_block = summary_match.group(1)
        summary_held_none = re.search(r"^currentHeldObjects\s*=\s*none\s*$", summary_block, re.MULTILINE | re.IGNORECASE) is not None
        attention_match = re.search(r"^currentAttention\s*=\s*([^,\n]+)", summary_block, re.MULTILINE | re.IGNORECASE)
        if attention_match:
            current_attention = attention_match.group(1).strip()

    if not current_attention:
        attention_match = re.search(r"voiceWindowAttention[^\n]*hitName=([^,\n]+)", scene_context or "", re.IGNORECASE)
        if attention_match:
            current_attention = attention_match.group(1).strip()

    held_block_match = re.search(
        r"\[CURRENT_HELD_OBJECTS\]\s*(.*?)\s*\[/CURRENT_HELD_OBJECTS\]",
        scene_context or "",
        re.DOTALL | re.IGNORECASE,
    )
    if not held_block_match and not summary_held_none:
        return reply

    held_block = held_block_match.group(1).strip() if held_block_match else "none"
    held_lines = [
        line.strip("- ").strip()
        for line in held_block.splitlines()
        if line.strip().startswith("-")
    ]
    held_lines_lower = [line.lower() for line in held_lines]

    lower_reply = reply.lower()
    current_attention_lower = current_attention.lower()
    mentions_book = "book" in lower_reply
    attention_is_book = "book" in current_attention_lower
    attention_is_avatar = "avatar" in current_attention_lower or "social agent" in current_attention_lower
    attention_is_cup = "cup" in current_attention_lower
    claims_avatar_attention = any(
        phrase in lower_reply
        for phrase in (
            "looking at me",
            "looking toward me",
            "looking at the avatar",
            "facing the avatar",
            "focused on me",
        )
    )

    if claims_avatar_attention and current_attention and not attention_is_avatar:
        log(f"[SANITY] Rewrote false avatar-attention claim; currentAttention={current_attention!r}.")
        return build_current_turn_grounded_reply(scene_context, avatar_condition)

    # Controller state is direct interaction evidence and is more useful than
    # social gaze for grounding a task response. This also prevents a marginal
    # avatar ray hit from overriding an object that Unity says is in hand.
    if held_lines and claims_avatar_attention:
        log(f"[SANITY] Rewrote avatar-attention claim because currentHeldObjects={held_lines!r}.")
        return build_current_turn_grounded_reply(scene_context, avatar_condition)

    no_current_book = summary_held_none or not any("book" in line for line in held_lines_lower)
    claims_looking_at_book = mentions_book and any(
        phrase in lower_reply
        for phrase in (
            "looking at the book",
            "looking toward the book",
            "focused on the book",
            "still looking at the book",
            "still focused on the book",
            "reading the book",
            "examining the book",
            "book in front of you",
        )
    )
    claims_current_holding = any(
        phrase in lower_reply
        for phrase in (
            "you are holding",
            "you're holding",
            "you still have",
            "you have the book",
            "book in your hand",
            "in your hand",
            "holding the book",
            "annotating the book",
            "reading the book",
        )
    )
    if claims_looking_at_book and current_attention and not attention_is_book:
        log(f"[SANITY] Rewrote reply that claimed book attention while currentAttention={current_attention!r}.")
        return build_exploration_guidance_reply(scene_context, avatar_condition)

    if no_current_book and mentions_book and claims_current_holding:
        log("[SANITY] Rewrote reply that claimed the participant currently held/used the book without CURRENT_HELD_OBJECTS evidence.")
        return build_exploration_guidance_reply(scene_context, avatar_condition)

    return reply


def build_exact_auto_task_briefing(
    user_text: str,
    scene_context: str = "",
    avatar_condition: str | None = None,
) -> str:
    if "[SYSTEM_AUTO_TASK_BRIEFING]" not in user_text:
        return ""

    scene_match = re.search(r"^Scene:\s*(.+)$", user_text, re.MULTILINE)
    task_match = re.search(r"^Task:\s*(.+)$", user_text, re.MULTILINE)
    scene = scene_match.group(1).strip() if scene_match else "this"
    task = task_match.group(1).strip() if task_match else "use the highlighted object with the highlighted target"
    if normalized_scene_name(scene) == "tutorialinteraction":
        return "Hi, welcome! Take a moment to look at the objects in front of you, and please tell me what is in front of you."
    if task:
        task = task.rstrip(".!? ")
        task = task[0].lower() + task[1:]
    # The Unity Task line already contains the exact object and target. Adding
    # planned/highlighted hints here repeated names (for example
    # "banana ... banana, Banana and Elephant") in the spoken briefing.
    tone_name = backend_selected_tone(avatar_condition)["name"]
    if tone_name == "warm":
        return f"Hi! I'm so glad you're here with me. Whenever you're ready, let's {task} together!"
    if tone_name == "cold":
        task_sentence = task[0].upper() + task[1:] if task else task
        return f"Location: {scene}. {task_sentence}."
    return f"You are in the {scene} scene. Task: {task}"


def build_exact_tutorial_stage_reply(user_text: str) -> str:
    if "[SYSTEM_TUTORIAL_STAGE]" not in user_text:
        return ""
    stage_match = re.search(r"^Stage:\s*(.+)$", user_text, re.MULTILINE)
    stage = stage_match.group(1).strip().lower() if stage_match else ""
    if stage == "choose_second":
        return (
            "Nice—you tried it. When you're ready, choose a different object, keep holding it, and tell me one way it looks or feels "
            "different from the first one."
        )
    return ""


def _tutorial_context_value(scene_context: str, key: str, fallback: str = "") -> str:
    match = re.search(rf"^{re.escape(key)}=(.*)$", scene_context or "", re.MULTILINE)
    return match.group(1).strip() if match else fallback


def _meaningful_tutorial_response(user_text: str) -> bool:
    normalized = re.sub(r"[^a-z0-9\u4e00-\u9fff]+", " ", (user_text or "").lower()).strip()
    return bool(normalized) and normalized not in {"inaudible", "silence", "no speech", "unintelligible"}


def _tutorial_exit_requested(user_text: str) -> bool:
    normalized = re.sub(r"[^a-z0-9\u4e00-\u9fff]+", " ", (user_text or "").lower()).strip()
    return any(phrase in normalized for phrase in (
        "how do i exit", "how can i exit", "how do i leave", "how can i leave", "how do i get out",
        "where is the exit", "where is exit", "can i leave", "finish the tutorial", "end the tutorial",
        "退出", "离开", "出口",
    ))


def _tutorial_movement_help_requested(user_text: str) -> bool:
    normalized = re.sub(r"[^a-z0-9\u4e00-\u9fff]+", " ", (user_text or "").lower()).strip()
    return any(phrase in normalized for phrase in (
        "how do i move", "how can i move", "how do i throw", "how can i throw",
        "how do i let go", "how can i let go", "how do i release", "how can i release",
        "i don t know how", "i do not know how", "not sure how", "can t move", "cannot move",
        "怎么移动", "怎么动", "不知道怎么", "怎么扔", "怎么放开", "怎么松手",
    ))


def _tutorial_locomotion_help_requested(user_text: str) -> bool:
    normalized = re.sub(r"[^a-z0-9\u4e00-\u9fff]+", " ", (user_text or "").lower()).strip()
    if any(phrase in normalized for phrase in (
        "how do i move", "how can i move", "how do i walk", "how can i walk",
        "how do i get there", "how can i get there", "how do i come over", "which joystick",
        "can i move to you", "move to you", "come to you", "get to you",
        "move closer", "come closer", "get closer", "move over there", "come over there",
        "怎么移动", "怎么走", "怎么过去", "哪个摇杆", "如何移动", "如何走",
    )):
        return True
    words = set(normalized.split())
    return bool(words & {"move", "walk", "come", "get"}) and bool(words & {"you", "there", "closer", "over"})


def _tutorial_locomotion_help_reply() -> str:
    return (
        "Push the joystick forward to move. Gently move it left or right to change direction and come closer to me."
    )


def _tutorial_movement_help_reply() -> str:
    return (
        "Keep holding the grip button while you move your hand. When you want to let the object go, "
        "release the grip button. What would you like to try?"
    )


def build_exact_tutorial_control_result(user_text: str, scene_name: str, scene_context: str) -> dict | None:
    if normalized_scene_name(scene_name) != "tutorialinteraction" or "[SYSTEM_" in (user_text or ""):
        return None

    stage = _tutorial_context_value(scene_context, "stage", "Inactive")
    held_throughout = _tutorial_context_value(scene_context, "heldThroughoutVoiceTurn", "False").lower() == "true"
    held_object = _tutorial_context_value(scene_context, "heldObjectKey", "none")
    first_object = _tutorial_context_value(scene_context, "firstObjectKey", "none")
    if not _meaningful_tutorial_response(user_text):
        return {"reply": "I didn't quite catch that. Please try telling me again when you're ready.", "action": "", "objectKey": ""}
    if _tutorial_exit_requested(user_text):
        return {
            "reply": "We'll make the Exit available after you explore two different objects with me. Let's continue with this step first.",
            "action": "", "objectKey": "",
        }

    if stage in {"Inactive", "AwaitingInitialDescription"} and _tutorial_locomotion_help_requested(user_text):
        return {"reply": _tutorial_locomotion_help_reply(), "action": "", "objectKey": ""}

    if stage == "AwaitingInitialDescription":
        return {
            "reply": "Thank you! Please choose one object, pick it up, keep holding it, and tell me what color it looks like to you—or describe another visual detail you notice.",
            "action": "initial_description_received", "objectKey": "",
        }
    if stage == "AwaitingFirstHeldDescription":
        if not held_throughout or held_object == "none":
            return {"reply": "That's okay—please pick up one of the objects and keep holding it while you tell me about it.", "action": "", "objectKey": ""}
        return {"reply": "Nice! While you're still holding it, how would you describe its shape?", "action": "first_visual_description_received", "objectKey": held_object}
    if stage == "AwaitingFirstShapeDescription":
        if not held_throughout or held_object == "none" or held_object.lower() != first_object.lower():
            return {"reply": "Please pick up the same object again and keep holding it while you describe its shape.", "action": "", "objectKey": ""}
        return {"reply": "Before you move it or let it go, please keep holding it and tell me—what would you like to do with it?", "action": "first_shape_response_received", "objectKey": held_object}
    if stage == "AwaitingFirstActionChoice":
        if not held_throughout or held_object == "none" or held_object.lower() != first_object.lower():
            return {"reply": "Please hold that first object again while you tell me what you'd like to do with it.", "action": "", "objectKey": ""}
        if _tutorial_movement_help_requested(user_text):
            return {"reply": _tutorial_movement_help_reply(), "action": "", "objectKey": ""}
        return {"reply": "That sounds good—go ahead and move it around, or let it go and see what happens.", "action": "first_action_choice_received", "objectKey": held_object}
    if stage == "AwaitingFirstInteraction":
        if _tutorial_movement_help_requested(user_text):
            return {"reply": _tutorial_movement_help_reply(), "action": "", "objectKey": ""}
        return {"reply": "Go ahead and try moving the first object around, or let it go when you're ready.", "action": "", "objectKey": ""}
    if stage == "AwaitingSecondHeldDescription":
        if not held_throughout or held_object == "none":
            return {"reply": "Please choose a different object and keep holding it while you tell me how it differs from the first one.", "action": "", "objectKey": ""}
        if held_object.lower() == first_object.lower():
            return {"reply": "You've picked up the first object again. Please choose a different one and tell me what you notice.", "action": "", "objectKey": ""}
        return {
            "reply": "Lovely—you've explored two different objects. When you're ready, teleport to the highlighted Exit position and press B on the right controller to finish, or feel free to keep exploring and asking questions to me.",
            "action": "second_description_received", "objectKey": held_object,
        }
    return None


def build_exact_tutorial_exit_help_reply(user_text: str, scene_name: str, scene_context: str = "") -> str:
    if normalized_scene_name(scene_name) != "tutorialinteraction" or _tutorial_context_value(scene_context, "stage", "Inactive") != "Complete":
        return ""
    return "Of course—when you're ready, use the thumbstick to teleport to the highlighted Exit position, then press B on the right controller to finish." if _tutorial_exit_requested(user_text) else ""


# Per-scene phrasing for what to do once the participant is holding the
# highlighted guided-task object. Keys are normalized_scene_name() output.
STAGE_PROGRESS_NEXT_ACTION = {
    "puppies": "throw it toward the highlighted puppy",
    "elephant": "throw it toward the highlighted elephant",
    "lake": "throw it toward the highlighted target by the lake",
    "solitaryconfinement": "move or throw it toward the highlighted door",
    "tunnel": "carry it toward the highlighted position in the tunnel",
    "attic": "move behind the highlighted safe position",
}

_OBJECT_NAME_ALIASES = {
    "tennisball": "tennis ball",
    "banana": "banana",
    "airplane": "airplane",
    "stone": "stone",
    "baseball": "baseball",
    "book": "book",
    "cup": "cup",
    "flashlight": "flashlight",
    "handtorch": "flashlight",
    "torch": "flashlight",
    "shield": "shield",
    "shield01": "shield",
}


def humanize_object_name(raw_name: str) -> str:
    name = re.sub(r"\(Clone\)\s*$", "", (raw_name or "").strip()).strip()
    if not name:
        return "object"
    key = name.lower().replace(" ", "").replace("_", "")
    if key in _OBJECT_NAME_ALIASES:
        return _OBJECT_NAME_ALIASES[key]
    spaced = re.sub(r"(?<!^)(?=[A-Z])", " ", name)
    return spaced.strip().lower() or "object"


def build_exact_stage_progress_reply(user_text: str, avatar_condition: str | None = None) -> str:
    if "[SYSTEM_STAGE_PROGRESS]" not in user_text:
        return ""

    scene_match = re.search(r"^Scene:\s*(.+)$", user_text, re.MULTILINE)
    object_match = re.search(r"^Object:\s*(.+)$", user_text, re.MULTILINE)
    scene_name = scene_match.group(1).strip() if scene_match else ""
    object_name = humanize_object_name(object_match.group(1).strip() if object_match else "")
    next_action = STAGE_PROGRESS_NEXT_ACTION.get(
        normalized_scene_name(scene_name), "use it with the highlighted target"
    )

    tone_name = backend_selected_tone(avatar_condition)["name"]
    if tone_name == "warm":
        return f"Yes, nice, you've got the {object_name}! Let's {next_action} together!"
    if tone_name == "cold":
        action_sentence = next_action[0].upper() + next_action[1:]
        return f"{object_name.capitalize()} confirmed. {action_sentence}."
    return f"You have the {object_name}. Next, {next_action}."


# Scene-independent: once the guided task is fully complete, the wording is
# the same regardless of which scene/object it was, only the tone changes.
STAGE_COMPLETE_REPLY = {
    "warm": "Wonderful, I'm so glad that came together! You can keep exploring, try other objects, or just talk with me.",
    "cold": "Task complete. Exploration, available objects, and conversation remain available.",
}


def build_exact_stage_complete_reply(user_text: str, avatar_condition: str | None = None) -> str:
    if "[SYSTEM_STAGE_COMPLETE]" not in user_text:
        return ""

    scene_match = re.search(r"^Scene:\s*(.+)$", user_text, re.MULTILINE)
    scene_name = scene_match.group(1).strip() if scene_match else ""
    if normalized_scene_name(scene_name) == "tutorialinteraction":
        return (
            "Nice exploring! You've discovered how these shapes respond when you pick them up and let them go. "
            "You can keep trying any of them, or use the thumbstick to move to the Exit and press B on the right controller when you're ready."
        )

    tone_name = backend_selected_tone(avatar_condition)["name"]
    return STAGE_COMPLETE_REPLY.get(
        tone_name,
        "The guided interaction is complete. You can continue exploring or talk with the avatar.",
    )


def find_latest_wav(output_dir: Path, since: float) -> Path | None:
    candidates = []
    for pattern in ("*.wav", "*.WAV"):
        candidates.extend(output_dir.rglob(pattern))
    candidates = [p for p in candidates if p.stat().st_mtime >= since]
    if not candidates:
        return None
    return max(candidates, key=lambda p: p.stat().st_mtime)


def generate_vibevoice_wav(text: str) -> bytes:
    if VIBEVOICE_USE_WORKER:
        return vibevoice_worker_client.synthesize(text, VIBEVOICE_TIMEOUT_SECONDS)

    log(f"[TTS] Start VibeVoice generation. text_chars={len(text)}, timeout={VIBEVOICE_TIMEOUT_SECONDS}s")
    if not VIBEVOICE_REPO.exists():
        raise FileNotFoundError(f"VibeVoice repo not found: {VIBEVOICE_REPO}")
    if not VIBEVOICE_PYTHON.exists():
        raise FileNotFoundError(f"VibeVoice python not found: {VIBEVOICE_PYTHON}")

    inference_script = VIBEVOICE_REPO / "demo" / "realtime_model_inference_from_file.py"
    if not inference_script.exists():
        raise FileNotFoundError(f"VibeVoice realtime inference script not found: {inference_script}")

    with tempfile.TemporaryDirectory(prefix="vv_tts_") as temp_dir:
        temp_dir_path = Path(temp_dir)
        text_path = temp_dir_path / "reply.txt"
        output_dir = temp_dir_path / "outputs"
        output_dir.mkdir(parents=True, exist_ok=True)
        text_path.write_text(text, encoding="utf-8")

        start_time = time.time()
        command = [
            str(VIBEVOICE_PYTHON),
            str(inference_script),
            "--model_path",
            VIBEVOICE_MODEL,
            "--txt_path",
            str(text_path),
            "--speaker_name",
            VIBEVOICE_SPEAKER,
            "--output_dir",
            str(output_dir),
        ]
        if VIBEVOICE_DEVICE:
            command.extend(["--device", VIBEVOICE_DEVICE])

        log("[TTS] Running VibeVoice subprocess...")
        completed = subprocess.run(
            command,
            cwd=str(VIBEVOICE_REPO),
            capture_output=True,
            text=True,
            timeout=VIBEVOICE_TIMEOUT_SECONDS,
        )
        log(f"[TTS] VibeVoice subprocess finished. returncode={completed.returncode}")
        if completed.returncode != 0:
            raise RuntimeError(
                "VibeVoice failed.\n"
                f"STDOUT:\n{completed.stdout}\n"
                f"STDERR:\n{completed.stderr}"
            )

        wav_path = find_latest_wav(output_dir, start_time)
        if wav_path is None:
            raise RuntimeError(f"VibeVoice completed but no wav was found in {output_dir}")

        wav_bytes = wav_path.read_bytes()
        log(f"[TTS] Success. wav_path={wav_path}, wav_bytes={len(wav_bytes)}")
        return wav_bytes


def generate_test_wav(duration: float = 4.0, sample_rate: int = 24000) -> bytes:
    """Small audible fallback so Unity audio + mouth animation can still be tested."""
    frame_count = int(duration * sample_rate)
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        for i in range(frame_count):
            fade = min(i / 1200, (frame_count - i) / 1200, 1.0)
            sample = int(0.6 * fade * 32767 * math.sin(2 * math.pi * 440 * i / sample_rate))
            wav.writeframesraw(struct.pack("<h", sample))
    return buffer.getvalue()


def pcm16_to_wav(pcm_bytes: bytes, sample_rate: int = 16000, channels: int = 1) -> bytes:
    buffer = io.BytesIO()
    with wave.open(buffer, "wb") as wav:
        wav.setnchannels(channels)
        wav.setsampwidth(2)
        wav.setframerate(sample_rate)
        wav.writeframes(pcm_bytes)
    return buffer.getvalue()


def elevenlabs_sample_rate(output_format: str) -> int:
    parts = output_format.split("_")
    if len(parts) >= 2 and parts[0] == "pcm":
        try:
            return int(parts[1])
        except ValueError:
            pass
    return 16000


def elevenlabs_model_candidates() -> list[str]:
    candidates = [ELEVENLABS_MODEL_ID]
    candidates.extend(
        model_id.strip()
        for model_id in ELEVENLABS_FALLBACK_MODEL_IDS.split(",")
        if model_id.strip()
    )
    unique_candidates = []
    for model_id in candidates:
        if model_id not in unique_candidates:
            unique_candidates.append(model_id)
    return unique_candidates


def gemini_voice_for_tone(tone_name: str | None = None) -> str:
    tone_name = tone_name or ELEVENLABS_TONE["name"]
    if tone_name == "warm":
        return GEMINI_TTS_SUPPORTIVE_VOICE
    if tone_name == "cold":
        return GEMINI_TTS_GUIDE_VOICE
    return GEMINI_TTS_DETACHED_VOICE


def gemini_style_instruction(tone_name: str | None = None) -> str:
    tone_name = tone_name or ELEVENLABS_TONE["name"]
    if tone_name == "warm":
        return (
            "Read in a friendly, calm, socially warm voice. Use gentle emphasis and a supportive but concise delivery."
        )
    if tone_name == "cold":
        return (
            "Read in a brief, factual, emotionally neutral voice. Use low expressiveness and no reassuring warmth."
        )
    return (
        "Read in a clear, neutral, informative voice. Use natural pacing with minimal emotional color."
    )


def generate_gemini_wav(text: str, tone_name: str | None = None) -> bytes:
    if not GEMINI_API_KEY:
        raise RuntimeError("Missing GEMINI_API_KEY or GOOGLE_API_KEY.")

    tone_name = tone_name or ELEVENLABS_TONE["name"]
    voice_name = gemini_voice_for_tone(tone_name)
    url = f"https://generativelanguage.googleapis.com/v1beta/models/{GEMINI_TTS_MODEL}:generateContent"
    prompt = f"{gemini_style_instruction(tone_name)}\n\nSay exactly this text:\n{text}"
    payload = {
        "contents": [
            {
                "parts": [
                    {"text": prompt},
                ],
            }
        ],
        "generationConfig": {
            "responseModalities": ["AUDIO"],
            "speechConfig": {
                "voiceConfig": {
                    "prebuiltVoiceConfig": {
                        "voiceName": voice_name,
                    }
                }
            },
        },
    }
    request = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Content-Type": "application/json",
            "x-goog-api-key": GEMINI_API_KEY,
        },
        method="POST",
    )

    log(
        "[TTS] Start Gemini generation. "
        f"model={GEMINI_TTS_MODEL}, voice={voice_name}, tone={tone_name}, "
        f"text_chars={len(text)}"
    )
    try:
        with urllib.request.urlopen(request, timeout=GEMINI_TTS_TIMEOUT_SECONDS) as response:
            response_json = json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        error_body = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"Gemini TTS failed. status={exc.code}, body={error_body}") from exc

    try:
        inline_data = response_json["candidates"][0]["content"]["parts"][0]["inlineData"]["data"]
        pcm_bytes = base64.b64decode(inline_data)
    except Exception as exc:
        raise RuntimeError(f"Gemini TTS response did not include inline PCM audio: {response_json}") from exc

    wav_bytes = pcm16_to_wav(pcm_bytes, sample_rate=24000, channels=1)
    log(f"[TTS] Gemini PCM wrapped as wav. bytes={len(wav_bytes)}, pcm_bytes={len(pcm_bytes)}")
    return wav_bytes


def generate_elevenlabs_wav_for_model(text: str, model_id: str, tone: dict | None = None) -> bytes:
    if not ELEVENLABS_API_KEY:
        raise RuntimeError("Missing ELEVENLABS_API_KEY.")

    url = (
        f"https://api.elevenlabs.io/v1/text-to-speech/{ELEVENLABS_VOICE_ID}"
        f"?output_format={ELEVENLABS_OUTPUT_FORMAT}"
    )
    tone = tone or ELEVENLABS_TONE
    payload = {
        "text": text,
        "model_id": model_id,
        "voice_settings": {
            "stability": tone["stability"],
            "similarity_boost": tone["similarity_boost"],
            "style": tone["style"],
            "speed": tone["speed"],
            "use_speaker_boost": ELEVENLABS_USE_SPEAKER_BOOST,
        },
    }
    request = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Accept": "audio/mpeg" if ELEVENLABS_OUTPUT_FORMAT.startswith("mp3") else "application/octet-stream",
            "Content-Type": "application/json",
            "xi-api-key": ELEVENLABS_API_KEY,
        },
        method="POST",
    )

    log(
        "[TTS] Start ElevenLabs generation. "
        f"voice_id={ELEVENLABS_VOICE_ID}, model={model_id}, "
        f"format={ELEVENLABS_OUTPUT_FORMAT}, text_chars={len(text)}"
    )
    try:
        with urllib.request.urlopen(request, timeout=ELEVENLABS_TIMEOUT_SECONDS) as response:
            audio_bytes = response.read()
    except urllib.error.HTTPError as exc:
        error_body = exc.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"ElevenLabs TTS failed. status={exc.code}, body={error_body}") from exc

    if ELEVENLABS_OUTPUT_FORMAT.startswith("pcm_"):
        sample_rate = elevenlabs_sample_rate(ELEVENLABS_OUTPUT_FORMAT)
        wav_bytes = pcm16_to_wav(audio_bytes, sample_rate=sample_rate)
        log(f"[TTS] ElevenLabs PCM wrapped as wav. bytes={len(wav_bytes)}, sample_rate={sample_rate}")
        return wav_bytes

    if ELEVENLABS_OUTPUT_FORMAT.startswith("wav_"):
        log(f"[TTS] ElevenLabs wav success. bytes={len(audio_bytes)}")
        return audio_bytes

    raise RuntimeError(
        f"ElevenLabs output format {ELEVENLABS_OUTPUT_FORMAT!r} is not Unity WAV-compatible. "
        "Use pcm_16000, pcm_22050, pcm_24000, pcm_44100, or a wav_* format."
    )


def generate_elevenlabs_wav(text: str, tone: dict | None = None) -> bytes:
    errors = []
    for model_id in elevenlabs_model_candidates():
        try:
            return generate_elevenlabs_wav_for_model(text, model_id, tone)
        except Exception as exc:
            errors.append(f"{model_id}: {exc}")
            log(f"[TTS] ElevenLabs model failed, trying next if available. {model_id}: {exc}")
    raise RuntimeError("All ElevenLabs models failed. " + " | ".join(errors))


def elevenlabs_stream_request(text: str, model_id: str, tone: dict | None = None) -> urllib.request.Request:
    if not ELEVENLABS_API_KEY:
        raise RuntimeError("Missing ELEVENLABS_API_KEY.")
    if not ELEVENLABS_OUTPUT_FORMAT.startswith("pcm_"):
        raise RuntimeError("Streaming playback expects an ElevenLabs pcm_* output format.")

    url = (
        f"https://api.elevenlabs.io/v1/text-to-speech/{ELEVENLABS_VOICE_ID}/stream"
        f"?output_format={ELEVENLABS_OUTPUT_FORMAT}"
    )
    if ELEVENLABS_OPTIMIZE_STREAMING_LATENCY:
        url += f"&optimize_streaming_latency={ELEVENLABS_OPTIMIZE_STREAMING_LATENCY}"
    tone = tone or ELEVENLABS_TONE
    payload = {
        "text": text,
        "model_id": model_id,
        "voice_settings": {
            "stability": tone["stability"],
            "similarity_boost": tone["similarity_boost"],
            "style": tone["style"],
            "speed": tone["speed"],
            "use_speaker_boost": ELEVENLABS_USE_SPEAKER_BOOST,
        },
    }
    return urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={
            "Accept": "application/octet-stream",
            "Content-Type": "application/json",
            "xi-api-key": ELEVENLABS_API_KEY,
        },
        method="POST",
    )


async def stream_elevenlabs_pcm(websocket: WebSocket, text: str, tone: dict | None = None, source_label: str = "reply") -> dict:
    sample_rate = elevenlabs_sample_rate(ELEVENLABS_OUTPUT_FORMAT)
    chunk_size = max(512, ELEVENLABS_STREAM_CHUNK_BYTES)
    errors = []

    for model_id in elevenlabs_model_candidates():
        start_time = time.time()
        log(
            "[TTS] Start ElevenLabs streaming. "
            f"source={source_label}, voice_id={ELEVENLABS_VOICE_ID}, model={model_id}, "
            f"format={ELEVENLABS_OUTPUT_FORMAT}, timeout={ELEVENLABS_TIMEOUT_SECONDS}s, text_chars={len(text)}"
        )
        try:
            request = elevenlabs_stream_request(text, model_id, tone)
            log(f"[TTS] ElevenLabs request opening. source={source_label}, model={model_id}")
            with urllib.request.urlopen(request, timeout=ELEVENLABS_TIMEOUT_SECONDS) as response:
                log(f"[TTS] ElevenLabs response opened. source={source_label}, model={model_id}, status={getattr(response, 'status', 'unknown')}")
                start_payload = json.dumps(
                    {
                        "type": "audio_stream_start",
                        "sampleRate": sample_rate,
                        "channels": 1,
                        "format": "pcm_s16le",
                        "model": model_id,
                        "source": source_label,
                    },
                    ensure_ascii=False,
                )
                if not await safe_send_text(websocket, start_payload, "audio_stream_start"):
                    return {"ok": False, "model": model_id}
                log(f"[TTS] audio_stream_start sent to Unity. source={source_label}, sample_rate={sample_rate}, model={model_id}")

                total_bytes = 0
                first_chunk_at = None
                while True:
                    chunk = response.read(chunk_size)
                    if not chunk:
                        break
                    if first_chunk_at is None:
                        first_chunk_at = time.time()
                        log(f"[TTS] ElevenLabs first audio chunk after {first_chunk_at - start_time:.3f}s.")
                    total_bytes += len(chunk)
                    if not await send_stream_chunk(websocket, chunk):
                        return {"ok": False, "model": model_id}

                silence_bytes = int(sample_rate * 2 * max(0, ELEVENLABS_STREAM_END_SILENCE_MS) / 1000)
                if silence_bytes > 0:
                    silence = b"\x00" * (silence_bytes - (silence_bytes % 2))
                    total_bytes += len(silence)
                    if not await send_stream_chunk(websocket, silence):
                        return {"ok": False, "model": model_id}

                end_payload = json.dumps(
                    {"type": "audio_stream_end", "bytes": total_bytes, "model": model_id},
                    ensure_ascii=False,
                )
                if not await safe_send_text(websocket, end_payload, "audio_stream_end"):
                    return {"ok": False, "model": model_id}
                log(f"[TTS] ElevenLabs stream complete. bytes={total_bytes}, seconds={time.time() - start_time:.3f}")
                return {
                    "ok": True,
                    "model": model_id,
                    "bytes": total_bytes,
                    "first_audio_seconds": (first_chunk_at - start_time) if first_chunk_at else None,
                    "tts_seconds": time.time() - start_time,
                }
        except urllib.error.HTTPError as exc:
            error_body = exc.read().decode("utf-8", errors="replace")
            errors.append(f"{model_id}: status={exc.code}, body={error_body}")
            log(f"[TTS] ElevenLabs streaming model failed. {model_id}: status={exc.code}, body={error_body}")
        except Exception as exc:
            errors.append(f"{model_id}: {exc}")
            log(f"[TTS] ElevenLabs streaming model failed. {model_id}: {exc}")

    raise RuntimeError("All ElevenLabs streaming models failed. " + " | ".join(errors))


def generate_windows_tts_wav(text: str) -> bytes:
    log(f"[TTS] Start Windows fallback TTS. text_chars={len(text)}")
    with tempfile.TemporaryDirectory(prefix="win_tts_") as temp_dir:
        temp_dir_path = Path(temp_dir)
        text_path = temp_dir_path / "reply.txt"
        wav_path = temp_dir_path / "reply.wav"
        text_path.write_text(text, encoding="utf-8")

        text_path_ps = str(text_path).replace("'", "''")
        wav_path_ps = str(wav_path).replace("'", "''")
        ps_script = (
            "Add-Type -AssemblyName System.Speech; "
            f"$text = Get-Content -LiteralPath '{text_path_ps}' -Raw; "
            "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
            "$s.Rate = 0; "
            "$s.Volume = 100; "
            f"$s.SetOutputToWaveFile('{wav_path_ps}'); "
            "$s.Speak($text); "
            "$s.Dispose();"
        )
        completed = subprocess.run(
            ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps_script],
            capture_output=True,
            text=True,
            timeout=45,
            creationflags=WINDOWS_NO_WINDOW,
        )
        log(f"[TTS] Windows fallback subprocess finished. returncode={completed.returncode}")
        if completed.returncode != 0:
            raise RuntimeError(
                "Windows fallback TTS failed.\n"
                f"STDOUT:\n{completed.stdout}\n"
                f"STDERR:\n{completed.stderr}"
            )
        if not wav_path.exists():
            raise RuntimeError("Windows fallback TTS completed but no wav was produced.")

        wav_bytes = wav_path.read_bytes()
        log(f"[TTS] Windows fallback success. wav_bytes={len(wav_bytes)}")
        return wav_bytes


async def send_text_fallback(websocket: WebSocket, text: str) -> None:
    payload = {"type": "text", "text": text}
    await websocket.send_text(json.dumps(payload, ensure_ascii=False))


async def safe_send_bytes(websocket: WebSocket, payload: bytes, label: str) -> bool:
    log(f"[SEND] Preparing {label}. bytes={len(payload)}")
    try:
        await websocket.send_bytes(payload)
    except Exception as exc:
        log(f"[SEND] FAILED {label}. {type(exc).__name__}: {exc}")
        return False
    log(f"[SEND] OK {label}. bytes={len(payload)}")
    return True


async def send_stream_chunk(websocket: WebSocket, payload: bytes) -> bool:
    try:
        await websocket.send_bytes(payload)
    except Exception as exc:
        log(f"[SEND] FAILED audio_stream_chunk. {type(exc).__name__}: {exc}")
        return False
    return True


async def safe_send_text(websocket: WebSocket, text: str, label: str) -> bool:
    log(f"[SEND] Preparing {label}. chars={len(text)}")
    try:
        await websocket.send_text(text)
    except Exception as exc:
        log(f"[SEND] FAILED {label}. {type(exc).__name__}: {exc}")
        return False
    log(f"[SEND] OK {label}. chars={len(text)}")
    return True


def normalized_scene_name(scene_name: object) -> str:
    return str(scene_name or "").strip().lower().replace(" ", "").replace("_", "").replace("-", "")


def should_schedule_proactive_guide(metadata: dict) -> bool:
    if not PROACTIVE_GUIDE_ENABLED:
        return False
    return "*" in PROACTIVE_GUIDE_SCENES or "all" in PROACTIVE_GUIDE_SCENES or normalized_scene_name(metadata.get("sceneName")) in PROACTIVE_GUIDE_SCENES


def proactive_guide_key(metadata: dict) -> str:
    return "|".join(
        [
            str(metadata.get("participantId") or "unknown"),
            str(metadata.get("sessionId") or "unknown"),
            str(metadata.get("sceneName") or "unknown"),
            str(metadata.get("sceneIndex", -1)),
        ]
    )


async def send_avatar_reply(
    websocket: WebSocket,
    reply: str,
    metadata: dict,
    stream_reply_audio: bool,
    request_start_at: float,
    source_label: str,
    stt_seconds: float = 0.0,
    llm_seconds: float = 0.0,
) -> bool:
    turn_tone = backend_selected_tone(metadata.get("avatarCondition"))

    if TTS_PROVIDER in ("gemini", "auto") and GEMINI_TTS_ENABLED:
        try:
            tts_start_at = time.time()
            wav_data = await asyncio.get_event_loop().run_in_executor(
                None,
                generate_gemini_wav,
                reply,
                turn_tone["name"],
            )
            tts_seconds = time.time() - tts_start_at
            start_payload = json.dumps({"type": "avatar_reply_start", "source": source_label}, ensure_ascii=False)
            if not await safe_send_text(websocket, start_payload, f"{source_label}_reply_start"):
                return False
            if not await safe_send_bytes(websocket, wav_data, f"{source_label}_gemini_wav"):
                return False
            log(f"[TTS] Sent Gemini wav successfully for {source_label}. bytes={len(wav_data)}")
            append_latency_event(metadata, {
                "total_seconds": time.time() - request_start_at,
                "first_audio_seconds": time.time() - request_start_at,
                "stt_seconds": stt_seconds,
                "llm_seconds": llm_seconds,
                "tts_total_seconds": tts_seconds,
                "tts_first_seconds": None,
                "tts_model": GEMINI_TTS_MODEL,
                "tts_provider": "gemini",
                "tts_bytes": len(wav_data),
                "streaming": False,
                "source": source_label,
            })
            return True
        except Exception as exc:
            log(f"Gemini TTS failed for {source_label}; ElevenLabs fallback disabled for Gemini-only testing. {exc}")
            if TTS_PROVIDER == "gemini":
                fallback_payload = json.dumps({"type": "text", "text": reply}, ensure_ascii=False)
                return await safe_send_text(websocket, fallback_payload, f"{source_label}_text_fallback_gemini_failed")

    if ELEVENLABS_ENABLED:
        try:
            if stream_reply_audio and ELEVENLABS_STREAMING_ENABLED:
                stream_result = await stream_elevenlabs_pcm(websocket, reply, turn_tone, source_label)
                if stream_result.get("ok"):
                    total_seconds = time.time() - request_start_at
                    tts_first_seconds = stream_result.get("first_audio_seconds") or 0.0
                    first_audio_total = stt_seconds + llm_seconds + tts_first_seconds
                    append_latency_event(metadata, {
                        "total_seconds": total_seconds,
                        "first_audio_seconds": first_audio_total,
                        "stt_seconds": stt_seconds,
                        "llm_seconds": llm_seconds,
                        "tts_first_seconds": tts_first_seconds,
                        "tts_total_seconds": stream_result.get("tts_seconds"),
                        "tts_model": stream_result.get("model"),
                        "tts_provider": "elevenlabs",
                        "tts_bytes": stream_result.get("bytes"),
                        "streaming": True,
                        "source": source_label,
                    })
                    return True

            tts_start_at = time.time()
            wav_data = await asyncio.get_event_loop().run_in_executor(
                None,
                generate_elevenlabs_wav,
                reply,
                turn_tone,
            )
            tts_seconds = time.time() - tts_start_at
            start_payload = json.dumps({"type": "avatar_reply_start", "source": source_label}, ensure_ascii=False)
            if not await safe_send_text(websocket, start_payload, f"{source_label}_reply_start"):
                return False
            if not await safe_send_bytes(websocket, wav_data, f"{source_label}_elevenlabs_wav"):
                return False
            log(f"[TTS] Sent ElevenLabs wav successfully for {source_label}. bytes={len(wav_data)}")
            append_latency_event(metadata, {
                "total_seconds": time.time() - request_start_at,
                "first_audio_seconds": time.time() - request_start_at,
                "stt_seconds": stt_seconds,
                "llm_seconds": llm_seconds,
                "tts_first_seconds": None,
                "tts_total_seconds": tts_seconds,
                "tts_model": "batch_wav",
                "tts_provider": "elevenlabs",
                "tts_bytes": len(wav_data),
                "streaming": False,
                "source": source_label,
            })
            return True
        except Exception as exc:
            log(f"ElevenLabs TTS failed for {source_label}: {exc}")
            if WINDOWS_TTS_FALLBACK:
                try:
                    fallback_wav = await asyncio.get_event_loop().run_in_executor(
                        None,
                        generate_windows_tts_wav,
                        reply,
                    )
                    start_payload = json.dumps({"type": "avatar_reply_start", "source": source_label}, ensure_ascii=False)
                    if not await safe_send_text(websocket, start_payload, f"{source_label}_reply_start"):
                        return False
                    return await safe_send_bytes(websocket, fallback_wav, f"{source_label}_windows_tts_fallback_wav")
                except Exception as fallback_exc:
                    log(f"Windows fallback TTS failed for {source_label}: {fallback_exc}")

            fallback_payload = json.dumps({"type": "text", "text": reply}, ensure_ascii=False)
            return await safe_send_text(websocket, fallback_payload, f"{source_label}_text_fallback_no_audio")

    if not VIBEVOICE_ENABLED:
        log(f"ElevenLabs and VibeVoice disabled for {source_label}; no audio sent.")
        return True

    if WINDOWS_TTS_FIRST:
        try:
            windows_wav = await asyncio.get_event_loop().run_in_executor(
                None,
                generate_windows_tts_wav,
                reply,
            )
            start_payload = json.dumps({"type": "avatar_reply_start", "source": source_label}, ensure_ascii=False)
            if not await safe_send_text(websocket, start_payload, f"{source_label}_reply_start"):
                return False
            return await safe_send_bytes(websocket, windows_wav, f"{source_label}_windows_tts_first_wav")
        except Exception as windows_exc:
            log(f"Windows-first TTS failed for {source_label}, falling back to VibeVoice: {windows_exc}")

    try:
        wav_data = await asyncio.get_event_loop().run_in_executor(
            None,
            generate_vibevoice_wav,
            reply,
        )
        start_payload = json.dumps({"type": "avatar_reply_start", "source": source_label}, ensure_ascii=False)
        if not await safe_send_text(websocket, start_payload, f"{source_label}_reply_start"):
            return False
        return await safe_send_bytes(websocket, wav_data, f"{source_label}_vibevoice_wav")
    except Exception as exc:
        log(f"VibeVoice TTS failed for {source_label}: {exc}")
        if WINDOWS_TTS_FALLBACK:
            try:
                fallback_wav = await asyncio.get_event_loop().run_in_executor(
                    None,
                    generate_windows_tts_wav,
                    reply,
                )
                start_payload = json.dumps({"type": "avatar_reply_start", "source": source_label}, ensure_ascii=False)
                if not await safe_send_text(websocket, start_payload, f"{source_label}_reply_start"):
                    return False
                return await safe_send_bytes(websocket, fallback_wav, f"{source_label}_windows_tts_fallback_wav")
            except Exception as fallback_exc:
                log(f"Windows fallback TTS failed for {source_label}: {fallback_exc}")

        fallback_payload = json.dumps({"type": "text", "text": reply}, ensure_ascii=False)
        return await safe_send_text(websocket, fallback_payload, f"{source_label}_text_fallback_no_audio")


async def run_delayed_proactive_guide(
    websocket: WebSocket,
    groq_client: AsyncGroq,
    openai_client: AsyncOpenAI | None,
    metadata: dict,
    scene_prompt: str,
    scene_context: str,
    stream_reply_audio: bool,
    sent_keys: set[str],
    guide_key: str,
) -> None:
    try:
        delay_seconds = max(0.0, float(metadata.get("proactiveGuideDelaySeconds", PROACTIVE_GUIDE_DELAY_SECONDS) or PROACTIVE_GUIDE_DELAY_SECONDS))
        log(
            f"[PROACTIVE] Scheduled guide in {delay_seconds:.1f}s for key={guide_key}; "
            f"scene={metadata.get('sceneName')}, index={metadata.get('sceneIndex')}, "
            f"tone={metadata.get('avatarCondition')}, stream_audio={stream_reply_audio}, "
            f"context_chars={len(scene_context or '')}"
        )
        log(f"[PROACTIVE] LLM prewarm start. provider={LLM_PROVIDER}, openai_model={OPENAI_MODEL}, groq_model={GROQ_CHAT_MODEL}")
        llm_start_at = time.time()
        reply_task = asyncio.create_task(
            generate_reply(
                groq_client,
                openai_client,
                PROACTIVE_GUIDE_TRIGGER_TEXT,
                scene_prompt,
                scene_context,
                metadata.get("avatarCondition"),
                conversation_memory.get(conversation_key(metadata), []),
            )
        )
        await asyncio.sleep(delay_seconds)
        request_start_at = time.time()
        log(f"[PROACTIVE] 10s timer elapsed; preparing avatar audio. key={guide_key}")
        llm_seconds = 0.0
        try:
            if reply_task.done():
                reply = reply_task.result()
                log(f"[PROACTIVE] LLM reply was ready before timer. elapsed={time.time() - llm_start_at:.2f}s")
            else:
                log(f"[PROACTIVE] LLM still running at timer; waiting grace={PROACTIVE_GUIDE_LLM_GRACE_SECONDS}s")
                reply = await asyncio.wait_for(
                    asyncio.shield(reply_task),
                    timeout=max(0.0, PROACTIVE_GUIDE_LLM_GRACE_SECONDS),
                )
            llm_seconds = time.time() - llm_start_at
        except asyncio.TimeoutError:
            llm_seconds = time.time() - llm_start_at
            reply_task.cancel()
            reply = PROACTIVE_GUIDE_FALLBACK_TEXT
            log(
                "[PROACTIVE] LLM not ready at guide time; using fallback "
                f"after {llm_seconds:.2f}s."
            )
        except Exception as exc:
            llm_seconds = time.time() - llm_start_at if "llm_start_at" in locals() else 0.0
            reply = PROACTIVE_GUIDE_FALLBACK_TEXT
            log(f"[PROACTIVE] LLM failed; using fallback guide. {type(exc).__name__}: {exc}")
        append_conversation_log("[system: proactive guide after scene entry]", reply, CURRENT_MODE, metadata)
        log(f"[PROACTIVE] Reply ready. llm_seconds={llm_seconds:.2f}, chars={len(reply)}, text={reply}")
        log(f"[PROACTIVE] Dispatching avatar reply to TTS. source=proactive_guide, stream_audio={stream_reply_audio}")
        if await send_avatar_reply(
            websocket,
            reply,
            metadata,
            stream_reply_audio,
            request_start_at,
            "proactive_guide",
            stt_seconds=0.0,
            llm_seconds=llm_seconds,
        ):
            sent_keys.add(guide_key)
            log(f"[PROACTIVE] Sent guide for key={guide_key}; total_after_timer={time.time() - request_start_at:.2f}s")
    except asyncio.CancelledError:
        log(f"[PROACTIVE] Cancelled guide for key={guide_key}")
        raise
    except Exception as exc:
        log(f"[PROACTIVE] Failed guide for key={guide_key}: {type(exc).__name__}: {exc}")


def parse_heart_rate_measurement(data: bytearray) -> tuple[int, list[float]]:
    flags = data[0]
    uses_uint16 = bool(flags & 0x01)
    bpm = int.from_bytes(data[1:3], "little") if uses_uint16 else int(data[1])
    offset = 3 if uses_uint16 else 2
    if flags & 0x08:
        offset += 2
    rr_intervals_ms: list[float] = []
    if flags & 0x10:
        while offset + 1 < len(data):
            rr_1024 = int.from_bytes(data[offset:offset + 2], "little")
            rr_intervals_ms.append(round(rr_1024 * 1000.0 / 1024.0, 3))
            offset += 2
    return bpm, rr_intervals_ms


def append_heart_rate_csv(timestamp_utc: str, bpm: int, rr_intervals_ms: list[float]) -> None:
    HEART_RATE_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    session = safe_filename_part(heart_rate_context.get("sessionId"))[:32]
    path = HEART_RATE_OUTPUT_DIR / f"HeartRate_{session}.csv"
    new_file = not path.exists()
    with path.open("a", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        if new_file:
            writer.writerow(["TimestampUtc", "HeartRateBpm", "RRIntervalsMs", "ParticipantId",
                             "LoginId", "SessionId", "SceneName", "AvatarCondition", "Source"])
        writer.writerow([
            timestamp_utc, bpm, json.dumps(rr_intervals_ms, separators=(",", ":")),
            heart_rate_context.get("participantId", "unknown"), heart_rate_context.get("loginId", ""),
            heart_rate_context.get("sessionId", "unknown"), heart_rate_context.get("sceneName", "unknown"),
            heart_rate_context.get("avatarCondition", "unknown"), heart_rate_state.get("source", "Polar H10"),
        ])


async def run_heart_rate_collector() -> None:
    if BleakScanner is None or BleakClient is None:
        heart_rate_state["source"] = "bleak is not installed in the backend environment"
        log("[HEART_RATE] Bleak unavailable; install requirements.txt to enable Polar H10.")
        return
    while True:
        try:
            heart_rate_state.update(available=False, bpm=0, source="Scanning for Polar H10")
            log(f"[HEART_RATE] Scanning for BLE device containing {HEART_RATE_DEVICE_NAME!r}...")
            devices = await BleakScanner.discover(timeout=8.0)
            device = next((item for item in devices if item.name and
                           HEART_RATE_DEVICE_NAME.lower() in item.name.lower()), None)
            if device is None:
                heart_rate_state["source"] = f"No BLE device matching {HEART_RATE_DEVICE_NAME!r} found"
                await asyncio.sleep(5.0)
                continue
            source = f"Polar H10 via backend ({device.name})"
            async with BleakClient(device) as client:
                heart_rate_state["source"] = source
                log(f"[HEART_RATE] Connected to {device.name} ({device.address}).")
                log(f"[HEART_RATE] CSV output directory: {HEART_RATE_OUTPUT_DIR}")
                last_console_log_at = 0.0

                def notification_handler(_sender, payload: bytearray) -> None:
                    nonlocal last_console_log_at
                    try:
                        bpm, rr_intervals_ms = parse_heart_rate_measurement(payload)
                        timestamp_utc = datetime.utcnow().isoformat(timespec="milliseconds") + "Z"
                        heart_rate_state.update(available=True, bpm=bpm, rrIntervalsMs=rr_intervals_ms,
                                                source=source, timestampUtc=timestamp_utc,
                                                receivedAtMonotonic=time.monotonic())
                        append_heart_rate_csv(timestamp_utc, bpm, rr_intervals_ms)
                        now = time.monotonic()
                        if bpm > 0 and now - last_console_log_at >= 5.0:
                            log(f"[HEART_RATE] Live BPM={bpm}, RR(ms)={rr_intervals_ms or 'none'}, "
                                f"scene={heart_rate_context.get('sceneName', 'unknown')}, "
                                f"condition={heart_rate_context.get('avatarCondition', 'unknown')}")
                            last_console_log_at = now
                    except Exception as exc:
                        log(f"[HEART_RATE] Failed to parse/write measurement: {exc}")

                await client.start_notify(HEART_RATE_UUID, notification_handler)
                while client.is_connected:
                    await asyncio.sleep(1.0)
                heart_rate_state.update(available=False, bpm=0, source="Polar H10 disconnected")
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            heart_rate_state.update(available=False, bpm=0, source=f"Polar H10 error: {exc}")
            log(f"[HEART_RATE] Collector error; retrying: {exc}")
            await asyncio.sleep(5.0)


@app.get("/heart-rate")
async def get_heart_rate():
    age_seconds = (max(0.0, time.monotonic() - float(heart_rate_state["receivedAtMonotonic"]))
                   if heart_rate_state["receivedAtMonotonic"] else 1e9)
    fresh = bool(heart_rate_state["available"] and age_seconds <= 5.0)
    return {"available": fresh, "bpm": heart_rate_state["bpm"] if fresh else 0,
            "rrIntervalsMs": heart_rate_state["rrIntervalsMs"] if fresh else [],
            "source": heart_rate_state["source"], "timestampUtc": heart_rate_state["timestampUtc"],
            "ageSeconds": age_seconds}


@app.on_event("startup")
async def preload_vibevoice_worker():
    global heart_rate_task
    log("[HEART_RATE] Polar collector starting automatically with the avatar backend.")
    heart_rate_task = asyncio.create_task(run_heart_rate_collector())
    log(f"[STARTUP] SERVER_BUILD_TAG={SERVER_BUILD_TAG}")
    log(f"[STARTUP] SERVER_FILE={Path(__file__).resolve()}")
    log(f"[STARTUP] PROACTIVE_GUIDE enabled={PROACTIVE_GUIDE_ENABLED}, delay={PROACTIVE_GUIDE_DELAY_SECONDS}, scenes={sorted(PROACTIVE_GUIDE_SCENES)}")
    log(f"[STARTUP] ElevenLabs enabled={ELEVENLABS_ENABLED}, streaming={ELEVENLABS_STREAMING_ENABLED}, voice_id={ELEVENLABS_VOICE_ID}, model={ELEVENLABS_MODEL_ID}")
    if not ELEVENLABS_ENABLED and VIBEVOICE_ENABLED and VIBEVOICE_USE_WORKER and VIBEVOICE_PRELOAD:
        await asyncio.get_event_loop().run_in_executor(None, vibevoice_worker_client.start)


@app.on_event("shutdown")
async def shutdown_vibevoice_worker():
    if heart_rate_task is not None:
        heart_rate_task.cancel()
        try:
            await heart_rate_task
        except asyncio.CancelledError:
            pass
    vibevoice_worker_client.stop()


@app.websocket("/")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    log(f"Unity client connected; SERVER_BUILD_TAG={SERVER_BUILD_TAG}; waiting for scene config before scheduling proactive guide.")

    global CURRENT_MODE, script_index
    groq_client = AsyncGroq(api_key=GROQ_API_KEY)
    openai_client = AsyncOpenAI(api_key=OPENAI_API_KEY) if OPENAI_API_KEY else None
    if LLM_PROVIDER == "openai" and openai_client is None:
        log("[LLM] Warning: LLM_PROVIDER=openai but OPENAI_API_KEY is missing.")
    log(f"[LLM] Reply provider={LLM_PROVIDER}, openai_model={OPENAI_MODEL}, groq_model={GROQ_CHAT_MODEL}")
    scene_prompt = ""
    scene_context = ""
    stream_reply_audio = ELEVENLABS_STREAMING_ENABLED
    client_metadata = {
        "participantId": "unknown",
        "loginId": "",
        "sessionId": "unknown",
        "avatarCondition": ELEVENLABS_TONE["name"],
        "sceneName": "unknown",
        "sceneIndex": -1,
        "sceneContextChars": 0,
        "backendProactiveGuide": True,
        "proactiveGuideDelaySeconds": PROACTIVE_GUIDE_DELAY_SECONDS,
    }
    proactive_guide_task: asyncio.Task | None = None
    proactive_guide_task_key: str | None = None
    proactive_guide_sent_keys: set[str] = set()

    try:
        while True:
            request_start_at = None
            stt_seconds = 0.0
            llm_seconds = 0.0
            message = await websocket.receive()

            if message.get("type") == "websocket.disconnect":
                log("Unity client disconnected.")
                break

            if "text" in message:
                audio_turn_processed = False
                try:
                    data = json.loads(message["text"])
                    if data.get("type") == "config":
                        mode = data.get("mode")
                        if mode in ("ai", "scripted"):
                            if mode == "scripted" and CURRENT_MODE != "scripted":
                                script_index = 0
                            CURRENT_MODE = mode
                            log(f"Mode switched to {CURRENT_MODE}")
                        prompt = data.get("scenePrompt")
                        if isinstance(prompt, str):
                            scene_prompt = prompt.strip()
                            if scene_prompt:
                                log(f"Scene prompt updated: {scene_prompt[:120]}")
                            else:
                                log("Scene prompt cleared.")
                        context = data.get("sceneContext")
                        if isinstance(context, str):
                            scene_context = context.strip()
                            preview = scene_context.replace("\n", " | ")[:1200]
                            log(f"Scene context updated. chars={len(scene_context)}, preview={preview}")
                            client_metadata["sceneContextChars"] = len(scene_context)
                        for key in ("participantId", "loginId", "sessionId", "avatarCondition", "sceneName"):
                            value = data.get(key)
                            if isinstance(value, str) and value.strip():
                                client_metadata[key] = value.strip()
                                heart_rate_context[key] = value.strip()
                        scene_index = data.get("sceneIndex")
                        if isinstance(scene_index, int):
                            client_metadata["sceneIndex"] = scene_index
                        client_metadata["backendProactiveGuide"] = bool(data.get("backendProactiveGuide", True))
                        delay_value = data.get("proactiveGuideDelaySeconds")
                        if isinstance(delay_value, (int, float)):
                            client_metadata["proactiveGuideDelaySeconds"] = float(delay_value)
                        active_tone = backend_selected_tone(client_metadata.get("avatarCondition"))
                        condition_payload = json.dumps(
                            {
                                "type": "condition_config",
                                "avatarCondition": display_tone_name(active_tone["name"]),
                            },
                            ensure_ascii=False,
                        )
                        if not await safe_send_text(websocket, condition_payload, "condition_config"):
                            break
                        stream_reply_audio = bool(data.get("streamReplyAudio", ELEVENLABS_STREAMING_ENABLED))
                        log(f"Stream reply audio: {stream_reply_audio}")
                        log(
                            "[CONFIG] "
                            f"participant={client_metadata['participantId']}, session={client_metadata['sessionId']}, "
                            f"scene={client_metadata['sceneName']}, sceneIndex={client_metadata['sceneIndex']}, "
                            f"avatarCondition={client_metadata['avatarCondition']}, backendTone={display_tone_name(active_tone['name'])}, "
                            f"backendProactiveGuide={client_metadata.get('backendProactiveGuide')}, "
                            f"delay={client_metadata.get('proactiveGuideDelaySeconds')}, "
                            f"contextChars={client_metadata.get('sceneContextChars')}"
                        )
                        guide_key = proactive_guide_key(client_metadata)
                        schedule_allowed = should_schedule_proactive_guide(client_metadata)
                        log(f"[PROACTIVE] Schedule check. enabled={PROACTIVE_GUIDE_ENABLED}, allowed_scene={schedule_allowed}, requested={client_metadata.get('backendProactiveGuide', True)}, already_sent={guide_key in proactive_guide_sent_keys}, key={guide_key}")
                        if client_metadata.get("backendProactiveGuide", True) and schedule_allowed and guide_key not in proactive_guide_sent_keys:
                            if proactive_guide_task is None or proactive_guide_task.done() or proactive_guide_task_key != guide_key:
                                if proactive_guide_task is not None and not proactive_guide_task.done():
                                    proactive_guide_task.cancel()
                                proactive_guide_task_key = guide_key
                                proactive_guide_task = asyncio.create_task(
                                    run_delayed_proactive_guide(
                                        websocket,
                                        groq_client,
                                        openai_client,
                                        dict(client_metadata),
                                        scene_prompt,
                                        scene_context,
                                        stream_reply_audio,
                                        proactive_guide_sent_keys,
                                        guide_key,
                                    )
                                )
                        elif proactive_guide_task is not None and not proactive_guide_task.done():
                            proactive_guide_task.cancel()
                            proactive_guide_task = None
                            proactive_guide_task_key = None
                        continue
                    if data.get("type") == "turn_context":
                        prompt = data.get("scenePrompt")
                        if isinstance(prompt, str):
                            scene_prompt = prompt.strip()
                        context = data.get("sceneContext")
                        if isinstance(context, str):
                            scene_context = context.strip()
                            preview = scene_context.replace("\n", " | ")[:1200]
                            client_metadata["sceneContextChars"] = len(scene_context)
                            log(f"[TURN_CONTEXT] chars={len(scene_context)}, preview={preview}")
                        for key in ("participantId", "loginId", "sessionId", "avatarCondition", "sceneName"):
                            value = data.get(key)
                            if isinstance(value, str) and value.strip():
                                client_metadata[key] = value.strip()
                        scene_index = data.get("sceneIndex")
                        if isinstance(scene_index, int):
                            client_metadata["sceneIndex"] = scene_index
                        continue
                    if data.get("type") == "audio_turn":
                        request_start_at = time.time()
                        prompt = data.get("scenePrompt")
                        if isinstance(prompt, str):
                            scene_prompt = prompt.strip()
                        context = data.get("sceneContext")
                        if isinstance(context, str):
                            scene_context = context.strip()
                            preview = scene_context.replace("\n", " | ")[:1200]
                            client_metadata["sceneContextChars"] = len(scene_context)
                            log(f"[AUDIO_TURN_CONTEXT] chars={len(scene_context)}, preview={preview}")
                        for key in ("participantId", "loginId", "sessionId", "avatarCondition", "sceneName"):
                            value = data.get(key)
                            if isinstance(value, str) and value.strip():
                                client_metadata[key] = value.strip()
                        scene_index = data.get("sceneIndex")
                        if isinstance(scene_index, int):
                            client_metadata["sceneIndex"] = scene_index
                        audio_base64 = data.get("audioBase64")
                        if not isinstance(audio_base64, str) or not audio_base64.strip():
                            log("[AUDIO_TURN] Missing audioBase64; skipping turn.")
                            continue
                        try:
                            audio_bytes = base64.b64decode(audio_base64)
                        except Exception as exc:
                            log(f"[AUDIO_TURN] Failed to decode audioBase64: {exc}")
                            continue
                        log(f"Audio received in audio_turn: {len(audio_bytes)} bytes")
                        recording_path = save_voice_recording(audio_bytes, client_metadata)
                        client_metadata["lastRecordingPath"] = str(recording_path)
                        log(f"[RECORDING] Saved Unity microphone WAV: {recording_path}")
                        try:
                            stt_start_at = time.time()
                            user_text = await transcribe_audio(groq_client, audio_bytes)
                            stt_seconds = time.time() - stt_start_at
                        except Exception as exc:
                            log(f"STT failed: {exc}")
                            user_text = "(inaudible)"
                            stt_seconds = time.time() - request_start_at
                        append_transcription_log(user_text, recording_path, client_metadata, stt_seconds)
                        audio_turn_processed = True
                except json.JSONDecodeError:
                    user_text = message["text"].strip()
                else:
                    if not audio_turn_processed:
                        continue
            elif "bytes" in message:
                request_start_at = time.time()
                audio_bytes = message["bytes"]
                log(f"Audio received: {len(audio_bytes)} bytes")
                recording_path = save_voice_recording(audio_bytes, client_metadata)
                client_metadata["lastRecordingPath"] = str(recording_path)
                log(f"[RECORDING] Saved Unity microphone WAV: {recording_path}")
                try:
                    stt_start_at = time.time()
                    user_text = await transcribe_audio(groq_client, audio_bytes)
                    stt_seconds = time.time() - stt_start_at
                except Exception as exc:
                    log(f"STT failed: {exc}")
                    user_text = "(inaudible)"
                    stt_seconds = time.time() - request_start_at
                append_transcription_log(user_text, recording_path, client_metadata, stt_seconds)
            else:
                continue

            if not user_text:
                # Give the participant audible feedback even when an older
                # Unity client sends an all-zero WAV or STT cannot recover any
                # speech. The audio-stream end message also completes Unity's
                # isSending state, so no separate silent terminal event is
                # needed when TTS succeeds.
                microphone_retry_reply = "I didn't catch that. Please hold A and try again."
                request_start_at = request_start_at or time.time()
                log("[AUDIO_TURN] Empty transcript; speaking microphone retry guidance.")
                if not await send_avatar_reply(
                    websocket,
                    microphone_retry_reply,
                    client_metadata,
                    stream_reply_audio,
                    request_start_at,
                    "microphone_retry",
                    stt_seconds=stt_seconds,
                    llm_seconds=0.0,
                ):
                    break
                continue
            if request_start_at is None:
                request_start_at = time.time()

            tutorial_exit_help_reply = build_exact_tutorial_exit_help_reply(
                user_text,
                client_metadata.get("sceneName", ""),
                scene_context,
            )
            tutorial_control_result = None if tutorial_exit_help_reply else build_exact_tutorial_control_result(
                user_text,
                client_metadata.get("sceneName", ""),
                scene_context,
            )
            if tutorial_exit_help_reply:
                response_source = "tutorial_exit_help"
                highlight_payload = json.dumps(
                    {"type": "tutorial_exit_highlight"},
                    ensure_ascii=False,
                )
                if not await safe_send_text(websocket, highlight_payload, "tutorial_exit_highlight"):
                    break
            elif tutorial_control_result:
                response_source = "tutorial_control"
                tutorial_action = tutorial_control_result.get("action", "")
                if tutorial_action:
                    control_payload = json.dumps(
                        {
                            "type": "tutorial_control",
                            "action": tutorial_action,
                            "objectKey": tutorial_control_result.get("objectKey", ""),
                        },
                        ensure_ascii=False,
                    )
                    if not await safe_send_text(websocket, control_payload, "tutorial_control"):
                        break
            elif "[SYSTEM_AUTO_TASK_BRIEFING]" in user_text:
                response_source = "auto_briefing"
            elif "[SYSTEM_TUTORIAL_STAGE]" in user_text:
                response_source = "tutorial_stage"
            elif "[SYSTEM_STAGE_PROGRESS]" in user_text:
                response_source = "stage_progress"
            elif "[SYSTEM_STAGE_COMPLETE]" in user_text:
                response_source = "stage_complete"
            elif "[SYSTEM_OPENING_GREETING]" in user_text:
                # The six formal scenes have no spoken task objective, so their opening
                # line is just the plain greeting instruction wrapped in this marker
                # (see BuildAutoTaskBriefingPrompt in VrmeAtticClient.cs). It still goes
                # through the normal LLM path below — only the source tag changes, so
                # Unity's playback-hold check (source == "auto_briefing") actually gates it.
                response_source = "auto_briefing"
                user_text = re.sub(
                    r"\[SYSTEM_OPENING_GREETING\]\s*|\s*\[/SYSTEM_OPENING_GREETING\]",
                    "",
                    user_text,
                ).strip()
            else:
                response_source = "reply"
            log(f"User: {user_text}")
            try:
                llm_start_at = time.time()
                if tutorial_exit_help_reply:
                    reply = tutorial_exit_help_reply
                    log(f"[TUTORIAL_EXIT_HELP] Exact exit guidance used. reply={reply}")
                elif tutorial_control_result:
                    reply = tutorial_control_result["reply"]
                    log(f"[TUTORIAL_CONTROL] Exact controlled reply used. action={tutorial_control_result.get('action', '')}, reply={reply}")
                elif "[SYSTEM_" not in user_text and is_short_grounding_turn(user_text):
                    reply = build_current_turn_grounded_reply(
                        scene_context,
                        client_metadata.get("avatarCondition"),
                        user_text,
                    )
                    log(f"[GROUNDING_FAST_PATH] Deterministic current-turn reply used. reply={reply}")
                else:
                    reply = await asyncio.wait_for(
                        generate_reply(
                            groq_client,
                            openai_client,
                            user_text,
                            scene_prompt,
                            scene_context,
                            client_metadata.get("avatarCondition"),
                            conversation_memory.get(conversation_key(client_metadata), []),
                        ),
                        timeout=LLM_REPLY_TIMEOUT_SECONDS,
                    )
                llm_seconds = time.time() - llm_start_at
            except asyncio.TimeoutError:
                reply = build_current_turn_grounded_reply(
                    scene_context,
                    client_metadata.get("avatarCondition"),
                )
                log(f"[LLM_TIMEOUT] Reply exceeded {LLM_REPLY_TIMEOUT_SECONDS:.1f}s; deterministic current-turn reply used. reply={reply}")
                llm_seconds = time.time() - llm_start_at
            except Exception as exc:
                log(f"LLM failed: {exc}")
                reply = build_context_grounded_fallback_reply(
                    user_text,
                    scene_context,
                    client_metadata.get("sceneName", ""),
                    client_metadata.get("avatarCondition"),
                )
                log(f"[LLM] Context-grounded fallback reply used. reply={reply}")
                llm_seconds = time.time() - llm_start_at if "llm_start_at" in locals() else 0.0

            reply = sanitize_reply_against_current_held(
                reply,
                scene_context,
                client_metadata.get("avatarCondition"),
            )
            reply = normalize_avatar_self_reference(reply)
            append_conversation_log(user_text, reply, CURRENT_MODE, client_metadata, scene_context)
            remember_conversation_turn(client_metadata, user_text, reply)
            log(f"Reply: {reply}")
            turn_tone = backend_selected_tone(client_metadata.get("avatarCondition"))

            if SEND_TEST_WAV_FIRST:
                log("DEBUG_SEND_STEP_1: generating immediate test wav...")
                test_wav = generate_test_wav()
                log(f"DEBUG_SEND_STEP_2: generated immediate test wav bytes={len(test_wav)}")
                log("DEBUG_SEND_STEP_3: sending immediate test wav to Unity...")
                if not await safe_send_bytes(websocket, test_wav, "immediate_test_wav"):
                    break
                log(f"DEBUG_SEND_STEP_4: sent immediate test wav bytes={len(test_wav)}")

            if TTS_PROVIDER in ("gemini", "auto") and GEMINI_TTS_ENABLED:
                try:
                    tts_start_at = time.time()
                    wav_data = await asyncio.get_event_loop().run_in_executor(
                        None,
                        generate_gemini_wav,
                        reply,
                        turn_tone["name"],
                    )
                    tts_seconds = time.time() - tts_start_at
                    if not await safe_send_bytes(websocket, wav_data, "gemini_wav"):
                        break
                    log(f"[TTS] Sent Gemini wav successfully. bytes={len(wav_data)}")
                    log(
                        "[LATENCY] "
                        f"total={time.time() - request_start_at:.3f}s, first_audio={time.time() - request_start_at:.3f}s, "
                        f"stt={stt_seconds:.3f}s, llm={llm_seconds:.3f}s, "
                        f"tts_total={tts_seconds:.3f}s, tts_model={GEMINI_TTS_MODEL}, "
                        f"tts_provider=gemini, tts_bytes={len(wav_data)}"
                    )
                    append_latency_event(client_metadata, {
                        "total_seconds": time.time() - request_start_at,
                        "first_audio_seconds": time.time() - request_start_at,
                        "stt_seconds": stt_seconds,
                        "llm_seconds": llm_seconds,
                        "tts_total_seconds": tts_seconds,
                        "tts_first_seconds": None,
                        "tts_model": GEMINI_TTS_MODEL,
                        "tts_provider": "gemini",
                        "tts_bytes": len(wav_data),
                        "streaming": False,
                    })
                    continue
                except Exception as exc:
                    log(f"Gemini TTS failed; ElevenLabs fallback disabled for Gemini-only testing. {exc}")
                    if TTS_PROVIDER == "gemini":
                        fallback_payload = json.dumps({"type": "text", "text": reply}, ensure_ascii=False)
                        if not await safe_send_text(websocket, fallback_payload, "text_fallback_gemini_failed"):
                            break
                        continue

            if ELEVENLABS_ENABLED:
                try:
                    if stream_reply_audio and ELEVENLABS_STREAMING_ENABLED:
                        stream_result = await stream_elevenlabs_pcm(websocket, reply, turn_tone, response_source)
                        if stream_result.get("ok"):
                            total_seconds = time.time() - request_start_at
                            tts_first_seconds = stream_result.get("first_audio_seconds") or 0.0
                            first_audio_total = stt_seconds + llm_seconds + tts_first_seconds
                            log(
                                "[LATENCY] "
                                f"total={total_seconds:.3f}s, first_audio={first_audio_total:.3f}s, "
                                f"stt={stt_seconds:.3f}s, llm={llm_seconds:.3f}s, "
                                f"tts_first={tts_first_seconds:.3f}s, "
                                f"tts_total={stream_result.get('tts_seconds'):.3f}s, "
                                f"tts_model={stream_result.get('model')}, tts_bytes={stream_result.get('bytes')}"
                            )
                            append_latency_event(client_metadata, {
                                "total_seconds": total_seconds,
                                "first_audio_seconds": first_audio_total,
                                "stt_seconds": stt_seconds,
                                "llm_seconds": llm_seconds,
                                "tts_first_seconds": tts_first_seconds,
                                "tts_total_seconds": stream_result.get("tts_seconds"),
                                "tts_model": stream_result.get("model"),
                                "tts_provider": "elevenlabs",
                                "tts_bytes": stream_result.get("bytes"),
                                "streaming": True,
                            })
                            continue

                    tts_start_at = time.time()
                    wav_data = await asyncio.get_event_loop().run_in_executor(
                        None,
                        generate_elevenlabs_wav,
                        reply,
                        turn_tone,
                    )
                    tts_seconds = time.time() - tts_start_at
                    log(f"[TTS] Generated ElevenLabs wav successfully. bytes={len(wav_data)}")
                    if not await safe_send_bytes(websocket, wav_data, "elevenlabs_wav"):
                        break
                    log(f"[TTS] Sent ElevenLabs wav successfully. bytes={len(wav_data)}")
                    log(
                        "[LATENCY] "
                        f"total={time.time() - request_start_at:.3f}s, first_audio={time.time() - request_start_at:.3f}s, "
                        f"stt={stt_seconds:.3f}s, llm={llm_seconds:.3f}s, "
                        f"tts_total={tts_seconds:.3f}s, tts_model=batch_wav, tts_bytes={len(wav_data)}"
                    )
                    append_latency_event(client_metadata, {
                        "total_seconds": time.time() - request_start_at,
                        "first_audio_seconds": time.time() - request_start_at,
                        "stt_seconds": stt_seconds,
                        "llm_seconds": llm_seconds,
                        "tts_first_seconds": None,
                        "tts_total_seconds": tts_seconds,
                        "tts_model": "batch_wav",
                        "tts_provider": "elevenlabs",
                        "tts_bytes": len(wav_data),
                        "streaming": False,
                    })
                    continue
                except Exception as exc:
                    log(f"ElevenLabs TTS failed: {exc}")
                    if WINDOWS_TTS_FALLBACK:
                        try:
                            fallback_wav = await asyncio.get_event_loop().run_in_executor(
                                None,
                                generate_windows_tts_wav,
                                reply,
                            )
                            if not await safe_send_bytes(websocket, fallback_wav, "windows_tts_fallback_wav"):
                                break
                            log(f"[TTS] Sent Windows fallback wav successfully. bytes={len(fallback_wav)}")
                            continue
                        except Exception as fallback_exc:
                            log(f"Windows fallback TTS failed: {fallback_exc}")

                    fallback_payload = json.dumps({"type": "text", "text": reply}, ensure_ascii=False)
                    if not await safe_send_text(websocket, fallback_payload, "text_fallback_no_audio"):
                        break
                    log("[SEND] No audio fallback was sent. Waiting for real TTS audio only.")
                    continue

            if not VIBEVOICE_ENABLED:
                log("ElevenLabs and VibeVoice disabled for frontend audio test.")
                continue

            if WINDOWS_TTS_FIRST:
                try:
                    windows_wav = await asyncio.get_event_loop().run_in_executor(
                        None,
                        generate_windows_tts_wav,
                        reply,
                    )
                    if not await safe_send_bytes(websocket, windows_wav, "windows_tts_first_wav"):
                        break
                    log(f"[TTS] Sent Windows-first wav successfully. bytes={len(windows_wav)}")
                    continue
                except Exception as windows_exc:
                    log(f"Windows-first TTS failed, falling back to VibeVoice: {windows_exc}")

            try:
                wav_data = await asyncio.get_event_loop().run_in_executor(
                    None,
                    generate_vibevoice_wav,
                    reply,
                )
                log(f"[TTS] Generated VibeVoice wav successfully. bytes={len(wav_data)}")
                if not await safe_send_bytes(websocket, wav_data, "vibevoice_wav"):
                    break
                log(f"[TTS] Sent VibeVoice wav successfully. bytes={len(wav_data)}")
            except Exception as exc:
                log(f"VibeVoice TTS failed: {exc}")
                if WINDOWS_TTS_FALLBACK:
                    try:
                        fallback_wav = await asyncio.get_event_loop().run_in_executor(
                            None,
                            generate_windows_tts_wav,
                            reply,
                        )
                        if not await safe_send_bytes(websocket, fallback_wav, "windows_tts_fallback_wav"):
                            break
                        log(f"[TTS] Sent Windows fallback wav successfully. bytes={len(fallback_wav)}")
                        continue
                    except Exception as fallback_exc:
                        log(f"Windows fallback TTS failed: {fallback_exc}")

                fallback_payload = json.dumps({"type": "text", "text": reply}, ensure_ascii=False)
                if not await safe_send_text(websocket, fallback_payload, "text_fallback_no_audio"):
                    break
                log("[SEND] No beep fallback was sent. Waiting for real TTS audio only.")

    except WebSocketDisconnect:
        log("Unity client disconnected.")
    finally:
        if proactive_guide_task is not None and not proactive_guide_task.done():
            proactive_guide_task.cancel()


if __name__ == "__main__":
    try:
        require_environment()
    except Exception as exc:
        log(str(exc))
        sys.exit(1)

    try:
        release_existing_server_port(PORT)
    except Exception as exc:
        log(str(exc))
        sys.exit(1)

    log(f"Starting Unity VibeVoice server on {HOST}:{PORT}")
    log(f"SERVER_BUILD_TAG: {SERVER_BUILD_TAG}")
    log(f"SERVER_FILE: {Path(__file__).resolve()}")
    log(f"PROACTIVE_GUIDE enabled={PROACTIVE_GUIDE_ENABLED}, delay={PROACTIVE_GUIDE_DELAY_SECONDS}, scenes={sorted(PROACTIVE_GUIDE_SCENES)}")
    log(f"ElevenLabs enabled: {ELEVENLABS_ENABLED}")
    log(f"ElevenLabs voice id: {ELEVENLABS_VOICE_ID}")
    log(f"ElevenLabs model: {ELEVENLABS_MODEL_ID}")
    log(f"ElevenLabs output format: {ELEVENLABS_OUTPUT_FORMAT}")
    log(
        "ElevenLabs tone preset: "
        f"{ELEVENLABS_TONE['name']} "
        f"(stability={ELEVENLABS_STABILITY}, similarity={ELEVENLABS_SIMILARITY_BOOST}, "
        f"style={ELEVENLABS_STYLE}, speed={ELEVENLABS_SPEED}, manual={ELEVENLABS_MANUAL_VOICE_SETTINGS})"
    )
    log(f"VibeVoice repo: {VIBEVOICE_REPO}")
    log(f"VibeVoice speaker: {VIBEVOICE_SPEAKER}")
    log(f"VibeVoice enabled: {VIBEVOICE_ENABLED}")
    log(f"VibeVoice preload: {VIBEVOICE_PRELOAD}")
    log(f"VibeVoice use worker: {VIBEVOICE_USE_WORKER}")
    log(f"Windows TTS first: {WINDOWS_TTS_FIRST}")
    log(f"Send test wav first: {SEND_TEST_WAV_FIRST}")
    log(f"Windows TTS fallback: {WINDOWS_TTS_FALLBACK}")
    uvicorn.run(app, host=HOST, port=PORT)
