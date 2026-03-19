using System;
using System.Collections.Generic;
using ModularStoryFlow.Runtime.Actions;
using ModularStoryFlow.Runtime.Conditions;
using ModularStoryFlow.Runtime.Events;
using ModularStoryFlow.Runtime.Execution;
using ModularStoryFlow.Runtime.Integration;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Graph
{
    [Serializable]
    public sealed class StoryChoiceOptionDefinition
    {
        public string PortId = string.Empty;
        public string Label = "Choice";
        public StoryConditionAsset AvailabilityCondition;
        public bool HideWhenUnavailable;

        public void EnsureStableId(int index)
        {
            if (!string.IsNullOrWhiteSpace(PortId))
            {
                return;
            }

            PortId = $"choice_{index}_{StoryIds.NewId()}";
        }
    }

    [Serializable]
    public sealed class StoryConditionBranch
    {
        public string PortId = string.Empty;
        public string Label = "If";
        public StoryConditionAsset Condition;

        public void EnsureStableId(int index)
        {
            if (!string.IsNullOrWhiteSpace(PortId))
            {
                return;
            }

            PortId = $"branch_{index}_{StoryIds.NewId()}";
        }
    }

    [StoryNode("Flow/Start", "Start")]
    public sealed partial class StartNodeAsset : StoryNodeAsset
    {
        public override IEnumerable<StoryPortDefinition> GetPorts()
        {
            yield return Output(DefaultOutputPortId, "Next");
        }

        public override StoryExecutionResult Execute(IStoryExecutionContext context)
        {
            return StoryExecutionResult.Continue(DefaultOutputPortId);
        }
    }

    [StoryNode("Flow/End", "End")]
    public sealed partial class EndNodeAsset : StoryNodeAsset
    {
        public override IEnumerable<StoryPortDefinition> GetPorts()
        {
            yield return Input(capacity: StoryPortCapacity.Multi);
        }

        public override StoryExecutionResult Execute(IStoryExecutionContext context)
        {
            return StoryExecutionResult.Stop(true);
        }
    }

    [StoryNode("Narrative/Dialogue", "Dialogue")]
    public sealed partial class DialogueNodeAsset : StoryNodeAsset
    {
        [SerializeField] private string speakerId = string.Empty;
        [SerializeField] private string speakerDisplayName = "Narrator";
        [SerializeField, TextArea(3, 8)] private string body = "Dialogue line...";
        [SerializeField] private string portraitKey = string.Empty;
        [SerializeField] private string moodTag = string.Empty;
        [SerializeField] private bool autoAdvance;
        [SerializeField] private float autoAdvanceDelaySeconds = 1f;

        public override IEnumerable<StoryPortDefinition> GetPorts()
        {
            yield return Input();
            yield return Output(DefaultOutputPortId, "Next");
        }

        public override StoryExecutionResult Execute(IStoryExecutionContext context)
        {
            var request = new StoryDialogueRequest
            {
                SessionId = context.SessionId,
                RequestId = StoryIds.NewId(),
                GraphId = context.Graph.GraphId,
                NodeId = NodeId,
                SpeakerId = speakerId,
                SpeakerDisplayName = string.IsNullOrWhiteSpace(speakerDisplayName) ? speakerId : speakerDisplayName,
                Body = body,
                PortraitKey = portraitKey,
                MoodTag = moodTag,
                AutoAdvance = autoAdvance,
                AutoAdvanceDelaySeconds = autoAdvanceDelaySeconds
            };

            return StoryExecutionResult.Wait(StoryWaitDescriptor.ForDialogue(request, DefaultOutputPortId));
        }
    }

    [StoryNode("Narrative/Choice", "Choice")]
    public sealed partial class ChoiceNodeAsset : StoryNodeAsset
    {
        [SerializeField] private string prompt = "Make a choice";
        [SerializeField] private List<StoryChoiceOptionDefinition> options = new List<StoryChoiceOptionDefinition>();

        public override IEnumerable<StoryPortDefinition> GetPorts()
        {
            EnsureStableIds();

            yield return Input();
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null)
                {
                    continue;
                }

                yield return Output(option.PortId, string.IsNullOrWhiteSpace(option.Label) ? $"Choice {i + 1}" : option.Label);
            }
        }

        public override StoryExecutionResult Execute(IStoryExecutionContext context)
        {
            EnsureStableIds();

            var request = new StoryChoiceRequest
            {
                SessionId = context.SessionId,
                RequestId = StoryIds.NewId(),
                GraphId = context.Graph.GraphId,
                NodeId = NodeId,
                Prompt = prompt
            };

            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null)
                {
                    continue;
                }

                var available = option.AvailabilityCondition == null || option.AvailabilityCondition.Evaluate(context);
                if (!available && option.HideWhenUnavailable)
                {
                    continue;
                }

                request.Options.Add(new StoryChoiceOption
                {
                    PortId = option.PortId,
                    Label = string.IsNullOrWhiteSpace(option.Label) ? $"Choice {i + 1}" : option.Label,
                    IsAvailable = available
                });
            }

            if (request.Options.Count == 0)
            {
                return StoryExecutionResult.Stop(false);
            }

            return StoryExecutionResult.Wait(StoryWaitDescriptor.ForChoice(request));
        }

        public override void EnsureStableIds()
        {
            base.EnsureStableIds();

            for (var i = 0; i < options.Count; i++)
            {
                options[i]?.EnsureStableId(i);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureStableIds();
        }
#endif
    }

    [StoryNode("Logic/Branch", "Branch")]
    public sealed partial class BranchNodeAsset : StoryNodeAsset
    {
        public const string ElsePortId = "else";

        [SerializeField] private List<StoryConditionBranch> branches = new List<StoryConditionBranch>();
        [SerializeField] private string elseLabel = "Else";

        public override IEnumerable<StoryPortDefinition> GetPorts()
        {
            EnsureStableIds();

            yield return Input();
            for (var i = 0; i < branches.Count; i++)
            {
                var branch = branches[i];
                if (branch == null)
                {
                    continue;
                }

                yield return Output(branch.PortId, string.IsNullOrWhiteSpace(branch.Label) ? $"If {i + 1}" : branch.Label);
            }

            yield return Output(ElsePortId, string.IsNullOrWhiteSpace(elseLabel) ? "Else" : elseLabel);
        }

        public override StoryExecutionResult Execute(IStoryExecutionContext context)
        {
            EnsureStableIds();

            for (var i = 0; i < branches.Count; i++)
            {
                var branch = branches[i];
                if (branch?.Condition != null && branch.Condition.Evaluate(context))
                {
                    return StoryExecutionResult.Continue(branch.PortId);
                }
            }

            return StoryExecutionResult.Continue(ElsePortId);
        }

        public override void EnsureStableIds()
        {
            base.EnsureStableIds();

            for (var i = 0; i < branches.Count; i++)
            {
                branches[i]?.EnsureStableId(i);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureStableIds();
        }
#endif
    }

    [StoryNode("Actions/Action", "Action")]
    public sealed partial class ActionNodeAsset : StoryNodeAsset
    {
        [SerializeField] private List<StoryActionAsset> actions = new List<StoryActionAsset>();

        public override IEnumerable<StoryPortDefinition> GetPorts()
        {
            yield return Input();
            yield return Output(DefaultOutputPortId, "Next");
        }

        public override StoryExecutionResult Execute(IStoryExecutionContext context)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                actions[i]?.Execute(context);
            }

            return StoryExecutionResult.Continue(DefaultOutputPortId);
        }
    }

    [StoryNode("Actions/Raise Signal", "Signal")]
    public sealed partial class SignalNodeAsset : StoryNodeAsset
    {
        [SerializeField] private StorySignalDefinition signal;
        [SerializeField] private string payload = string.Empty;

        public override IEnumerable<StoryPortDefinition> GetPorts()
        {
            yield return Input();
            yield return Output(DefaultOutputPortId, "Next");
        }

        public override StoryExecutionResult Execute(IStoryExecutionContext context)
        {
            if (signal != null)
            {
                context.RaiseSignal(signal.SignalId, signal.name, payload);
            }

            return StoryExecutionResult.Continue(DefaultOutputPortId);
        }
    }

    [StoryNode("Integration/Timeline", "Timeline")]
    public sealed partial class TimelineNodeAsset : StoryNodeAsset
    {
        public const string CompletedPortId = "completed";
        public const string CancelledPortId = "cancelled";

        [SerializeField] private StoryTimelineCue cue;
        [SerializeField] private bool waitForCompletion = true;

        public override IEnumerable<StoryPortDefinition> GetPorts()
        {
            yield return Input();

            if (waitForCompletion)
            {
                yield return Output(CompletedPortId, "Completed");
                yield return Output(CancelledPortId, "Cancelled");
            }
            else
            {
                yield return Output(DefaultOutputPortId, "Next");
            }
        }

        public override StoryExecutionResult Execute(IStoryExecutionContext context)
        {
            var cueId = cue != null ? cue.CueId : string.Empty;
            var cueDisplayName = cue != null ? cue.name : "Timeline";

            if (!waitForCompletion)
            {
                context.RequestTimeline(cueId, cueDisplayName, false);
                return StoryExecutionResult.Continue(DefaultOutputPortId);
            }

            var request = new StoryTimelineRequest
            {
                SessionId = context.SessionId,
                RequestId = StoryIds.NewId(),
                GraphId = context.Graph.GraphId,
                NodeId = NodeId,
                CueId = cueId,
                CueDisplayName = cueDisplayName,
                WaitForCompletion = true
            };

            return StoryExecutionResult.Wait(StoryWaitDescriptor.ForTimeline(request, CompletedPortId, CancelledPortId));
        }
    }

    [StoryNode("Logic/Wait For Signal", "Wait For Signal")]
    public sealed partial class WaitSignalNodeAsset : StoryNodeAsset
    {
        [SerializeField] private StorySignalDefinition signal;

        public override IEnumerable<StoryPortDefinition> GetPorts()
        {
            yield return Input();
            yield return Output(DefaultOutputPortId, "Received");
        }

        public override StoryExecutionResult Execute(IStoryExecutionContext context)
        {
            return StoryExecutionResult.Wait(StoryWaitDescriptor.ForExternalSignal(signal != null ? signal.SignalId : string.Empty, DefaultOutputPortId));
        }
    }

    [StoryNode("Logic/Delay", "Delay")]
    public sealed partial class DelayNodeAsset : StoryNodeAsset
    {
        [SerializeField, Min(0f)] private float seconds = 1f;

        public override IEnumerable<StoryPortDefinition> GetPorts()
        {
            yield return Input();
            yield return Output(DefaultOutputPortId, "Next");
        }

        public override StoryExecutionResult Execute(IStoryExecutionContext context)
        {
            return StoryExecutionResult.Wait(StoryWaitDescriptor.ForDelay(seconds, DefaultOutputPortId));
        }
    }
}
