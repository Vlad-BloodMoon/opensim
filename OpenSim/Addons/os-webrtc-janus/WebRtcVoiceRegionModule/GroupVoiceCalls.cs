// Group voice invitations, using the existing credential key and grid IM transport.
// Distributed under the same MPL 2.0 licence as WebRtcVoiceRegionModule.cs.
using System;
using System.Collections.Generic;

using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using PresenceInfo = OpenSim.Services.Interfaces.PresenceInfo;

namespace WebRtcVoice
{
    public partial class WebRtcVoiceRegionModule
    {
        private const string GROUP_VOICE_MARK = "groupvoice1\n";
        private readonly object m_GroupVoiceLock = new object();
        private readonly Dictionary<UUID, DateTime> m_GroupVoiceNotified = new Dictionary<UUID, DateTime>();
        private readonly Dictionary<string, DateTime> m_GroupVoiceNonces = new Dictionary<string, DateTime>();
        private readonly Dictionary<UUID, (UUID Group, string Session, UUID Region)> m_GroupVoiceSessions = new();

        private void TrackGroupVoiceSession(UUID agent, UUID region, string channel, string session)
        {
            if (session.Length == 0 || !channel.StartsWith("group-", StringComparison.Ordinal)
                || !UUID.TryParse(channel.Substring(6), out UUID group) || group.IsZero()) return;
            lock (m_GroupVoiceLock)
                m_GroupVoiceSessions[agent] = (group, session, region);
        }

        private void ForgetGroupVoiceSession(UUID agent, string session)
        {
            lock (m_GroupVoiceLock)
                if (m_GroupVoiceSessions.TryGetValue(agent, out var current) && current.Session == session)
                    m_GroupVoiceSessions.Remove(agent);
        }

        private void OnGroupVoiceClientClosed(UUID agent, Scene scene)
        {
            lock (m_GroupVoiceLock)
                if (m_GroupVoiceSessions.TryGetValue(agent, out var current) && current.Region == scene.RegionInfo.RegionID)
                    m_GroupVoiceSessions.Remove(agent);
        }

        private const int GROUP_VOICE_CACHE_MAX = 4096;

        // Joining a call via an invitation uses its channel info directly, so it does not
        // request another "call". Suppress near-simultaneous manual starts in either region too.
        private bool ReserveGroupVoiceNotification(UUID group)
        {
            lock (m_GroupVoiceLock)
            {
                DateTime now = DateTime.UtcNow;
                PruneGroupVoiceCache(m_GroupVoiceNotified, now);
                if (m_GroupVoiceNotified.TryGetValue(group, out DateTime until) && until > now) return false;
                if (m_GroupVoiceNotified.Count >= GROUP_VOICE_CACHE_MAX) return false;
                m_GroupVoiceNotified[group] = now.AddSeconds(30);
                return true;
            }
        }

        private static void PruneGroupVoiceCache<T>(Dictionary<T, DateTime> cache, DateTime now)
        {
            List<T> expired = new List<T>();
            foreach (KeyValuePair<T, DateTime> item in cache)
                if (item.Value <= now) expired.Add(item.Key);
            foreach (T key in expired) cache.Remove(key);
        }

        private static bool MayReceiveGroupVoice(IGroupsModule groups, UUID group, UUID agent)
        {
            GroupMembershipData member = groups?.GetMembershipData(group, agent);
            return member != null && (member.GroupPowers & (ulong)GroupPowers.AllowVoiceChat) != 0;
        }

        private void QueueGroupVoiceInvitations(Scene scene, UUID caller, UUID group)
        {
            // Credentials are still returned even if notifications are throttled.
            if (!ReserveGroupVoiceNotification(group) || !AllowCall(caller)) return;
            Util.FireAndForget(delegate
            {
                try { SendGroupVoiceInvitations(scene, caller, group); }
                catch (Exception e)
                {
                    m_log.WarnFormat("{0}[GROUP CALL]: could not send invitations for {1}: {2}", logHeader, group, e.Message);
                }
            }, null, "GroupVoiceInvitations");
        }

        private void SendGroupVoiceInvitations(Scene scene, UUID caller, UUID group)
        {
            IGroupsModule groups = scene.RequestModuleInterface<IGroupsModule>();
            ScenePresence origin = scene.GetScenePresence(caller);
            if (origin == null || origin.IsChildAgent || !MayReceiveGroupVoice(groups, group, caller)) return;
            List<GroupMembersData> members = groups.GroupMembersRequest(origin.ControllingClient, group);
            if (members == null) return;
            HashSet<string> candidates = new HashSet<string>();
            foreach (GroupMembersData member in members)
                if (member.AgentID != caller && !member.AgentID.IsZero()
                    && (member.AgentPowers & (ulong)GroupPowers.AllowVoiceChat) != 0)
                    candidates.Add(member.AgentID.ToString());

            // Never enqueue invitations for offline members (or store them as offline IMs).
            // The presence service also covers other region processes.
            string[] ids = new string[candidates.Count];
            candidates.CopyTo(ids);
            PresenceInfo[] online = scene.PresenceService?.GetAgents(ids);
            if (online == null) return;
            IMessageTransferModule transfer = scene.RequestModuleInterface<IMessageTransferModule>();
            string callerName = CleanGroupVoiceName(AvatarName(scene, caller));
            string groupName = CleanGroupVoiceName(groups.GetGroupRecord(group)?.GroupName ?? "Group voice");
            HashSet<UUID> sent = new HashSet<UUID>();
            foreach (PresenceInfo presence in online)
            {
                if (!UUID.TryParse(presence.UserID, out UUID recipient)
                    || !candidates.Contains(recipient.ToString()) || !sent.Add(recipient)) continue;
                GroupVoiceInvitation invite = new GroupVoiceInvitation
                {
                    Group = group.Guid, Caller = caller.Guid, Recipient = recipient.Guid,
                    CallerName = callerName, GroupName = groupName,
                    Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Nonce = Guid.NewGuid()
                };
                string payload = GROUP_VOICE_MARK + GroupVoiceInvitation.Encode(invite, CredKey);
                GridInstantMessage message = new GridInstantMessage(scene, caller, callerName, recipient,
                    WOLF_CALL_DIALOG, false, string.Empty, group, false, Vector3.Zero,
                    Util.UTF8.GetBytes(payload), true);
                if (!TryDeliverGroupVoiceInvitation(message))
                    transfer?.SendInstantMessage(message, delegate(bool success) { });
            }
            m_log.InfoFormat("{0}[GROUP CALL]: {1} started group {2}; {3} online invitation target(s)",
                logHeader, caller, group, sent.Count);
        }

