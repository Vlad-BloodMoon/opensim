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
using System.Reflection;
using System.Timers;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.CoreModules.Avatar.InstantMessage
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule", Id = "InstantMessageModule")]
    public class InstantMessageModule : ISharedRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        /// <value>
        /// Is this module enabled?
        /// </value>
        protected bool m_enabled = false;

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
        }

        public virtual void AddRegion(Scene scene)
        {
            if (!m_enabled)
                return;

            lock (m_scenes)
                m_scenes.Add(scene);
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
                    delivered.toAgentID = member.Key.Guid;
                    delivered.offline = 0;
                    m_TransferModule.SendInstantMessage(delivered, delegate(bool success) { });
                }
                return;
            }

            if (client != null && im.dialog == (byte)InstantMessageDialog.SessionDrop)
            {
                m_adHocSessions.TryLeave(new UUID(im.imSessionID), client.AgentId,
                    out AdHocConferenceSessions.Snapshot _);
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
