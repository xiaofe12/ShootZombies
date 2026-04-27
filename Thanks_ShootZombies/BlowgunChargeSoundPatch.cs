using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch]
public class BlowgunChargeSoundPatch
{
	private static MethodInfo _targetMethod;

	private static FieldInfo _castProgressField;

	private static float _lastCastProgress;

	private static bool _hasPlayedGunshot;

	private static MethodBase TargetMethod()
	{
		if (_targetMethod == null)
		{
			Type type = typeof(Item).Assembly.GetType("SFX_Instance");
			if (type != null)
			{
				_targetMethod = type.GetMethod("Play", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(Vector3) }, null);
				if (_targetMethod == null)
				{
					_targetMethod = type.GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo m) => m.Name == "Play" && m.GetParameters().Length == 1);
				}
			}
		}
		return _targetMethod;
	}

	private static bool Prefix(object __instance)
	{
		try
		{
			Character localCharacter = Character.localCharacter;
			if ((Object)localCharacter == (Object)null)
			{
				return true;
			}
			Player player = localCharacter.player;
			if ((Object)player == (Object)null)
			{
				return true;
			}
			CharacterData data = localCharacter.data;
			Item val = ((data != null) ? data.currentItem : null);
			if ((Object)val == (Object)null || !ItemPatch.IsBlowgunLike(val))
			{
				return true;
			}
			if (_castProgressField == null)
			{
				_castProgressField = ((object)val).GetType().GetField("<castProgress>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			if (_castProgressField == null)
			{
				return true;
			}
			float num = (float)_castProgressField.GetValue(val);
			bool flag = _lastCastProgress >= 0.99f;
			bool flag2 = num >= 0.99f;
			_lastCastProgress = num;
			if (!flag2)
			{
				_hasPlayedGunshot = false;
				return false;
			}
			if (!flag && flag2 && !_hasPlayedGunshot)
			{
				_hasPlayedGunshot = true;
				return false;
			}
			return false;
		}
		catch (Exception)
		{
		}
		return true;
	}
}
