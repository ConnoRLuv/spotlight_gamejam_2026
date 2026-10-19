using UnityEngine;

namespace SpotlightGameJam.UI
{
    /// <summary>统一管理本地总音量，跨场景与重启保留，不依赖场景中的设置面板。</summary>
    public static class GameAudioSettings
    {
        public const string MasterVolumeKey = "SpotlightGameJam.Audio.MasterVolume";
        private static bool dirty;
        public static float MasterVolume => AudioListener.volume;

        /// <summary>游戏启动或打开设置时应用存储的音量；非法旧值回退为最大音量。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void ApplySavedVolume()
        {
            float volume = PlayerPrefs.GetFloat(MasterVolumeKey, 1);
            AudioListener.volume = float.IsNaN(volume) || float.IsInfinity(volume) ? 1 : Mathf.Clamp01(volume);
        }

        /// <summary>实时设置所有声音的总音量，滑动期间只更新内存，避免逐帧写入磁盘。</summary>
        public static void SetMasterVolume(float volume)
        {
            if (float.IsNaN(volume) || float.IsInfinity(volume)) return;
            volume = Mathf.Clamp01(volume);
            AudioListener.volume = volume;
            PlayerPrefs.SetFloat(MasterVolumeKey, volume); dirty = true;
        }

        /// <summary>关闭设置、切换场景或退出时保存已修改的音量。</summary>
        public static void Save()
        {
            if (!dirty) return;
            PlayerPrefs.Save(); dirty = false;
        }
    }
}
