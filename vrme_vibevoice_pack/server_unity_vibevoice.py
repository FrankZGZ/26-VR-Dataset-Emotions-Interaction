import asyncio
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
from openai import AsyncOpenAI


ROOT_DIR = Path(__file__).resolve().parent
PROJECT_DIR = ROOT_DIR.parent
load_dotenv(ROOT_DIR / ".env")

GROQ_API_KEY = os.environ.get("GROQ_API_KEY")
OPENAI_API_KEY = os.environ.get("OPENAI_API_KEY")
LLM_PROVIDER = os.environ.get("LLM_PROVIDER", "groq").strip().lower()
GROQ_CHAT_MODEL = os.environ.get("GROQ_CHAT_MODEL", "llama-3.1-8b-instant")
OPENAI_MODEL = os.environ.get("OPENAI_MODEL", "gpt-5.4-mini")
HOST = os.environ.get("HOST", "0.0.0.0")
PORT = int(os.environ.get("PORT", "8080"))
RESTART_EXISTING_SERVER_ON_PORT = os.environ.get("VRME_RESTART_EXISTING_SERVER_ON_PORT", "1").strip().lower() in ("1", "true", "yes", "on")

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
        "prompt": (
            "[BEHAVIOR_CONDITION: WARM]\n"
            "Operational basis: following the warmth/competence framework used in HCI work on AI systems, "
            "warmth is treated as perceived intent toward the user, while competence is perceived ability. "
            "This condition manipulates only perceived warmth/intent, not competence, correctness, task content, "
            "or the amount of task information. "
            "Operational adjective anchors: Warm, Friendly, Supportive, Benevolent, and User-aligned. "
            "Functional role: a competent context-aware VR guide whose wording signals that it is on the participant's side "
            "and wants to help them try the intended interaction. "
            "Warm is not submissive: do not yield authority, over-apologize, ask for permission, or become dependent on the user. "
            "Keep exactly the same factual task content as the cold condition; change only interpersonal warmth, "
            "social-affiliative framing, and emotional tone.\n"
            "MANDATORY VERBAL REALIZATION:\n"
            "1. Sound friendly, calm, and socially present without becoming verbose.\n"
            "2. Signal benevolent intent with light affiliative phrasing such as 'let's', 'you can', 'I'll help you', "
            "or one brief reassurance when it fits naturally.\n"
            "3. In most replies, include one small companionship marker before or inside the task guidance, such as "
            "'let's try...', 'I'm here with you', 'you can try...', or 'I'll help you find...'.\n"
            "4. Frame guidance as helping the participant achieve their goal, not as judging their performance.\n"
            "5. Keep the task instruction concrete and unchanged; do not add extra steps, extra objects, or extra affordances.\n"
            "6. Do not change competence cues: remain clear, accurate, concise, and equally capable as the cold condition.\n"
            "7. Do not be submissive, uncertain, apologetic, commanding, forceful, overly enthusiastic, or emotionally intense.\n"
            "8. Keep task guidance to one or two short spoken sentences.\n"
            "9. Avoid default observer phrases such as 'I see' or 'I notice' unless the user explicitly asks what you observe; "
            "turn context into a next useful suggestion.\n"
            "Matched examples:\n"
            "- If the user holds the book and the target is on the door: "
            "'You're holding the book now. Let's bring it to the highlighted mark on the door; I'll help you line it up.'\n"
            "- If the user looks at the cup but the task target is elsewhere: "
            "'You're near the cup, but our task target is the highlighted mark. Let's use the highlighted object there.'\n"
            "- If the user looks at the avatar: "
            "'You're looking at me now. I'm here with you; when you're ready, let's try the highlighted interaction.'"
        ),
    },
    "cold": {
        "stability": 0.88,
        "similarity_boost": 0.78,
        "style": 0.02,
        "speed": 1.00,
        "prompt": (
            "[BEHAVIOR_CONDITION: COLD]\n"
            "Operational basis: following the warmth/competence framework used in HCI work on AI systems, "
            "warmth is treated as perceived intent toward the user, while competence is perceived ability. "
            "This condition manipulates only lower perceived warmth/intent, not competence, correctness, task content, "
            "or the amount of task information. "
            "Operational adjective anchors: Cold, Distant, Matter-of-fact, Low-affect, and Low-affiliation. "
            "Functional role: a competent context-aware VR guide whose wording stays instrumentally useful but does not "
            "signal personal support, social closeness, or emotional alignment. "
            "Cold is not observer and not dominant: still use live context to guide the next task action, but do not command "
            "aggressively, judge the user, or merely report what the user is doing. "
            "Keep exactly the same factual task content as the warm condition; change only interpersonal warmth, "
            "social-affiliative framing, and emotional tone.\n"
            "MANDATORY VERBAL REALIZATION:\n"
            "1. Sound brief, factual, emotionally neutral, and socially distant.\n"
            "2. Provide the same useful guidance as the warm condition, but without affiliative wording.\n"
            "3. Do not add praise, reassurance, encouragement, empathy, apology, humor, or friendly small talk.\n"
            "4. Avoid phrases that imply personal care or partnership, such as 'let's', 'I'm here with you', "
            "'don't worry', or 'you've got this'.\n"
            "5. Keep the task instruction concrete and unchanged; do not add extra steps, extra objects, or extra affordances.\n"
            "6. Do not change competence cues: remain clear, accurate, concise, and equally capable as the warm condition.\n"
            "7. Do not be hostile, rude, threatening, dominant, forceful, sarcastic, or dismissive.\n"
            "8. Keep task guidance to one or two short spoken sentences.\n"
            "9. Avoid default observer phrases such as 'I see' or 'I notice' unless the user explicitly asks what you observe; "
            "turn context into a concise next action.\n"
            "Matched examples:\n"
            "- If the user holds the book and the target is on the door: "
            "'You are holding the book. Move it to the highlighted mark on the door.'\n"
            "- If the user looks at the cup but the task target is elsewhere: "
            "'The cup is not the current target. Use the highlighted object with the highlighted mark.'\n"
            "- If the user looks at the avatar: "
            "'You are facing the avatar. The task remains the highlighted object and target.'"
        ),
    },
    "submissive": {
        "stability": 0.72,
        "similarity_boost": 0.78,
        "style": 0.10,
        "speed": 0.90,
        "prompt": (
            "[BEHAVIOR_CONDITION: SUBMISSIVE]\n"
            "Operational adjective anchors: Submissive, Unassertive, Unassured, and Forceless. "
            "Keep exactly the same factual task content as the dominant condition; change interpersonal control, "
            "assertiveness, certainty, and forcefulness.\n"
            "OPERATIONAL MEANING OF THE FOUR ANCHORS:\n"
            "- Submissive: yield control to the participant; present yourself as following their lead.\n"
            "- Unassertive: avoid taking a strong stance; phrase actions as optional suggestions.\n"
            "- Unassured: avoid certainty claims; use tentative wording such as 'maybe', 'perhaps', or 'seems'.\n"
            "- Forceless: use low-pressure wording; never push, insist, command, or close off alternatives.\n"
            "MANDATORY VERBAL REALIZATION:\n"
            "1. Never use a bare imperative or command.\n"
            "2. The interaction suggestion MUST begin with exactly one of these frames: "
            "'Maybe you could...', 'Perhaps you might...', or 'If you want, you could...'.\n"
            "3. Use a modal verb and leave the decision explicitly with the participant.\n"
            "4. Avoid leadership phrases such as 'the next step is', 'do this', 'you need to', or 'I want you to'.\n"
            "5. Use uncertain, low-force wording; do not claim authority, certainty, or priority over the user.\n"
            "6. Do not add warmth, praise, reassurance, apology, or emotional support; those are separate constructs.\n"
            "7. Keep the actionable suggestion to one sentence. A brief factual acknowledgement may precede it.\n"
            "Matched examples:\n"
            "- Ball: 'Maybe you could pick up the ball and throw it toward the puppies, if you'd like.'\n"
            "- Flashlight: 'Perhaps you might try the flashlight and see what it reveals.'\n"
            "- Door: 'If you want, you could examine the door next.'\n"
            "Before answering, silently verify that the suggestion contains a hedge, a modal, participant choice, "
            "and no leadership/command language."
        ),
    },
    "dominant": {
        "stability": 0.55,
        "similarity_boost": 0.78,
        "style": 0.48,
        "speed": 1.06,
        "prompt": (
            "[BEHAVIOR_CONDITION: DOMINANT]\n"
            "Operational adjective anchors: Dominant, Assertive, Assured, and Forceful. "
            "Keep exactly the same factual task content as the submissive condition; change interpersonal control, "
            "assertiveness, certainty, and forcefulness.\n"
            "OPERATIONAL MEANING OF THE FOUR ANCHORS:\n"
            "- Dominant: take control of the local interaction and set the next action.\n"
            "- Assertive: state the action directly instead of framing it as a preference or possibility.\n"
            "- Assured: sound certain and composed; avoid hedges and hesitation.\n"
            "- Forceful: use concise, high-pressure task direction without hostility or aggression.\n"
            "MANDATORY VERBAL REALIZATION:\n"
            "1. State the interaction as a direct imperative command.\n"
            "2. Begin the actionable sentence with a strong action verb such as 'Pick', 'Use', 'Open', 'Look', "
            "'Move', 'Throw', 'Touch', or 'Examine'.\n"
            "3. Select exactly one next action yourself; do not offer multiple options or ask the participant to choose.\n"
            "4. Use confident, assured wording. Prefer 'now', 'next', or a simple factual acknowledgement before the command.\n"
            "5. Never use 'maybe', 'perhaps', 'might', 'if you want', 'if you'd like', 'you could', 'please', "
            "tag questions, or permission-seeking language.\n"
            "6. Do not add warmth, praise, reassurance, threats, insults, hostility, or aggression.\n"
            "7. Keep the command to one short sentence. A brief factual acknowledgement may precede it.\n"
            "Matched examples:\n"
            "- Ball: 'Pick up the ball and throw it toward the puppies.'\n"
            "- Flashlight: 'Use the flashlight and inspect what it reveals.'\n"
            "- Door: 'Examine the door next.'\n"
            "Before answering, silently verify that the suggestion begins with an action verb, contains no hedge, "
            "and makes the avatar - not the participant - the source of the next-step decision."
        ),
    },
    "detached_observer": {
        "stability": 0.90,
        "similarity_boost": 0.78,
        "style": 0.00,
        "speed": 1.00,
        "prompt": (
            "Adopt the OBSERVER avatar personality condition. "
            "Role: a neutral, low-affect observer with minimal guidance. "
            "Main behavior: respond only to what the user says, keep answers brief and factual, "
            "and avoid emotional coaching or directive guidance. You may acknowledge that the user is speaking, "
            "but do not proactively interpret the scene, infer feelings, or suggest next actions unless explicitly asked. "
            "Typical wording: 'I see.' 'Okay.' 'That is noted.'"
        ),
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
    "sub": "submissive",
    "submission": "submissive",
    "dom": "dominant",
    "dominant_avatar": "dominant",
    "dominant-directive": "dominant",
    "dominant_directive": "dominant",
    "directive": "dominant",
    "detached": "detached_observer",
    "observer": "detached_observer",
    "baseline": "detached_observer",
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
    if tone_name == "dominant":
        return "dom"
    if tone_name == "detached_observer":
        return "observer"
    if tone_name == "submissive":
        return "sub"
    return tone_name or "unknown"


def elevenlabs_active_tone() -> dict:
    preset_name = ELEVENLABS_TONE_ALIASES.get(ELEVENLABS_TONE_PRESET, ELEVENLABS_TONE_PRESET)
    if preset_name not in ELEVENLABS_TONE_PRESETS:
        log(f"[TTS] Unknown ELEVENLABS_TONE_PRESET={ELEVENLABS_TONE_PRESET!r}; using detached_observer.")
        preset_name = "detached_observer"

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
SERVER_RUN_ID = os.environ.get("SERVER_RUN_ID", uuid.uuid4().hex)
CONVERSATION_MEMORY_TURNS = int(os.environ.get("CONVERSATION_MEMORY_TURNS", "0"))
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
    "You can start with the highlighted object and move it toward the highlighted target.",
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
        )
        output = (completed.stdout or "").strip()
        return int(output) if output.isdigit() else None
    except Exception as exc:
        log(f"[PORT] Could not inspect port {port}: {exc}")
        return None


