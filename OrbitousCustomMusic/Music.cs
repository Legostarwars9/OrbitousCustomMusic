using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.IO;
using System.Collections.Generic;

[BepInPlugin("com.username.orbitousmusic", "Orbitous Dynamic Music", "1.0.0")]
public class OrbitousMusicMod : BaseUnityPlugin
{
    private static List<AudioClip> customClips = new List<AudioClip>();
    private static List<string> customSongNames = new List<string>();

    void Awake()
    {
        // 1. Establish path to your specific mod folder
        string modFolder = Path.Combine(Paths.PluginPath, "OrbitousMusic");

        // Create the directory automatically if a player boots the game without it
        if (!Directory.Exists(modFolder))
        {
            Directory.CreateDirectory(modFolder);
            Logger.LogWarning($"Created missing music folder at: {modFolder}. Drop your .wav files here!");
            return; 
        }

        // 2. Scan the directory for all .wav files
        string[] audioFiles = Directory.GetFiles(modFolder, "*.wav");

        if (audioFiles.Length == 0)
        {
            Logger.LogInfo("No custom .wav songs found in the OrbitousMusic folder.");
            return;
        }

        // 3. Loop through and load every found file dynamically
        foreach (string filePath in audioFiles)
        {
            // Extracts "MySong" from "BepInEx/plugins/OrbitousMusic/MySong.wav"
            string cleanSongName = Path.GetFileNameWithoutExtension(filePath);
            
            LoadSong(filePath, cleanSongName);
        }

        // 4. Initialize Harmony if we successfully queued up assets
        if (customClips.Count > 0)
        {
            var harmony = new Harmony("com.username.orbitousmusic");
            harmony.PatchAll();
            Logger.LogInfo($"Successfully hooked audioManager with {customClips.Count} dynamic songs!");
        }
    }

    private void LoadSong(string absolutePath, string songName)
    {
        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("file://" + absolutePath, AudioType.WAV))
        {
            www.SendWebRequest();
            while (!www.isDone) { } // Synchronous hold for initialization processing

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                clip.name = songName;
                
                customClips.Add(clip);
                customSongNames.Add(songName); // Cache name matching the clip index
                Logger.LogInfo($"Dynamically loaded: {songName}");
            }
            else
            {
                Logger.LogError($"Failed loading {songName}: {www.error}");
            }
        }
    }

    // 5. Harmony Prefix Hook
    [HarmonyPatch(typeof(audioManager), "Awake")]
    public class AudioManagerAwakePatch
    {
        static void Prefix(audioManager __instance)
        {
            if (__instance.sounds == null || customClips.Count == 0) return;

            Type soundType = __instance.sounds.GetType().GetElementType();
            var originalArray = __instance.sounds;
            
            // Allocate exact space for whatever number of songs were found in the folder
            var newArray = Array.CreateInstance(soundType, originalArray.Length + customClips.Count);
            Array.Copy(originalArray, newArray, originalArray.Length);

            for (int i = 0; i < customClips.Count; i++)
            {
                object newSoundInstance = Activator.CreateInstance(soundType);

                soundType.GetField("name").SetValue(newSoundInstance, customSongNames[i]);
                soundType.GetField("clip").SetValue(newSoundInstance, customClips[i]);
                soundType.GetField("volume").SetValue(newSoundInstance, 1.0f);
                soundType.GetField("pitch").SetValue(newSoundInstance, 1.0f);

                newArray.SetValue(newSoundInstance, originalArray.Length + i);
            }

            __instance.sounds = (dynamic)newArray;
        }
    }
}
