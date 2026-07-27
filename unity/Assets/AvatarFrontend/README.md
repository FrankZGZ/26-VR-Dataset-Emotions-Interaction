# Avatar front-end package

This folder documents the Avatar capability layer migrated from the original VR emotion project. No Unity scenes were copied or modified.

## Main prefab

Use `Assets/RocketboxTest/VRME_Rocketbox_Female_Adult_01.prefab` in a scene. The prefab already contains:

- `VrmeAtticClient`: microphone input, websocket conversation, reply audio, interaction context, and backend condition sync.
- `AvatarDominanceBehaviorController`: warm/cold nonverbal behaviour and a fixed 1.62 m avatar eye-height target.
- `AvatarGroundAligner`: one-time floor alignment without continuous drifting.
- Face-camera, procedural idle, speech animation, and audio-driven mouth components.

## Perception and participant rig

Add `CameraPoseSender` to the XR rig or its camera object and assign `cameraRig` to the headset camera/rig. It records head pose, eye gaze, face-expression availability, attended objects, and the voice-turn perception summary consumed by `VrmeAtticClient`.

Add `ParticipantRigHeightCalibrator` to the XR origin when participant eye height also needs to be normalized to 1.62 m. This is separate from Avatar height normalization.

`VrmeAtticClient` automatically creates attention-only `InteractionTracker` targets for the conversational Avatar and can attach trackers to relevant interactable objects. Add `InteractionTracker` manually when an object needs a stable display name or explicit tracking settings.

## Required runtime configuration

- Meta XR, OpenXR, XR Management, and XR Interaction Toolkit packages are declared in `Packages/manifest.json`.
- Eye tracking, face tracking, body tracking, microphone permission, OpenXR loaders, and Meta XR runtime settings were migrated under `Assets/Oculus`, `Assets/XR`, `Assets/MetaXR`, `Assets/Resources`, and `Assets/Plugins/Android`.
- The voice backend defaults to `ws://127.0.0.1:8080/`; change `VrmeAtticClient.serverUrl` on the prefab instance when the backend runs elsewhere.
- Set `PlayerData.avatarCondition` to `warm`, `cold`, or `backend`. Leaving it as `backend` lets the server choose the condition.

## Experimental counterbalance and heart rate

- The six experimental scenes use `AvatarConditionCounterbalance`: each participant receives three warm and three cold scenes; adjacent numeric participant IDs receive complementary assignments. `Tutorial_Interaction`, `Real`, and `EndScene` are excluded.
- The complete assignment is exported once per session under `Application.persistentDataPath/CounterbalanceData/<participantId>/AvatarConditionAssignment_<sessionId>.json`. Survey and camera-pose exports also retain the active `avatarCondition` per scene.
- `BackendHeartRateClient` polls `http://127.0.0.1:8080/heart-rate`. Fresh Polar H10 samples replace the old unavailable/zero entries in `CameraPoseSender.heartRateSamples`; disconnects remain explicit rather than carrying the last BPM forward.

## Migration boundary

The package intentionally contains no `.unity` files. Scene geometry, teleport visuals, cubes, and authored scene placement remain outside this migration.
