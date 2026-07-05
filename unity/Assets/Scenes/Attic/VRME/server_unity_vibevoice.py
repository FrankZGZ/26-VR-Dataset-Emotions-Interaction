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
from datetime import datetime
from pathlib import Path

import uvicorn
from dotenv import load_dotenv
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from groq import AsyncGroq


ROOT_DIR = Path(__file__).resolve().parent
PROJECT_DIR = ROOT_DIR.parent
load_dotenv(ROOT_DIR / ".env")

GROQ_API_KEY = os.environ.get("GROQ_API_KEY")
HOST = os.environ.get("HOST", "0.0.0.0")
PORT = int(os.environ.get("PORT", "8080"))

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
        "stability": 0.65,
        "similarity_boost": 0.78,
        "style": 0.35,
        "speed": 1.00,
        "prompt": (
            "Adopt the WARM avatar personality condition. "
            "Big Five trait embedding on a 0-4 scale: Extraversion=4, Agreeableness=4, "
            "Conscientiousness=2, Neuroticism=0, Openness=4. "
            "Role: a warm, friendly, emotionally supportive VR avatar companion. "
            "Main behavior: speak gently and reassuringly, validate the user's feelings, "
            "and invite continued exploration without pressure. You may lightly use the user's "
            "current gaze, head direction, and recent interaction state to make the response feel aware, "
            "but do not turn it into detailed navigation or a long instruction. Keep the same functional "
            "help content as other conditions while changing only the interpersonal delivery style. "
            "Use short, natural spoken responses suitable for VR. Do not over-explain. "
            "Do not become directive, controlling, judgmental, or overly clinical."
        ),
    },
    "dominant": {
        "stability": 0.65,
        "similarity_boost": 0.78,
        "style": 0.35,
        "speed": 1.00,
        "prompt": (
            "Adopt the DOMINANT avatar personality condition. "
            "Big Five trait embedding on a 0-4 scale: Extraversion=4, Agreeableness=0, "
            "Conscientiousness=2, Neuroticism=2, Openness=3. "
            "Role: a confident, assertive, task-focused VR avatar assistant. "
            "Main behavior: speak clearly, take the lead, make firm suggestions, and keep emotional "
            "reassurance minimal. Use gaze, head direction, nearby objects, and recent interaction state "
            "to provide concrete, directive guidance when relevant. Keep the same functional help content as other conditions while "
            "changing only the interpersonal delivery style. "
            "Use short, natural spoken responses suitable for VR. Sound competent and decisive, "
            "but do not become rude, hostile, insulting, or aggressive."
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
    "supportive_companion": {
        "stability": 0.25,
        "similarity_boost": 0.78,
        "style": 0.90,
        "speed": 0.86,
        "prompt": (
            "Adopt the SUPPORTIVE COMPANION condition. Role: high warmth, low scene awareness. "
            "Scene-context rule: do not proactively use object positions, interaction events, used states, distances, or navigation cues. Treat the user's speech as the main signal. "
            "Main behavior: empathize with the user's expression, accept and validate what they say, and encourage them gently. If the user talks about an object or action, respond supportively without turning it into task guidance. "
            "Research intent: test whether user speech increases and whether perceived social presence increases. Typical wording: 'That sounds understandable.' 'I am with you.' 'You are doing okay.'"
        ),
    },
    "context_aware_guide": {
        "stability": 0.65,
        "similarity_boost": 0.80,
        "style": 0.25,
        "speed": 1.08,
        "prompt": (
            "Adopt the CONTEXT-AWARE GUIDE condition. Role: high scene awareness and high guidance. "
            "Scene-context rule: actively use player position, tracked object positions, distanceToPlayer, used states, and recent interaction events to infer what is nearby, what has been tried, and what to explore next. "
            "Main behavior: guide exploration with concrete suggestions tied to the current scene. First read the 'High-priority scene cues' section if present, then use the detailed object list only as backup. Prioritize concrete interaction objects over generic body direction: if the live context mentions a flashlight, door, stone, airplane, banana, dog, ball, cup, book, or recent interaction event, refer to that object naturally in the first sentence when it is relevant. Use attentionSource=eye_gaze as gaze evidence; use attentionSource=head_forward_fallback only as a coarse head-direction/attention proxy. Do not merely say where the user is facing if an interaction object is available. Mention relevant objects, direction, or interaction state when useful, but do not recite raw coordinates. "
            "Research intent: test whether interaction count, walking range, exploration depth, and active situational awareness change. Typical wording: 'Look toward...' 'You have not tried...' 'The next useful area is...'"
        ),
    },
}

ELEVENLABS_TONE_ALIASES = {
    "warm_avatar": "warm",
    "warm-companion": "warm",
    "warm_companion": "warm",
    "supportive": "warm",
    "supportive_companion": "warm",
    "companion": "warm",
    "emotional": "warm",
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
    if tone_name == "dominant":
        return "dom"
    if tone_name == "detached_observer":
        return "observer"
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


def backend_selected_tone() -> dict:
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
CONVERSATION_EVENTS_JSONL_PATH = Path(os.environ.get(
    "CONVERSATION_EVENTS_JSONL_PATH",
    PROJECT_DIR / "conversation_events.jsonl",
))
LATENCY_EVENTS_JSONL_PATH = Path(os.environ.get(
    "LATENCY_EVENTS_JSONL_PATH",
    PROJECT_DIR / "latency_events.jsonl",
))
SERVER_RUN_ID = os.environ.get("SERVER_RUN_ID", uuid.uuid4().hex)
CONVERSATION_MEMORY_TURNS = int(os.environ.get("CONVERSATION_MEMORY_TURNS", "8"))
conversation_memory: dict[str, list[dict[str, str]]] = {}

CURRENT_MODE = "ai"
script_index = 0

FALLBACK_SCRIPT = [
    "What moment felt the most uncomfortable to you?",
    "What do you think you were afraid would happen?",
    "What objective sign suggests you were more stable than you felt?",
    "If a friend had the same performance, would you judge them as harshly?",
    "What is one coping strategy you can reuse next time?",
]

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


def append_conversation_log(user_text: str, ai_text: str, mode: str, metadata: dict) -> None:
    CONVERSATION_LOG_PATH.parent.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    condition = display_tone_name(ELEVENLABS_TONE["name"])
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
        "tonePreset": ELEVENLABS_TONE["name"],
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
    event = {
        "type": "latency",
        "timestampUtcUnixMs": int(time.time() * 1000),
        "serverRunId": SERVER_RUN_ID,
        "mode": CURRENT_MODE,
        "tonePreset": ELEVENLABS_TONE["name"],
        "ttsProvider": payload.get("tts_provider", TTS_PROVIDER),
        "participantId": metadata.get("participantId") or "unknown",
        "loginId": metadata.get("loginId") or "",
        "sessionId": metadata.get("sessionId") or "unknown",
        "avatarCondition": display_tone_name(ELEVENLABS_TONE["name"]),
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
    tone_name = display_tone_name(ELEVENLABS_TONE["name"])
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
    user_text: str,
    scene_prompt: str = "",
    scene_context: str = "",
    avatar_condition: str | None = None,
    conversation_history: list[dict[str, str]] | None = None,
) -> str:
    global script_index

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
    active_tone = backend_selected_tone()
    tone_name = active_tone["name"]
    system_parts = [
        "Always respond in natural spoken English, regardless of the user's input language.",
        (
            "STRICT EVIDENCE POLICY: Treat only the user's transcribed words, explicit InteractionTracker states/events, "
            "and measured head/gaze data in the current voice-turn context as observations. Never invent or infer an "
            "object's color, exact location, identity, movement, outcome, or availability. Gaze/head evidence means only "
            "that the user looked or faced that way; it never proves holding or using. Say the user grabbed, held, used, "
            "released, moved, or completed an action only when an explicit current interaction event/state says so. "
            "Do not claim that a puppy caught a ball, that the user threw something, or that the user holds a flashlight "
            "without such evidence. The avatar cannot move, fetch, throw, hand over, or manipulate scene objects, so never "
            "promise or narrate those actions. If evidence is missing, say you cannot verify it and ask the user to describe "
            "what they see or try an available tracked interaction. Scene descriptions are background only, never proof of "
            "the current state. Do not continue an earlier assistant claim unless current evidence independently confirms it."
        ),
        "Express the selected support style strongly enough that a listener can tell it apart from the other styles.",
        "For simple conversational turns, reply in one or two short spoken sentences.",
        "For complex questions, answer fully enough to be useful, but keep it conversational rather than like a lecture or narration.",
        "Do not list many points unless the user explicitly asks for a list.",
        "Do not omit needed details or end abruptly just to stay short.",
        "Always finish the final sentence cleanly with punctuation; never stop mid-thought.",
        "Treat this as an ongoing conversation in the same VR session. Use prior turns for continuity, but do not repeat them unless needed.",
        active_tone["prompt"],
    ]
    if scene_prompt:
        system_parts.append(
            "Background scene label only; do not use it as evidence that an object is present, held, usable, or acted upon:\n"
            + scene_prompt
        )
    if scene_context and tone_name == "warm":
        system_parts.append(
            "Use this voice-turn VR context only lightly. It only covers the period while the user held the voice key. "
            "It may help you notice what the user was looking at, facing, or interacting with while speaking. "
            "Refer to one explicitly measured cue only when it naturally supports "
            "a warm, validating response. Do not list objects, recite coordinates, or give step-by-step navigation.\n"
            + scene_context
        )
    elif scene_context and tone_name == "dominant":
        system_parts.append(
            "Use this voice-turn VR context for concise directive guidance. It only covers the period while the user held the voice key. "
            "Prioritize explicit speech-window interaction events. Head/gaze may only be described as attention, not action. Mention an object only when it appears in the supplied tracked evidence, "
            "but do not recite raw numeric coordinates.\n"
            + scene_context
        )
    elif scene_context and tone_name == "context_aware_guide":
        system_parts.append(
            "Use this live VR scene context for context-aware guidance. "
            "Convert coordinates and distances into natural spatial language; do not read raw numeric coordinates aloud unless asked.\n"
            + scene_context
        )
    elif scene_context:
        log(f"[CONTEXT] Suppressed live scene context for tone={tone_name}. chars={len(scene_context)}")
    if scene_context and tone_name in ("warm", "dominant", "context_aware_guide"):
        log(f"[CONTEXT] Included live scene context for tone={tone_name}. chars={len(scene_context)}")
    elif not scene_context:
        log(f"[CONTEXT] No live scene context available for tone={tone_name}.")
    if system_parts:
        messages.append({"role": "system", "content": "\n\n".join(system_parts)})
    if conversation_history and CONVERSATION_MEMORY_TURNS > 0:
        max_messages = max(0, CONVERSATION_MEMORY_TURNS) * 2
        messages.extend(conversation_history[-max_messages:])
    messages.append({"role": "user", "content": user_text})

    completion = await groq_client.chat.completions.create(
        messages=messages,
        model="llama-3.1-8b-instant",
        temperature=0.45,
        max_tokens=int(os.environ.get("GROQ_REPLY_MAX_TOKENS", "300")),
    )
    reply = completion.choices[0].message.content.strip()
    finish_reason = getattr(completion.choices[0], "finish_reason", None)
    if finish_reason:
        log(f"[LLM] finish_reason={finish_reason}, reply_chars={len(reply)}")
    return reply


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


def gemini_voice_for_tone() -> str:
    tone_name = ELEVENLABS_TONE["name"]
    if tone_name == "supportive_companion":
        return GEMINI_TTS_SUPPORTIVE_VOICE
    if tone_name == "context_aware_guide":
        return GEMINI_TTS_GUIDE_VOICE
    return GEMINI_TTS_DETACHED_VOICE


def gemini_style_instruction() -> str:
    tone_name = ELEVENLABS_TONE["name"]
    if tone_name == "supportive_companion":
        return (
            "Read in a warm, gentle, supportive companion voice. "
            "Use a slower pace, soft emotional presence, and reassuring intonation."
        )
    if tone_name == "context_aware_guide":
        return (
            "Read in a clear, alert, context-aware guide voice. "
            "Use a crisp pace, task-focused delivery, and confident directional tone."
        )
    return (
        "Read in a detached observer voice. "
        "Use an even, neutral pace with low warmth, low expressiveness, and minimal emotional color."
    )


def generate_gemini_wav(text: str) -> bytes:
    if not GEMINI_API_KEY:
        raise RuntimeError("Missing GEMINI_API_KEY or GOOGLE_API_KEY.")

    voice_name = gemini_voice_for_tone()
    url = f"https://generativelanguage.googleapis.com/v1beta/models/{GEMINI_TTS_MODEL}:generateContent"
    prompt = f"{gemini_style_instruction()}\n\nSay exactly this text:\n{text}"
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
        f"model={GEMINI_TTS_MODEL}, voice={voice_name}, tone={ELEVENLABS_TONE['name']}, "
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


def generate_elevenlabs_wav_for_model(text: str, model_id: str) -> bytes:
    if not ELEVENLABS_API_KEY:
        raise RuntimeError("Missing ELEVENLABS_API_KEY.")

    url = (
        f"https://api.elevenlabs.io/v1/text-to-speech/{ELEVENLABS_VOICE_ID}"
        f"?output_format={ELEVENLABS_OUTPUT_FORMAT}"
    )
    payload = {
        "text": text,
        "model_id": model_id,
        "voice_settings": {
            "stability": ELEVENLABS_STABILITY,
            "similarity_boost": ELEVENLABS_SIMILARITY_BOOST,
            "style": ELEVENLABS_STYLE,
            "speed": ELEVENLABS_SPEED,
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


def generate_elevenlabs_wav(text: str) -> bytes:
    errors = []
    for model_id in elevenlabs_model_candidates():
        try:
            return generate_elevenlabs_wav_for_model(text, model_id)
        except Exception as exc:
            errors.append(f"{model_id}: {exc}")
            log(f"[TTS] ElevenLabs model failed, trying next if available. {model_id}: {exc}")
    raise RuntimeError("All ElevenLabs models failed. " + " | ".join(errors))


def elevenlabs_stream_request(text: str, model_id: str) -> urllib.request.Request:
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
    payload = {
        "text": text,
        "model_id": model_id,
        "voice_settings": {
            "stability": ELEVENLABS_STABILITY,
            "similarity_boost": ELEVENLABS_SIMILARITY_BOOST,
            "style": ELEVENLABS_STYLE,
            "speed": ELEVENLABS_SPEED,
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


async def stream_elevenlabs_pcm(websocket: WebSocket, text: str) -> dict:
    sample_rate = elevenlabs_sample_rate(ELEVENLABS_OUTPUT_FORMAT)
    chunk_size = max(512, ELEVENLABS_STREAM_CHUNK_BYTES)
    errors = []

    for model_id in elevenlabs_model_candidates():
        start_time = time.time()
        log(
            "[TTS] Start ElevenLabs streaming. "
            f"voice_id={ELEVENLABS_VOICE_ID}, model={model_id}, "
            f"format={ELEVENLABS_OUTPUT_FORMAT}, text_chars={len(text)}"
        )
        try:
            request = elevenlabs_stream_request(text, model_id)
            with urllib.request.urlopen(request, timeout=ELEVENLABS_TIMEOUT_SECONDS) as response:
                start_payload = json.dumps(
                    {
                        "type": "audio_stream_start",
                        "sampleRate": sample_rate,
                        "channels": 1,
                        "format": "pcm_s16le",
                        "model": model_id,
                    },
                    ensure_ascii=False,
                )
                if not await safe_send_text(websocket, start_payload, "audio_stream_start"):
                    return {"ok": False, "model": model_id}

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


@app.on_event("startup")
async def preload_vibevoice_worker():
    if not ELEVENLABS_ENABLED and VIBEVOICE_ENABLED and VIBEVOICE_USE_WORKER and VIBEVOICE_PRELOAD:
        await asyncio.get_event_loop().run_in_executor(None, vibevoice_worker_client.start)


@app.on_event("shutdown")
async def shutdown_vibevoice_worker():
    vibevoice_worker_client.stop()


@app.websocket("/")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    log("Unity client connected.")

    global CURRENT_MODE, script_index
    groq_client = AsyncGroq(api_key=GROQ_API_KEY)
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
    }

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
                            preview = scene_context.replace("\n", " | ")[:220]
                            log(f"Scene context updated. chars={len(scene_context)}, preview={preview}")
                            client_metadata["sceneContextChars"] = len(scene_context)
                        for key in ("participantId", "loginId", "sessionId", "avatarCondition", "sceneName"):
                            value = data.get(key)
                            if isinstance(value, str) and value.strip():
                                client_metadata[key] = value.strip()
                        scene_index = data.get("sceneIndex")
                        if isinstance(scene_index, int):
                            client_metadata["sceneIndex"] = scene_index
                        active_tone = backend_selected_tone()
                        stream_reply_audio = bool(data.get("streamReplyAudio", ELEVENLABS_STREAMING_ENABLED))
                        log(f"Stream reply audio: {stream_reply_audio}")
                        log(
                            "[CONFIG] "
                            f"participant={client_metadata['participantId']}, session={client_metadata['sessionId']}, "
                            f"scene={client_metadata['sceneName']}, sceneIndex={client_metadata['sceneIndex']}, "
                            f"avatarCondition={client_metadata['avatarCondition']}, backendTone={display_tone_name(active_tone['name'])}"
                        )
                        continue
                    if data.get("type") == "turn_context":
                        context = data.get("sceneContext")
                        if isinstance(context, str):
                            scene_context = context.strip()
                            preview = scene_context.replace("\n", " | ")[:220]
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
                except json.JSONDecodeError:
                    user_text = message["text"].strip()
                else:
                    continue
            elif "bytes" in message:
                request_start_at = time.time()
                audio_bytes = message["bytes"]
                log(f"Audio received: {len(audio_bytes)} bytes")
                try:
                    stt_start_at = time.time()
                    user_text = await transcribe_audio(groq_client, audio_bytes)
                    stt_seconds = time.time() - stt_start_at
                except Exception as exc:
                    log(f"STT failed: {exc}")
                    user_text = "(inaudible)"
                    stt_seconds = time.time() - request_start_at
            else:
                continue

            if not user_text:
                continue
            if request_start_at is None:
                request_start_at = time.time()

            log(f"User: {user_text}")
            try:
                llm_start_at = time.time()
                reply = await generate_reply(
                    groq_client,
                    user_text,
                    scene_prompt,
                    scene_context,
                    client_metadata.get("avatarCondition"),
                    conversation_memory.get(conversation_key(client_metadata), []),
                )
                llm_seconds = time.time() - llm_start_at
            except Exception as exc:
                log(f"LLM failed: {exc}")
                reply = "I'm listening. What did that moment feel like for you?"
                llm_seconds = time.time() - llm_start_at if "llm_start_at" in locals() else 0.0

            append_conversation_log(user_text, reply, CURRENT_MODE, client_metadata)
            remember_conversation_turn(client_metadata, user_text, reply)
            log(f"Reply: {reply}")

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
                        stream_result = await stream_elevenlabs_pcm(websocket, reply)
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


if __name__ == "__main__":
    try:
        require_environment()
    except Exception as exc:
        log(str(exc))
        sys.exit(1)

    log(f"Starting Unity VibeVoice server on {HOST}:{PORT}")
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
