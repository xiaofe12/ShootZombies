using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(BackpackWheel), "UpdateCookedAmount")]
public static class BackpackWheelCookColorPatch
{
	[HarmonyPostfix]
	public static void UpdateCookedAmountPostfix(BackpackWheel __instance, Item item)
	{
		if ((Object)(object)__instance == (Object)null)
		{
			return;
		}
		try
		{
			object obj;
			if (!((Object)item != (Object)null))
			{
				Character localCharacter = Character.localCharacter;
				if (localCharacter == null)
				{
					obj = null;
				}
				else
				{
					CharacterData data = localCharacter.data;
					obj = ((data != null) ? data.currentItem : null);
				}
			}
			else
			{
				obj = item;
			}
			Item val = (Item)obj;
			if (!((Object)val == (Object)null) && ItemPatch.IsBlowgunLike(val))
			{
				AkUiPatchHelpers.ApplyAkIconForce(__instance.currentlyHeldItem);
			}
		}
		catch
		{
		}
	}
}

