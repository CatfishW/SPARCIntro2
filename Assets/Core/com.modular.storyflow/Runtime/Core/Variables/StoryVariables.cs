using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Variables
{
    public enum StoryVariableType
    {
        Boolean = 0,
        Integer = 1,
        Float = 2,
        String = 3
    }

    public abstract class StoryVariableDefinition : ScriptableObject
    {
        [SerializeField] private string key = string.Empty;
        [SerializeField] private string description = string.Empty;

        public string Key => string.IsNullOrWhiteSpace(key) ? name : key;
        public string Description => description;
        public abstract StoryVariableType VariableType { get; }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                key = name;
            }
        }
#endif
    }

    [CreateAssetMenu(fileName = "BoolVariable", menuName = "Story Flow/Variables/Boolean Variable")]
    public sealed partial class StoryBooleanVariableDefinition : StoryVariableDefinition
    {
        [SerializeField] private bool defaultValue;
        public bool DefaultValue => defaultValue;
        public override StoryVariableType VariableType => StoryVariableType.Boolean;
    }

    [CreateAssetMenu(fileName = "IntVariable", menuName = "Story Flow/Variables/Integer Variable")]
    public sealed partial class StoryIntegerVariableDefinition : StoryVariableDefinition
    {
        [SerializeField] private int defaultValue;
        public int DefaultValue => defaultValue;
        public override StoryVariableType VariableType => StoryVariableType.Integer;
    }

    [CreateAssetMenu(fileName = "FloatVariable", menuName = "Story Flow/Variables/Float Variable")]
    public sealed partial class StoryFloatVariableDefinition : StoryVariableDefinition
    {
        [SerializeField] private float defaultValue;
        public float DefaultValue => defaultValue;
        public override StoryVariableType VariableType => StoryVariableType.Float;
    }

    [CreateAssetMenu(fileName = "StringVariable", menuName = "Story Flow/Variables/String Variable")]
    public sealed partial class StoryStringVariableDefinition : StoryVariableDefinition
    {
        [SerializeField] private string defaultValue = string.Empty;
        public string DefaultValue => defaultValue;
        public override StoryVariableType VariableType => StoryVariableType.String;
    }

    [CreateAssetMenu(fileName = "StoryVariableCatalog", menuName = "Story Flow/Catalogs/Variable Catalog")]
    public sealed partial class StoryVariableCatalog : ScriptableObject
    {
        [SerializeField] private List<StoryVariableDefinition> variables = new List<StoryVariableDefinition>();

        public IReadOnlyList<StoryVariableDefinition> Variables => variables;

        public void SetVariables(IEnumerable<StoryVariableDefinition> definitions)
        {
            variables = definitions
                .Where(definition => definition != null)
                .Distinct()
                .OrderBy(definition => definition.name)
                .ToList();
        }
    }
}
