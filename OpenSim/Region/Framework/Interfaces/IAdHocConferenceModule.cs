/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * Licensed under the OpenSimulator BSD license.
 */

using System.Collections.Generic;
using OpenMetaverse;
using OpenSim.Region.Framework.Scenes;

namespace OpenSim.Region.Framework.Interfaces
{
    /// <summary>Text-conference operations exposed to capability providers.
    /// The interface has no dependency on any voice backend.</summary>
    public interface IAdHocConferenceModule
    {
        bool StartConference(Scene scene, UUID caller, UUID session, ICollection<UUID> invitees);
        bool AcceptInvitation(Scene scene, UUID caller, UUID session);
        bool DeclineInvitation(Scene scene, UUID caller, UUID session);
        bool InviteToConference(Scene scene, UUID caller, UUID session, ICollection<UUID> invitees);
    }
}
