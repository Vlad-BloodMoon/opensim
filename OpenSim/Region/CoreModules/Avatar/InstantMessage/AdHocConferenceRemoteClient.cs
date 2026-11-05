/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * Licensed under the OpenSimulator BSD license.
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenMetaverse;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.CoreModules.Avatar.InstantMessage
{
    /// <summary>Signed connection to the grid-wide temporary IM session registry.</summary>
    internal sealed class AdHocConferenceRemoteClient
    {
        private static readonly HttpClient s_client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private readonly string m_url;
        private readonly string m_key;

        internal AdHocConferenceRemoteClient(string url, string keyFile)
        {
            m_url = url;
            m_key = File.ReadAllText(keyFile).Trim();
            if (m_key.Length < 32)
                throw new InvalidOperationException("AdHocConferences shared key is too short");
        }

        internal bool TryRequest(Scene scene, string action, UUID actor, UUID session,
            ICollection<UUID> invitees, out AdHocConferenceSessions.Snapshot snapshot)
        {
            snapshot = null;
            List<string> participants = new List<string>();
            if (invitees != null)
                foreach (UUID id in invitees)
                    participants.Add(id.ToString());

            string body = JsonSerializer.Serialize(new
            {
                Action = action,
                Session = session.ToString(),
                Invitees = participants
            });
            string time = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            string region = scene.RegionInfo.RegionID.ToString();
            string signature;
            using (HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(m_key)))
                signature = Convert.ToHexString(hmac.ComputeHash(
                    Encoding.UTF8.GetBytes(region + "\n" + actor + "\n" + time + "\n" + body)));

            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, m_url))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.Add("X-Adhoc-Region", region);
                    request.Headers.Add("X-Adhoc-Agent", actor.ToString());
                    request.Headers.Add("X-Adhoc-Time", time);
                    request.Headers.Add("X-Adhoc-Signature", signature);

                    using (HttpResponseMessage response = s_client.SendAsync(request).GetAwaiter().GetResult())
                    {
                        if (!response.IsSuccessStatusCode)
                            return false;
                        string payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        using (JsonDocument json = JsonDocument.Parse(payload))
                        {
                            JsonElement root = json.RootElement;
                            if (!root.TryGetProperty("success", out JsonElement success) || !success.GetBoolean()
                                || !root.TryGetProperty("members", out JsonElement members))
                                return false;
                            AdHocConferenceSessions.Snapshot result = new AdHocConferenceSessions.Snapshot
                            {
                                SessionId = session,
                                OwnerId = UUID.Zero,
                                Members = new Dictionary<UUID, AdHocConferenceSessions.MemberState>()
                            };
                            foreach (JsonElement member in members.EnumerateArray())
                            {
                                if (UUID.TryParse(member.GetProperty("id").GetString(), out UUID id))
                                    result.Members[id] = member.GetProperty("joined").GetBoolean()
                                        ? AdHocConferenceSessions.MemberState.Joined
                                        : AdHocConferenceSessions.MemberState.Invited;
                            }
                            snapshot = result;
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fail closed: a temporarily unreachable registry must never
                // cause unverified conference membership or message delivery.
                return false;
            }
        }
    }
}
