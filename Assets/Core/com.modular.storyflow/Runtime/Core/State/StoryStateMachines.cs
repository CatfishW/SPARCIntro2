using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModularStoryFlow.Runtime.State
{
    [Serializable]
    public sealed class StoryStateDefinition
    {
        public string Id = "State";
        public string DisplayName = "State";
    }

    [CreateAssetMenu(fileName = "StateMachine", menuName = "Story Flow/State Machines/State Machine")]
    public sealed partial class StoryStateMachineDefinition : ScriptableObject
    {
        [SerializeField] private string machineId = string.Empty;
        [SerializeField] private string defaultStateId = string.Empty;
        [SerializeField] private List<StoryStateDefinition> states = new List<StoryStateDefinition>();

        public string MachineId => string.IsNullOrWhiteSpace(machineId) ? name : machineId;
        public string DefaultStateId => string.IsNullOrWhiteSpace(defaultStateId)
            ? states.FirstOrDefault()?.Id ?? string.Empty
            : defaultStateId;

        public IReadOnlyList<StoryStateDefinition> States => states;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(machineId))
            {
                machineId = name;
            }

            if (states.Count == 0)
            {
                states.Add(new StoryStateDefinition { Id = "Default", DisplayName = "Default" });
            }

            if (string.IsNullOrWhiteSpace(defaultStateId))
            {
                defaultStateId = states[0].Id;
            }
        }
#endif
    }

    [CreateAssetMenu(fileName = "StoryStateCatalog", menuName = "Story Flow/Catalogs/State Machine Catalog")]
    public sealed partial class StoryStateMachineCatalog : ScriptableObject
    {
        [SerializeField] private List<StoryStateMachineDefinition> stateMachines = new List<StoryStateMachineDefinition>();

        public IReadOnlyList<StoryStateMachineDefinition> StateMachines => stateMachines;

        public void SetStateMachines(IEnumerable<StoryStateMachineDefinition> definitions)
        {
            stateMachines = definitions
                .Where(definition => definition != null)
                .Distinct()
                .OrderBy(definition => definition.name)
                .ToList();
        }
    }
}
