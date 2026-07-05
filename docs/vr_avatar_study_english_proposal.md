# Experimental Design and Potential Research Framework

This project builds on six VR emotion-elicitation scenes and extends them with embodied conversational avatars. The overall goal is to examine how different avatar roles influence users' emotional experience, scene exploration, perceived agency, social presence, and interaction behavior in VR. The two planned papers can share the same system design and measurement framework, but they should answer different research questions.

The general design is a mixed design. The six VR scenes are treated as a within-subjects factor, because every participant experiences the same set of emotional scenarios. The avatar condition is treated as a between-subjects factor, because each participant should interact with one avatar role to avoid carry-over effects between different social behaviors.

## 1. Overall System Design

Participants enter six VR emotional scenes. In each scene, they can look around, move, interact with objects, and, in avatar conditions, have free conversation with an embodied conversational avatar. The system records user speech, avatar responses, latency, user position, head orientation, gaze or head-based attention, object interaction events, and attention toward the avatar.

The key manipulation is not the avatar's visual appearance. Instead, the conditions differ in the avatar's access to context, level of social presence, guidance behavior, and proactive agency.

Candidate avatar conditions:

1. No-avatar baseline: no digital human is present. Participants only experience the original VR scene and object interactions.
2. Observer Avatar: the avatar is present and can answer simple user questions, but it does not read scene information, gaze, user position, or interaction history. It does not guide exploration. This condition serves as a basic embodied social presence baseline.
3. Context-aware Guide: the avatar reads scene information, user position, gaze or head direction, recent interactions, and key object states. It provides situated explanations and concrete suggestions, such as noticing what the user is looking at or suggesting a nearby interaction object.
4. Exploratory Encourager: the avatar does not read detailed scene information and does not refer to specific objects. Instead, it uses general encouragement, companionship, and open-ended prompts to motivate users to explore more actively.
5. Proactive Social Companion: the avatar can initiate interaction at appropriate moments, such as when the user is silent, stays in one area for a long time, looks toward the avatar, or appears uncertain. The proactive behavior should remain lightweight and should not interrupt the user's exploration too often.

## 2. Unified Design Controls

The two papers should use a consistent avatar setup so that the main manipulation is the avatar role, rather than appearance, voice, gender, or spatial placement.

### Avatar appearance

All avatar conditions should use the same female-presenting Rocketbox avatar. The purpose is not to study gender differences. The purpose is to control visual appearance across conditions and reduce possible confounds from avatar attractiveness, realism, familiarity, or uncanny-valley effects. Compared with a highly personalized ReadyPlayerMe avatar, Rocketbox provides a more standardized, controlled, and less individually stylized character. Using one consistent avatar allows the study to attribute differences mainly to role, context awareness, and agency.

### Avatar position and distance

The avatar should appear at a fixed relative position or a fixed distance from the user, for example slightly in front of and to the side of the user. This position should keep the avatar visible without blocking key objects. A fixed position is important because avatar distance can change perceived social pressure, intimacy, co-presence, and comfort. It also makes gaze toward the avatar and distance-to-avatar measures comparable across participants and conditions.

### Voice and tone

All avatar conditions should use the same base voice if possible. This avoids confounding the role manipulation with differences in voice identity. Differences between conditions can be expressed through prompt style, response length, speaking style, and prosody settings. The Observer should be short and neutral. The Context-aware Guide should be concise but information-rich. The Exploratory Encourager should sound warm and motivating. The Proactive Social Companion should be socially responsive but not overly talkative.

### Interaction and gaze recognition

The system should distinguish between two types of attention target:

- Scene-object attention: attention to interactive or task-relevant objects such as a flashlight, stone, plane, door, or other scenario-specific objects.
- Social attention: attention to the avatar as a social agent.

The avatar should not be treated as just another scene object. It should be registered as a social attention target. Useful measures include how often the user looks at the avatar, total dwell time on the avatar, whether the user looks at the avatar before or after speaking, and whether avatar attention predicts social presence, warmth, trust, or perceived support.

## 3. Paper 1: Avatar Presence and Context Awareness

