using System;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(BackpackWheelSlice), "InitItemSlot")]
public static class BackpackWheelSlicePatch
{
	[HarmonyPostfix]
	public static void InitItemSlotPostfix(BackpackWheelSlice __instance, (BackpackReference, byte) slot, BackpackWheel wheel)
	{
		if ((Object)(object)__instance == (Object)null)
		{
			return;
		}
		try
		{
			ItemSlot val = slot.Item1.GetData().itemSlots[slot.Item2];
			if (val != null && !val.IsEmpty() && ItemPatch.IsBlowgunLike(val.prefab))
			{
				Item val2 = AkUiPatchHelpers.ResolveItemFromSlot(val);
				Item item = (((Object)val2 != (Object)null) ? val2 : val.prefab);
				ItemPatch.ApplyAkDisplayIfNeeded(item);
				ItemPatch.ApplyAkDisplayIfNeeded(val.prefab);
				AkUiPatchHelpers.ApplyAkToSliceImage(__instance, item);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[ShootZombies] BackpackWheelSlicePatch failed: " + ex));
		}
	}
}
