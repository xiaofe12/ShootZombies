using System;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(BackpackWheelSlice), "SetItemIcon")]
public static class BackpackWheelSetItemIconPatch
{
	[HarmonyPostfix]
	public static void SetItemIconPostfix(BackpackWheelSlice __instance, Item iconHolder, ItemInstanceData itemInstanceData)
	{
		if ((Object)(object)__instance == (Object)null)
		{
			return;
		}
		try
		{
			Item val = (((Object)iconHolder != (Object)null) ? iconHolder : AkUiPatchHelpers.ResolveItemFromSlice(__instance));
			if (!((Object)val == (Object)null) && ItemPatch.IsBlowgunLike(val))
			{
				AkUiPatchHelpers.ApplyAkToSliceImage(__instance, val);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] BackpackWheelSetItemIconPatch failed: " + ex.Message));
		}
	}
}
