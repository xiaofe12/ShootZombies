using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch]
public static class ItemUIDataPatch
{
	private static MethodBase _targetMethod;

	private static bool _hasPerformedFallbackVisibleUiScan;

	public static MethodBase TargetMethod()
	{
		if (_targetMethod != null)
		{
			return _targetMethod;
		}
		try
		{
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			foreach (Assembly assembly in assemblies)
			{
				try
				{
					Type type = assembly.GetTypes().FirstOrDefault((Type t) => t.Name == "ItemUIData");
					if (!(type == null))
					{
						MethodInfo method = type.GetMethod("GetIcon", BindingFlags.Instance | BindingFlags.Public);
						if (method != null)
						{
							_targetMethod = method;
							return _targetMethod;
						}
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return null;
	}

	[HarmonyPostfix]
	public static void GetIconPostfix(object __instance, ref Texture2D __result)
	{
		try
		{
			Texture2D akIconTexture = Plugin.GetAkIconTexture();
			if (!((Object)akIconTexture == (Object)null) && ShouldUseAkIcon(__instance.GetType().GetField("itemName", BindingFlags.Instance | BindingFlags.Public)?.GetValue(__instance) as string))
			{
				__result = akIconTexture;
			}
		}
		catch
		{
		}
	}

	private static bool ShouldUseAkIcon(string itemName)
	{
		return ItemPatch.ContainsWeaponKeyword(itemName);
	}

	public static void ForceRefreshVisibleUi()
	{
		if (Plugin.IsRuntimeVisualRefreshBlocked())
		{
			return;
		}
		try
		{
			Plugin.GetAkIconTexture();
			if (!AkUiPatchHelpers.RefreshTrackedUi() && !_hasPerformedFallbackVisibleUiScan)
			{
				FallbackScanVisibleUiOnce();
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] ForceRefreshVisibleUi failed: " + ex.Message));
		}
	}

	private static void FallbackScanVisibleUiOnce()
	{
		_hasPerformedFallbackVisibleUiScan = true;
		InventoryItemUI[] array = Object.FindObjectsByType<InventoryItemUI>((FindObjectsSortMode)0);
		for (int i = 0; i < array.Length; i++)
		{
			AkUiPatchHelpers.ApplyAkToInventoryUi(array[i]);
		}
		BackpackWheelSlice[] array2 = Object.FindObjectsByType<BackpackWheelSlice>((FindObjectsSortMode)0);
		for (int i = 0; i < array2.Length; i++)
		{
			AkUiPatchHelpers.ApplyAkToSliceImage(array2[i]);
		}
		BackpackWheel[] array3 = Object.FindObjectsByType<BackpackWheel>((FindObjectsSortMode)0);
		for (int i = 0; i < array3.Length; i++)
		{
			AkUiPatchHelpers.ApplyAkToBackpackWheel(array3[i]);
		}
	}

	internal static void ResetVisibleUiTrackingFallback()
	{
		_hasPerformedFallbackVisibleUiScan = false;
	}
}