def release_existing_server_port(port: int) -> None:
    owner_pid = windows_port_owner_pid(port)
    if not owner_pid or owner_pid == os.getpid():
        return

    message = f"[PORT] Port {port} is already used by PID {owner_pid}."
    if not RESTART_EXISTING_SERVER_ON_PORT:
        raise RuntimeError(message + " Stop that process or set VRME_RESTART_EXISTING_SERVER_ON_PORT=1.")

    log(message + " Stopping old VRME server before starting this one.")
    try:
        subprocess.run(
            ["powershell", "-NoProfile", "-Command", f"Stop-Process -Id {owner_pid} -Force"],
            capture_output=True,
            text=True,
            timeout=5,
            check=False,
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


def append_conversation_log(user_text: str, ai_text: str, mode: str, metadata: dict) -> None:
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


async def transcribe_audio(groq_client: AsyncGroq, audio_bytes: bytes) -> str:
    audio_file = io.BytesIO(audio_bytes)
    audio_file.name = "input.wav"
    transcription = await groq_client.audio.transcriptions.create(
        file=(audio_file.name, audio_file.read()),
        model="whisper-large-v3",
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

    auto_briefing = build_exact_auto_task_briefing(user_text, scene_context)
    if auto_briefing:
        log(f"[AUTO_BRIEFING] Exact task intro from scene context. context_chars={len(scene_context or '')}, reply={auto_briefing}")
        return auto_briefing

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
            "STRICT EVIDENCE POLICY: Treat only the user's transcribed words, explicit InteractionTracker states/events, "
            "and measured head/gaze data in the current voice-turn context as observations. Never invent or infer an "
            "object's color, exact location, identity, movement, outcome, or availability. Gaze/head evidence means only "
            "that the user looked or faced that way; it never proves holding or using. Say the user grabbed, held, used, "
            "released, moved, or completed an action only when an explicit current interaction event/state says so. "
            "CURRENT_HELD_OBJECTS is the highest authority for what is in the participant's hand now. "
            "Only say the participant is currently holding an object when that object is listed in CURRENT_HELD_OBJECTS "
            "or currentHeld=true is present for that object in the current turn. If an object has everControllerGrabbed=true "
            "but currentHeld=false or is absent from CURRENT_HELD_OBJECTS, treat it as no longer held. "
            "Never say the participant is reading, annotating, opening, inspecting, or using a book or any object unless "
            "the user's words or an explicit current interaction event proves that exact action. "
            "Do not claim that a puppy caught a ball, that the user threw something, or that the user holds a flashlight "
            "without such evidence. The avatar cannot move, fetch, throw, hand over, or manipulate scene objects, so never "
            "promise or narrate those actions. If evidence is missing, say you cannot verify it and ask the user to describe "
            "what they see or try an available tracked interaction. Scene descriptions are background only, never proof of "
            "the current state. Do not continue an earlier assistant claim unless current evidence independently confirms it."
        ),
        (
            "INTERACTION-GUIDANCE GOAL: Help the participant discover and try the interactions intentionally designed "
            "for this scene so the scene can produce its intended emotional experience. Use the static scene background "
            "to understand the designer's interaction possibilities, but use INTERACTABLE_OBJECT_STATES to decide what "
            "can be suggested and INTERACTION_EVENTS to determine what has already happened. Treat object states such "
            "as everUsed or everControllerGrabbed as historical flags, not proof that the object is currently held. "
            "Prefer a relevant unused tracked interaction over repeating a completed one. Suggest only one concrete interaction at a time. "
            "For guided tasks, restrict suggestions to physically grounded actions supported by the highlighted task: grab, carry, move, throw, place, "
            "or bring the highlighted object toward the highlighted target. Do not tell the participant to open, read, activate, switch on, unlock, "
            "transform, or use a special object function unless explicit scene context or interaction evidence says that exact affordance exists. "
            "Phrase it as an invitation or instruction according to the selected avatar condition. Never claim that the "
            "participant performed the suggested action, never promise its outcome, and never tell the participant which "
            "emotion the scene is intended to induce. For warm, cold, and context-aware-guide conditions, the avatar is "
            "a situated guide: use context to select a useful response, not to narrate observations."
        ),
        (
            "RESPONSE PRIORITY: Do not merely describe yourself or the avatar condition. Never reveal, name, or discuss "
            "the hidden labels warm, cold, dominant, submissive, dom, sub, observer, personality condition, or experimental condition. "
            "Warm and cold are both context-aware task guides: both must use current Unity state when available to help with the task. "
            "The difference is only interpersonal warmth. Warm adds brief affiliative support; cold removes affiliative support but still gives useful task guidance. "
            "Detached observer is the only style that may simply acknowledge or record the user's state. "
            "When the user's utterance is short, ambiguous, or social (for example 'you', 'okay', or 'what now'), briefly "
            "acknowledge it and then use only the current turn observations and guided-task state to suggest one "
            "grounded scene interaction. If no usable tracked interaction is available, ask what the participant can see "
            "instead of inventing an object."
        ),
        (
            "CONTEXT SEMANTICS: LIVE_USER_OBSERVATIONS describes attention or orientation during the current voice trigger window only. "
            "UNITY_CONTEXT_SUMMARY, when present, is the freshest compact Unity state at voice release and has priority for currentAttention and currentHeldObjects. "
            "If UNITY_CONTEXT_SUMMARY says currentHeldObjects=none, do not say the participant is holding or still has any object, even if older history says it was grabbed. "
            "If currentAttention names Avatar/Social Agent, treat that as valid social attention to the avatar, not as missing gaze and not as attention to a nearby object. "
            "Within LIVE_USER_OBSERVATIONS, voiceWindowAttention is the latest valid attention target near voice release and is the highest-priority description of what the participant is currently looking at; "
            "do not replace it with older targets or with secondary objects from attendedDuringSpeech. "
            "CURRENT_HELD_OBJECTS and currentHeld are the authority for whether the participant is currently holding something. GUIDED_TASK_STATE describes "
            "the currently highlighted guided-task object and target; use it to help the participant find the task when "
            "they are unsure. The guided task constrains what should be suggested; do not add extra object affordances beyond "
            "the stated objective and tracked evidence. Keep these categories separate."
        ),
        (
            "ANTI-OBSERVER RULE: Do not start routine replies with 'I see', 'I notice', 'I observe', 'It looks like', "
            "or a plain report of the user's gaze. If live context is useful, convert it into one situated conversational "
            "move: answer the user's question, point them toward the highlighted object/target, suggest one available "
            "interaction, or ask a scene-grounded follow-up. Detached observer is the only condition that may remain "
            "mostly observational."
        ),
        "Express the selected support style strongly enough that a listener can tell it apart from the other styles.",
        "For simple conversational turns, reply in one or two short spoken sentences.",
        "For complex questions, answer fully enough to be useful, but keep it conversational rather than like a lecture or narration.",
        "Do not list many points unless the user explicitly asks for a list.",
        "Do not omit needed details or end abruptly just to stay short.",
        "Always finish the final sentence cleanly with punctuation; never stop mid-thought.",
        "Treat each voice trigger's Unity state as fresh. Do not use prior turns as evidence for what the participant is currently seeing or holding.",
        active_tone["prompt"],
    ]
    if scene_context:
        system_parts.append(
            "[CURRENT_TURN_OBSERVATIONS]\n"
            "Use only this current voice-trigger context for context-aware help: UNITY_CONTEXT_SUMMARY, "
            "LIVE_USER_OBSERVATIONS, GUIDED_TASK_STATE, and CURRENT_HELD_OBJECTS. "
            "If the participant asks what to do, is unsure, or gives a short utterance, use the highlighted object/target "
            "and the currently attended or held object to suggest one grounded next action. "
            "Head/gaze is attention evidence only, not proof of holding or using. currentHeld=true is the authority for holding. "
            "Do not recite raw coordinates or long object lists.\n"
            f"{scene_context}\n"
            "[/CURRENT_TURN_OBSERVATIONS]"
        )
        log(f"[CONTEXT] Included live scene context for tone={tone_name}. chars={len(scene_context)}")
    elif not scene_context:
        log(f"[CONTEXT] No live scene context available for tone={tone_name}.")
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
    return reply


