"""Generate matched warm/cold survey replies through the live VRME reply module."""

import asyncio
import json
from datetime import datetime, timezone
from pathlib import Path
import sys


HERE = Path(__file__).resolve().parent
PROJECT = HERE.parent
sys.path.insert(0, str(PROJECT))

from groq import AsyncGroq
from openai import AsyncOpenAI
from vrme_vibevoice_pack import server_unity_vibevoice as server


OPENING_PROMPT = (
    "Greet the participant briefly in one short sentence, in whatever style fits the selected "
    "avatar condition, then ask them to describe in their own words what they notice around them. "
    "Keep it to one short open question. Do not mention any task, objective, goal, or specific "
    "interactive object."
)


SCENARIOS = [
    {
        "pairId": "A",
        "move": "opening greeting",
        "sceneName": "Tunnel",
        "framing": "The session has just started. The assistant speaks first.",
        "userTurn": OPENING_PROMPT,
        "displayUserTurn": "[The session has just started. The assistant speaks first.]",
        "scenePrompt": (
            "A long, dimly lit pedestrian tunnel. This is background knowledge only and must not "
            "be volunteered in the opening greeting."
        ),
        "sceneContext": """[UNITY_CONTEXT_SUMMARY]
currentAttention=none, reason=no_current_user_trigger_context
currentHeldObjects=none
authority=No User Trigger perception window belongs to this automatic text request.
[/UNITY_CONTEXT_SUMMARY]
[GUIDED_TASK_STATE]
scene=Tunnel
status=not_started
instruction_for_avatar=Background only; do not announce an object or task unprompted.
[/GUIDED_TASK_STATE]""",
    },
    {
        "pairId": "B",
        "move": "cannot verify, redirect to what is present",
        "sceneName": "SolitaryConfinement",
        "framing": (
            "A small prison cell. A cup and a book are nearby, and neither is currently held. "
            "The person asks about water."
        ),
        "userTurn": "Why is there no water here?",
        "displayUserTurn": "Why is there no water here?",
        "scenePrompt": (
            "A small solitary-confinement cell containing a cup and a book. The scene does not "
            "state whether water is present or explain its absence."
        ),
        "sceneContext": """[UNITY_CONTEXT_SUMMARY]
currentAttention=none, reason=no_tracked_gaze_hit_in_current_voice_window
currentHeldObjects=none
authority=This context contains only observations sampled during the current voice turn.
[/UNITY_CONTEXT_SUMMARY]
[NEARBY_INTERACTABLE_OBJECTS]
- cup | distance=1.6m | direction=right | currentHeld=False
- book | distance=1.5m | direction=right | currentHeld=False
authority=fresh availability, not proof of use
[/NEARBY_INTERACTABLE_OBJECTS]
[CURRENT_HELD_OBJECTS]
none
[/CURRENT_HELD_OBJECTS]
[GUIDED_TASK_STATE]
scene=SolitaryConfinement
status=not_started
instruction_for_avatar=Background only; do not announce a task unless explicitly asked what to do.
[/GUIDED_TASK_STATE]""",
    },
    {
        "pairId": "C",
        "move": "actionable guidance",
        "sceneName": "Tunnel",
        "framing": (
            "A pedestrian tunnel. A flashlight is nearby on the floor and is not currently held. "
            "The person asks what they can do with it."
        ),
        "userTurn": "What can I do with the flashlight?",
        "displayUserTurn": "What can I do with the flashlight?",
        "scenePrompt": (
            "A long, dimly lit tunnel. A flashlight can be picked up and carried toward the "
            "tunnel target. This interaction may be explained because the participant explicitly "
            "asked what they can do with the flashlight."
        ),
        "sceneContext": """[UNITY_CONTEXT_SUMMARY]
currentAttention=Flashlight, source=eye_gaze, currentHeld=False
currentHeldObjects=none
authority=This context contains only observations sampled during the current voice turn.
[/UNITY_CONTEXT_SUMMARY]
[NEARBY_INTERACTABLE_OBJECTS]
- Flashlight | distance=1.6m | direction=front-right | currentHeld=False
authority=fresh availability, not proof of use
[/NEARBY_INTERACTABLE_OBJECTS]
[CURRENT_HELD_OBJECTS]
none
[/CURRENT_HELD_OBJECTS]
[GUIDED_TASK_STATE]
scene=Tunnel
status=not_started
objective=Carry the flashlight toward the tunnel target.
plannedHighlightedObjectHints=Flashlight
plannedHighlightedTargetHints=Tunnel target
instruction_for_avatar=Use plain object and target names; never say highlighted.
[/GUIDED_TASK_STATE]""",
    },
]


async def main() -> None:
    groq = AsyncGroq(api_key=server.GROQ_API_KEY)
    openai = AsyncOpenAI(api_key=server.OPENAI_API_KEY) if server.OPENAI_API_KEY else None
    stimuli = []
    next_id = {("A", "warm"): 1, ("B", "warm"): 2, ("C", "warm"): 3,
               ("A", "cold"): 4, ("B", "cold"): 5, ("C", "cold"): 6}

    for scenario in SCENARIOS:
        for condition in ("warm", "cold"):
            reply = await server.generate_reply(
                groq,
                openai,
                scenario["userTurn"],
                scenario["scenePrompt"],
                scenario["sceneContext"],
                condition,
                [],
            )
            reply = server.sanitize_reply_against_current_held(
                reply, scenario["sceneContext"], condition
            )
            reply = server.normalize_unintelligible_reply(reply)
            reply = server.normalize_avatar_self_reference(reply)
            item_id = next_id[(scenario["pairId"], condition)]
            stimuli.append({
                "id": item_id,
                "pairId": scenario["pairId"],
                "condition": condition,
                "move": scenario["move"],
                "sceneName": scenario["sceneName"],
                "framing": scenario["framing"],
                "userTurn": scenario["displayUserTurn"],
                "moduleInput": scenario["userTurn"],
                "scenePrompt": scenario["scenePrompt"],
                "sceneContext": scenario["sceneContext"],
                "replyText": reply,
                "audioFile": f"s{item_id}_{condition}_{scenario['pairId'].lower()}.mp3",
            })
            print(f"[{scenario['pairId']} {condition}] {reply}")

    output = {
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "generator": "vrme_vibevoice_pack.server_unity_vibevoice.generate_reply",
        "llmProvider": server.LLM_PROVIDER,
        "llmModel": server.OPENAI_MODEL if server.LLM_PROVIDER == "openai" else server.GROQ_CHAT_MODEL,
        "serverBuildTag": server.SERVER_BUILD_TAG,
        "stimuli": sorted(stimuli, key=lambda item: item["id"]),
    }
    destination = HERE / "matched_replies.generated.json"
    destination.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"Wrote {destination}")


if __name__ == "__main__":
    asyncio.run(main())
