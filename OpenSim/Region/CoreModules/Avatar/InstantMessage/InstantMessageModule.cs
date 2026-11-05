/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Timers;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.CoreModules.Avatar.InstantMessage
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "InstantMessageModule")]
    public class InstantMessageModule : ISharedRegionModule, IAdHocConferenceModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        /// <value>
        /// Is this module enabled?
        /// </value>
        protected bool m_enabled = false;
        private bool m_registerAdHocCaps = false;

        protected readonly List<Scene> m_scenes = new List<Scene>();

        // The membership store is independent of the voice implementation.
        // It is initially process-local; multi-simulator persistence and routing
        // will be provided by a grid service before this feature is enabled.
        private readonly AdHocConferenceSessions m_adHocSessions = new AdHocConferenceSessions();

        #region Region Module interface

        protected IMessageTransferModule m_TransferModule = null;

        public virtual void Initialise(IConfigSource config)
        {
            if (config.Configs["Messaging"] != null)
            {
                if (config.Configs["Messaging"].GetString(
                        "InstantMessageModule", "InstantMessageModule") !=
                        "InstantMessageModule")
                    return;
            }

            m_enabled = true;

            // A WebRTC module already owns ChatSessionRequest when enabled.
            // Otherwise the core IM module can expose the text-only capability.
            IConfig webrtc = config.Configs["WebRtcVoice"];
            m_registerAdHocCaps = webrtc == null || !webrtc.GetBoolean("Enabled", false);
        }

        public virtual void AddRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            lock (m_scenes)
                m_scenes.Add(scene);
            scene.RegisterModuleInterface<IAdHocConferenceModule>(this);
            if (m_registerAdHocCaps)
                scene.EventManager.OnRegisterCaps += (UUID agent, Caps caps) =>
                    RegisterTextConferenceCaps(scene, agent, caps);
        }

        public virtual void RegionLoaded(Scene scene)
        {
            if (!m_enabled)
                return;

            if (m_TransferModule == null)
            {
                m_TransferModule = scene.RequestModuleInterface<IMessageTransferModule>();

                if (m_TransferModule == null)
                {
                    m_log.Error("[INSTANT MESSAGE]: No message transfer module, IM will not work!");
                    scene.EventManager.OnNewClient -= OnClientConnect;
                    scene.EventManager.OnIncomingInstantMessage -= OnGridInstantMessage;

                    m_scenes.Clear();
                    m_enabled = false;
                }
            }
            scene.EventManager.OnNewClient += OnClientConnect;
            scene.EventManager.OnIncomingInstantMessage += OnGridInstantMessage;
        }

        public virtual void RemoveRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            scene.UnregisterModuleInterface<IAdHocConferenceModule>(this);

            lock (m_scenes)
            {
                m_scenes.Remove(scene);
            }
        }

        protected virtual void OnClientConnect(IClientAPI client)
        {
            client.OnInstantMessage += OnInstantMessage;
        }

        public virtual void PostInitialise()
        {
        }

        public virtual void Close()
        {
        }

        public virtual string Name
        {
            get { return "InstantMessageModule"; }
        }

        public virtual Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion
