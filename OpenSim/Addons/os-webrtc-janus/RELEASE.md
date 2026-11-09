# BloodMoon WebRTC voice modules — OpenSimulator 0.9.3.1

**Release: `v0.9.3.1-webrtc.3` · .NET 8 · Linux / OpenSimulator · BloodMoon-only server compatibility**

This is the **complete matching WebRTC module package** for BloodMoon's modified WolfVoice server. It extends `v0.9.3.1-webrtc.2` with the tested ad-hoc group-IM conference text and optional WebRTC voice functionality.

> **Compatibility warning.** The complete feature set requires **the BloodMoon WolfVoice server**, which has the spatial-axis correction and BloodMoon's group-voice extensions. **The upstream WolfVoice server has not yet incorporated those BloodMoon changes.** Therefore this package is **not advertised as fully compatible with the stock / upstream WolfVoice server**, and it is not a generic Janus package. Supported simulators must also be **authorized on the BloodMoon voice infrastructure**. Do not expose or redistribute shared private keys.

## What's included

The ZIP includes the following **matching release build** files:

| Binary | Responsibility |
| --- | --- |
| `WebRtcVoice.dll` + `WebRtcVoice.pdb` | WebRTC voice service connector |
| `WebRtcVoiceRegionModule.dll` + `WebRtcVoiceRegionModule.pdb` | Regional capabilities, voice, ad-hoc text conferences, call invitations |
| `WebRtcVoiceServiceModule.dll` + `WebRtcVoiceServiceModule.pdb` | WebRTC service integration |

Also included: `wolfvoice.ini.example`, `README.md`, `BUILD_COMMIT.txt`, and `SHA256SUMS`.