Paper 1 focuses on how adding an embodied avatar, and then making that avatar context-aware, changes emotional VR experiences and exploration behavior.

Suggested conditions:

1. No-avatar baseline
2. Observer Avatar
3. Context-aware Guide

The logic of Paper 1 is a three-level comparison:

- No-avatar baseline: the user relies only on the scene and object interactions.
- Observer Avatar: the system adds embodied social presence and basic conversation.
- Context-aware Guide: the avatar adds scene understanding, gaze or head-attention grounding, and situated guidance.

Research Questions:

RQ1. How does the presence of an embodied conversational avatar influence users' emotional experiences in VR emotion-elicitation scenes compared with no avatar?

RQ2. Does a context-aware avatar guide improve users' exploration and object interaction compared with a passive observer avatar?

RQ3. How does context-aware guidance affect perceived social presence, trust, helpfulness, competence, autonomy, and intrusiveness?

RQ4. How do gaze, head direction, user position, and object interaction logs explain users' emotional and behavioral responses to context-aware avatar guidance?

Expected outcomes:

H1. Observer Avatar and Context-aware Guide will produce higher perceived social presence than the No-avatar baseline.

H2. Context-aware Guide will lead to more task-relevant object interactions, longer dwell time on key objects, and shorter time-to-first-interaction than Observer Avatar.

H3. Context-aware Guide will be rated higher in perceived competence, helpfulness, intelligence, and trust than Observer Avatar.

H4. Context-aware Guide may reduce perceived autonomy or increase perceived intrusiveness if the guidance feels too directive, so autonomy and intrusiveness should be measured explicitly.

H5. Better alignment between the user's gaze or object attention and the avatar's response content will predict higher helpfulness and trust.

## 4. Paper 2: Avatar Support and Social Agency

Paper 2 focuses on how different avatar social roles shape exploration, autonomy, emotional support, and social agency.

Suggested conditions:

1. Observer Avatar
2. Context-aware Guide
3. Exploratory Encourager
4. Proactive Social Companion

If the sample size allows, the No-avatar baseline can also be included. However, Paper 2 may be cleaner if it focuses specifically on avatar roles rather than comparing avatar and no-avatar experiences.

The logic of Paper 2 is to compare different kinds of avatar involvement:

- Observer Avatar: low involvement and low guidance.
- Context-aware Guide: high informational support and concrete scene guidance.
- Exploratory Encourager: high encouragement and emotional support without specific scene knowledge.
- Proactive Social Companion: high social agency through timely avatar-initiated interaction.

Research Questions:

RQ1. How do different conversational avatar roles influence perceived social presence, warmth, competence, trust, and user autonomy in VR emotional scenes?

RQ2. Does general encouragement without scene-specific knowledge increase users' willingness to explore and emotional comfort compared with passive observation or context-aware guidance?

RQ3. How does proactive avatar interaction affect engagement, conversation frequency, perceived support, autonomy, and intrusiveness?

RQ4. What behavioral indicators, such as conversation frequency, gaze toward the avatar, movement coverage, and object interaction, distinguish users' responses to different avatar roles?

RQ5. How do different avatar roles shape emotional outcomes, including valence, arousal, and dominance, across positive and negative VR scenes?

Expected outcomes:

H1. Exploratory Encourager will produce higher warmth, companionship, perceived encouragement, and emotional support than Observer Avatar.

H2. Context-aware Guide will produce higher perceived competence, intelligence, and task helpfulness than Observer Avatar and Exploratory Encourager.

H3. Proactive Social Companion will increase engagement, gaze toward the avatar, and conversation frequency, but may also increase intrusiveness or reduce autonomy if the timing of intervention is inappropriate.

H4. Exploratory Encourager may increase exploration willingness and perceived autonomy because it encourages exploration without directing users to specific objects.

H5. Gaze toward the avatar and conversation frequency will predict social presence and warmth, while gaze-object-response alignment will predict perceived competence and helpfulness.

## 5. Unified Measurement Framework

Both papers can use the same mixed-method evaluation framework, combining questionnaires, system logs, and multimodal behavioral data.

