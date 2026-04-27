using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch]
public class BlowgunInfiniteUsePatch
{
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
			if (!ItemPatch.IsBlowgunLike(__instance, __instance?.GetName()))
			{
				return;
			}
			MethodInfo methodInfo = (from m in typeof(Item).GetMethods(BindingFlags.Instance | BindingFlags.Public)
				where m.Name == "GetData"
				select m).ToList().FirstOrDefault((MethodInfo m) => m.IsGenericMethod);
			if (methodInfo != null)
			{
				Type type = typeof(Item).Assembly.GetTypes().FirstOrDefault((Type t) => t.Name == "OptionableIntItemData");
				if (type != null)
				{
					MethodInfo methodInfo2 = methodInfo.MakeGenericMethod(type);
					ParameterInfo[] parameters = methodInfo2.GetParameters();
					object[] array = new object[parameters.Length];
					for (int num = 0; num < parameters.Length; num++)
					{
						if (parameters[num].ParameterType == typeof(DataEntryKey))
						{
							array[num] = (object)(DataEntryKey)2;
						}
						else
						{
							array[num] = null;
						}
					}
					object obj = methodInfo2.Invoke(__instance, array);
					if (obj != null)
					{
						FieldInfo field = obj.GetType().GetField("Value", BindingFlags.Instance | BindingFlags.Public);
						if (field != null)
						{
							field.SetValue(obj, 9999);
						}
					}
				}
			}
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
}
