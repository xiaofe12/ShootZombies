using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(InventoryItemUI), "SetItem")]
public static class InventoryItemUiPatch
{
	[HarmonyPostfix]
	public static void SetItemPostfix(InventoryItemUI __instance, ItemSlot slot)
	{
		if ((Object)(object)__instance == (Object)null || slot == null || slot.IsEmpty())
		{
			return;
		}
		try
		{
			Item val = AkUiPatchHelpers.ResolveItemFromSlot(slot);
			Item item = (((Object)val != (Object)null) ? val : slot.prefab);
			if (ItemPatch.IsBlowgunLike(item))
			{
				ItemPatch.ApplyAkDisplayIfNeeded(item);
				if ((Object)slot.prefab != (Object)null)
				{
					ItemPatch.ApplyAkDisplayIfNeeded(slot.prefab);
				}
				AkUiPatchHelpers.ApplyAkToInventoryUi(__instance, item);
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[ShootZombies] InventoryItemUiPatch failed: " + ex));
		}
	}
}