### A. Measures after each scene

Self-Assessment Manikin:

After each scene, participants complete the Self-Assessment Manikin to measure:

- Valence
- Arousal
- Dominance

This keeps the study compatible with the existing six-scene VR emotion-elicitation framework and allows direct comparison of emotional changes across avatar conditions.

Short scene-level Likert items:

After each scene, participants can answer a small set of 7-point Likert items to avoid survey fatigue:

- I felt motivated to explore this scene.
- I felt I knew what I could interact with in this scene.
- I felt in control of my exploration.
- The avatar helped me engage with the scene.
- The avatar distracted me from the scene.
- The avatar's response matched what I was doing in the scene.

### B. Measures after all scenes

Social presence and co-presence:

Measure whether users felt that the avatar was present with them, aware of them, and socially responsive. Relevant dimensions include co-presence, attentional allocation, perceived message understanding, and perceived social presence.

Avatar perception:

Measure perceived intelligence, likeability, animacy, comfort, safety, and naturalness. These items help identify whether the avatar is perceived as a believable social agent or as an awkward system component.

Warmth, competence, and discomfort:

These dimensions help separate the intended roles. The Exploratory Encourager is expected to score higher on warmth. The Context-aware Guide is expected to score higher on competence. Discomfort should be measured to detect possible uncanny-valley or social unease effects.

Trust and reliability:

Measure whether users trusted the avatar's suggestions, whether the responses felt reliable, and whether the avatar seemed to understand what the user was doing.

Autonomy and workload:

Measure whether users still felt free to explore, and whether the avatar increased cognitive load, pressure, distraction, or interruption.

Manipulation checks:

These items confirm whether participants perceived the role manipulation as intended:

- The avatar behaved like a passive observer.
- The avatar understood what I was looking at or interacting with.
- The avatar gave concrete guidance about the scene.
- The avatar encouraged me to explore.
- The avatar interacted with me proactively.
- The avatar allowed me to explore freely.

## 6. System Logs and Multimodal Data

Conversation logs:

- Turn index
- User transcript
- Avatar response text
- User speech duration
- Avatar response duration
- User word count
- Avatar word count
- Number of conversation turns
- Whether the response mentioned a scene object
- Whether the response mentioned the avatar or user state
- Whether scene context was used
- Whether the response was user-initiated or avatar-initiated

Latency logs:

- Total latency
- STT latency
- LLM latency
- TTS first-audio latency
- TTS total latency
- First-audio time
- Model or provider used
- Streaming status
- Failure or retry count

Object interaction logs:

- Object name
- Object category
- Interaction type, such as selected, grabbed, touched, or triggered
- First interaction time
- Interaction count per object
- Interaction sequence
- Whether task-relevant objects were explored
- Scene completion time

Spatial logs:

- User position
- Head orientation
- Head forward vector
- Movement path length
- Area or coverage proxy
- Distance to avatar
- Distance to key objects
- Time spent near each object

Gaze and head-attention logs:

- Eye-tracking availability
- Whether head-forward fallback was used
- Gaze hit object
- Gaze hit avatar
- Attended objects
- Dwell time per object and avatar
- Fixation count
- Maximum continuous dwell
- Gaze-object-speech alignment, meaning what the user was looking at while speaking

Physiological and affective logs, if device support is reliable:

- Heart rate or HRV
- Facial expression blendshape values
- Eye-tracking confidence
- Timestamp alignment with scene events, dialogue turns, and object interactions

## 7. Potential Contributions

Paper 1 contributes by extending existing VR emotion-elicitation and object-interaction research with embodied conversational avatars. It tests whether avatar presence and context-aware guidance change emotional experience, exploration behavior, and perceived social presence.

Paper 2 contributes by comparing different avatar social roles. It examines how informational guidance, general encouragement, and proactive social interaction affect social presence, autonomy, trust, intrusiveness, and exploration behavior.

The overall contribution is a VR conversational-avatar evaluation framework grounded in free conversation, gaze and object attention, user position, and multimodal behavioral logs. This framework can help explain how digital humans shape emotional and social experiences in interactive VR scenes.
