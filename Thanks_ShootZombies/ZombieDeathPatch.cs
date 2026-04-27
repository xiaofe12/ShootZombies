using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(MushroomZombie), "Update")]
public static class ZombieDeathPatch
{
	private static HashSet<GameObject> _processedZombies = new HashSet<GameObject>();

	[HarmonyPostfix]
	public static void Postfix(MushroomZombie __instance)
	{
		if ((Object)__instance == (Object)null)
		{
			return;
		}
		try
		{
			GameObject gameObject = ((Component)__instance).gameObject;
			if ((Object)gameObject == (Object)null || _processedZombies.Contains(gameObject))
			{
				return;
			}
			Character component = ((Component)__instance).GetComponent<Character>();
			if ((Object)component == (Object)null)
			{
				return;
			}
			bool flag = false;
			if ((Object)component.data != (Object)null && component.data.dead)
			{
				flag = true;
			}
			FieldInfo field = ((object)component).GetType().GetField("data", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				object value = field.GetValue(component);
				if (value != null)
				{
					FieldInfo field2 = value.GetType().GetField("dead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (field2 != null)
					{
						flag = (bool)field2.GetValue(value);
					}
				}
			}
			if (!flag)
			{
				return;
			}
			_processedZombies.Add(gameObject);
			if (!((Object)gameObject != (Object)null))
			{
				return;
			}
			_processedZombies.Remove(gameObject);
			ZombieSpawner.RemoveZombie(gameObject);
			if (PhotonNetwork.IsMasterClient)
			{
				PhotonView component2 = gameObject.GetComponent<PhotonView>();
				if ((Object)component2 != (Object)null)
				{
					PhotonNetwork.Destroy(component2);
				}
				else
				{
					PhotonNetwork.Destroy(gameObject);
				}
			}
		}
		catch (Exception)
		{
		}
	}
}

