"""Promote a reviewed matched-reply run into the Qualtrics stimulus spec."""

import json
from pathlib import Path


HERE = Path(__file__).resolve().parent
source = json.loads((HERE / "matched_replies.generated.json").read_text(encoding="utf-8"))

spec = {
    "study": "Current VRME warm/cold matched voice validation",
    "builtFrom": "Matched calls to the current server reply module using identical input and scene state",
    "generation": {
        key: source[key]
        for key in ("generatedAtUtc", "generator", "llmProvider", "llmModel", "serverBuildTag")
    },
    "notes": [
        "Each pair uses identical user text, scene prompt, and current-turn Unity context for warm and cold.",
        "The reply text is verbatim module output; it was not manually rewritten.",
        "Audio uses the current server's condition-specific ElevenLabs settings with the same voice and model.",
    ],
    "pairs": [
        {"pairId": "A", "move": "opening greeting", "warm": 1, "cold": 4,
         "matchQuality": "exact matched input and state"},
        {"pairId": "B", "move": "cannot verify and redirect", "warm": 2, "cold": 5,
         "matchQuality": "exact matched input and state"},
        {"pairId": "C", "move": "actionable guidance", "warm": 3, "cold": 6,
         "matchQuality": "exact matched input and state"},
    ],
    "stimuli": source["stimuli"],
    "tts": {
        "provider": "elevenlabs",
        "voiceId": "pFZP5JQG7iQjIQuC4Bku",
        "modelId": "eleven_flash_v2_5",
        "presets": {
            "warm": {"stability": 0.66, "similarity_boost": 0.80, "style": 0.34,
                     "speed": 0.96, "use_speaker_boost": True},
            "cold": {"stability": 0.88, "similarity_boost": 0.78, "style": 0.02,
                     "speed": 1.00, "use_speaker_boost": True},
        },
    },
    "counterbalance": {
        "scheme": "6x6 reverse-balanced Latin square",
        "sequences": {
            "1": [1, 2, 4, 3, 5, 6],
            "2": [2, 1, 6, 5, 3, 4],
            "3": [3, 4, 2, 1, 6, 5],
            "4": [4, 3, 5, 6, 1, 2],
            "5": [5, 6, 1, 2, 4, 3],
            "6": [6, 5, 3, 4, 2, 1],
        },
    },
}

(HERE / "stimuli.json").write_text(
    json.dumps(spec, ensure_ascii=False, indent=2), encoding="utf-8"
)
print("Promoted matched_replies.generated.json to stimuli.json")
