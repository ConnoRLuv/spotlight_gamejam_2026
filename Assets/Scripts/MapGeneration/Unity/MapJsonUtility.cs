using UnityEngine;
using RoguelikeMap.Serialization;

namespace RoguelikeMap.Unity
{
    /// <summary>
    /// JSON 读写。刻意单独放在 Unity 层，让核心逻辑保持零引擎依赖。
    ///
    /// 如果你更习惯 Newtonsoft 或需要多态，把这里换掉即可，核心逻辑不受影响。
    /// </summary>
    public static class MapJsonUtility
    {
        public static string ToJson(MapRunSnapshot snapshot)
        {
            return JsonUtility.ToJson(snapshot, true);
        }

        public static string ToJson(MapGraphDto dto)
        {
            return JsonUtility.ToJson(dto, true);
        }

        public static MapRunSnapshot SnapshotFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return JsonUtility.FromJson<MapRunSnapshot>(json);
        }

        public static MapGraphDto GraphFromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            return JsonUtility.FromJson<MapGraphDto>(json);
        }
    }
}