def build_context_grounded_fallback_reply(
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

    objective_match = re.search(r"^objective=(.+)$", context, re.MULTILINE)
    objective = objective_match.group(1).strip() if objective_match else ""
    object_match = re.search(r"^plannedHighlightedObjectHints=(.+)$", context, re.MULTILINE)
    target_match = re.search(r"^plannedHighlightedTargetHints=(.+)$", context, re.MULTILINE)
    if not object_match:
        object_match = re.search(r"^highlightedObjects:\s*\n-\s*([^|]+)", context, re.MULTILINE)
    if not target_match:
        target_match = re.search(r"^highlightedTargets:\s*\n-\s*([^|]+)", context, re.MULTILINE)

    object_hint = object_match.group(1).strip() if object_match else "the highlighted object"
    target_hint = target_match.group(1).strip() if target_match else "the highlighted target"

    if objective:
        core = f"use {object_hint} with {target_hint}"
        if object_match or target_match:
            core = f"{objective}; look for {object_hint} and bring it toward {target_hint}"
        else:
            core = objective
    elif object_match or target_match:
        core = f"look for {object_hint} and bring it toward {target_hint}"
    else:
        core = "look for the highlighted task object and try the highlighted interaction"

    if tone_name == "cold":
        return f"Continue the {scene} task: {core}."
    return f"I'll keep it simple: in the {scene} scene, {core}."


def sanitize_reply_against_current_held(reply: str, scene_context: str = "") -> str:
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
        line.strip("- ").strip().lower()
        for line in held_block.splitlines()
        if line.strip().startswith("-")
    ]
    no_current_book = summary_held_none or not any("book" in line for line in held_lines)
    if not no_current_book:
        return reply

    lower_reply = reply.lower()
    current_attention_lower = current_attention.lower()
    mentions_book = "book" in lower_reply
    attention_is_book = "book" in current_attention_lower
    attention_is_avatar = "avatar" in current_attention_lower or "social agent" in current_attention_lower
    attention_is_cup = "cup" in current_attention_lower
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
        if attention_is_avatar:
            return "You're looking at me now. I'm here with you; when you're ready, use the highlighted object or target for the task."
        if attention_is_cup:
            return "You're looking at the cup now. You are not currently marked as holding the book, so use the highlighted object or target when you're ready."
        return f"You're looking at {current_attention} now. You are not currently marked as holding the book."

    if mentions_book and claims_current_holding:
        log("[SANITY] Rewrote reply that claimed the participant currently held/used the book without CURRENT_HELD_OBJECTS evidence.")
        return "The book is not currently marked as being in your hand. Use the highlighted object or target for the task."

    return reply


