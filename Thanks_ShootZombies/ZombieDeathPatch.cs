using System;
using System.Collections.Generic;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch(typeof(MushroomZombie), "Update")]
public static class ZombieDeathPatch
{
	private static readonly HashSet<int> ProcessedZombieIds = new HashSet<int>();

	private static readonly Dictionary<int, Character> ZombieCharacterCache = new Dictionary<int, Character>();

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
			if ((Object)gameObject == (Object)null)
			{
				return;
			}
			int instanceID = ((Object)__instance).GetInstanceID();
			if (ProcessedZombieIds.Contains(instanceID))
			{
				return;
			}
			Character component;
			if (!ZombieCharacterCache.TryGetValue(instanceID, out component) || (Object)component == (Object)null)
			{
				component = ((Component)__instance).GetComponent<Character>();
				if ((Object)component == (Object)null)
				{
					ZombieCharacterCache.Remove(instanceID);
					return;
				}
				ZombieCharacterCache[instanceID] = component;
			}
			CharacterData data = component.data;
			if ((Object)data == (Object)null || !data.dead)
			{
				return;
			}
			ProcessedZombieIds.Add(instanceID);
			ZombieCharacterCache.Remove(instanceID);
			if (!((Object)gameObject != (Object)null))
			{
				return;
			}
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

	internal static void ClearCaches()
	{
		ProcessedZombieIds.Clear();
		ZombieCharacterCache.Clear();
	}
}
