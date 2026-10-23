# WebRTC modules for OpenSimulator 0.9.3.1

Release v0.9.3.1-webrtc.1. Module sources: intelligentwolf/os-webrtc-janus 7f08ce453fc5938beea12c452348d46b3ed1088e, compiled against OpenSim eb01e50a678ce69c67bf7f9cd8865a28f7d2224a (.NET 8).

## Contents and installation
The archive contains WebRtcVoice, WebRtcVoiceRegionModule and WebRtcVoiceServiceModule DLL/PDB pairs. It is not a full OpenSim distribution.

Stop the simulator, back up these six files and the existing dedicated WebRTC INI, and replace the matching DLL/PDB pairs in bin. Configure the existing [WebRtcVoice] section with the voice server URL and CredKeyFile, then restart from bin. The .ini.example file is a template, not a ready-made grid configuration. Keep the existing WebRtcJanusService.dll if present.

Use a matching WolfVoice 0.2.0 server. Private calls require a credential key shared privately by the grid administrator. No actual key is included.
The earlier one-DLL prototype is superseded and must not be combined with this release.

## Validation
The full OpenSim solution build succeeded. The operator confirmed local voice, participant dots, per-user volume and private IM voice calls, then updated and restarted the hosted simulators.
Group voice is implemented upstream but has not been confirmed by these operator tests. Ad-hoc conferences require an additional external service and are not enabled by this package.
Cross-simulator invitations are implemented; a separate cross-simulator test was not reported.
Binaries were built from d408fdfc14a805bac732e4a90cce73230d4bff4e (Actions run 39933468262).
No grid secrets, user data, private INI or voice key are included.
