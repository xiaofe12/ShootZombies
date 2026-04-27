using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(BackpackWheelSlice), "UpdateCookedAmount")]
public static class BackpackWheelSliceCookColorPatch
{
	[HarmonyPostfix]
	public static void UpdateCookedAmountPostfix(BackpackWheelSlice __instance, Item item, ItemInstanceData itemInstanceData)
	{
		if ((Object)(object)__instance == (Object)null)
		{
			return;
		}
		try
		{
			Item val = (((Object)item != (Object)null) ? item : AkUiPatchHelpers.ResolveItemFromSlice(__instance));
			if (!((Object)val == (Object)null) && ItemPatch.IsBlowgunLike(val))
			{
				AkUiPatchHelpers.ApplyAkIconForce(__instance.image);
			}
		}
		catch
		{
		}
	}
}

