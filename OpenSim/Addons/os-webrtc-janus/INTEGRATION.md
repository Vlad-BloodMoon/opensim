# WebRTC 1.2.0 integration test

OpenSim base: eb01e50a678ce69c67bf7f9cd8865a28f7d2224a.
Module sources: intelligentwolf/os-webrtc-janus 7f08ce453fc5938beea12c452348d46b3ed1088e.

This archive contains the three matching modules and debug symbols, not an OpenSim distribution.
Use with the WolfVoice 0.2.0 integration test instance. Do not combine with the earlier one-DLL prototype.
Keep the existing dedicated WebRTC INI; add RpcKeyFile and CredKeyFile pointing to files whose contents match the test voice server. Change WebRtcVoiceServerURI for the test simulator only.

Back up the current three DLL/PDB pairs and configuration, stop the test simulator, replace these files and restart it. No installer, database change or modification to the shared voice service is included.

Test local audio, participant dots, per-user gain/mute, Reset Voice and private invitation/accept/decline/hangup on Displayname. Cross-simulator calls need both simulators updated. Ordinary group voice is implemented; ad-hoc conferences need an additional external service.

Compiled against the base listed above; real Firestorm and deployed binary compatibility still require testing.