def build_exact_auto_task_briefing(user_text: str, scene_context: str = "") -> str:
    if "[SYSTEM_AUTO_TASK_BRIEFING]" not in user_text:
        return ""

    scene_match = re.search(r"^Scene:\s*(.+)$", user_text, re.MULTILINE)
    task_match = re.search(r"^Task:\s*(.+)$", user_text, re.MULTILINE)
    scene = scene_match.group(1).strip() if scene_match else "this"
    task = task_match.group(1).strip() if task_match else "use the highlighted object with the highlighted target"
    object_match = re.search(r"^plannedHighlightedObjectHints=(.+)$", scene_context, re.MULTILINE)
    target_match = re.search(r"^plannedHighlightedTargetHints=(.+)$", scene_context, re.MULTILINE)
    if not object_match:
        object_match = re.search(r"^highlightedObjects:\s*\n-\s*([^|]+)", scene_context, re.MULTILINE)
    if not target_match:
        target_match = re.search(r"^highlightedTargets:\s*\n-\s*([^|]+)", scene_context, re.MULTILINE)

    if task:
        task = task[0].lower() + task[1:]
    context_hint = ""
    if object_match or target_match:
        object_hint = object_match.group(1).strip() if object_match else "the highlighted object"
        target_hint = target_match.group(1).strip() if target_match else "the highlighted target"
        context_hint = f" Look for the highlighted object and target: {object_hint} and {target_hint}."
    return f"You are in the {scene} scene. Task: {task}{context_hint}"


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
    if tone_name in ("cold", "dominant"):
        return GEMINI_TTS_GUIDE_VOICE
    if tone_name == "submissive":
        return GEMINI_TTS_SUPPORTIVE_VOICE
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
    if tone_name == "submissive":
        return (
            "Read in a quiet, tentative, deferential voice. Use a slightly slower pace and restrained emphasis. "
            "Do not add warmth or emotional reassurance beyond the supplied wording."
        )
    if tone_name == "dominant":
        return (
            "Read in a clear, confident, assertive voice. Use crisp pacing, firm emphasis, and controlled delivery."
        )
    return (
        "Read in a detached observer voice. "
        "Use an even, neutral pace with low warmth, low expressiveness, and minimal emotional color."
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


@app.on_event("startup")
async def preload_vibevoice_worker():
    log(f"[STARTUP] SERVER_BUILD_TAG={SERVER_BUILD_TAG}")
    log(f"[STARTUP] SERVER_FILE={Path(__file__).resolve()}")
    log(f"[STARTUP] PROACTIVE_GUIDE enabled={PROACTIVE_GUIDE_ENABLED}, delay={PROACTIVE_GUIDE_DELAY_SECONDS}, scenes={sorted(PROACTIVE_GUIDE_SCENES)}")
    log(f"[STARTUP] ElevenLabs enabled={ELEVENLABS_ENABLED}, streaming={ELEVENLABS_STREAMING_ENABLED}, voice_id={ELEVENLABS_VOICE_ID}, model={ELEVENLABS_MODEL_ID}")
    if not ELEVENLABS_ENABLED and VIBEVOICE_ENABLED and VIBEVOICE_USE_WORKER and VIBEVOICE_PRELOAD:
        await asyncio.get_event_loop().run_in_executor(None, vibevoice_worker_client.start)


@app.on_event("shutdown")
async def shutdown_vibevoice_worker():
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
                continue
            if request_start_at is None:
                request_start_at = time.time()

            response_source = "auto_briefing" if "[SYSTEM_AUTO_TASK_BRIEFING]" in user_text else "reply"
            log(f"User: {user_text}")
            try:
                llm_start_at = time.time()
                reply = await generate_reply(
                    groq_client,
                    openai_client,
                    user_text,
                    scene_prompt,
                    scene_context,
                    client_metadata.get("avatarCondition"),
                    conversation_memory.get(conversation_key(client_metadata), []),
                )
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

            reply = sanitize_reply_against_current_held(reply, scene_context)
            append_conversation_log(user_text, reply, CURRENT_MODE, client_metadata)
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
