using System.IO;
using UnityEngine;

namespace ModularStoryFlow.Runtime.Save
{
    public interface IStorySaveProvider
    {
        void Save(string slot, StorySaveData data);
        bool TryLoad(string slot, out StorySaveData data);
        void Delete(string slot);
        string GetDebugPath(string slot);
    }

    public abstract class StorySaveProviderAsset : ScriptableObject, IStorySaveProvider
    {
        public abstract void Save(string slot, StorySaveData data);
        public abstract bool TryLoad(string slot, out StorySaveData data);
        public abstract void Delete(string slot);
        public abstract string GetDebugPath(string slot);
    }

    [CreateAssetMenu(fileName = "JsonFileSaveProvider", menuName = "Story Flow/Save/JSON File Save Provider")]
    public sealed partial class JsonFileStorySaveProviderAsset : StorySaveProviderAsset
    {
        [SerializeField] private string folderName = "StoryFlowSaves";
        [SerializeField] private bool prettyPrint = true;

        public override void Save(string slot, StorySaveData data)
        {
            if (data == null)
            {
                return;
            }

            Directory.CreateDirectory(GetFolderPath());
            File.WriteAllText(GetDebugPath(slot), JsonUtility.ToJson(data, prettyPrint));
        }

        public override bool TryLoad(string slot, out StorySaveData data)
        {
            var path = GetDebugPath(slot);
            if (!File.Exists(path))
            {
                data = null;
                return false;
            }

            data = JsonUtility.FromJson<StorySaveData>(File.ReadAllText(path));
            return data != null;
        }

        public override void Delete(string slot)
        {
            var path = GetDebugPath(slot);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public override string GetDebugPath(string slot)
        {
            var safeSlot = string.IsNullOrWhiteSpace(slot) ? "default" : slot.Trim();
            return Path.Combine(GetFolderPath(), $"{safeSlot}.json");
        }

        private string GetFolderPath()
        {
            return Path.Combine(Application.persistentDataPath, folderName);
        }
    }
}
