using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(ItemCooking), "Wreck")]
public class BlowgunWreckPatch
{
	private static bool Prefix(ItemCooking __instance)
	{
		try
		{
			Item component = ((Component)__instance).GetComponent<Item>();
			if ((Object)component != (Object)null)
			{
				string name = component.GetName();
		if ((name != null && name.Contains("吹箭筒")) || (name != null && name.Contains("Blowgun")) || (name != null && name.Contains("HealingDart")) || (name != null && name.Contains("Dart")))
				{
					return false;
				}
			}
		}
		catch
		{
		}
		return true;
	}
}


