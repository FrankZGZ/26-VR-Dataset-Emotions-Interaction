"""
Synthesise six matched clips with the current VRME warm/cold ElevenLabs presets.

    export ELEVENLABS_API_KEY=...        (bash)
    $env:ELEVENLABS_API_KEY="..."        (PowerShell)
    python generate_audio.py

Writes audio/*.mp3. Both conditions use the same speaker and model, while each uses
the condition-specific settings currently selected by server_unity_vibevoice.py.
"""
import json
import os
import pathlib
import sys
import urllib.error
import urllib.request

from dotenv import load_dotenv

HERE = pathlib.Path(__file__).parent
OUT = HERE / "audio"
load_dotenv(HERE.parent / "vrme_vibevoice_pack" / ".env")
KEY = os.environ.get("ELEVENLABS_API_KEY", "").strip()
if not KEY:
    sys.exit("Set ELEVENLABS_API_KEY first (see the docstring).")

spec = json.loads((HERE / "stimuli.json").read_text(encoding="utf-8"))
VOICE = spec["tts"]["voiceId"]
MODEL = spec["tts"]["modelId"]

VOICE_SETTINGS = spec["tts"]["presets"]


def synth(text, condition, dest):
    url = (f"https://api.elevenlabs.io/v1/text-to-speech/{VOICE}"
           f"?output_format=mp3_44100_128")
    body = json.dumps({"text": text, "model_id": MODEL,
                       "voice_settings": VOICE_SETTINGS[condition]}).encode()
    req = urllib.request.Request(url, data=body, method="POST", headers={
        "xi-api-key": KEY, "Content-Type": "application/json", "Accept": "audio/mpeg"})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            dest.write_bytes(r.read())
    except urllib.error.HTTPError as e:
        sys.exit(f"ElevenLabs {e.code} for {dest.name}:\n{e.read().decode()[:600]}")


def main():
    OUT.mkdir(exist_ok=True)
    print(f"voice={VOICE}  model={MODEL}  presets={VOICE_SETTINGS}\n")
    for s in sorted(spec["stimuli"], key=lambda x: x["id"]):
        dest = OUT / s["audioFile"]
        synth(s["replyText"], s["condition"], dest)
        kb = dest.stat().st_size / 1024
        print(f"  [{s['id']}] {s['condition']:<4} {dest.name:<28} {kb:7.1f} KB")
        print(f"        {s['replyText'][:88]}...")

    # a two-tone headphone check, same as the July pretest used
    print("\nAlso needed: test_tones.wav for the headphone check.")
    print("The July pretest's copy is already live and can be reused as-is:")
    print("  https://avatar1234.netlify.app/test_tones.wav")

    print(f"\nWrote {len(spec['stimuli'])} clips to {OUT}")
    print("\nNext:")
    print("  1. Listen to all six. Confirm warm and cold sound like the same speaker.")
    print("  2. Upload them to https://avatar1234.netlify.app (or any https host).")
    print("  3. Re-run:  QUALTRICS_TOKEN=... python build_qualtrics.py")


if __name__ == "__main__":
    main()
