// Group voice invitation transport. Distributed under the module's MPL 2.0 licence.
using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebRtcVoice
{
    public sealed class GroupVoiceInvitation
    {
        public Guid Group { get; set; }
        public Guid Caller { get; set; }
        public Guid Recipient { get; set; }
        public string CallerName { get; set; }
        public string GroupName { get; set; }
        public long Time { get; set; }
        public Guid Nonce { get; set; }

        private sealed class Envelope
        {
            public string Payload { get; set; }
            public string Signature { get; set; }
        }

        public static string Encode(GroupVoiceInvitation invite, string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A signing key is required");
            string payload = JsonSerializer.Serialize(invite);
            return JsonSerializer.Serialize(new Envelope { Payload = payload, Signature = Sign(payload, key) });
        }

        private static string Sign(string payload, string key)
        {
            using HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("groupvoice1\n" + payload))).ToLowerInvariant();
        }

        private static bool Matches(string payload, string signature, string key)
        {
            if (string.IsNullOrEmpty(key) || signature == null || signature.Length != 64) return false;
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Sign(payload, key)), Encoding.ASCII.GetBytes(signature));
        }

        // Bind the authenticated envelope to the grid IM routing fields as well as its destination.
        // The caller and recipient's group membership/voice powers are checked by the receiving region.
        public static bool TryDecode(string encoded, string key, string legacyKey,
                Guid from, Guid to, Guid session, long now, out GroupVoiceInvitation invite)
        {
            invite = null;
            if (encoded == null || encoded.Length > 16384) return false;
            try
            {
                Envelope envelope = JsonSerializer.Deserialize<Envelope>(encoded);
                if (envelope?.Payload == null || envelope.Payload.Length > 8192
                    || (!Matches(envelope.Payload, envelope.Signature, key)
                        && !Matches(envelope.Payload, envelope.Signature, legacyKey))) return false;
                GroupVoiceInvitation decoded = JsonSerializer.Deserialize<GroupVoiceInvitation>(envelope.Payload);
                if (decoded == null || decoded.Group == Guid.Empty || decoded.Caller == Guid.Empty
                    || decoded.Recipient == Guid.Empty || decoded.Nonce == Guid.Empty
                    || decoded.Caller == decoded.Recipient || decoded.Caller != from
                    || decoded.Recipient != to || decoded.Group != session
                    || decoded.Time < now - 60 || decoded.Time > now + 60
                    || decoded.CallerName == null || decoded.CallerName.Length > 256
                    || decoded.GroupName == null || decoded.GroupName.Length > 256) return false;
                invite = decoded;
                return true;
            }
            catch (JsonException) { return false; }
        }
    }
}
