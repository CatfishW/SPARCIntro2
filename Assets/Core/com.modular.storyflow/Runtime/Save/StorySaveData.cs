using System;
using System.Collections.Generic;
using ModularStoryFlow.Runtime.Events;

namespace ModularStoryFlow.Runtime.Save
{
    [Serializable]
    public sealed class StoryVariableSnapshotEntry
    {
        public string Key;
        public string Type;
        public bool BooleanValue;
        public int IntegerValue;
        public float FloatValue;
        public string StringValue;
    }

    [Serializable]
    public sealed class StoryStateSnapshotEntry
    {
        public string MachineId;
        public string StateId;
    }

    [Serializable]
    public sealed class StorySaveData
    {
        public int SaveVersion = 1;
        public string CreatedAtUtc;
        public string SessionId;
        public string GraphId;
        public string GraphName;
        public string CurrentNodeId;
        public bool IsCompleted;
        public StoryWaitDescriptor PendingOperation;
        public List<string> History = new List<string>();
        public List<StoryVariableSnapshotEntry> Variables = new List<StoryVariableSnapshotEntry>();
        public List<StoryStateSnapshotEntry> States = new List<StoryStateSnapshotEntry>();
    }
}
