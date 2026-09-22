using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

[BepInPlugin("com.username.orbitousmusic", "Orbitous Hybrid Music Manager", "1.3.0")]
public class OrbitousMusicMod : BaseUnityPlugin
{
    private static OrbitousMusicMod Instance;

    private static readonly List<AudioClip> customClips = new List<AudioClip>();
    public static readonly List<string> customSongNames = new List<string>();

    private static readonly Dictionary<string, ConfigEntry<float>> trackWeights =
        new Dictionary<string, ConfigEntry<float>>();

    private static AudioSource modAudioSource;

    public static musicManager ActiveMusicManagerInstance;

    private static ConfigEntry<float> customMusicChance;
    private static ConfigEntry<bool> enableMusicLogging;

    private static float modCustomVolume = 0.5f;

    private bool showInterface = true;

    public static bool dynamicMusicEnabled = true;

    private void Awake()
    {
        Instance = this;

        string modFolder = Path.Combine(Paths.PluginPath, "OrbitousMusic");

        if (!Directory.Exists(modFolder))
        {
            Directory.CreateDirectory(modFolder);
            Logger.LogInfo("Created OrbitousMusic folder.");
        }

        LoadConfiguration();
        LoadCustomTracks(modFolder);

        new Harmony("com.username.orbitousmusic").PatchAll();

        Logger.LogInfo("Orbitous Hybrid Music Manager 1.3.0 loaded.");
        Logger.LogInfo($"Custom music chance: {customMusicChance.Value}%");
        Logger.LogInfo($"Loaded {customSongNames.Count} custom track(s).");
    }

    private void LoadConfiguration()
    {
        customMusicChance = Config.Bind(
            "Music Settings",
            "Custom Music Chance",
            50f,
            new ConfigDescription(
                "Percentage chance that a normal ambience selection uses custom music instead of vanilla music.",
                new AcceptableValueRange<float>(0f, 100f)
            )
        );

        enableMusicLogging = Config.Bind(
            "Music Settings",
            "Enable Music Logging",
            true,
            "Logs music selections and custom track playback to the BepInEx log."
        );
    }

