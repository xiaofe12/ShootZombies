using System;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(BackpackWheelSlice), "SharedInit")]
public static class BackpackWheelSharedInitPatch
{
	[HarmonyPostfix]
	public static void SharedInitPostfix(BackpackWheelSlice __instance, BackpackReference bpRef, BackpackWheel wheel)
	{
		if ((Object)(object)__instance == (Object)null)
		{
			return;
		}
		try
		{
			AkUiPatchHelpers.ApplyAkToSliceImage(__instance);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] BackpackWheelSharedInitPatch failed: " + ex.Message));
		}
	}
}

