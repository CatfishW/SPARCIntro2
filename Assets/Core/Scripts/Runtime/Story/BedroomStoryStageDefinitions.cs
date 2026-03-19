using System;

namespace Blocks.Gameplay.Core.Story
{
    public enum BedroomStoryStage
    {
        FreshSpawn = 0,
        ArrivalDialogueComplete = 1,
        LaptopObjectiveActive = 2,
        LaptopResolved = 3,
        DoorReady = 4,
        TransitionCommitted = 5
    }

    [Serializable]
    public readonly struct BedroomStoryStageDefinition
    {
        public BedroomStoryStageDefinition(BedroomStoryStage stage, string displayName)
        {
            Stage = stage;
            DisplayName = displayName;
        }

        public BedroomStoryStage Stage { get; }
        public string DisplayName { get; }
    }

    public static class BedroomStoryStages
    {
        public static readonly BedroomStoryStageDefinition[] Ordered =
        {
            new BedroomStoryStageDefinition(BedroomStoryStage.FreshSpawn, "Fresh Spawn"),
            new BedroomStoryStageDefinition(BedroomStoryStage.ArrivalDialogueComplete, "Arrival Dialogue Complete"),
            new BedroomStoryStageDefinition(BedroomStoryStage.LaptopObjectiveActive, "Laptop Objective Active"),
            new BedroomStoryStageDefinition(BedroomStoryStage.LaptopResolved, "Laptop Resolved"),
            new BedroomStoryStageDefinition(BedroomStoryStage.DoorReady, "Door Ready"),
            new BedroomStoryStageDefinition(BedroomStoryStage.TransitionCommitted, "Transition Committed")
        };
    }
}