    private void LoadCustomTracks(string modFolder)
    {
        customClips.Clear();
        customSongNames.Clear();
        trackWeights.Clear();

        string[] files = Directory.GetFiles(modFolder, "*.wav");

        foreach (string filePath in files)
        {
            string cleanSongName = Path.GetFileNameWithoutExtension(filePath);

            try
            {
                AudioClip clip = LoadWavAsAudioClip(filePath, cleanSongName);

                if (clip == null)
                    continue;

                customClips.Add(clip);
                customSongNames.Add(cleanSongName);

                ConfigEntry<float> weight = Config.Bind(
                    "Track Weights",
                    cleanSongName,
                    1f,
                    new ConfigDescription(
                        "Relative chance for this track to be selected compared to other custom tracks.",
                        new AcceptableValueRange<float>(0f, 100f)
                    )
                );

                trackWeights[cleanSongName] = weight;

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
            dynamicMusicEnabled ? "ENABLED" : "DISABLED";

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
            dynamicMusicEnabled = !dynamicMusicEnabled;

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

        float previousVolume = modCustomVolume;

        modCustomVolume = GUI.HorizontalSlider(
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
            trackDisplay = modAudioSource.clip.name;
        }

        GUI.Label(
            new Rect(20, 212, 270, 20),
            $"Custom Playing: {trackDisplay}"
        );
    }

    private static void Log(string message)
    {
        if (enableMusicLogging != null &&
            enableMusicLogging.Value &&
            Instance != null)
        {
            Instance.Logger.LogInfo("[Music] " + message);
        }
    }

    private void ForceSkipTrack()
    {
        if (ActiveMusicManagerInstance == null)
        {
            ActiveMusicManagerInstance =
                UnityEngine.Object.FindObjectOfType<musicManager>();
        }

        if (ActiveMusicManagerInstance == null)
            return;

        StopCustomMusic();

        PlayRandomCustomTrack();
    }

    private static void PlayRandomCustomTrack()
    {
        if (!dynamicMusicEnabled)
            return;

        if (customSongNames.Count == 0)
        {
            Log("No custom tracks available.");
            return;
        }

        string selectedTrack = SelectWeightedCustomTrack();

        if (string.IsNullOrEmpty(selectedTrack))
        {
            Log("Custom track selection failed because all track weights are 0.");
            return;
        }

        PlayCustomTrackDirectly(selectedTrack);
    }

    private static string SelectWeightedCustomTrack()
    {
        float totalWeight = 0f;

        foreach (string songName in customSongNames)
        {
            if (!trackWeights.TryGetValue(songName, out ConfigEntry<float> weight))
                continue;

            if (weight.Value > 0f)
                totalWeight += weight.Value;
        }

        if (totalWeight <= 0f)
            return null;

        float roll = UnityEngine.Random.Range(0f, totalWeight);

        foreach (string songName in customSongNames)
        {
            if (!trackWeights.TryGetValue(songName, out ConfigEntry<float> weight))
                continue;

            if (weight.Value <= 0f)
                continue;

            roll -= weight.Value;

            if (roll <= 0f)
                return songName;
        }

        return customSongNames[customSongNames.Count - 1];
    }

    public static void PlayCustomTrackDirectly(string trackName)
    {
        if (!dynamicMusicEnabled)
            return;

        if (ActiveMusicManagerInstance == null)
        {
            ActiveMusicManagerInstance =
                UnityEngine.Object.FindObjectOfType<musicManager>();
        }

        if (ActiveMusicManagerInstance == null)
            return;

        int targetIndex = customSongNames.IndexOf(trackName);

        if (targetIndex < 0 ||
            targetIndex >= customClips.Count)
        {
            Log($"Could not find loaded custom track: {trackName}");
            return;
        }

        if (modAudioSource == null)
        {
            modAudioSource =
                ActiveMusicManagerInstance.gameObject.AddComponent<AudioSource>();

            modAudioSource.loop = false;
            modAudioSource.playOnAwake = false;
        }

        StopCustomMusic();

        modAudioSource.clip = customClips[targetIndex];

        SyncCustomAudioSourceVolume();

        modAudioSource.Play();

        Log($"Started custom track: {trackName}");
    }

    public static void StopCustomMusic()
    {
        if (modAudioSource != null)
        {
            if (modAudioSource.isPlaying)
            {
                Log("Stopped custom music.");
                modAudioSource.Stop();
            }

            modAudioSource.clip = null;
        }
    }

    public static void SyncCustomAudioSourceVolume()
    {
        if (modAudioSource == null)
            return;

        float musicMultiplier = 1f;

        if (ActiveMusicManagerInstance != null)
        {
            musicMultiplier =
                ActiveMusicManagerInstance.GetMusicVolumeMult();
        }

        modAudioSource.volume =
            modCustomVolume *
            musicMultiplier;
    }

    private static bool IsCustomTrack(string name)
    {
        return !string.IsNullOrEmpty(name) &&
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

        for (int i = 0; i < manager.songs.Length; i++)
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

        float roll = UnityEngine.Random.Range(0f, 100f);

        bool result = roll < customMusicChance.Value;

        Log(
            $"Custom music roll: {roll:0.00} / " +
            $"{customMusicChance.Value:0.00} -> " +
            (result ? "CUSTOM" : "VANILLA")
        );

        return result;
    }

    private static AudioClip LoadWavAsAudioClip(
        string filePath,
        string clipName)
    {
        byte[] fileBytes = File.ReadAllBytes(filePath);

        if (fileBytes.Length < 44)
            throw new Exception("WAV file is too small.");

        ushort audioChannels =
            BitConverter.ToUInt16(fileBytes, 22);

        int frequencySampleRate =
            BitConverter.ToInt32(fileBytes, 24);

        ushort bitsPerSample =
            BitConverter.ToUInt16(fileBytes, 34);

        if (bitsPerSample != 16)
        {
            throw new Exception(
                $"Unsupported WAV bit depth: {bitsPerSample}. " +
                "Only 16-bit PCM WAV files are supported."
            );
        }

        int dataOffset = 12;
        int dataSize = 0;

        while (dataOffset + 8 <= fileBytes.Length)
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

            dataOffset += 8 + chunkSize;
        }

        if (dataSize <= 0)
            throw new Exception("WAV data chunk was not found.");

        int sampleCount = dataSize / 2;

        float[] soundBuffer =
            new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
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
                sampleCount / audioChannels,
                audioChannels,
                frequencySampleRate,
                false
            );

        clip.SetData(soundBuffer, 0);

        return clip;
    }

    [HarmonyPatch(typeof(musicManager), "Awake")]
    public static class MusicManagerAwakePatch
    {
        private static void Postfix(musicManager __instance)
        {
            ActiveMusicManagerInstance = __instance;

            Log("Found Orbitous music manager.");
        }
    }

