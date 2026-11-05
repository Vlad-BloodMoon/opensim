# OpenSimulator temporary multi-avatar IM conferences (development)

This add-on provides a shared membership registry for **text-only ad-hoc chat sessions**. It does not depend on WolfVoice, Discord, or group membership. OpenSimulator continues to deliver messages through its existing `IMessageTransferModule`.

## Components

- `Server/`: .NET 8 registry to run on the grid's ROBUST host. Membership is held in memory and expires after 24 hours of inactivity. Restarting the service loses active sessions.
- `OpenSim.Region.CoreModules`: creates sessions, handles `ChatSessionRequest`, validates membership, and sends IMs to each joined participant.
- `OpenSim.Region.Framework`: interface used when an existing WebRTC module owns the `ChatSessionRequest` capability.

## Development configuration

Run the registry privately on the ROBUST VM (or behind TLS with firewall restrictions):

```bash
cd OpenSim/Addons/AdHocConferences/Server
ADHOC_SHARED_KEY='replace-with-a-long-random-shared-key' \
ASPNETCORE_URLS='http://127.0.0.1:9449' dotnet run
```

On *each* simulator, configure:

```ini
[AdHocConferences]
    ServiceUrl = https://your-private-robust-host/conference
    KeyFile = /absolute/path/to/private/adhoc_shared_key
```

`KeyFile` contains the same secret as `ADHOC_SHARED_KEY`, without quotes or a trailing newline. Never commit the key. A single shared registry is required across all simulator processes; a localhost-only listener cannot be reached by other VMs unless a private proxy is configured.

If WebRTC is enabled, the WebRTC `ChatSessionRequest` capability forwards text-conference operations to the IM module. Without WebRTC, the core IM module registers the capability itself.

## Intended first integration test

1. Start a temporary conference from Firestorm with avatars on **two separate region processes**.
2. Verify the conversation opens for the other avatar without a voice-call confirmation.
3. Verify messages in both directions, an additional participant, leave/close, and teleport to another region.
4. Repeat with WebRTC enabled and disabled, and check that ordinary IMs and persistent group chat still work.

**Not yet production-ready:** These changes have not been compiled or tested on a live simulator. The service uses an in-memory session registry and currently has no restart recovery or Hypergrid identity-validation policy. Keep it on an isolated test grid until the protocol and failure cases are validated.
