using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(InventoryItemUI), "UpdateCookedAmount")]
public static class InventoryItemUiCookColorPatch
{
	[HarmonyPostfix]
	public static void UpdateCookedAmountPostfix(InventoryItemUI __instance)
	{
		if ((Object)(object)__instance == (Object)null)
		{
			return;
		}
		try
		{
			AkUiPatchHelpers.ApplyAkToInventoryUi(__instance);
		}
		catch
		{
		}
	}
}

