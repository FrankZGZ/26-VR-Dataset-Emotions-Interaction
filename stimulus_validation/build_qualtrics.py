"""
Build the warm/cold audio validation survey in Qualtrics via the API.

    set QUALTRICS_TOKEN=...          (Windows cmd)
    $env:QUALTRICS_TOKEN="..."       (PowerShell)
    export QUALTRICS_TOKEN=...       (bash)
    python build_qualtrics.py

Creates a NEW survey every run and prints its ID + preview link. It never
touches an existing survey, so re-running is safe; delete the drafts you
do not want from the Qualtrics UI.

What it guarantees, and what the July pretest did not have:
  * six clips rated INDIVIDUALLY, not two voices rated as a whole
  * a true 6x6 Latin square over the six clips, assigned by an even-presentation
    randomiser, with the sequence written to embedded data SeqID
  * matched pair members never adjacent, never 3 same-condition clips in a row,
    sequences 4-6 are the reverses of 1-3
  * condition carried in the QUESTION TAG (S1_warm_intro...), not in the
    presentation slot, so the export is scoreable no matter what order ran
  * the Next button stays hidden until each clip has finished playing once
"""
import json
import os
import sys
import urllib.error
import urllib.request

DC = "au1"
BASE = f"https://{DC}.qualtrics.com/API/v3"
TOKEN = os.environ.get("QUALTRICS_TOKEN", "").strip()
if not TOKEN:
    sys.exit("Set QUALTRICS_TOKEN first (see the docstring).")

SURVEY_NAME = "Avatar Voice Tone Validation v2 (revised warm/cold, counterbalanced)"

# ---------------------------------------------------------------- config

SCALE_MAX = 9          # RoSAS as published is 9-point. Set to 7 to match a 7-point Unity slider.
AUDIO = "https://github.com/FrankZGZ/26-VR-Dataset-Emotions-Interaction/raw/refs/heads/qualtrics-stimuli/stimulus_validation/audio"
AUDIO_VERSION = "warmth-pairing-2026-08-09-v1"
TONE_CHECK = "https://avatar1234.netlify.app/test_tones.wav"  # July pretest asset
SHOW_TRANSCRIPT = False                     # True also prints the words under the player

# 7 RoSAS items: the 6 the Unity build stores, plus Strange to rescue Discomfort
# (rosasScary had zero variance for P1 across all six scenes).
ROSAS = [
    ("Compassionate", "Warmth"),
    ("Social",        "Warmth"),
    ("Competent",     "Competence"),
    ("Reliable",      "Competence"),
    ("Scary",         "Discomfort"),
    ("Awkward",       "Discomfort"),
    ("Strange",       "Discomfort"),
]

EXTRA = [
    "Warmth — 1 = very cold; 4 = neutral; 7 = very warm",
    "Helpfulness in this situation — 1 = not at all; 4 = moderately; 7 = extremely",
    "Want to continue talking — 1 = not at all; 4 = somewhat; 7 = very much",
]

# The stimulus set and the Latin square both come from stimuli.json, which holds the
# verbatim 2026-08-08 utterances with their provenance. Nothing is duplicated here, so
# the survey can never drift out of step with the logged text.
SPEC = json.loads((os.path.dirname(os.path.abspath(__file__)) and
                   open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                     "stimuli.json"), encoding="utf-8").read()))
STIM = {s["id"]: s for s in SPEC["stimuli"]}
PAIR_MOVE = {p["pairId"]: p["move"] for p in SPEC["pairs"]}
MOVE_NAME = {"A": "greeting", "B": "cannotverify", "C": "guidance"}
SEQ = {int(k): v for k, v in SPEC["counterbalance"]["sequences"].items()}


# ---------------------------------------------------------------- http

def call(method, path, payload=None):
    url = BASE + path
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={
        "X-API-TOKEN": TOKEN, "Content-Type": "application/json",
        "Accept": "application/json", "User-Agent": "Mozilla/5.0 VRME-Qualtrics-Builder/2.0"})
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            return json.loads(r.read().decode())
    except urllib.error.HTTPError as e:
        sys.exit(f"{method} {path} -> {e.code}\n{e.read().decode()[:900]}")


# ---------------------------------------------------------------- helpers

def choices(labels):
    return ({str(i): {"Display": t} for i, t in enumerate(labels, 1)},
            [str(i) for i in range(1, len(labels) + 1)])


