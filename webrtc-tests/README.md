# same-scene P2P prototype

Experimental, enabled by default in this test build; no configuration change is
required. Set `EnableP2PVoice = false` in the existing WebRtcVoice section to disable
it. Validate on an isolated test simulator before any production deployment.

Scope: two root avatars in one scene, server-created private channel, invitation
via EventQueue, credentials returned on call, and provisioning membership checks.
Explicit private viewer logout removes signalling state so a later call can invite
again. Existing local-voice code is used when the switch is explicitly false.

Known validation gaps: actual Firestorm invitation/acceptance, simultaneous calls,
pending-call cancellation, caller notification after refusal, reset/logout ordering,
teleports, and repeated calls. Cross-scene, Hypergrid and group/conference signalling
are outside this prototype. Do not claim complete private-call support.

The standalone P2PGuards harness exercises access to the compiled module's private
channel authorization logic. It is not a viewer or signalling integration test.
