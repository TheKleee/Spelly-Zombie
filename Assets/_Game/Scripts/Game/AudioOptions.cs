using UnityEngine;

namespace SpellyZombie
{
    /// The volume split: master on the listener, music and sounds as
    /// multipliers the music director and Juice read. Saved per machine.
    public static class AudioOptions
    {
        public static float Master { get; private set; } = PlayerPrefs.GetFloat("sz_volume", 1f);
        public static float Music { get; private set; } = PlayerPrefs.GetFloat("sz_music", 1f);
        public static float Sfx { get; private set; } = PlayerPrefs.GetFloat("sz_sfx", 1f);

        public static void SetMaster(float v)
        {
            Master = Mathf.Clamp01(v);
            AudioListener.volume = Master;
            PlayerPrefs.SetFloat("sz_volume", Master);
        }

        public static void SetMusic(float v)
        {
            Music = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat("sz_music", Music);
        }

        public static void SetSfx(float v)
        {
            Sfx = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat("sz_sfx", Sfx);
        }
    }
}
