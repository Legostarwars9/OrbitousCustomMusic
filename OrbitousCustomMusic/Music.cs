using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

[BepInPlugin(
    "com.legostarwars9.orbitousmusic",
    "OrbitousCustomMusic",
    "1.3.1"
)]
public class OrbitousMusicMod : BaseUnityPlugin
{
    private static OrbitousMusicMod Instance;

    private static readonly List<AudioClip> customClips =
        new List<AudioClip>();

    public static readonly List<string> customSongNames =
        new List<string>();

    private static readonly Dictionary<string, ConfigEntry<float>> trackWeights =
        new Dictionary<string, ConfigEntry<float>>();

    private static AudioSource modAudioSource;

    public static musicManager ActiveMusicManagerInstance;

    private static ConfigEntry<float> customMusicChance;
    private static ConfigEntry<bool> enableMusicLogging;

    private static float modCustomVolume = 0.5f;

    private bool showInterface = true;

    public static bool dynamicMusicEnabled = true;

    private Harmony harmony;

    // These songs are not part of the normal background
    // ambience playlist and should always take priority.
    private static readonly HashSet<string> priorityMusic =
        new HashSet<string>
        {
            "ShopSong",
            "CoolSong",
            "ChaseSong",
            "HunterSong",
            "BlackHoleSong",
            "IntroFightSong",
            "CreditsSong"
        };

    private static readonly HashSet<string> loopingPriorityMusic =
        new HashSet<string>
        {
            "CoolSong",
            "ChaseSong",
            "HunterSong",
            "BlackHoleSong",
            "IntroFightSong",
            "CreditsSong"
        };

    private void Awake()
    {
        Instance = this;

        string modFolder =
            Path.Combine(
                Paths.PluginPath,
                "OrbitousMusic"
            );

        if (!Directory.Exists(modFolder))
        {
            Directory.CreateDirectory(modFolder);

            Logger.LogInfo(
                "Created OrbitousMusic folder."
            );
        }

        LoadConfiguration();
        LoadCustomTracks(modFolder);

        harmony =
            new Harmony(
                "com.legostarwars9.orbitousmusic"
            );

        harmony.PatchAll();

        Logger.LogInfo(
            "Orbitous Hybrid Music Manager 1.3.1 loaded."
        );

        Logger.LogInfo(
            $"Custom music chance: {customMusicChance.Value}%"
        );

        Logger.LogInfo(
            $"Loaded {customSongNames.Count} custom track(s)."
        );
    }

    private void OnDestroy()
    {
        StopCustomMusic();

        if (harmony != null)
            harmony.UnpatchSelf();
    }

    // ============================================================
    // CONFIGURATION
    // ============================================================

    private void LoadConfiguration()
    {
        customMusicChance = Config.Bind(
            "Music Settings",
            "Custom Music Chance",
            35f,
            new ConfigDescription(
                "Percentage chance that a normal ambience selection uses custom music instead of vanilla music.",
                new AcceptableValueRange<float>(
                    0f,
                    100f
                )
            )
        );

        enableMusicLogging = Config.Bind(
            "Music Settings",
            "Enable Music Logging",
            true,
            "Logs music selections and custom track playback to the BepInEx log."
        );
    }

    // ============================================================
    // CUSTOM TRACK LOADING
    // ============================================================

