using ModularStoryFlow.Runtime.Execution;
using ModularStoryFlow.Runtime.State;
using ModularStoryFlow.Runtime.Variables;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Conditions
{
    public enum StoryComparisonOperator
    {
        Equals = 0,
        NotEquals = 1,
        LessThan = 2,
        LessThanOrEqual = 3,
        GreaterThan = 4,
        GreaterThanOrEqual = 5
    }

    public abstract class StoryConditionAsset : ScriptableObject, IStoryCondition
    {
        public abstract bool Evaluate(IStoryExecutionContext context);
    }

    [CreateAssetMenu(fileName = "VariableCondition", menuName = "Story Flow/Conditions/Variable Condition")]
    public sealed partial class StoryVariableConditionAsset : StoryConditionAsset
    {
        [SerializeField] private StoryVariableDefinition variable;
        [SerializeField] private StoryComparisonOperator comparisonOperator = StoryComparisonOperator.Equals;
        [SerializeField] private bool booleanValue;
        [SerializeField] private int integerValue;
        [SerializeField] private float floatValue;
        [SerializeField] private string stringValue = string.Empty;

        public override bool Evaluate(IStoryExecutionContext context)
        {
            if (variable == null)
            {
                return false;
            }

            switch (variable.VariableType)
            {
                case StoryVariableType.Boolean:
                    if (!context.Variables.TryGetBoolean(variable.Key, out var booleanCurrent))
                    {
                        return false;
                    }

                    return Compare(booleanCurrent, booleanValue, comparisonOperator);

                case StoryVariableType.Integer:
                    if (!context.Variables.TryGetInteger(variable.Key, out var integerCurrent))
                    {
                        return false;
                    }

                    return Compare(integerCurrent, integerValue, comparisonOperator);

                case StoryVariableType.Float:
                    if (!context.Variables.TryGetFloat(variable.Key, out var floatCurrent))
                    {
                        return false;
                    }

                    return Compare(floatCurrent, floatValue, comparisonOperator);

                case StoryVariableType.String:
                    if (!context.Variables.TryGetString(variable.Key, out var stringCurrent))
                    {
                        return false;
                    }

                    return Compare(stringCurrent, stringValue, comparisonOperator);

                default:
                    return false;
            }
        }

        private static bool Compare(bool left, bool right, StoryComparisonOperator comparisonOperator)
        {
            return comparisonOperator switch
            {
                StoryComparisonOperator.Equals => left == right,
                StoryComparisonOperator.NotEquals => left != right,
                _ => false
            };
        }

        private static bool Compare(int left, int right, StoryComparisonOperator comparisonOperator)
        {
            return comparisonOperator switch
            {
                StoryComparisonOperator.Equals => left == right,
                StoryComparisonOperator.NotEquals => left != right,
                StoryComparisonOperator.LessThan => left < right,
                StoryComparisonOperator.LessThanOrEqual => left <= right,
                StoryComparisonOperator.GreaterThan => left > right,
                StoryComparisonOperator.GreaterThanOrEqual => left >= right,
                _ => false
            };
        }

        private static bool Compare(float left, float right, StoryComparisonOperator comparisonOperator)
        {
            return comparisonOperator switch
            {
                StoryComparisonOperator.Equals => Mathf.Approximately(left, right),
                StoryComparisonOperator.NotEquals => !Mathf.Approximately(left, right),
                StoryComparisonOperator.LessThan => left < right,
                StoryComparisonOperator.LessThanOrEqual => left <= right,
                StoryComparisonOperator.GreaterThan => left > right,
                StoryComparisonOperator.GreaterThanOrEqual => left >= right,
                _ => false
            };
        }

        private static bool Compare(string left, string right, StoryComparisonOperator comparisonOperator)
        {
            return comparisonOperator switch
            {
                StoryComparisonOperator.Equals => string.Equals(left, right),
                StoryComparisonOperator.NotEquals => !string.Equals(left, right),
                StoryComparisonOperator.LessThan => string.Compare(left, right, System.StringComparison.Ordinal) < 0,
                StoryComparisonOperator.LessThanOrEqual => string.Compare(left, right, System.StringComparison.Ordinal) <= 0,
                StoryComparisonOperator.GreaterThan => string.Compare(left, right, System.StringComparison.Ordinal) > 0,
                StoryComparisonOperator.GreaterThanOrEqual => string.Compare(left, right, System.StringComparison.Ordinal) >= 0,
                _ => false
            };
        }
    }

    [CreateAssetMenu(fileName = "StateEqualsCondition", menuName = "Story Flow/Conditions/State Equals Condition")]
    public sealed partial class StoryStateEqualsConditionAsset : StoryConditionAsset
    {
        [SerializeField] private StoryStateMachineDefinition stateMachine;
        [SerializeField] private string expectedStateId = string.Empty;

        public override bool Evaluate(IStoryExecutionContext context)
        {
            if (stateMachine == null)
            {
                return false;
            }

            var current = context.States.GetState(stateMachine.MachineId);
            return string.Equals(current, expectedStateId);
        }
    }

    [CreateAssetMenu(fileName = "RandomChanceCondition", menuName = "Story Flow/Conditions/Random Chance Condition")]
    public sealed partial class StoryRandomChanceConditionAsset : StoryConditionAsset
    {
        [SerializeField, Range(0f, 1f)] private float chance = 0.5f;

        public override bool Evaluate(IStoryExecutionContext context)
        {
            return Random.value <= chance;
        }
    }
}
