using System.Collections.Generic;
using ModularStoryFlow.Runtime.Execution;
using ModularStoryFlow.Runtime.Integration;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Actions
{
    public abstract class StoryActionAsset : ScriptableObject, IStoryAction
    {
        public abstract void Execute(IStoryExecutionContext context);
    }

    [CreateAssetMenu(fileName = "SetVariableAction", menuName = "Story Flow/Actions/Set Variable Action")]
    public sealed partial class StorySetVariableActionAsset : StoryActionAsset
    {
        [SerializeField] private StoryVariableDefinition variable;
        [SerializeField] private bool booleanValue;
        [SerializeField] private int integerValue;
        [SerializeField] private float floatValue;
        [SerializeField] private string stringValue = string.Empty;

        public override void Execute(IStoryExecutionContext context)
        {
            if (variable == null)
            {
                return;
            }

            switch (variable.VariableType)
            {
                case StoryVariableType.Boolean:
                    context.Variables.SetBoolean(variable.Key, booleanValue);
                    break;

                case StoryVariableType.Integer:
                    context.Variables.SetInteger(variable.Key, integerValue);
                    break;

                case StoryVariableType.Float:
                    context.Variables.SetFloat(variable.Key, floatValue);
                    break;

                case StoryVariableType.String:
                    context.Variables.SetString(variable.Key, stringValue);
                    break;
            }
        }
    }

    [CreateAssetMenu(fileName = "SetStateAction", menuName = "Story Flow/Actions/Set State Action")]
    public sealed partial class StorySetStateActionAsset : StoryActionAsset
    {
        [SerializeField] private StoryStateMachineDefinition stateMachine;
        [SerializeField] private string stateId = string.Empty;

        public override void Execute(IStoryExecutionContext context)
        {
            if (stateMachine == null)
            {
                return;
            }

            context.States.SetState(stateMachine.MachineId, stateId);
        }
    }

    [CreateAssetMenu(fileName = "RaiseSignalAction", menuName = "Story Flow/Actions/Raise Signal Action")]
    public sealed partial class StoryRaiseSignalActionAsset : StoryActionAsset
    {
        [SerializeField] private StorySignalDefinition signal;
        [SerializeField] private string payload = string.Empty;

        public override void Execute(IStoryExecutionContext context)
        {
            if (signal == null)
            {
                return;
            }

            context.RaiseSignal(signal.SignalId, signal.name, payload);
        }
    }

    [CreateAssetMenu(fileName = "CompositeAction", menuName = "Story Flow/Actions/Composite Action")]
    public sealed partial class StoryCompositeActionAsset : StoryActionAsset
    {
        [SerializeField] private List<StoryActionAsset> actions = new List<StoryActionAsset>();

        public override void Execute(IStoryExecutionContext context)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                actions[i]?.Execute(context);
            }
        }
    }
}
