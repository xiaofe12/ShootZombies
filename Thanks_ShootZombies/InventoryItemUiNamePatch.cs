using System;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(InventoryItemUI), "UpdateNameText")]
public static class InventoryItemUiNamePatch
{
	[HarmonyPostfix]
	public static void UpdateNameTextPostfix(InventoryItemUI __instance)
	{
		if ((Object)(object)__instance == (Object)null)
		{
			return;
		}
		try
		{
			AkUiPatchHelpers.ApplyAkToInventoryUi(__instance);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] InventoryItemUiNamePatch failed: " + ex.Message));
		}
	}
}