    private void LoadCustomTracks(string modFolder)
    {
        customClips.Clear();
        customSongNames.Clear();
        trackWeights.Clear();

        string[] files =
            Directory.GetFiles(
                modFolder,
                "*.wav"
            );

        foreach (string filePath in files)
        {
            string cleanSongName =
                Path.GetFileNameWithoutExtension(
                    filePath
                );

            try
            {
                AudioClip clip =
                    LoadWavAsAudioClip(
                        filePath,
                        cleanSongName
                    );

                if (clip == null)
                    continue;

                customClips.Add(clip);
                customSongNames.Add(cleanSongName);

                ConfigEntry<float> weight =
                    Config.Bind(
                        "Track Weights",
                        cleanSongName,
                        1f,
                        new ConfigDescription(
                            "Relative chance for this track to be selected compared to other custom tracks.",
                            new AcceptableValueRange<float>(
                                0f,
                                100f
                            )
                        )
                    );

                trackWeights[cleanSongName] =
                    weight;

                Logger.LogInfo(
                    $"Cached custom track: {cleanSongName} | Weight: {weight.Value}"
                );
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    $"Failed to load WAV '{cleanSongName}': {ex}"
                );
            }
        }
    }

    // ============================================================
    // GUI
    // ============================================================

    private void Update()
    {
        if (UnityInput.Current.GetKeyDown(KeyCode.F4))
            showInterface = !showInterface;

        if (UnityInput.Current.GetKeyDown(KeyCode.F5))
            ForceSkipTrack();
    }

    private void OnGUI()
    {
        if (!showInterface)
            return;

        GUI.Box(
            new Rect(10, 10, 300, 225),
            "Orbitous Music Manager (F4 to Hide)"
        );

        string toggleStatus =
            dynamicMusicEnabled
                ? "ENABLED"
                : "DISABLED";

        GUI.Label(
            new Rect(20, 35, 270, 20),
            $"Custom Music: {toggleStatus}"
        );

        if (GUI.Button(
            new Rect(20, 60, 260, 25),
            dynamicMusicEnabled
                ? "Disable Custom Music"
                : "Enable Custom Music"))
        {
            dynamicMusicEnabled =
                !dynamicMusicEnabled;

            if (!dynamicMusicEnabled)
                StopCustomMusic();
        }

        if (GUI.Button(
            new Rect(20, 90, 260, 25),
            "Skip / Play Random Track (F5)"))
        {
            ForceSkipTrack();
        }

        GUI.Label(
            new Rect(20, 120, 270, 20),
            $"Custom Chance: {customMusicChance.Value:0}%"
        );

        GUI.Label(
            new Rect(20, 145, 270, 20),
            $"Loaded Tracks: {customSongNames.Count}"
        );

        GUI.Label(
            new Rect(20, 170, 270, 20),
            $"Custom Volume: {Mathf.RoundToInt(modCustomVolume * 100f)}%"
        );

        float previousVolume =
            modCustomVolume;

        modCustomVolume =
            GUI.HorizontalSlider(
                new Rect(20, 192, 260, 15),
                modCustomVolume,
                0f,
                1f
            );

        if (previousVolume != modCustomVolume)
            SyncCustomAudioSourceVolume();

        string trackDisplay = "None";

        if (modAudioSource != null &&
            modAudioSource.isPlaying &&
            modAudioSource.clip != null)
        {
            trackDisplay =
                modAudioSource.clip.name;
        }

        GUI.Label(
            new Rect(20, 212, 270, 20),
            $"Custom Playing: {trackDisplay}"
        );
    }

    // ============================================================
    // LOGGING
    // ============================================================

    private static void Log(string message)
    {
        if (enableMusicLogging != null &&
            enableMusicLogging.Value &&
            Instance != null)
        {
            Instance.Logger.LogInfo(
                "[Music] " + message
            );
        }
    }

    // ============================================================
    // MANAGER
    // ============================================================

    private static void FindMusicManager()
    {
        if (ActiveMusicManagerInstance != null)
            return;

        ActiveMusicManagerInstance =
            UnityEngine.Object.FindObjectOfType<musicManager>();
    }

    // ============================================================
    // F5
    // ============================================================

    private void ForceSkipTrack()
    {
        FindMusicManager();

        if (ActiveMusicManagerInstance == null)
            return;

        StopCustomMusic();

        PlayRandomCustomTrack();
    }

    // ============================================================
    // CUSTOM MUSIC SELECTION
    // ============================================================

    private static void PlayRandomCustomTrack()
    {
        if (!dynamicMusicEnabled)
            return;

        if (customSongNames.Count == 0)
        {
            Log("No custom tracks available.");
            return;
        }

        string selectedTrack =
            SelectWeightedCustomTrack();

        if (string.IsNullOrEmpty(selectedTrack))
        {
            Log(
                "Custom track selection failed because all track weights are 0."
            );

            return;
        }

        PlayCustomTrackDirectly(
            selectedTrack
        );
    }

    private static string SelectWeightedCustomTrack()
    {
        float totalWeight = 0f;

        foreach (string songName in customSongNames)
        {
            if (!trackWeights.TryGetValue(
                songName,
                out ConfigEntry<float> weight))
            {
                continue;
            }

            if (weight.Value > 0f)
                totalWeight += weight.Value;
        }

        if (totalWeight <= 0f)
            return null;

        float roll =
            UnityEngine.Random.Range(
                0f,
                totalWeight
            );

        foreach (string songName in customSongNames)
        {
            if (!trackWeights.TryGetValue(
                songName,
                out ConfigEntry<float> weight))
            {
                continue;
            }

            if (weight.Value <= 0f)
                continue;

            roll -= weight.Value;

            if (roll < 0f)
                return songName;
        }

        return null;
    }

    // ============================================================
    // PLAY CUSTOM MUSIC
    // ============================================================

    public static void PlayCustomTrackDirectly(
        string trackName)
    {
        if (!dynamicMusicEnabled)
            return;

        FindMusicManager();

        if (ActiveMusicManagerInstance == null)
            return;

        int targetIndex =
            customSongNames.IndexOf(
                trackName
            );

        if (targetIndex < 0 ||
            targetIndex >= customClips.Count)
        {
            Log(
                $"Could not find loaded custom track: {trackName}"
            );

            return;
        }

        if (modAudioSource == null)
        {
            modAudioSource =
                ActiveMusicManagerInstance.gameObject
                    .AddComponent<AudioSource>();

            modAudioSource.loop = false;
            modAudioSource.playOnAwake = false;
            modAudioSource.spatialBlend = 0f;
        }

        StopCustomMusic();

        /*
         * Stop normal ambience before starting custom music.
         *
         * We do NOT call StopBackgroundMusic() here because that
         * would go through our Harmony patch again.
         */
        StopVanillaBackgroundMusic(
            ActiveMusicManagerInstance
        );

        modAudioSource.clip =
            customClips[targetIndex];

        SyncCustomAudioSourceVolume();

        modAudioSource.Play();

        Log(
            $"Started custom track: {trackName}"
        );
    }

    // ============================================================
    // STOP CUSTOM MUSIC
    // ============================================================

    public static void StopCustomMusic()
    {
        if (modAudioSource == null)
            return;

        if (modAudioSource.isPlaying)
        {
            Log(
                "Stopped custom music."
            );

            modAudioSource.Stop();
        }

        modAudioSource.clip = null;
    }

    // ============================================================
    // STOP VANILLA AMBIENCE
    // ============================================================

    private static void StopVanillaBackgroundMusic(
        musicManager manager)
    {
        if (manager == null ||
            manager.audioManager == null ||
            manager.songs == null)
        {
            return;
        }

        foreach (string song in manager.songs)
        {
            if (!string.IsNullOrEmpty(song))
            {
                manager.audioManager.Stop(song);
            }
        }
    }

    // ============================================================
    // VOLUME
    // ============================================================

    public static void SyncCustomAudioSourceVolume()
    {
        if (modAudioSource == null)
            return;

        float musicMultiplier = 1f;
        float masterMultiplier = 1f;

        if (ActiveMusicManagerInstance != null)
        {
            musicMultiplier =
                ActiveMusicManagerInstance
                    .GetMusicVolumeMult();

            if (ActiveMusicManagerInstance.audioManager != null)
            {
                masterMultiplier =
                    ActiveMusicManagerInstance
                        .audioManager
                        .GetMasterVolumeMult();
            }
        }

        modAudioSource.volume =
            modCustomVolume *
            musicMultiplier *
            masterMultiplier;
    }

    // ============================================================
    // TRACK CHECKS
    // ============================================================

    private static bool IsCustomTrack(
        string name)
    {
        return
            !string.IsNullOrEmpty(name) &&
            customSongNames.Contains(name);
    }

    private static bool IsNormalAmbienceSong(
        musicManager manager,
        string songName)
    {
        if (manager == null ||
            manager.songs == null ||
            string.IsNullOrEmpty(songName))
        {
            return false;
        }

        for (int i = 0;
             i < manager.songs.Length;
             i++)
        {
            if (manager.songs[i] == songName)
                return true;
        }

        return false;
    }

    private static bool ShouldUseCustomMusic()
    {
        if (!dynamicMusicEnabled)
            return false;

        if (customSongNames.Count == 0)
            return false;

        float roll =
            UnityEngine.Random.Range(
                0f,
                100f
            );

        bool result =
            roll < customMusicChance.Value;

        Log(
            $"Custom music roll: {roll:0.00} / " +
            $"{customMusicChance.Value:0.00} -> " +
            (result
                ? "CUSTOM"
                : "VANILLA")
        );

        return result;
    }

    // ============================================================
    // WAV LOADER
    // ============================================================

    private static AudioClip LoadWavAsAudioClip(
        string filePath,
        string clipName)
    {
        byte[] fileBytes =
            File.ReadAllBytes(filePath);

        if (fileBytes.Length < 44)
            throw new Exception(
                "WAV file is too small."
            );

        ushort audioChannels =
            BitConverter.ToUInt16(
                fileBytes,
                22
            );

        int frequencySampleRate =
            BitConverter.ToInt32(
                fileBytes,
                24
            );

        ushort bitsPerSample =
            BitConverter.ToUInt16(
                fileBytes,
                34
            );

        if (bitsPerSample != 16)
        {
            throw new Exception(
                $"Unsupported WAV bit depth: {bitsPerSample}. " +
                "Only 16-bit PCM WAV files are supported."
            );
        }

        int dataOffset = 12;
        int dataSize = 0;

        while (
            dataOffset + 8 <=
            fileBytes.Length)
        {
            string chunkID =
                System.Text.Encoding.ASCII.GetString(
                    fileBytes,
                    dataOffset,
                    4
                );

            int chunkSize =
                BitConverter.ToInt32(
                    fileBytes,
                    dataOffset + 4
                );

            if (chunkID == "data")
            {
                dataOffset += 8;
                dataSize = chunkSize;
                break;
            }

            dataOffset +=
                8 + chunkSize;
        }

        if (dataSize <= 0)
            throw new Exception(
                "WAV data chunk was not found."
            );

        int sampleCount =
            dataSize / 2;

        float[] soundBuffer =
            new float[sampleCount];

        for (int i = 0;
             i < sampleCount;
             i++)
        {
            short sample =
                BitConverter.ToInt16(
                    fileBytes,
                    dataOffset + (i * 2)
                );

            soundBuffer[i] =
                sample / 32768f;
        }

        AudioClip clip =
            AudioClip.Create(
                clipName,
                sampleCount /
                    audioChannels,
                audioChannels,
                frequencySampleRate,
                false
            );

        clip.SetData(
            soundBuffer,
            0
        );

        return clip;
    }

    // ============================================================
    // MUSIC MANAGER AWAKE
    // ============================================================

    [HarmonyPatch(
        typeof(musicManager),
        "Awake"
    )]
    public static class MusicManagerAwakePatch
    {
        private static void Postfix(
            musicManager __instance)
        {
            ActiveMusicManagerInstance =
                __instance;

            Log(
                "Found Orbitous music manager."
            );
        }
    }

    // ============================================================
    // PLAY SONG
    // ============================================================

    [HarmonyPatch(
        typeof(musicManager),
        "PlaySong"
    )]
    public static class MusicManagerPlaySongPatch
    {
        private static bool Prefix(
            musicManager __instance,
            string songString)
        {
            ActiveMusicManagerInstance =
                __instance;

            if (string.IsNullOrEmpty(songString))
                return true;

            /*
             * Custom tracks should never normally reach this
             * method, but keep this protection here anyway.
             */
            if (IsCustomTrack(songString))
            {
                PlayCustomTrackDirectly(
                    songString
                );

                return false;
            }

            /*
             * Special music:
             *
             * ShopSong
             * CoolSong
             * ChaseSong
             * etc.
             *
             * Always interrupt custom ambience.
             */
            if (priorityMusic.Contains(songString))
            {
                StopCustomMusic();

                Log(
                    $"Priority music started: {songString}"
                );

                return true;
            }

            /*
             * Only normal ambience songs are eligible for
             * replacement with custom music.
             */
            if (!IsNormalAmbienceSong(
                __instance,
                songString))
            {
                return true;
            }

            /*
             * If the custom roll fails, allow Orbitous to
             * play its normal vanilla song.
             */
            if (!ShouldUseCustomMusic())
                return true;

            /*
             * Custom music won the roll.
             *
             * Prevent the vanilla PlaySong() from executing.
             */
            PlayRandomCustomTrack();

            return false;
        }
    }

    // ============================================================
    // MUSIC PLAYING
    // ============================================================

    [HarmonyPatch(
        typeof(musicManager),
        "MusicPlaying"
    )]
    public static class MusicPlayingPatch
    {
        private static void Postfix(
            ref bool __result)
        {
            /*
             * Custom ambience counts as active music.
             *
             * This keeps the normal background music system
             * from starting another ambience track while our
             * custom track is playing.
             */
            if (modAudioSource != null &&
                modAudioSource.isPlaying)
            {
                __result = true;
            }
        }
    }

    // ============================================================
    // STOP BACKGROUND MUSIC
    // ============================================================

    [HarmonyPatch(
        typeof(musicManager),
        "StopBackgroundMusic"
    )]
    public static class StopBackgroundMusicPatch
    {
        private static void Prefix()
        {
            StopCustomMusic();
        }
    }

    // ============================================================
    // END MUSIC CYCLE
    // ============================================================

    [HarmonyPatch(
        typeof(musicManager),
        "EndMusicCycle"
    )]
    public static class EndMusicCyclePatch
    {
        private static void Prefix()
        {
            StopCustomMusic();
        }
    }

    // ============================================================
    // STOP SONG
    // ============================================================

    [HarmonyPatch(
        typeof(musicManager),
        "StopSong"
    )]
    public static class StopSongPatch
    {
        private static void Prefix(
            string songString)
        {
            /*
             * Special music should stop custom music too.
             */
            if (priorityMusic.Contains(
                songString))
            {
                StopCustomMusic();
            }

            /*
             * Protection in case something explicitly tries
             * to stop a custom track.
             */
            if (IsCustomTrack(songString))
            {
                StopCustomMusic();
            }
        }
    }

    // ============================================================
    // MUSIC VOLUME
    // ============================================================

    [HarmonyPatch(
        typeof(musicManager),
        "SetMusicVolume"
    )]
    public static class SetMusicVolumePatch
    {
        private static bool Prefix(
            musicManager __instance,
            float newVol)
        {
            if (modAudioSource == null ||
                !modAudioSource.isPlaying)
            {
                return true;
            }

            /*
             * The original method would do:
             *
             * ChangeSingleVolume(currentSong, ...)
             *
             * which can crash if currentSong refers to a
             * custom track that doesn't exist in audioManager.sounds.
             */
            __instance.musicVolume =
                newVol;

            SyncCustomAudioSourceVolume();

            return false;
        }
    }

    // ============================================================
    // MASTER VOLUME
    // ============================================================

    [HarmonyPatch(
        typeof(audioManager),
        "ChangeMasterVolume"
    )]
    public static class ChangeMasterVolumePatch
    {
        private static void Postfix()
        {
            SyncCustomAudioSourceVolume();
        }
    }

    // ============================================================
    // DIRECT AUDIO PLAY
    // ============================================================

    [HarmonyPatch(
        typeof(audioManager),
        "Play",
        new Type[] { typeof(string) }
    )]
    public static class AudioManagerPlayPatch
    {
        private static void Prefix(
            string name)
        {
            if (ActiveMusicManagerInstance == null)
                return;

            if (IsNormalAmbienceSong(
                    ActiveMusicManagerInstance,
                    name))
            {
                StopCustomMusic();
                return;
            }

            if (priorityMusic.Contains(name))
            {
                StopCustomMusic();

                if (loopingPriorityMusic.Contains(name))
                {
                    sound targetSound =
                        Array.Find(
                            audioManager.Instance.sounds,
                            sound2 => sound2.name == name
                        );

                    if (targetSound != null &&
                        targetSound.source != null)
                    {
                        targetSound.source.loop = true;
                    }

                    Log(
                        $"Priority music started and set to loop: {name}"
                    );
                }
            }
        }
    }

    // ============================================================
    // DIRECT AUDIO STOP
    // ============================================================

    [HarmonyPatch(
        typeof(audioManager),
        "Stop"
    )]
    public static class AudioManagerStopPatch
    {
        private static void Prefix(
            string name)
        {
            /*
             * Stopping a special song should not leave custom
             * ambience running underneath it.
             */
            if (priorityMusic.Contains(name))
            {
                StopCustomMusic();
            }
        }
    }

    // ============================================================
    // SHOP ENTRY FIX
    // ============================================================

    [HarmonyPatch(
        typeof(player),
        "OnTriggerEnter2D"
    )]
    public static class PlayerShopMusicPatch
    {
        private static void Prefix(
            Collider2D collider)
        {
            if (collider == null)
                return;

            /*
             * Orbitous does:
             *
             * if (!musicManager.MusicPlaying())
             * {
             *     musicManager.PlaySong("ShopSong");
             * }
             *
             * Since we correctly report custom music as playing,
             * that check would prevent ShopSong from starting.
             *
             * Therefore we handle the shop transition here.
             */

            shopField shop =
                collider.GetComponent<shopField>();

            if (shop == null)
                return;

            FindMusicManager();

            if (ActiveMusicManagerInstance == null)
                return;

            /*
             * Stop both types of normal ambience.
             */
            StopCustomMusic();

            StopVanillaBackgroundMusic(
                ActiveMusicManagerInstance
            );

            /*
             * Start ShopSong before the original method reaches
             * its MusicPlaying() check.
             *
             * The original code then sees ShopSong already playing
             * and won't start it a second time.
             */
            ActiveMusicManagerInstance.PlaySong(
                "ShopSong"
            );

            Log(
                "Entered shop: stopped ambience and started ShopSong."
            );
        }
    }
}