using System;
using WebRtcVoice;
using System.Text.Json;

static class Program
{
    static int checks;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
    }
    static void Main()
    {
        const string key = "credential-test-only";
        const string relay = "different-relay-test-only";
        Guid group = Guid.Parse("10000000-0000-4000-8000-000000000001");
        Guid caller = Guid.Parse("20000000-0000-4000-8000-000000000002");
        Guid recipient = Guid.Parse("30000000-0000-4000-8000-000000000003");
        long now = 1800000000;
        GroupVoiceInvitation invite = new GroupVoiceInvitation
        {
            Group = group, Caller = caller, Recipient = recipient,
            CallerName = "Caller", GroupName = "Existing group", Time = now, Nonce = Guid.NewGuid()
        };
        bool Decode(string data, string cred, string legacy, Guid from, Guid to, Guid session, long clock)
            => GroupVoiceInvitation.TryDecode(data, cred, legacy, from, to, session, clock, out _);
        string wire = GroupVoiceInvitation.Encode(invite, key);
        Check(Decode(wire, key, "", caller, recipient, group, now), "credential-only deployment works");
        Check(!Decode(wire, relay, "", caller, recipient, group, now), "wrong key refused");
        Check(Decode(wire, relay, key, caller, recipient, group, now), "explicit legacy key accepted");
        Check(!Decode(wire, key, "", recipient, recipient, group, now), "forged sender refused");
        Check(!Decode(wire, key, "", caller, caller, group, now), "credential bound to recipient");
        Check(!Decode(wire, key, "", caller, recipient, Guid.NewGuid(), now), "credential bound to group");
        Check(!Decode(wire, key, "", caller, recipient, group, now + 61), "expired invitation refused");
        Check(!Decode(wire, key, "", caller, recipient, group, now - 61), "future invitation refused");
        Check(!Decode(wire.Replace("Caller", "Attacker"), key, "", caller, recipient, group, now), "tampered names refused");
        Check(!Decode("not json", key, "", caller, recipient, group, now), "invalid JSON refused");
        Check(!Decode(new string('x', 17000), key, "", caller, recipient, group, now), "oversized envelope refused");
        Check(!Decode("{\"Payload\":null}", key, "", caller, recipient, group, now), "null payload refused");
        invite.Nonce = Guid.Empty;
        Check(!Decode(GroupVoiceInvitation.Encode(invite,key), key, "", caller, recipient, group, now), "nonce required");
        invite.Nonce = Guid.NewGuid();
        invite.Recipient = caller;
        Check(!Decode(GroupVoiceInvitation.Encode(invite,key), key, "", caller, caller, group, now), "self invitation refused");
        invite.Recipient = recipient;
        invite.Group = Guid.Empty;
        Check(!Decode(GroupVoiceInvitation.Encode(invite,key), key, "", caller, recipient, Guid.Empty, now), "group required");
        invite.Group = group;
        invite.CallerName = new string('x',257);
        Check(!Decode(GroupVoiceInvitation.Encode(invite,key), key, "", caller, recipient, group, now), "name limit enforced");
        bool missingKeyRefused = false;
        try { GroupVoiceInvitation.Encode(invite, ""); } catch (ArgumentException) { missingKeyRefused = true; }
        Check(missingKeyRefused, "cannot sign with empty key");
        Console.WriteLine(checks + " group voice invitation checks passed");
    }
}