/*
        public virtual void OnViewerInstantMessage(IClientAPI client, GridInstantMessage im)
        {
            im.fromAgentName = client.FirstName + " " + client.LastName;
            OnInstantMessage(client, im);
        }
*/
        public virtual void OnInstantMessage(IClientAPI client, GridInstantMessage im)
        {
            if (m_TransferModule == null)
                return;

            // Only a client connected to this region may submit conference
            // text. Never trust a forged sender id from the viewer.
            if (client != null && im.dialog == (byte)InstantMessageDialog.SessionSend
                && m_adHocSessions.TryGetForSender(new UUID(im.imSessionID), client.AgentId,
                    out AdHocConferenceSessions.Snapshot conference))
            {
                foreach (KeyValuePair<UUID, AdHocConferenceSessions.MemberState> member in conference.Members)
                {
                    if (member.Key == client.AgentId || member.Value != AdHocConferenceSessions.MemberState.Joined)
                        continue;

                    GridInstantMessage delivered = new GridInstantMessage(im, true);
                    delivered.fromAgentID = client.AgentId.Guid;
                    delivered.fromAgentName = client.Name;
                    delivered.fromGroup = false;
                    delivered.toAgentID = member.Key.Guid;
                    delivered.offline = 0;
                    m_TransferModule.SendInstantMessage(delivered, delegate(bool success) { });
                }
                return;
            }

            if (client != null && im.dialog == (byte)InstantMessageDialog.SessionDrop)
            {
                DeclineInvitation(client.Scene, client.AgentId, new UUID(im.imSessionID));
                return;
            }

            switch(im.dialog)
            {
                case (byte)InstantMessageDialog.MessageFromAgent:
                case (byte)InstantMessageDialog.StartTyping:
                case (byte)InstantMessageDialog.StopTyping:
                case (byte)InstantMessageDialog.BusyAutoResponse:
                case (byte)InstantMessageDialog.MessageFromObject:
                    break;
                default:
                    return;
            }

            if (client != null)
                im.offline = 0;

            //if (im.offline == 0)
            if(im.timestamp == 0)
                im.timestamp = (uint)Util.UnixTimeSinceEpoch();

            m_TransferModule.SendInstantMessage(im,
                delegate(bool success)
                {
                    if(success || client == null)
                        return;

                    switch (im.dialog)
                    {
                        case (byte)InstantMessageDialog.StartTyping:
                        case (byte)InstantMessageDialog.StopTyping:
                        case (byte)InstantMessageDialog.MessageFromObject:
                            return;
                        default:
                            break;
                    }

                    client.SendInstantMessage(new GridInstantMessage(                              
                            null, new UUID(im.fromAgentID), "System",
                            new UUID(im.toAgentID),
                            (byte)InstantMessageDialog.BusyAutoResponse,
                            "Unable to send instant message. User is not logged in.",
                            false, new Vector3())
                        );
                }
            );
        }

        /// <summary>
        /// First-stage membership API for the conference capability handler.
        /// No session is made visible to the viewer until the caller has sent
        /// its event-queue start reply and invitations.
        /// </summary>
        internal bool TryCreateAdHocConference(UUID sessionId, UUID creator, ICollection<UUID> invited,
            out AdHocConferenceSessions.Snapshot conference)
        {
            return m_adHocSessions.TryCreate(sessionId, creator, invited, out conference);
        }

        private ScenePresence FindRootPresence(UUID agentId)
        {
            lock (m_scenes)
            {
                foreach (Scene scene in m_scenes)
                {
                    ScenePresence sp = scene.GetScenePresence(agentId);
                    if (sp != null && !sp.IsChildAgent)
                        return sp;
                }
            }
            return null;
        }

        private static void SendConferenceReply(Scene scene, UUID agent, UUID session,
            string name, bool success)
        {
            IEventQueue eq = scene.RequestModuleInterface<IEventQueue>();
            eq?.ChatterBoxSessionStartReply(session, name, 1, false, false,
                session, success, success ? string.Empty : "generic_request_error", agent);
        }

        /// <summary>
        /// Registers the viewer's standard conference capability when no WebRTC
        /// provider owns it. The exact same text operations are also callable
        /// through IAdHocConferenceModule by WebRTC providers.
        /// </summary>
        private void RegisterTextConferenceCaps(Scene scene, UUID agent, Caps caps)
        {
            caps.RegisterSimpleHandler("ChatSessionRequest",
                new SimpleStreamHandler("/" + UUID.Random(),
                    delegate(IOSHttpRequest request, IOSHttpResponse response)
                    {
                        HandleTextConferenceCaps(scene, agent, request, response);
                    }));
        }

        private void HandleTextConferenceCaps(Scene scene, UUID agent,
            IOSHttpRequest request, IOSHttpResponse response)
        {
            if (request.HttpMethod != "POST")
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            OSDMap map;
            using (Stream input = request.InputStream)
                map = input.Length > 0 ? OSDParser.DeserializeLLSDXml(input) as OSDMap : null;
            if (map == null)
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            string method = map.ContainsKey("method") ? map["method"].AsString() : string.Empty;
            UUID session = map.ContainsKey("session-id") ? map["session-id"].AsUUID() : UUID.Zero;
            if (session.IsZero())
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            List<UUID> invitees = new List<UUID>();
            if (map.ContainsKey("params") && map["params"] is OSDArray ids)
            {
                foreach (OSD id in ids)
                {
                    UUID candidate = id.AsUUID();
                    if (!candidate.IsZero() && candidate != agent && !invitees.Contains(candidate))
                        invitees.Add(candidate);
                }
            }

            switch (method)
            {
                case "start conference":
                    StartConference(scene, agent, session, invitees);
                    break;
                case "invite":
                    InviteToConference(scene, agent, session, invitees);
                    break;
                case "accept invitation":
                    if (!AcceptInvitation(scene, agent, session))
                    {
                        response.StatusCode = (int)HttpStatusCode.NotFound;
                        return;
                    }
                    response.RawBuffer = Util.UTF8.GetBytes("<llsd><map><key>agent_info</key><map /></map></llsd>");
                    return;
                case "decline invitation":
                    DeclineInvitation(scene, agent, session);
                    break;
                case "start p2p voice":
                    scene.RequestModuleInterface<IEventQueue>()?.ChatterBoxSessionStartReply(
                        session, string.Empty, 2, false, false,
                        session, true, string.Empty, agent);
                    break;
                case "fetch history":
                    response.RawBuffer = Util.UTF8.GetBytes("<llsd><array /></llsd>");
                    return;
                default:
                    response.RawBuffer = Util.UTF8.GetBytes("<llsd><undef /></llsd>");
                    return;
            }
            response.RawBuffer = Util.UTF8.GetBytes("<llsd><map /></llsd>");
        }

        private static void SendConferenceRoster(Scene scene, UUID recipient,
            AdHocConferenceSessions.Snapshot conference)
        {
            IEventQueue eq = scene.RequestModuleInterface<IEventQueue>();
            if (eq == null)
                return;
            List<GroupChatListAgentUpdateData> updates = new List<GroupChatListAgentUpdateData>();
            foreach (KeyValuePair<UUID, AdHocConferenceSessions.MemberState> member in conference.Members)
                if (member.Value == AdHocConferenceSessions.MemberState.Joined)
                    updates.Add(new GroupChatListAgentUpdateData(member.Key, true, false, false, true));
            if (updates.Count != 0)
                eq.ChatterBoxSessionAgentListUpdates(conference.SessionId, recipient, updates);
        }

        private static void SendConferenceInvitation(Scene scene, UUID caller, string callerName,
            UUID recipient, UUID session)
        {
            IEventQueue eq = scene.RequestModuleInterface<IEventQueue>();
            string name = "Conference with " + callerName;
            eq?.ChatterboxInvitation(session, name, caller, "(added you to this conversation)", recipient,
                callerName, (byte)InstantMessageDialog.SessionSend,
                (uint)Util.UnixTimeSinceEpoch(), false, 0, Vector3.Zero, 1,
                session, false, Utils.StringToBytes(name));
        }

        public bool StartConference(Scene scene, UUID caller, UUID session, ICollection<UUID> invitees)
        {
            ScenePresence sender = FindRootPresence(caller);
            if (sender == null || invitees == null || scene.RequestModuleInterface<IEventQueue>() == null)
            {
                SendConferenceReply(scene, caller, session, string.Empty, false);
                return false;
            }

            // This first-stage implementation is deliberately restricted to
            // avatars on the same simulator process. An external session service
            // is required before cross-process invitations can be reliable.
            foreach (UUID id in invitees)
            {
                if (id == caller || id.IsZero())
                    continue;
                if (FindRootPresence(id) == null)
                {
                    SendConferenceReply(scene, caller, session, string.Empty, false);
                    return false;
                }
            }

            if (!TryCreateAdHocConference(session, caller, invitees,
                out AdHocConferenceSessions.Snapshot conference))
            {
                SendConferenceReply(scene, caller, session, string.Empty, false);
                return false;
            }

            string name = "Conference with " + sender.Name;
            SendConferenceReply(scene, caller, session, name, true);
            SendConferenceRoster(scene, caller, conference);
            foreach (KeyValuePair<UUID, AdHocConferenceSessions.MemberState> member in conference.Members)
            {
                if (member.Key == caller)
                    continue;
                ScenePresence invited = FindRootPresence(member.Key);
                if (invited != null)
                    SendConferenceInvitation(invited.Scene, caller, sender.Name, member.Key, session);
            }
            return true;
        }

        public bool AcceptInvitation(Scene scene, UUID caller, UUID session)
        {
            if (!m_adHocSessions.TryJoin(session, caller,
                out AdHocConferenceSessions.Snapshot conference))
                return false;
            SendConferenceRoster(scene, caller, conference);
            foreach (KeyValuePair<UUID, AdHocConferenceSessions.MemberState> member in conference.Members)
            {
                if (member.Key == caller || member.Value != AdHocConferenceSessions.MemberState.Joined)
                    continue;
                ScenePresence other = FindRootPresence(member.Key);
                IEventQueue eq = other?.Scene.RequestModuleInterface<IEventQueue>();
                eq?.ChatterBoxSessionAgentListUpdates(session, member.Key,
                    new List<GroupChatListAgentUpdateData>
                    {
                        new GroupChatListAgentUpdateData(caller, true, false, false, true)
                    });
            }
            return true;
        }

        public bool DeclineInvitation(Scene scene, UUID caller, UUID session)
        {
            if (!m_adHocSessions.TryLeave(session, caller,
                out AdHocConferenceSessions.Snapshot conference))
                return false;
            if (conference != null)
            {
                foreach (KeyValuePair<UUID, AdHocConferenceSessions.MemberState> member in conference.Members)
                {
                    if (member.Value != AdHocConferenceSessions.MemberState.Joined)
                        continue;
                    ScenePresence other = FindRootPresence(member.Key);
                    IEventQueue eq = other?.Scene.RequestModuleInterface<IEventQueue>();
                    eq?.ChatterBoxSessionAgentListUpdates(session, member.Key,
                        new List<GroupChatListAgentUpdateData>
                        {
                            new GroupChatListAgentUpdateData(caller, false, false, false, false)
                        });
                }
            }
            return true;
        }

        public bool InviteToConference(Scene scene, UUID caller, UUID session,
            ICollection<UUID> invitees)
        {
            if (invitees == null)
                return false;
            foreach (UUID id in invitees)
                if (FindRootPresence(id) == null)
                    return false;
            if (!m_adHocSessions.TryInvite(session, caller, invitees,
                out AdHocConferenceSessions.Snapshot conference))
                return false;
            ScenePresence sender = FindRootPresence(caller);
            if (sender == null)
                return false;
            foreach (UUID id in invitees)
            {
                ScenePresence invited = FindRootPresence(id);
                if (invited != null)
                    SendConferenceInvitation(invited.Scene, caller, sender.Name, id, session);
            }
            return true;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="msg"></param>
        protected virtual void OnGridInstantMessage(GridInstantMessage msg)
        {
            // The normal IM handler intentionally ignores SessionSend. For an
            // ad-hoc conference known to this process, deliver only to a joined
            // recipient and only when the sender is also a joined member.
            if (msg.dialog == (byte)InstantMessageDialog.SessionSend
                && m_adHocSessions.TryGetForSender(new UUID(msg.imSessionID), new UUID(msg.fromAgentID),
                    out AdHocConferenceSessions.Snapshot conference)
                && conference.Members.TryGetValue(new UUID(msg.toAgentID), out AdHocConferenceSessions.MemberState state)
                && state == AdHocConferenceSessions.MemberState.Joined)
            {
                lock (m_scenes)
                {
                    foreach (Scene scene in m_scenes)
                    {
                        ScenePresence recipient = scene.GetScenePresence(new UUID(msg.toAgentID));
                        if (recipient != null && !recipient.IsChildAgent)
                        {
                            recipient.ControllingClient.SendInstantMessage(msg);
                            return;
                        }
                    }
                }
                return;
            }

            // Just call the Text IM handler above
            // This event won't be raised unless we have that agent,
            // so we can depend on the above not trying to send
            // via grid again
            //
            OnInstantMessage(null, msg);
        }
    }
}
