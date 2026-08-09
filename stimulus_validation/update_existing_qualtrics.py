"""Update the existing Qualtrics draft with the reviewed matched stimuli."""

import html
import json
import os
from pathlib import Path
import sys
import urllib.error
import urllib.request


HERE = Path(__file__).resolve().parent
DC = "au1"
BASE = f"https://{DC}.qualtrics.com/API/v3"
TOKEN = os.environ.get("QUALTRICS_TOKEN", "").strip()
SURVEY_ID = os.environ.get("QUALTRICS_SURVEY_ID", "SV_8xpqtEWWF2wl5C6").strip()
AUDIO_BASE = os.environ.get(
    "QUALTRICS_AUDIO_BASE",
    "https://github.com/FrankZGZ/26-VR-Dataset-Emotions-Interaction/raw/refs/heads/qualtrics-stimuli/stimulus_validation/audio",
).rstrip("/")
SURVEY_NAME = os.environ.get(
    "QUALTRICS_SURVEY_NAME",
    "Avatar Voice Tone Validation v2 (revised warm/cold module)",
).strip()

if not TOKEN:
    sys.exit("Set QUALTRICS_TOKEN first.")

SPEC = json.loads((HERE / "stimuli.json").read_text(encoding="utf-8"))
STIM = {item["id"]: item for item in SPEC["stimuli"]}
MOVE_NAME = {"A": "greeting", "B": "cannotverify", "C": "guidance"}


def call(method, path, payload=None):
    data = json.dumps(payload).encode("utf-8") if payload is not None else None
    request = urllib.request.Request(
        BASE + path,
        data=data,
        method=method,
        headers={
            "X-API-TOKEN": TOKEN,
            "Content-Type": "application/json",
            "Accept": "application/json",
            "User-Agent": "Mozilla/5.0 VRME-Qualtrics-Updater/1.0",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as exc:
        raise RuntimeError(
            f"{method} {path} -> {exc.code}: {exc.read().decode('utf-8', errors='replace')[:900]}"
        ) from exc


def audio_html(item):
    aid = f"aud_s{item['id']}"
    framing = html.escape(item["framing"])
    user_turn = html.escape(item["userTurn"])
    src = f"{AUDIO_BASE}/{item['audioFile']}"
    body = '<div style="font-size:15px;line-height:1.6">'
    body += f'<p style="color:#666;margin:0 0 4px">{framing}</p>'
    if not item["userTurn"].startswith("["):
        body += f'<p style="margin:0 0 12px"><i>The person said:</i> &ldquo;{user_turn}&rdquo;</p>'
    body += (
        f'<audio id="{aid}" controls preload="auto" style="width:100%;max-width:420px" '
        f'src="{src}"></audio>'
        f'<p id="{aid}_note" style="color:#8a5a2b;font-size:13px;margin:10px 0 0">'
        "Please listen to the whole clip before continuing.</p></div>"
    )
    return aid, body


def clean_question(question):
    return {
        key: value
        for key, value in question.items()
        if key not in {"QuestionID", "QuestionText_Unsafe", "QuestionJS"}
    }


def main():
    definition = call("GET", f"/survey-definitions/{SURVEY_ID}")["result"]
    if definition.get("SurveyStatus") != "Inactive":
        raise RuntimeError("Refusing to edit an active survey.")

    questions = definition["Questions"]
    by_tag = {question.get("DataExportTag", ""): (qid, question) for qid, question in questions.items()}

    for item_id, item in STIM.items():
        old_prefix = f"S{item_id}_"
        matches = [(tag, value) for tag, value in by_tag.items() if tag.startswith(old_prefix)]
        if len(matches) != 3:
            raise RuntimeError(f"Expected three existing questions for S{item_id}; found {len(matches)}")

        new_prefix = f"S{item_id}_{item['condition']}_{MOVE_NAME[item['pairId']]}"
        for old_tag, (qid, original) in matches:
            suffix = old_tag.rsplit("_", 1)[-1]
            updated = clean_question(original)
            updated["DataExportTag"] = f"{new_prefix}_{suffix}"
            if suffix != "audio" and old_tag == updated["DataExportTag"]:
                print(f"verified {qid}: {old_tag}")
                continue
            if suffix == "audio":
                aid, body = audio_html(item)
                updated["QuestionText"] = body
                updated["QuestionDescription"] = updated["DataExportTag"]
            call("PUT", f"/survey-definitions/{SURVEY_ID}/questions/{qid}", updated)
            print(f"updated {qid}: {old_tag} -> {updated['DataExportTag']}")

    instructions = next(
        (value for value in by_tag.values() if value[1].get("DataExportTag") == "Instructions"),
        None,
    )
    if instructions:
        qid, original = instructions
        updated = clean_question(original)
        updated["QuestionText"] = (
            "<h3>What you will do</h3>"
            "<p>You will hear <b>six short clips</b>. Each is a reply produced by the current "
            "virtual-assistant module from a controlled VR situation.</p>"
            "<p>Within each matched pair, the person’s words and the VR scene state were identical. "
            "The assistant condition was the only input changed.</p>"
            "<p>After each clip, rate <b>the assistant as it comes across in that clip</b>. "
            "Judge the overall reply, including both its wording and vocal delivery.</p>"
            "<p>There are no right answers. Judge each clip on its own and use headphones in a quiet place.</p>"
        )
        call("PUT", f"/survey-definitions/{SURVEY_ID}/questions/{qid}", updated)
        print("updated instructions")

    headphones = next(
        (value for value in by_tag.values() if value[1].get("DataExportTag") == "Headphones"),
        None,
    )
    if headphones:
        qid, original = headphones
        updated = clean_question(original)
        updated["QuestionText"] = (
            "Please put on headphones or earphones before continuing. Are you using them now?"
        )
        updated["QuestionDescription"] = "Headphones"
        call("PUT", f"/survey-definitions/{SURVEY_ID}/questions/{qid}", updated)
        print("updated headphone requirement")

    options = call("GET", f"/survey-definitions/{SURVEY_ID}/options")["result"]
    options["SurveyTitle"] = SURVEY_NAME
    options["BackButton"] = "false"
    call("PUT", f"/survey-definitions/{SURVEY_ID}/options", options)
    print(f"updated survey content/options for {SURVEY_ID}; it remains inactive")


if __name__ == "__main__":
    main()
