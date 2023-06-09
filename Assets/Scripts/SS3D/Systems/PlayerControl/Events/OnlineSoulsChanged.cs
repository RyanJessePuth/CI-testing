﻿using System.Collections.Generic;
using Coimbra.Services.Events;
using SS3D.Systems.Entities;

namespace SS3D.Systems.PlayerControl.Events
{
    public partial struct OnlineSoulsChanged  : IEvent
    {
        public readonly List<Soul> OnlineSouls;

        public ChangeType ChangeType;
        public readonly Soul ChangedSoul;
        public readonly string ChangedCkey;
        public readonly bool AsServer;

        public OnlineSoulsChanged(List<Soul> onlineSouls, ChangeType changeType, Soul changed, string ckey, bool asServer)
        {
            OnlineSouls = onlineSouls;
            ChangeType = changeType;
            ChangedSoul = changed;
            ChangedCkey = ckey;
            AsServer = asServer;
        }
    }
}