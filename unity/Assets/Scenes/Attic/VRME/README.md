# VRME VibeVoice Pack

This folder is the clean test workspace for the Unity avatar server.

It keeps the original project files untouched and gathers the pieces you are likely to use:

- `server_unity_vibevoice.py`: WebSocket server for Unity on port 8080. It now uses ElevenLabs streaming TTS by default.
- `run_server.ps1`: starts the Unity server.
- `setup_vibevoice.ps1`: clones/installs Microsoft VibeVoice into a local folder.
- `run_dp4.ps1`: runs the current final TSST analysis script.
- `run_tsst_batch.ps1`: runs the batch STT/fusion script.
- `.env.example`: environment variables to copy into `.env`.

## Current Final TSST Code

Your most usable final TSST analysis script is:

`D:\leetcode\avartar-server\dp4.py`

It processes:

`D:\leetcode\avartar-server\data\12.19`

and writes:

`D:\leetcode\avartar-server\data\12.19\ISMAR_FullPaper_Context.txt`

## Unity Runtime Flow

Unity sends recorded WAV bytes to:

`ws://localhost:8080`

This pack's server then:

1. Runs Groq Whisper STT.
2. Produces a short reflection response using Groq chat.
3. Streams speech using ElevenLabs voice `pFZP5JQG7iQjIQuC4Bku` with `eleven_v3`, falling back to `eleven_multilingual_v2` and then Flash only if needed.
4. Sends PCM audio chunks back to Unity for low-latency playback. If streaming is disabled or fails before audio starts, it falls back to a complete WAV response.

## First-Time Setup

1. Copy `.env.example` to `.env`.
2. Put your `GROQ_API_KEY` and `ELEVENLABS_API_KEY` in `.env`.
3. Run:

```powershell
cd D:\leetcode\avartar-server\vrme_vibevoice_pack
.\setup_vibevoice.ps1
```

4. Start server:

```powershell
.\run_server.ps1
```

## Notes

VibeVoice is local and free to run, but it needs model downloads and GPU/CPU time. The first run may take a while.

Model downloads are kept under `D:\leetcode\model-cache` by default, so HuggingFace and PyTorch do not fill C:.

If VibeVoice is not installed yet, the server sends a text fallback instead of audio, so Unity will not crash.

ElevenLabs Voice Library voices may require a paid plan for API use. If a voice returns `paid_plan_required`, the server will fall back through the configured model list, but you still need an API-accessible voice ID.
