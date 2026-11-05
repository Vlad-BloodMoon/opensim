/*
 * Copyright (c) 2026 OpenSimulator contributors.
 * Licensed under the BSD license used by OpenSimulator.
 */

using System;
using System.Collections.Generic;
using OpenMetaverse;

namespace OpenSim.Region.CoreModules.Avatar.InstantMessage
{
    /// <summary>
    /// Thread-safe, transport-independent membership model for temporary IM conferences.
    /// This is deliberately not coupled to Groups or any voice backend. A grid-wide
    /// implementation must keep the authoritative state in a shared service; this
    /// class provides the same membership rules for a local implementation.
    /// </summary>
    internal sealed class AdHocConferenceSessions
    {
        internal const int MaximumParticipants = 25;

        internal enum MemberState
        {
            Invited,
            Joined
        }

        internal sealed class Snapshot
        {
            internal UUID SessionId;
            internal UUID OwnerId;
            internal Dictionary<UUID, MemberState> Members;
        }

        private sealed class Session
        {
            internal UUID OwnerId;
            internal readonly Dictionary<UUID, MemberState> Members = new Dictionary<UUID, MemberState>();
        }

        private readonly object m_lock = new object();
        private readonly Dictionary<UUID, Session> m_sessions = new Dictionary<UUID, Session>();

        internal bool TryCreate(UUID sessionId, UUID ownerId, ICollection<UUID> invitees, out Snapshot snapshot)
        {
            snapshot = null;
            if (sessionId.IsZero() || ownerId.IsZero() || invitees == null)
                return false;

            HashSet<UUID> unique = new HashSet<UUID>();
            foreach (UUID invitee in invitees)
            {
                if (invitee.IsZero() || invitee == ownerId || !unique.Add(invitee))
                    continue;
                if (unique.Count >= MaximumParticipants)
                    return false;
            }
            if (unique.Count == 0)
                return false;

            lock (m_lock)
            {
                if (m_sessions.ContainsKey(sessionId))
                    return false;
                Session session = new Session { OwnerId = ownerId };
                session.Members.Add(ownerId, MemberState.Joined);
                foreach (UUID invitee in unique)
                    session.Members.Add(invitee, MemberState.Invited);
                m_sessions.Add(sessionId, session);
                snapshot = Copy(sessionId, session);
                return true;
            }
        }

        internal bool TryInvite(UUID sessionId, UUID senderId, ICollection<UUID> invitees, out Snapshot snapshot)
        {
            snapshot = null;
            if (invitees == null)
                return false;

            lock (m_lock)
            {
                if (!m_sessions.TryGetValue(sessionId, out Session session)
                    || !IsJoined(session, senderId))
                    return false;

                HashSet<UUID> newMembers = new HashSet<UUID>();
                foreach (UUID id in invitees)
                    if (!id.IsZero() && !session.Members.ContainsKey(id))
                        newMembers.Add(id);
                if (session.Members.Count + newMembers.Count > MaximumParticipants)
                    return false;

                foreach (UUID id in newMembers)
                    session.Members.Add(id, MemberState.Invited);
                snapshot = Copy(sessionId, session);
                return true;
            }
        }

        internal bool TryJoin(UUID sessionId, UUID agentId, out Snapshot snapshot)
        {
            snapshot = null;
            lock (m_lock)
            {
                if (!m_sessions.TryGetValue(sessionId, out Session session)
                    || !session.Members.ContainsKey(agentId))
                    return false;
                session.Members[agentId] = MemberState.Joined;
                snapshot = Copy(sessionId, session);
                return true;
            }
        }

        internal bool TryLeave(UUID sessionId, UUID agentId, out Snapshot snapshot)
        {
            snapshot = null;
            lock (m_lock)
            {
                if (!m_sessions.TryGetValue(sessionId, out Session session)
                    || !session.Members.Remove(agentId))
                    return false;
                if (session.Members.Count == 0)
                    m_sessions.Remove(sessionId);
                else
                    snapshot = Copy(sessionId, session);
                return true;
            }
        }

        internal bool TryGetForSender(UUID sessionId, UUID senderId, out Snapshot snapshot)
        {
            snapshot = null;
            lock (m_lock)
            {
                if (!m_sessions.TryGetValue(sessionId, out Session session)
                    || !IsJoined(session, senderId))
                    return false;
                snapshot = Copy(sessionId, session);
                return true;
            }
        }

        private static bool IsJoined(Session session, UUID agentId)
        {
            return session.Members.TryGetValue(agentId, out MemberState state)
                && state == MemberState.Joined;
        }

        private static Snapshot Copy(UUID id, Session session)
        {
            return new Snapshot
            {
                SessionId = id,
                OwnerId = session.OwnerId,
                Members = new Dictionary<UUID, MemberState>(session.Members)
            };
        }
    }
}