**The regional DLL and PDB are derived from the latest compiled AdHoc maintenance binaries** distributed in [OpenSimulator-AdHocConferences v0.1.0](https://github.com/Vlad-BloodMoon/OpenSimulator-AdHocConferences/releases/tag/v0.1.0), validated on two BloodMoon simulators. The other two matching module DLL/PDB pairs are compiled from this `v0.9.3.1-webrtc.2`-derived source branch. A CI check also verifies that these built binaries exist and that the original tested DLL/PDB are copied into the published archive. The release ZIP is built in GitHub Actions and contains no credentials.

## Maintenance refresh — 7 November 2026 (same release tag and download filename)

- Conference system notifications now display **OpenSimulator** instead of **Wolf Territories**.
- The associated [PHP conference API](https://github.com/Vlad-BloodMoon/OpenSimulator-AdHocConferences/blob/main/web/wolf_api.php) now allows users still marked `invited` to be re-invited after a crash/relogin while preserving joined participants.
- **PHP deployment is separate:** download/deploy the updated `wolf_api.php` on the host of the central conference service; this simulator ZIP does not install PHP.
- The brand-name correction was compiled successfully in [AdHoc run #40](https://github.com/Vlad-BloodMoon/OpenSimulator-AdHocConferences/actions/runs/42413642157). **First-reconnect recovery requires further live testing:** it is possible for a reconnected avatar to remain incorrectly marked `joined` until a subsequent reconnect.
- The public release name and ZIP filename stay identical, while the actual DLL and SHA-256 values change. Re-download the updated checksum files.

## Features tested on BloodMoon

- Proximity / spatial voice (including BloodMoon spatial-axis fix in the **server**).
- Private voice calls in avatar IMs.
- Standard OpenSimulator group voice invitations and participant rejoin handling.
- **Ad-hoc IM conferences** for three or more avatars, independent of group membership.
- Conference text and invitations **across different simulators**, with no self-message duplication.
- **Optional conference voice**: joining the text conference does not automatically force voice.
- Independent hang-up and rejoin while keeping the text IM open, without interrupting others or showing them misleading leave/rejoin notices.

These are **BloodMoon tests**, not exhaustive validation on arbitrary external OpenSimulator forks or upstream WolfVoice releases.

## Installation — every participating simulator

1. Make sure the simulator is OpenSimulator **0.9.3.1, .NET 8**, with the same WebRTC/WolfVoice baseline.
2. **Stop the simulator** and back up its existing `bin/` voice DLL/PDB files, `wolfvoice.ini`, and key configuration.
3. Extract the ZIP and copy the **three matching DLL/PDB pairs** into `bin/`, replacing the corresponding files. Do **not** remove other OpenSimulator modules. Keep `WebRtcJanusService.dll` if already installed.
4. Compare the included `wolfvoice.ini.example` with your current `wolfvoice.ini`. **Merge settings manually**; never overwrite real server URLs or key paths with example placeholders. Keep the existing configured `WebRtcVoiceServerURI`, `RpcKeyFile` and `CredKeyFile` as appropriate.
5. In `[WebRtcVoice]`, configure the URL of **your own separately installed conference PHP/MySQL API** from [OpenSimulator-AdHocConferences](https://github.com/Vlad-BloodMoon/OpenSimulator-AdHocConferences). The following domain is a placeholder, not a public BloodMoon service:

   ```ini
   ConferenceApiUrl = https://conference.example.org/adhocconf/wolf_api.php
   ```

6. Restart the simulator, verify ordinary spatial/private/group voice, then test ad-hoc IM and optional group call.
7. **Repeat on every simulator** where conference participants may reside. Both the updated DLLs **and** `ConferenceApiUrl` are mandatory for cross-simulator invitations.

**Compatibility note:** For independent installations, use [BloodMoon Voice v1.0.0](https://github.com/Vlad-BloodMoon/BloodMoon-Voice/releases/tag/v1.0.0) and your own AdHoc API. This is for the **BloodMoon-modified WolfVoice server and trusted, explicitly approved simulators**. The endpoint is not open access; a shared voice credential key and allowed server IP are required. Contact the BloodMoon administrator through a private channel for authorization. **Never post a key in a GitHub issue or upload it with diagnostics.**

## Architecture and security

- Firestorm sends ad-hoc `ChatSessionRequest` / IM dialog 17.
- The regional module manages conference membership through BloodMoon's authenticated `wolf_api.php` + MySQL service.
- Internal signed grid IMs deliver invitations and messages to the recipient's root simulator.
- The WolfVoice server transports audio in `conf-<session UUID>` channels.
- `CredKeyFile` supplies the existing shared signing key; no additional `SimSecretPath` is required by default.

**Security boundary:** The shared credential-key/IP allowlist model trusts the simulator administrator. The HMAC does **not** independently verify that an avatar really authorized an action; do not extend this setup to untrusted operators without stronger per-simulator/agent authentication.

Details on the central API, its MySQL table dependencies and more: [AdHoc project](https://github.com/Vlad-BloodMoon/OpenSimulator-AdHocConferences), [installation English](https://github.com/Vlad-BloodMoon/OpenSimulator-AdHocConferences/blob/main/docs/INSTALLATION.md), [installation French](https://github.com/Vlad-BloodMoon/OpenSimulator-AdHocConferences/blob/main/docs/INSTALLATION.fr.md).

## Reproducibility

The **full source** of both updated region-module files is committed to the dedicated branch `release/bloodmoon-webrtc.3`, based on `fix/group-voice-invitations` / tag `v0.9.3.1-webrtc.2` (commit `07fca6e6e50d8998e61f3fb066f39013281216ba`). These modifications correspond to the seven tested patches in the AdHoc repository. Build using `./runprebuild.sh` then `dotnet build --configuration Release OpenSim.sln` with .NET 8.

The GitHub release is created from this source branch via [the CI release workflow](https://github.com/Vlad-BloodMoon/opensim/actions), and the tag points to the source commit used to package it. `SHA256SUMS` provides an integrity check for the distributed files.

## Credits

Thanks to **Lone Wolf (WolfGrid)** for WolfVoice and WebRTC OpenSimulator integration; to the OpenSimulator and Firestorm developers; and to BloodMoon's residents and testers.

This is a **BloodMoon-specific integration release**, not an upstream WolfVoice server release.
