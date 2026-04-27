using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch]
public static class DartImpactPatch
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
			MethodInfo methodInfo = typeof(Item).Assembly.GetType("Action_RaycastDart")?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(MethodInfo m)
			{
				if (!string.Equals(m.Name, "RPC_DartImpact", StringComparison.Ordinal))
				{
					return false;
				}
				ParameterInfo[] parameters = m.GetParameters();
				if (parameters.Length != 3 && parameters.Length != 4)
				{
					return false;
				}
				return parameters[0].ParameterType == typeof(int) && parameters[1].ParameterType == typeof(Vector3) && parameters[2].ParameterType == typeof(Vector3);
			});
			if (methodInfo != null)
			{
				_targetMethod = methodInfo;
				return _targetMethod;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[DartImpactPatch] TargetMethod error: " + ex));
		}
		Plugin.Log.LogWarning((object)"[DartImpactPatch] TargetMethod returning null - RPC_DartImpact method not found");
		return null;
	}

	[HarmonyPrefix]
	public static bool DartImpactPrefix(MonoBehaviour __instance, int characterID, Vector3 origin, Vector3 endpoint)
	{
		try
		{
			Item componentInParent2 = ((Component)__instance).GetComponentInParent<Item>();
			if (!Plugin.IsWeaponFeatureEnabled() || (Object)componentInParent2 == (Object)null || !ItemPatch.IsBlowgunLike(componentInParent2))
			{
				return true;
			}
			if (characterID > 0)
			{
				PhotonView val = PhotonView.Find(characterID);
				if ((Object)val != (Object)null)
				{
					Character componentInParent = ((Component)val).gameObject.GetComponentInParent<Character>();
					if ((Object)componentInParent != (Object)null && (componentInParent.isZombie || componentInParent.isBot))
					{
						Plugin.HandleZombieDartImpactVisual((Component)(object)__instance, endpoint);
						Plugin.Instance?.HitZombie(componentInParent, origin);
						return false;
					}
					if ((Object)componentInParent != (Object)null && !componentInParent.isZombie && !componentInParent.isBot)
					{
						return false;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[DartImpactPatch] Error: " + ex));
		}
		return true;
	}
}

[HarmonyPatch]
public static class LocalDartImpactPatch
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
			MethodInfo methodInfo = typeof(Item).Assembly.GetType("Action_RaycastDart")?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => string.Equals(m.Name, "DartImpact", StringComparison.Ordinal) && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(Character) && m.GetParameters()[1].ParameterType == typeof(Vector3) && m.GetParameters()[2].ParameterType == typeof(Vector3));
			if (methodInfo != null)
			{
				_targetMethod = methodInfo;
				return _targetMethod;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[LocalDartImpactPatch] TargetMethod error: " + ex));
		}
		Plugin.Log.LogWarning((object)"[LocalDartImpactPatch] TargetMethod returning null - DartImpact method not found");
		return null;
	}

	[HarmonyPrefix]
	public static bool DartImpactPrefix(MonoBehaviour __instance, Character hitCharacter, Vector3 origin, Vector3 endpoint)
	{
		try
		{
			Item componentInParent = ((Component)__instance).GetComponentInParent<Item>();
			if (!Plugin.IsWeaponFeatureEnabled() || (Object)componentInParent == (Object)null || !ItemPatch.IsBlowgunLike(componentInParent) || (Object)hitCharacter == (Object)null)
			{
				return true;
			}
			if (hitCharacter.isZombie || hitCharacter.isBot)
			{
				Plugin.HandleZombieDartImpactVisual((Component)(object)__instance, endpoint);
				Plugin.Instance?.HitZombie(hitCharacter, origin);
				return false;
			}
			return false;
		}
		catch (Exception ex)
		{
			Plugin.Log.LogError((object)("[LocalDartImpactPatch] Error: " + ex));
		}
		return true;
	}
}

