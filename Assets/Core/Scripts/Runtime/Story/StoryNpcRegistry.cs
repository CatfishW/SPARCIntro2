using System;
using System.Collections.Generic;
using UnityEngine;

namespace Blocks.Gameplay.Core.Story
{
    [DisallowMultipleComponent]
    public sealed class StoryNpcRegistry : MonoBehaviour
    {
        [SerializeField] private List<StoryNpcAgent> npcs = new List<StoryNpcAgent>();

        public IReadOnlyList<StoryNpcAgent> Npcs => npcs;

        private void Awake()
        {
            Refresh();
        }

        private void OnValidate()
        {
            Refresh();
        }

        public void Refresh()
        {
            npcs.RemoveAll(static npc => npc == null);

            var discovered = GetComponentsInChildren<StoryNpcAgent>(true);
            for (var index = 0; index < discovered.Length; index++)
            {
                var npc = discovered[index];
                if (npc != null && !npcs.Contains(npc))
                {
                    npcs.Add(npc);
                }
            }
        }

        public StoryNpcAgent GetNpc(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                return null;
            }

            for (var index = 0; index < npcs.Count; index++)
            {
                var npc = npcs[index];
                if (npc != null && string.Equals(npc.NpcId, npcId, StringComparison.OrdinalIgnoreCase))
                {
                    return npc;
                }
            }

            return null;
        }
    }
}
