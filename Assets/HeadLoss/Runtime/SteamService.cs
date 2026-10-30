using System;
using HeadLoss.Core.Data.Defs;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
using Steamworks;
#endif

namespace HeadLoss.Unity
{
    /// <summary>
    /// Steamworks.NET (Phase 3 task 9). Starts the Steam API when Steam is running and pumps its callbacks; without
    /// Steam the game plays exactly the same and says so quietly in the log. Saves reach Steam Cloud through Auto-Cloud
    /// (configured on the Steamworks site, see the phase report), so no Steam calls touch the save files. No static
    /// state (Fast Enter Play Mode): the service is a component owned by the game root.
    /// </summary>
    public sealed class SteamService : MonoBehaviour
    {
        /// <summary>The Steam API is running.</summary>
        public bool Running { get; private set; }
        /// <summary>A short line for the menu ("" when Steam is not running).</summary>
        public string Status { get; private set; } = "";

        public void Init(SteamDef def)
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
            try
            {
                if (def.RestartIfNecessary && SteamAPI.RestartAppIfNecessary(new AppId_t(def.AppId)))
                {
                    Application.Quit();
                    return;
                }
                Running = SteamAPI.Init();
                if (Running)
                {
                    Status = $"Steam · {SteamFriends.GetPersonaName()}";
                    Debug.Log($"Head Loss: Steam running (app {SteamUtils.GetAppID()}).");
                }
                else Debug.Log("Head Loss: Steam is not running; playing without it.");
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is BadImageFormatException)
            {
                Running = false;
                Debug.Log("Head Loss: Steam library not available; playing without Steam. " + e.Message);
            }
#else
            Debug.Log("Head Loss: Steam is not used on this platform.");
#endif
        }

        void Update()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
            if (Running) SteamAPI.RunCallbacks();
#endif
        }

        void OnDestroy()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX
            if (Running) SteamAPI.Shutdown();
#endif
            Running = false;
        }
    }
}