        private static string CleanGroupVoiceName(string name)
        {
            if (name == null) return string.Empty;
            System.Text.StringBuilder clean = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (!char.IsControl(c)) clean.Append(c);
                if (clean.Length == 256) break;
            }
            return clean.ToString();
        }

        private void OnIncomingGroupVoiceInvitation(GridInstantMessage message)
        {
            if (message.dialog != WOLF_CALL_DIALOG || message.binaryBucket == null || !CallsOn) return;
            if (message.binaryBucket.Length > 20000) return;
            string payload = Util.UTF8.GetString(message.binaryBucket);
            if (!payload.StartsWith(GROUP_VOICE_MARK, StringComparison.Ordinal)) return;
            TryDeliverGroupVoiceInvitation(message);
        }

        private bool TryDeliverGroupVoiceInvitation(GridInstantMessage message)
        {
            UUID recipient = new UUID(message.toAgentID);
            Scene[] scenes;
            lock (m_Scenes) scenes = m_Scenes.ToArray();
            foreach (Scene scene in scenes)
            {
                ScenePresence target = scene.GetScenePresence(recipient);
                if (target == null || target.IsChildAgent) continue;
                string payload = Util.UTF8.GetString(message.binaryBucket);
                if (!payload.StartsWith(GROUP_VOICE_MARK, StringComparison.Ordinal)) return true;
                string legacyKey = m_AcceptLegacyCredentials ? RpcKey : string.Empty;
                if (!GroupVoiceInvitation.TryDecode(payload.Substring(GROUP_VOICE_MARK.Length), CredKey, legacyKey,
                    message.fromAgentID, message.toAgentID, message.imSessionID,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out GroupVoiceInvitation invite)) return true;
                UUID group = new UUID(invite.Group);
                UUID caller = new UUID(invite.Caller);
                IGroupsModule groups = scene.RequestModuleInterface<IGroupsModule>();
                // Never ring someone who left the group or lacks voice rights. Check the caller too.
                if (!MayReceiveGroupVoice(groups, group, recipient) || !MayReceiveGroupVoice(groups, group, caller)) return true;
                IEventQueue queue = scene.RequestModuleInterface<IEventQueue>();
                if (queue == null) return true;
                lock (m_GroupVoiceLock)
                {
                    DateTime now = DateTime.UtcNow;
                    PruneGroupVoiceCache(m_GroupVoiceNonces, now);
                    string nonce = recipient.ToString() + ":" + invite.Nonce;
                    if (m_GroupVoiceNonces.ContainsKey(nonce) || m_GroupVoiceNonces.Count >= GROUP_VOICE_CACHE_MAX) return true;
                    m_GroupVoiceNonces[nonce] = now.AddMinutes(3);
                    PruneGroupVoiceCache(m_GroupVoiceNotified, now);
                    if (m_GroupVoiceNotified.Count < GROUP_VOICE_CACHE_MAX)
                        m_GroupVoiceNotified[group] = now.AddSeconds(30);
                }
                string channel = "group-" + group;
                OSDMap voice = new OSDMap
                {
                    { "channel_uri", OSD.FromString(channel) },
                    { "channel_credentials", OSD.FromString(CallCredentials(channel, recipient)) },
                    { "invitation_type", OSD.FromInteger(GROUP_CHAT_SESSION) },
                    { "voice_server_type", OSD.FromString("webrtc") }
                };
                OSDMap body = new OSDMap
                {
                    { "session_id", OSD.FromUUID(group) },
                    { "session_name", OSD.FromString(invite.GroupName) },
                    { "from_id", OSD.FromUUID(caller) },
                    { "from_name", OSD.FromString(invite.CallerName) },
                    { "voice", voice }
                };
                // Match Firestorm's VoiceInviteGroup flow; the existing IM session keeps its group id.
                OSD evt = new OSDMap { { "message", OSD.FromString("ChatterBoxInvitation") }, { "body", body } };
                lock (m_GroupVoiceLock)
                {
                    // A new ring can make the viewer replace the live group call UI.
                    if (m_GroupVoiceSessions.TryGetValue(recipient, out var current) && current.Group == group)
                    {
                        m_log.InfoFormat("{0}[GROUP CALL]: skipped {1}; already in group {2}", logHeader, recipient, group);
                        return true;
                    }
                    queue.Enqueue(evt, recipient);
                }
                m_log.InfoFormat("{0}[GROUP CALL]: rang {1} for group {2} in {3}",
                    logHeader, recipient, group, scene.RegionInfo.RegionName);
                return true;
            }
            return false;
        }
    }
}
