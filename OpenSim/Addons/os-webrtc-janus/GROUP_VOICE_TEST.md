# Group voice invitation test build

Based on the deployed WebRTC module release v0.9.3.1-webrtc.1, including the existing
credential key, optional RPC key, P2P calls and conference handling.

Changes: a group call now sends signed invitations to online group members with
voice rights. Invitations use the existing group ID, group-<UUID> room and
GROUP_CHAT_SESSION type. Accepting joins using the recipient's own credential.
No new configuration or server change is required.

The same module must be installed on the caller and recipient simulators for
cross-simulator delivery. Credential-only and two-key modes are both supported.
Old modules can still join manually but cannot display this new cross-region
group notification. Requests are throttled and delivery timestamps/nonces are
checked. Membership/voice rights are rechecked by the destination.

This build does not change WolfVoice or its spatial-axis correction.
It does not implement ad-hoc conferences. It does not invent viewer connection
state events: Firestorm derives connected state from its voice provider.
The reported erroneous "no answer" message on hangup remains to be verified in
a live test. If it persists, capture the actual error and Firestorm voice logs;
the notification alone does not establish the source of that shutdown error.

## First test
Stop and back up one test simulator. Replace WebRtcVoiceRegionModule.dll and its
matching PDB, then restart. Test proximity and a private call first.
With two group members on that simulator, start a group call from only one
avatar. The other should receive a group invitation, accept it and exchange audio.
Check reject, hangup and return to local voice. Then test two updated simulators.
Logs contain [GROUP CALL] for notification targets and destination delivery.

The package contains three matching DLL/PDB pairs for provenance. Only the region
module has a source change; do not replace other OpenSim libraries.
This is a candidate for testing, not a stable release.
