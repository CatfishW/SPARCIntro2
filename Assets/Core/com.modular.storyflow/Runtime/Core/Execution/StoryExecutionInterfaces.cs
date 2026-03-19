using System.Collections.Generic;
using ModularStoryFlow.Runtime.Graph;

namespace ModularStoryFlow.Runtime.Execution
{
    public interface IStoryVariableStore
    {
        bool TryGetBoolean(string key, out bool value);
        bool TryGetInteger(string key, out int value);
        bool TryGetFloat(string key, out float value);
        bool TryGetString(string key, out string value);

        void SetBoolean(string key, bool value);
        void SetInteger(string key, int value);
        void SetFloat(string key, float value);
        void SetString(string key, string value);
    }

    public interface IStoryStateStore
    {
        string GetState(string machineId);
        void SetState(string machineId, string stateId);
    }

    public interface IStoryExecutionContext
    {
        string SessionId { get; }
        StoryGraphAsset Graph { get; }
        StoryNodeAsset CurrentNode { get; }
        IReadOnlyList<string> History { get; }
        IStoryVariableStore Variables { get; }
        IStoryStateStore States { get; }

        void RaiseSignal(string signalId, string signalDisplayName, string payload = null);
        string RequestTimeline(string cueId, string cueDisplayName, bool waitForCompletion);
    }

    public interface IStoryCondition
    {
        bool Evaluate(IStoryExecutionContext context);
    }

    public interface IStoryAction
    {
        void Execute(IStoryExecutionContext context);
    }
}