def mc(tag, text, opts, force=True, selector="SAVR"):
    ch, order = choices(opts)
    return {"QuestionText": text, "DataExportTag": tag, "QuestionType": "MC",
            "Selector": selector, "SubSelector": "TX",
            "Configuration": {"QuestionDescriptionOption": "UseText"},
            "QuestionDescription": text[:100], "Choices": ch, "ChoiceOrder": order,
            "Validation": {"Settings": {"ForceResponse": "ON" if force else "OFF",
                                        "ForceResponseType": "ON", "Type": "None"}},
            "Language": []}


def matrix(tag, text, rows, cols, force=True):
    ch, corder = choices(rows)
    an, aorder = choices(cols)
    return {"QuestionText": text, "DataExportTag": tag, "QuestionType": "Matrix",
            "Selector": "Likert", "SubSelector": "SingleAnswer",
            "Configuration": {"QuestionDescriptionOption": "UseText", "TextPosition": "inline",
                              "ChoiceColumnWidth": 25, "RepeatHeaders": "none",
                              "WhiteSpace": "OFF", "MobileFirst": True},
            "QuestionDescription": text[:100],
            "Choices": ch, "ChoiceOrder": corder, "Answers": an, "AnswerOrder": aorder,
            "Validation": {"Settings": {"ForceResponse": "ON" if force else "OFF",
                                        "ForceResponseType": "ON", "Type": "None"}},
            "Language": []}


def text_entry(tag, text, essay=False, force=False):
    return {"QuestionText": text, "DataExportTag": tag, "QuestionType": "TE",
            "Selector": "ESTB" if essay else "SL",
            "Configuration": {"QuestionDescriptionOption": "UseText"},
            "QuestionDescription": text[:100],
            "Validation": {"Settings": {"ForceResponse": "ON" if force else "OFF",
                                        "Type": "None"}},
            "Language": []}


def descriptive(tag, html):
    return {"QuestionText": html, "DataExportTag": tag, "QuestionType": "DB",
            "Selector": "TB", "Configuration": {"QuestionDescriptionOption": "UseText"},
            "QuestionDescription": tag, "Language": []}


# Next button stays hidden until the clip has played through once.
GATE_JS = """Qualtrics.SurveyEngine.addOnload(function(){
  var q=this, a=document.getElementById('%s');
  if(!a){return;}
  q.hideNextButton();
  var note=document.getElementById('%s_note');
  a.addEventListener('ended',function(){ q.showNextButton(); if(note){note.style.display='none';} });
});"""


def audio_q(tag, aid, framing, src, said=None, transcript=None):
    html = f'<div style="font-size:15px;line-height:1.6">'
    if said:
        html += (f'<p style="color:#666;margin:0 0 4px">{framing}</p>'
                 f'<p style="margin:0 0 12px"><i>The person said:</i> &ldquo;{said}&rdquo;</p>')
    else:
        html += f'<p style="color:#666;margin:0 0 12px">{framing}</p>'
    html += (f'<audio id="{aid}" controls preload="auto" style="width:100%;max-width:420px" '
             f'src="{src}"></audio>')
    if transcript:
        html += (f'<p style="margin:10px 0 0;padding:10px 12px;background:#f3ece3;'
                 f'border-radius:8px">&ldquo;{transcript}&rdquo;</p>')
    html += (f'<p id="{aid}_note" style="color:#8a5a2b;font-size:13px;margin:10px 0 0">'
             f'Please listen to the whole clip. The Next button appears when it finishes.'
             f'</p></div>')
    q = descriptive(tag, html)
    q["QuestionJS"] = GATE_JS % (aid, aid)
    return q


def set_options(sid):
    """Qualtrics rejects a partial options object, so read-modify-write the whole thing."""
    opts = call("GET", f"/survey-definitions/{sid}/options")["result"]
    opts.update({
        "BackButton": "false",            # no going back to re-hear an earlier clip
        "ProgressBarDisplay": "VerboseText",
        "SaveAndContinue": "true",
        "SurveyTitle": SURVEY_NAME,
    })
    call("PUT", f"/survey-definitions/{sid}/options", opts)
    print("options set: back button off, progress bar on")


# ---------------------------------------------------------------- build

