using MelonLoader;
using RoomCodeDisplay;
using UnityEngine;

// This is used by MelonLoader to initialize your mod. Please put all of your mod code in Main.cs.

[assembly: MelonInfo(typeof(PluginMelonLoader), RoomCodeDisplay.Constants.Name, RoomCodeDisplay.Constants.Version, RoomCodeDisplay.Constants.Author)]
[assembly: MelonGame("Another Axiom", "Gorilla Tag")]
[assembly: HarmonyDontPatchAll]

namespace RoomCodeDisplay;

public class PluginMelonLoader : MelonMod
{
    public override void OnLateInitializeMelon()
    {
        GameObject obj = new GameObject(Constants.Guid);
        obj.AddComponent<Main>();
        Object.DontDestroyOnLoad(obj);
    }
}
