using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch]
public static class SuppressOriginalBlowgunFirePatch
{
	private static MethodBase _targetMethod;

	public static MethodBase TargetMethod()
	{
		if (_targetMethod != null)
		{
			return _targetMethod;
		}
		try
		{
			MethodInfo methodInfo = typeof(Item).Assembly.GetType("Action_RaycastDart")?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => string.Equals(m.Name, "RunAction", StringComparison.Ordinal) && m.GetParameters().Length == 0);
			if (methodInfo != null)
			{
				_targetMethod = methodInfo;
				return _targetMethod;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[SuppressOriginalBlowgunFirePatch] TargetMethod error: " + ex));
		}
		Plugin.Log.LogWarning((object)"[SuppressOriginalBlowgunFirePatch] TargetMethod returning null - RunAction method not found");
		return null;
	}

	[HarmonyPrefix]
	public static bool RunActionPrefix(MonoBehaviour __instance)
	{
		try
		{
			Item componentInParent = ((Component)__instance).GetComponentInParent<Item>();
			if (!Plugin.IsWeaponFeatureEnabled() || (Object)componentInParent == (Object)null)
			{
				return true;
			}
			if (ItemPatch.IsBlowgunLike(componentInParent))
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[SuppressOriginalBlowgunFirePatch] RunActionPrefix failed: " + ex.Message));
		}
		return true;
	}
}
