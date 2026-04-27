using System;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(BackpackWheel), "InitWheel")]
public static class BackpackWheelPatch
{
	[HarmonyPostfix]
	public static void InitWheelPostfix(BackpackWheel __instance)
	{
		if ((Object)(object)__instance == (Object)null)
		{
			return;
		}
		try
		{
			AkUiPatchHelpers.ApplyAkToBackpackWheel(__instance);
			BackpackWheelSlice[] componentsInChildren = ((Component)__instance).GetComponentsInChildren<BackpackWheelSlice>(true);
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				AkUiPatchHelpers.ApplyAkToSliceImage(componentsInChildren[i]);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[ShootZombies] BackpackWheelPatch failed: " + ex));
		}
	}
}
