using System;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(BackpackWheelSlice), "InitStashSlot")]
public static class BackpackWheelStashSlicePatch
{
	[HarmonyPostfix]
	public static void InitStashSlotPostfix(BackpackWheelSlice __instance, BackpackReference bpRef, BackpackWheel wheel)
	{
		if ((Object)(object)__instance == (Object)null)
		{
			return;
		}
		try
		{
			Item val = AkUiPatchHelpers.ResolveItemFromSlice(__instance);
			if ((Object)val == (Object)null)
			{
				Character localCharacter = Character.localCharacter;
				object obj;
				if (localCharacter == null)
				{
					obj = null;
				}
				else
				{
					CharacterData data = localCharacter.data;
					obj = ((data != null) ? data.currentItem : null);
				}
				val = (Item)obj;
			}
			if (!((Object)val == (Object)null) && ItemPatch.IsBlowgunLike(val))
			{
				AkUiPatchHelpers.ApplyAkToSliceImage(__instance, val);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[ShootZombies] BackpackWheelStashSlicePatch failed: " + ex));
		}
	}
}
