# VR Avatar Study Measurement Checklist

## Condition metadata

Every recorded file should include:

- `participantId`
- `loginId`
- `sessionId`
- `avatarCondition`
- `sceneName`
- `sceneIndex`
- timestamp

The active backend tone is logged as `tonePreset` in `conversation_events.jsonl` and `latency_events.jsonl`.

## Per-scene self-report

Use after every scene:

- SAM valence, 1-9
- SAM arousal, 1-9
- SAM dominance/control, 1-9
- Social presence
- Autonomy/control
- Avatar agency
- Helpfulness
- Trust
- Intrusiveness
- Warmth
- Competence
- Context awareness
- Guidance clarity
- Attention accuracy
- Conversation naturalness

Primary Unity SAM files are saved under:

`Application.persistentDataPath/SurveyData/<participantId>/`

The HTML backup/full questionnaire is:

`docs/vr_avatar_post_scene_survey.html`

## Behavioral and multimodal logs

Camera/gaze files are saved under:

`Application.persistentDataPath/CameraPoseData/<participantId>/`

Record:

- head position
- head orientation
- eye-gaze availability
- eye-gaze confidence
- left/right eye pose and direction
- whether eye gaze or head-forward fallback was used
- gaze hit object
- gaze hit point and distance
- per-object dwell summary
- attendedLongEnough threshold result
- face expression samples when available
- heart-rate placeholder or future heart-rate sample
- interaction object usage state
- recent interaction events

Important gaze interpretation rule:

Gaze is an attention estimate, not ground truth. Treat `attentionSource=eye_gaze` as stronger evidence and `attentionSource=head_forward_fallback` as a coarse proxy. Analyze with thresholds rather than single-frame hits.

## Conversation and latency logs

Backend text log:

- `conversation_history.txt`

Structured logs:

- `conversation_events.jsonl`
- `latency_events.jsonl`

Record:

- user transcript
- avatar response
- active mode
- active tone preset
- TTS provider
- TTS model
- total latency
- first-audio latency
- STT latency
- LLM latency
- TTS first chunk latency
- TTS total latency
- TTS bytes
- streaming flag
- scene context character count

## Pilot check after each participant

Before running the next participant, verify:

- SAM file exists for each completed scene
- CameraPose file exists for each completed scene
- `conversation_events.jsonl` has one row per conversation turn
- `latency_events.jsonl` has one row per audio reply
- `avatarCondition` matches the assigned condition
- `sceneName` and `sceneIndex` are correct
- gaze object summaries include expected objects or clearly show no hit
- avatar attention appears as `Avatar/Social Agent` when the user looks at the avatar
