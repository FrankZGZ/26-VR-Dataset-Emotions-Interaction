Rocketbox test import
=====================

Imported one Microsoft Rocketbox avatar for visual testing only.

Preview path in Unity:

Assets/RocketboxTest/Microsoft-Rocketbox/Assets/Avatars/Adults/Female_Adult_01/Export/Female_Adult_01.fbx

VRME test prefab:

Assets/RocketboxTest/VRME_Rocketbox_Female_Adult_01.prefab

The prefab is generated automatically by:

Assets/Editor/VRMERocketboxPrefabBuilder.cs

If Unity does not generate it automatically after importing, run:

VRME > Create Rocketbox Test Prefab

Also included:

- Female_Adult_01_facial.fbx
- Full texture folder for Female_Adult_01
- Microsoft Rocketbox README and MIT license
- Assets/Editor/FixRocketboxMaxImport.cs

Notes:

- This does not replace the current Ready Player Me avatar.
- The generated Rocketbox prefab has the old VRME voice client, voice AudioSource, face-camera behavior, and avatar animation controller.
- Unity will generate the .meta files after opening/importing the project.
- The Rocketbox import script has been adjusted to import this avatar as Humanoid so it can use the existing VRME animation controller.
