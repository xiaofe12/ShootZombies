using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch]
public class BlowgunInfiniteUsePatch
{
	private const int BlowgunItemId = 70;

	private static Type _actionRaycastDartType;

	private static Type _actionConsumeType;

	private static Type _actionConsumeAndSpawnType;

	private static bool _itemDataAccessorsResolved;

	private static MethodInfo _getDataGenericMethod;

	private static Type _optionableIntItemDataType;

	private static FieldInfo _optionableIntHasDataField;

	private static FieldInfo _optionableIntValueField;

	private static MethodBase TargetMethod()
	{
		MethodInfo[] methods = typeof(Item).GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);
		foreach (MethodInfo methodInfo in methods)
		{
			if (methodInfo.Name.Contains("Use") || methodInfo.Name.Contains("Fire") || methodInfo.Name.Contains("Shoot"))
			{
				return methodInfo;
			}
		}
		return null;
	}

	private static void Postfix(Item __instance)
	{
		try
		{
			if (!Plugin.IsWeaponFeatureEnabled() || !IsBlowgunItem(__instance))
			{
				return;
			}
			SetBlowgunUseCount(__instance, 9999);
			NormalizeEnabledBlowgunRuntimeState(__instance);
			Type type2 = __instance.GetType();
			float num2 = Plugin.FireInterval?.Value ?? 0.4f;
			FieldInfo field2 = type2.GetField("usingTimePrimary", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field2 != null)
			{
				field2.SetValue(__instance, num2);
			}
		}
		catch (Exception)
		{
		}
	}

	internal static void RestoreVanillaSingleUseOnAllBlowguns()
	{
		try
		{
			Item[] array = UnityEngine.Object.FindObjectsByType<Item>((FindObjectsSortMode)0);
			foreach (Item item in array)
			{
				RestoreVanillaSingleUse(item);
			}
		}
		catch (Exception)
		{
		}
	}

	internal static void EnsureInfiniteUse(Item item)
	{
		try
		{
			if (!Plugin.IsWeaponFeatureEnabled() || !IsBlowgunItem(item))
			{
				return;
			}
			SetBlowgunUseCount(item, 9999);
			NormalizeEnabledBlowgunRuntimeState(item);
		}
		catch (Exception)
		{
		}
	}

	private static void RestoreVanillaSingleUse(Item item)
	{
		try
		{
			if (!IsBlowgunItem(item))
			{
				return;
			}
			SetBlowgunUseCount(item, 1);
			NormalizeRaycastDartComponents(item);
			NormalizeRaycastDartComponents(item.isSecretlyOtherItemPrefab);
			RestoreRaycastDartAction(item);
			RestoreRaycastDartAction(item.isSecretlyOtherItemPrefab);
		}
		catch (Exception)
		{
		}
	}

	internal static bool IsBlowgunItem(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		try
		{
			if (item.itemID == BlowgunItemId)
			{
				return true;
			}
			if ((Object)item.isSecretlyOtherItemPrefab != (Object)null && item.isSecretlyOtherItemPrefab.itemID == BlowgunItemId)
			{
				return true;
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	internal static void NormalizeEnabledBlowgunRuntimeState(Item item)
	{
		try
		{
			if (!Plugin.IsWeaponFeatureEnabled() || !IsBlowgunItem(item))
			{
				return;
			}
			NormalizeRaycastDartComponents(item);
			NormalizeRaycastDartComponents(item.isSecretlyOtherItemPrefab);
			DisableConsumableActions(item);
		}
		catch
		{
		}
	}

	private static void NormalizeRaycastDartComponents(Item item)
	{
		try
		{
			if ((Object)item == (Object)null)
			{
				return;
			}
			if (_actionRaycastDartType == null)
			{
				_actionRaycastDartType = typeof(Item).Assembly.GetType("Action_RaycastDart");
			}
			if (_actionRaycastDartType == null)
			{
				return;
			}
			Component[] components = ((Component)item).GetComponentsInChildren(_actionRaycastDartType, true);
			if (components == null || components.Length <= 1)
			{
				return;
			}
			for (int i = 1; i < components.Length; i++)
			{
				if ((Object)components[i] != (Object)null)
				{
					Object.DestroyImmediate((Object)components[i]);
				}
			}
		}
		catch
		{
		}
	}

	private static void DisableConsumableActions(Item item)
	{
		try
		{
			if ((Object)item == (Object)null)
			{
				return;
			}
			if (_actionConsumeType == null)
			{
				_actionConsumeType = typeof(Item).Assembly.GetType("Action_Consume");
			}
			if (_actionConsumeAndSpawnType == null)
			{
				_actionConsumeAndSpawnType = typeof(Item).Assembly.GetType("Action_ConsumeAndSpawn");
			}
			DestroyActionsByType(item, _actionConsumeType);
			DestroyActionsByType(item, _actionConsumeAndSpawnType);
		}
		catch
		{
		}
	}

	private static void DestroyActionsByType(Item item, Type actionType)
	{
		if ((Object)item == (Object)null || actionType == null)
		{
			return;
		}
		Component[] componentsInChildren = ((Component)item).GetComponentsInChildren(actionType, true);
		foreach (Component component in componentsInChildren)
		{
			if ((Object)component != (Object)null)
			{
				Object.DestroyImmediate((Object)component);
			}
		}
	}

	private static void RestoreRaycastDartAction(Item item)
	{
		try
		{
			if ((Object)item == (Object)null)
			{
				return;
			}
			if (_actionRaycastDartType == null)
			{
				_actionRaycastDartType = typeof(Item).Assembly.GetType("Action_RaycastDart");
			}
			if (_actionRaycastDartType == null)
			{
				return;
			}
			Component component = ((Component)item).GetComponentInChildren(_actionRaycastDartType, true);
			if ((Object)component == (Object)null)
			{
				component = ((Component)item).GetComponent(_actionRaycastDartType);
			}
			Behaviour behaviour = component as Behaviour;
			if (behaviour != null)
			{
				behaviour.enabled = true;
			}
		}
		catch
		{
		}
	}

	private static void SetBlowgunUseCount(Item item, int value)
	{
		if ((UnityEngine.Object)item == (UnityEngine.Object)null)
		{
			return;
		}
		if (!TryResolveItemDataAccessors())
		{
			return;
		}
		MethodInfo methodInfo = _getDataGenericMethod.MakeGenericMethod(_optionableIntItemDataType);
		ParameterInfo[] parameters = methodInfo.GetParameters();
		object[] array = new object[parameters.Length];
		for (int num = 0; num < parameters.Length; num++)
		{
			if (parameters[num].ParameterType == typeof(DataEntryKey))
			{
				array[num] = (object)(DataEntryKey)2;
			}
		}
		object obj = methodInfo.Invoke(item, array);
		if (obj == null)
		{
			return;
		}
		_optionableIntHasDataField?.SetValue(obj, true);
		_optionableIntValueField?.SetValue(obj, value);
	}

	private static bool TryResolveItemDataAccessors()
	{
		if (_itemDataAccessorsResolved)
		{
			return _getDataGenericMethod != null && _optionableIntItemDataType != null && _optionableIntValueField != null;
		}
		_itemDataAccessorsResolved = true;
		foreach (MethodInfo method in typeof(Item).GetMethods(BindingFlags.Instance | BindingFlags.Public))
		{
			if (method.Name == "GetData" && method.IsGenericMethod)
			{
				_getDataGenericMethod = method;
				break;
			}
		}
		foreach (Type type in typeof(Item).Assembly.GetTypes())
		{
			if (type.Name == "OptionableIntItemData")
			{
				_optionableIntItemDataType = type;
				break;
			}
		}
		if (_optionableIntItemDataType != null)
		{
			_optionableIntHasDataField = _optionableIntItemDataType.GetField("HasData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			_optionableIntValueField = _optionableIntItemDataType.GetField("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		}
		return _getDataGenericMethod != null && _optionableIntItemDataType != null && _optionableIntValueField != null;
	}
}
