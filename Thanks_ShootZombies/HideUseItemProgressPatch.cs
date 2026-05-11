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
			if (!Plugin.IsWeaponFeatureEnabled())
			{
				return true;
			}
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
			if (BlowgunInfiniteUsePatch.IsBlowgunItem(val))
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

	internal static void RestoreVanillaUseProgressOnAllUi()
	{
		try
		{
			UI_UseItemProgress[] array = Object.FindObjectsByType<UI_UseItemProgress>((FindObjectsSortMode)0);
			foreach (UI_UseItemProgress ui in array)
			{
				RestoreVanillaUseProgressUi(ui);
			}
		}
		catch
		{
		}
	}

	private static void RestoreVanillaUseProgressUi(UI_UseItemProgress ui)
	{
		if ((Object)(object)ui == (Object)null)
		{
			return;
		}
		try
		{
			Character localCharacter = Character.localCharacter;
			Item item = localCharacter?.data?.currentItem;
			float progress = ((Object)item != (Object)null) ? item.progress : 0f;
			bool visible = progress > 0f;
			if ((Object)ui.fill != (Object)null)
			{
				ui.fill.fillAmount = visible ? progress : 0f;
				((Behaviour)ui.fill).enabled = true;
			}
			if ((Object)ui.empty != (Object)null)
			{
				((Behaviour)ui.empty).enabled = true;
			}
		}
		catch
		{
		}
	}
}

