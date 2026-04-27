using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(UI_UseItemProgress), "UpdateFillAmount")]
public static class HideUseItemProgressPatch
{
	private static bool Prefix(UI_UseItemProgress __instance, ref bool __result)
	{
		try
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
			Item val = (Item)obj;
			if ((Object)val != (Object)null && ItemPatch.IsBlowgunLike(val))
			{
				if ((Object)__instance.fill != (Object)null)
				{
					__instance.fill.fillAmount = 0f;
				}
				__result = false;
				return false;
			}
		}
		catch
		{
		}
		return true;
	}
}

