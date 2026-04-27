using System;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(ItemSlot), "SetItem")]
public static class ItemSlotSetItemPatch
{
	[HarmonyPostfix]
	public static void SetItemPostfix(ItemSlot __instance, object[] __args)
	{
		if (__instance == null)
		{
			return;
		}
		try
		{
			Item val = null;
			if (__args != null && __args.Length != 0)
			{
				object obj = __args[0];
				val = (Item)((obj is Item) ? obj : null);
			}
			if ((Object)val == (Object)null)
			{
				val = AkUiPatchHelpers.ResolveItemFromSlot(__instance);
			}
			if ((Object)val != (Object)null && ItemPatch.IsBlowgunLike(val))
			{
				ItemPatch.ApplyAkDisplayIfNeeded(val);
			}
			if ((Object)__instance.prefab != (Object)null && ItemPatch.IsBlowgunLike(__instance.prefab))
			{
				ItemPatch.ApplyAkDisplayIfNeeded(__instance.prefab);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] ItemSlotSetItemPatch failed: " + ex.Message));
		}
	}
}