    /*
     * Orbitous calls musicManager.PlaySong() whenever it
     * wants to start music.
     *
     * If the requested song is one of the normal ambience
     * songs, we use our custom-music chance first.
     *
     * Special music such as:
     * ShopSong
     * CoolSong
     * ChaseSong
     * IntroFightSong
     * HunterSong
     * BlackHoleSong
     * CreditsSong
     *
     * is NOT in manager.songs, so it passes through normally.
     */
    [HarmonyPatch(typeof(musicManager), "PlaySong")]
    public static class MusicManagerPlaySongPatch
    {
        private static bool Prefix(
            musicManager __instance,
            string songString)
        {
            ActiveMusicManagerInstance = __instance;

            // Shop music ALWAYS takes priority.
            if (songString == "ShopSong")
            {
                StopCustomMusic();
                __instance.StopBackgroundMusic();

                return true;
            }

            // Other special music also takes priority over custom ambience.
            if (IsSpecialMusic(songString))
            {
                StopCustomMusic();
                __instance.StopBackgroundMusic();

                return true;
            }

            // Custom track requested directly.
            if (IsCustomTrack(songString))
            {
                PlayCustomTrackDirectly(songString);
                return false;
            }

            // Only normal ambience songs are eligible for replacement.
            if (!IsNormalAmbienceSong(__instance, songString))
                return true;

            // Let vanilla play if the custom-music roll fails.
            if (!ShouldUseCustomMusic())
                return true;

            // Replace this normal ambience track with a custom one.
            StopCustomMusic();

            PlayRandomCustomTrack();

            return false;
        }

        private static bool IsSpecialMusic(string songName)
        {
            return songName == "ShopSong" ||
                   songName == "CoolSong" ||
                   songName == "ChaseSong" ||
                   songName == "IntroFightSong" ||
                   songName == "HunterSong" ||
                   songName == "BlackHoleSong" ||
                   songName == "CreditsSong";
        }
    }

    /*
     * StopBackgroundMusic() is used by Orbitous when it
     * reaches a point where normal ambience should stop.
     *
     * We let the original method stop vanilla music and
     * additionally stop our custom AudioSource.
     */
    [HarmonyPatch(typeof(musicManager), "StopBackgroundMusic")]
    public static class StopBackgroundMusicPatch
    {
        private static void Prefix()
        {
            StopCustomMusic();
        }
    }

    /*
     * Some special music uses musicManager.StopSong().
     * If the song being stopped is custom, stop our source.
     */
    [HarmonyPatch(typeof(musicManager), "StopSong")]
    public static class StopSongPatch
    {
        private static bool Prefix(string songString)
        {
            if (!IsCustomTrack(songString))
                return true;

            StopCustomMusic();

            return false;
        }
    }

    /*
     * musicManager.MusicPlaying() normally checks every
     * entry in musicManager.songs through audioManager.
     *
     * Our custom tracks are deliberately NOT in that array,
     * so we add our AudioSource to the result.
     */
    [HarmonyPatch(typeof(musicManager), "MusicPlaying")]
    public static class MusicPlayingPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (modAudioSource != null &&
                modAudioSource.isPlaying)
            {
                __result = true;
            }
        }
    }

    /*
     * Orbitous normally does:
     *
     * audioManager.ChangeSingleVolume(currentSong, ...)
     *
     * That would crash if currentSong were a custom track.
     *
     * We therefore handle the musicManager volume change
     * ourselves while custom music is playing.
     */
    [HarmonyPatch(typeof(musicManager), "SetMusicVolume")]
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

            __instance.musicVolume = newVol;

            SyncCustomAudioSourceVolume();

            return false;
        }
    }

    /*
     * If vanilla Orbitous audio starts playing a sound that
     * is also part of the musicManager's ambience playlist,
     * stop custom music first.
     *
     * This mainly protects against another code path starting
     * normal music without going through PlaySong().
     */
    [HarmonyPatch(typeof(audioManager), "Play", new Type[] { typeof(string) })]
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

            /*
             * Special music should also interrupt custom music.
             * These are the known musicManager special tracks
             * from the Orbitous dump.
             */
            if (name == "ShopSong" ||
                name == "CoolSong" ||
                name == "ChaseSong" ||
                name == "IntroFightSong" ||
                name == "HunterSong" ||
                name == "BlackHoleSong" ||
                name == "CreditsSong")
            {
                StopCustomMusic();
            }
        }
    }

    /*
     * If the game directly asks audioManager.Stop() to stop
     * a music track, also make sure custom ambience is stopped
     * for the relevant music names.
     */
    [HarmonyPatch(typeof(audioManager), "Stop")]
    public static class AudioManagerStopPatch
    {
        private static void Prefix(string name)
        {
            if (name == "ShopSong" ||
                name == "CoolSong" ||
                name == "ChaseSong" ||
                name == "IntroFightSong" ||
                name == "HunterSong" ||
                name == "BlackHoleSong" ||
                name == "CreditsSong")
            {
                StopCustomMusic();
            }
        }
    }
}