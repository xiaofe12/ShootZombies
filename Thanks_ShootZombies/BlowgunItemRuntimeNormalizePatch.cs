using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ShootZombies;

[HarmonyPatch]
public static class BlowgunItemRuntimeNormalizePatch
{
	private static readonly HashSet<string> TargetMethodNames = new HashSet<string>(StringComparer.Ordinal)
	{
		"Awake",
		"OnEnable",
		"Start",
		"OnInstanceDataSet",
		"SetState"
	};

	private static IEnumerable<MethodBase> TargetMethods()
	{
		foreach (MethodInfo method in typeof(Item).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			if (TargetMethodNames.Contains(method.Name))
			{
				yield return method;
			}
		}
	}

	private static void Postfix(Item __instance)
	{
		BlowgunInfiniteUsePatch.NormalizeDartRpcComponents(__instance);
		if (!Plugin.IsWeaponFeatureEnabled())
		{
			BlowgunInfiniteUsePatch.RestoreVanillaSingleUse(__instance);
			return;
		}
		BlowgunInfiniteUsePatch.EnsureInfiniteUse(__instance);
		Plugin.TryProtectWeaponItemRuntime(__instance);
	}
}
