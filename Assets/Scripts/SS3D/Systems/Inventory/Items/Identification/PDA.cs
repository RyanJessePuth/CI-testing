﻿using System.Collections.Generic;
using System.Linq;
using SS3D.Interactions;
using SS3D.Interactions.Interfaces;
using SS3D.Systems.Roles;
using SS3D.Systems.Inventory.Containers;
using UnityEngine;
using FishNet.Object.Synchronizing;

namespace SS3D.Systems.Inventory.Items.Generic
{
    /// <summary>
    /// The honking device used by the clown on honking purposes
    /// </summary>
    public class PDA : Item, IIdentification
    {
        public IDPermission testPermission;
        private Container container;

        [HideInInspector] public Item StartingIDCard;

        protected override void OnStart()
        {
            base.OnStart();

            container = GetComponent<Container>();
            if (StartingIDCard)
            {
                container.AddItem(StartingIDCard);
            }
        }

        public override void Update()
        {
            base.Update();
        }

        public bool HasPermission(IDPermission permission)
        {
            if (container == null)
            {
                return false;
            }

            var idCard = container.Items.FirstOrDefault() as IDCard;
            if (idCard == null)
            {
                return false;
            }

            return idCard.HasPermission(permission);
        }

        public override IInteraction[] CreateTargetInteractions(InteractionEvent interactionEvent)
        {
            List<IInteraction> interactions = base.CreateTargetInteractions(interactionEvent).ToList();

            return interactions.ToArray();
        }
    }
}