def main():
    who = call("GET", "/whoami")["result"]
    print(f"account: {who['firstName']} {who['lastName']} <{who['email']}>  dc={who['datacenter']}")

    sid = call("POST", "/survey-definitions", {
        "SurveyName": SURVEY_NAME, "Language": "EN", "ProjectCategory": "CORE"
    })["result"]["SurveyID"]
    print(f"created survey {sid}")

    made = {}

    def block(desc):
        bid = call("POST", f"/survey-definitions/{sid}/blocks",
                   {"Description": desc, "Type": "Standard"})["result"]["BlockID"]
        made[desc] = bid
        return bid

    def q(bid, payload):
        call("POST", f"/survey-definitions/{sid}/questions?blockId={bid}", payload)

    scale_cols = [str(i) for i in range(1, SCALE_MAX + 1)]
    scale_cols[0] = "1 - Definitely not"
    scale_cols[-1] = f"{SCALE_MAX} - Definitely yes"

    # ---- consent
    b = block("Consent")
    q(b, descriptive("Info", (
        "<h3>Participant information</h3>"
        "<p>You will hear six short audio clips of a virtual assistant speaking, and rate "
        "your impression of the assistant after each one. It takes about 12 minutes.</p>"
        "<p>Participation is voluntary and anonymous; no identifying information is collected "
        "and you may close the page at any time. Please use headphones in a quiet place.</p>")))
    q(b, mc("Consent", "I am at least 18 years old, have read the information above, "
                       "and voluntarily agree to participate.",
            ["I agree", "I do not agree"]))

    # ---- audio setup
    b = block("Audio Setup")
    q(b, audio_q("ToneCheck", "aud_tone",
                 "Put on headphones, set a comfortable volume, and press play. "
                 "You should hear two short tones.", TONE_CHECK))
    q(b, mc("HeardTones", "Could you hear both tones clearly?",
            ["Yes", "No", "Not sure"]))
    q(b, mc("Headphones", "Please put on headphones or earphones before continuing. Are you using them now?",
            ["Yes, headphones or earphones", "No, laptop or phone speakers"]))

    # ---- instructions
    b = block("Instructions")
    q(b, descriptive("Instructions", (
        "<h3>What you will do</h3>"
        "<p>You will hear <b>six short clips</b>. Each one is a single reply the assistant gave "
        "to a person exploring a virtual environment.</p>"
        "<p>After each clip you will rate <b>the assistant, as it comes across in that clip</b> "
        "on seven words, then answer three short questions.</p>"
        "<p>Many of the words will not fit — that is expected, just rate them low. There are no "
        "right answers. Judge each clip on its own; do not try to stay consistent with earlier ones.</p>"
        "<p>All six clips use the same synthetic speaker. Judge the overall reply, including "
        "both its wording and vocal delivery.</p>")))

    # ---- six stimulus blocks
    stim_block = {}
    for sid_num in sorted(STIM):
        s = STIM[sid_num]
        cond, move = s["condition"], MOVE_NAME[s["pairId"]]
        tag = f"S{sid_num}_{cond}_{move}"
        b = block(f"Clip {sid_num} ({cond}/{move})")
        stim_block[sid_num] = b
        said = None if s["userTurn"].startswith("[") else s["userTurn"]
        q(b, audio_q(f"{tag}_audio", f"aud_s{sid_num}", s["framing"],
                     f"{AUDIO}/{s['audioFile']}?v={AUDIO_VERSION}", said=said,
                     transcript=s["replyText"] if SHOW_TRANSCRIPT else None))
        q(b, matrix(f"{tag}_rosas",
                    "How closely do you associate each word with this assistant?",
                    [w for w, _ in ROSAS], scale_cols))
        q(b, matrix(f"{tag}_check",
                    "Rate this reply from 1 to 7 using the anchors shown in each row.",
                    EXTRA, [str(i) for i in range(1, 8)]))

    # ---- post
    b = block("Post")
    q(b, mc("SameAssistant",
            "Do you think all six clips came from the same assistant, or more than one?",
            ["One assistant", "More than one", "Not sure"]))
    q(b, text_entry("DifferenceText",
                    "What, if anything, made the clips feel different from each other? (optional)",
                    essay=True))

    # ---- demographics
    b = block("Demographics")
    q(b, text_entry("Age", "Age in years (18-100)", force=True))
    q(b, mc("Gender", "Gender",
            ["Woman", "Man", "Non-binary", "Prefer to self-describe", "Prefer not to say"]))
    q(b, mc("Education", "Highest education completed",
            ["High school or below", "Some university", "Bachelor's degree",
             "Master's degree", "Doctoral degree", "Prefer not to say"]))
    q(b, mc("English", "English listening proficiency",
            ["Native or near-native", "Advanced", "Intermediate", "Basic"]))
    q(b, mc("EnglishYears",
            "How many years have you lived in a country where English is the main "
            "everyday language?",
            ["None", "Less than 1 year", "1-3 years", "4-10 years",
             "More than 10 years", "I have lived in one all my life"]))
    q(b, mc("FirstLanguage", "Is English your first language?", ["Yes", "No"]))
    q(b, mc("Hearing", "Hearing status",
            ["No known hearing difficulty", "Corrected hearing difficulty",
             "Uncorrected hearing difficulty", "Prefer not to say"]))
    q(b, mc("Device", "Device used",
            ["Phone", "Tablet", "Laptop computer", "Desktop computer"]))
    q(b, mc("VR", "Prior VR experience",
            ["Never", "Once or twice", "Occasionally", "Monthly or more", "Weekly or more"]))
    q(b, mc("AIUse", "How often do you use voice assistants or conversational AI?",
            ["Never", "Rarely", "Sometimes", "Often", "Very often"]))

    # ---- quality
    b = block("Quality Check")
    q(b, mc("Attention", 'To confirm you are reading, please select "Somewhat agree".',
            ["Strongly disagree", "Disagree", "Somewhat disagree", "Neither",
             "Somewhat agree", "Agree", "Strongly agree"]))
    q(b, text_entry("Issues", "Any technical or audio problems? (optional)", essay=True))
    q(b, text_entry("Guess", "What do you think this study was testing? (optional)", essay=True))

    # ---- flow: counterbalance
    n = [1]

    def fid():
        n[0] += 1
        return f"FL_{n[0]}"

    def blk(desc):
        return {"Type": "Block", "ID": made[desc], "FlowID": fid()}

    groups = []
    for s in sorted(SEQ):
        inner = [{"Type": "EmbeddedData", "FlowID": fid(), "EmbeddedData": [
            {"Description": "SeqID", "Type": "Custom", "Field": "SeqID",
             "VariableType": "String", "DataVisibility": {"Private": False, "Hidden": False},
             "AnalyzeText": False, "Value": str(s)},
            {"Description": "SeqOrder", "Type": "Custom", "Field": "SeqOrder",
             "VariableType": "String", "DataVisibility": {"Private": False, "Hidden": False},
             "AnalyzeText": False, "Value": "-".join(str(x) for x in SEQ[s])}]}]
        for stim in SEQ[s]:
            inner.append({"Type": "Block", "ID": stim_block[stim], "FlowID": fid()})
        groups.append({"Type": "Group", "FlowID": fid(), "Description": f"Sequence {s}",
                       "Flow": inner})

    flow = [
        blk("Consent"), blk("Audio Setup"), blk("Instructions"),
        {"Type": "EmbeddedData", "FlowID": fid(), "EmbeddedData": [
            {"Description": "SeqID", "Type": "Recipient", "Field": "SeqID",
             "VariableType": "String",
             "DataVisibility": {"Private": False, "Hidden": False}, "AnalyzeText": False},
            {"Description": "SeqOrder", "Type": "Recipient", "Field": "SeqOrder",
             "VariableType": "String",
             "DataVisibility": {"Private": False, "Hidden": False}, "AnalyzeText": False}]},
        {"Type": "BlockRandomizer", "FlowID": fid(), "SubSet": 1,
         "EvenPresentation": True, "Flow": groups},
        blk("Post"), blk("Demographics"), blk("Quality Check"),
    ]
    call("PUT", f"/survey-definitions/{sid}/flow",
         {"Type": "Root", "FlowID": "FL_1", "Flow": flow, "Properties": {"Count": n[0]}})
    print("flow written: even-presentation randomiser over 6 Latin-square sequences")

    set_options(sid)

    print(f"\n  survey id : {sid}")
    print(f"  edit      : https://sydney.au1.qualtrics.com/survey-builder/{sid}/edit")
    print(f"  preview   : https://sydney.au1.qualtrics.com/jfe/preview/{sid}")
    print("\n  NOT activated and NOT distributed - do that yourself in the UI when you are happy.")


if __name__ == "__main__":
    main()
