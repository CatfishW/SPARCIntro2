using System;
using System.Collections.Generic;
using System.Linq;
using ModularStoryFlow.Runtime.Execution;
using ModularStoryFlow.Runtime.Save;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;

namespace ModularStoryFlow.Runtime.Player
{
    internal sealed class StoryVariableStore : IStoryVariableStore
    {
        private readonly Dictionary<string, bool> booleans = new Dictionary<string, bool>();
        private readonly Dictionary<string, int> integers = new Dictionary<string, int>();
        private readonly Dictionary<string, float> floats = new Dictionary<string, float>();
        private readonly Dictionary<string, string> strings = new Dictionary<string, string>();

        public void Initialize(IEnumerable<StoryVariableDefinition> definitions)
        {
            booleans.Clear();
            integers.Clear();
            floats.Clear();
            strings.Clear();

            foreach (var definition in definitions ?? Array.Empty<StoryVariableDefinition>())
            {
                if (definition == null)
                {
                    continue;
                }

                switch (definition)
                {
                    case StoryBooleanVariableDefinition booleanDefinition:
                        booleans[booleanDefinition.Key] = booleanDefinition.DefaultValue;
                        break;

                    case StoryIntegerVariableDefinition integerDefinition:
                        integers[integerDefinition.Key] = integerDefinition.DefaultValue;
                        break;

                    case StoryFloatVariableDefinition floatDefinition:
                        floats[floatDefinition.Key] = floatDefinition.DefaultValue;
                        break;

                    case StoryStringVariableDefinition stringDefinition:
                        strings[stringDefinition.Key] = stringDefinition.DefaultValue;
                        break;
                }
            }
        }

        public bool TryGetBoolean(string key, out bool value) => booleans.TryGetValue(key, out value);
        public bool TryGetInteger(string key, out int value) => integers.TryGetValue(key, out value);
        public bool TryGetFloat(string key, out float value) => floats.TryGetValue(key, out value);
        public bool TryGetString(string key, out string value) => strings.TryGetValue(key, out value);

        public void SetBoolean(string key, bool value) => booleans[key] = value;
        public void SetInteger(string key, int value) => integers[key] = value;
        public void SetFloat(string key, float value) => floats[key] = value;
        public void SetString(string key, string value) => strings[key] = value ?? string.Empty;

        public List<StoryVariableSnapshotEntry> Export()
        {
            var result = new List<StoryVariableSnapshotEntry>(booleans.Count + integers.Count + floats.Count + strings.Count);

            result.AddRange(booleans.OrderBy(pair => pair.Key).Select(pair => new StoryVariableSnapshotEntry
            {
                Key = pair.Key,
                Type = "bool",
                BooleanValue = pair.Value
            }));

            result.AddRange(integers.OrderBy(pair => pair.Key).Select(pair => new StoryVariableSnapshotEntry
            {
                Key = pair.Key,
                Type = "int",
                IntegerValue = pair.Value
            }));

            result.AddRange(floats.OrderBy(pair => pair.Key).Select(pair => new StoryVariableSnapshotEntry
            {
                Key = pair.Key,
                Type = "float",
                FloatValue = pair.Value
            }));

            result.AddRange(strings.OrderBy(pair => pair.Key).Select(pair => new StoryVariableSnapshotEntry
            {
                Key = pair.Key,
                Type = "string",
                StringValue = pair.Value
            }));

            return result;
        }

        public void Import(IEnumerable<StoryVariableSnapshotEntry> entries)
        {
            foreach (var entry in entries ?? Array.Empty<StoryVariableSnapshotEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                switch (entry.Type)
                {
                    case "bool":
                        booleans[entry.Key] = entry.BooleanValue;
                        break;

                    case "int":
                        integers[entry.Key] = entry.IntegerValue;
                        break;

                    case "float":
                        floats[entry.Key] = entry.FloatValue;
                        break;

                    case "string":
                        strings[entry.Key] = entry.StringValue ?? string.Empty;
                        break;
                }
            }
        }
    }

    internal sealed class StoryStateStore : IStoryStateStore
    {
        private readonly Dictionary<string, string> states = new Dictionary<string, string>();
        private readonly Action<string, string, string> onStateChanged;

        public StoryStateStore(Action<string, string, string> onStateChanged)
        {
            this.onStateChanged = onStateChanged;
        }

        public void Initialize(IEnumerable<StoryStateMachineDefinition> definitions)
        {
            states.Clear();

            foreach (var definition in definitions ?? Array.Empty<StoryStateMachineDefinition>())
            {
                if (definition == null)
                {
                    continue;
                }

                states[definition.MachineId] = definition.DefaultStateId;
            }
        }

        public string GetState(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
            {
                return string.Empty;
            }

            return states.TryGetValue(machineId, out var stateId) ? stateId : string.Empty;
        }

        public void SetState(string machineId, string stateId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
            {
                return;
            }

            var previous = GetState(machineId);
            states[machineId] = stateId ?? string.Empty;

            if (!string.Equals(previous, stateId, StringComparison.Ordinal))
            {
                onStateChanged?.Invoke(machineId, previous, stateId);
            }
        }

        public List<StoryStateSnapshotEntry> Export()
        {
            return states.OrderBy(pair => pair.Key).Select(pair => new StoryStateSnapshotEntry
            {
                MachineId = pair.Key,
                StateId = pair.Value
            }).ToList();
        }

        public void Import(IEnumerable<StoryStateSnapshotEntry> entries)
        {
            foreach (var entry in entries ?? Array.Empty<StoryStateSnapshotEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.MachineId))
                {
                    continue;
                }

                states[entry.MachineId] = entry.StateId ?? string.Empty;
            }
        }
    }
}
