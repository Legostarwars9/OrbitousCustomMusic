using BepInEx;
using HarmonyLib;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

[BepInPlugin("com.legostarwars9.orbitousmusic", "OrbitousCustomMusic", "1.0.0")]
public class OrbitousMusicMod : BaseUnityPlugin
{
    private static List<AudioClip> customClips = new List<AudioClip>();
    private static List<string> customSongNames = new List<string>();

    void Awake()
    {
        string modFolder = Path.Combine(Paths.PluginPath, "OrbitousMusic");

        if (!Directory.Exists(modFolder))
        {
            Directory.CreateDirectory(modFolder);
            Logger.LogWarning($"Created missing music folder at: {modFolder}. Drop your .wav files here!");
            return; 
        }

        string[] audioFiles = Directory.GetFiles(modFolder, "*.wav");

        foreach (string filePath in audioFiles)
        {
            string cleanSongName = Path.GetFileNameWithoutExtension(filePath);
            
            try
            {
                // Load using clean .NET file streaming—bypassing all UnityWebRequest/WWW modules
                AudioClip clip = LoadWavAsAudioClip(filePath, cleanSongName);
                if (clip != null)
                {
                    customClips.Add(clip);
                    customSongNames.Add(cleanSongName);
                    Logger.LogInfo($"Dynamically loaded via native stream: {cleanSongName}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed parsing wav structure for {cleanSongName}: {ex.Message}");
            }
        }

        if (customClips.Count > 0)
        {
            var harmony = new Harmony("com.legostarwars9.orbitousmusic");
            harmony.PatchAll();
            Logger.LogInfo($"Successfully hooked audioManager with {customClips.Count} native tracks!");
        }
    }

    // Hand-parses a standard uncompressed PCM .wav file directly into a Unity AudioClip
    private static AudioClip LoadWavAsAudioClip(string filePath, string clipName)
    {
        byte[] wavData = File.ReadAllBytes(filePath);

        // Standard WAV headers locate channels at byte 22, and sample rate at byte 24
        ushort channels = BitConverter.ToUInt16(wavData, 22);
        int sampleRate = BitConverter.ToInt32(wavData, 24);

        // Position where raw PCM audio data actually begins (usually byte 44)
        int dataPos = 12;
        while (dataPos < wavData.Length - 8)
        {
            if (wavData[dataPos] == 'd' && wavData[dataPos + 1] == 'a' && wavData[dataPos + 2] == 't' && wavData[dataPos + 3] == 'a')
            {
                dataPos += 4;
                break;
            }
            dataPos++;
        }

        int subChunk2Size = BitConverter.ToInt32(wavData, dataPos);
        dataPos += 4;

        // Convert the 16-bit integer bytes into floating-point audio data (-1.0 to 1.0)
        int sampleCount = subChunk2Size / 2; 
        float[] audioData = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short sample = BitConverter.ToInt16(wavData, dataPos + (i * 2));
            audioData[i] = sample / 32768f; 
        }

        // Generate the native Unity structure inside memory
        AudioClip audioClip = AudioClip.Create(clipName, sampleCount / channels, channels, sampleRate, false);
        audioClip.SetData(audioData, 0);

        return audioClip;
    }

    [HarmonyPatch(typeof(audioManager), "Awake")]
    public class AudioManagerAwakePatch
    {
        static void Prefix(audioManager __instance)
        {
            if (__instance.sounds == null || customClips.Count == 0) return;

            Type soundType = __instance.sounds.GetType().GetElementType();
            var originalArray = __instance.sounds;
            
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
