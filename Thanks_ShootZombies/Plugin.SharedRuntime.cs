﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Zorro.Core;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;
using RoomPlayer = Photon.Realtime.Player;

namespace ShootZombies;

public partial class Plugin
{
	public static bool IsModFeatureEnabled()
	{
		if (ModEnabled != null)
		{
			return ModEnabled.Value;
		}
		return true;
	}

	internal static bool HasOnlineRoomSession()
	{
		if (!PhotonNetwork.InRoom || PhotonNetwork.OfflineMode)
		{
			return false;
		}
		return PhotonNetwork.CurrentRoom != null;
	}

	internal static bool HasGameplayAuthority()
	{
		if (PhotonNetwork.OfflineMode)
		{
			return true;
		}
		if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
		{
			return true;
		}
		return PhotonNetwork.IsMasterClient;
	}

	private static bool ShouldRelayZombieHitToHost()
	{
		return HasOnlineRoomSession() && !PhotonNetwork.IsMasterClient;
	}

	public static bool IsWeaponFeatureEnabled()
	{
		if (IsModFeatureEnabled())
		{
			if (WeaponEnabled != null)
			{
				return WeaponEnabled.Value;
			}
			return true;
		}
		return false;
	}

	private static bool IsZombieSpawnFeatureEnabled()
	{
		if (IsModFeatureEnabled())
		{
			return Mathf.Max((MaxZombies != null) ? MaxZombies.Value : 0, 0) > 0;
		}
		return false;
	}

	internal static bool IsZombieSpawnFeatureEnabledRuntime()
	{
		return IsZombieSpawnFeatureEnabled();
	}

	private static bool IsFogModeEnabled()
	{
		return false;
	}

	public static void ClearLocalAmbientColdStatus()
	{
	}

	private void RemoveLegacyRecoilConfig()
	{
		try
		{
			ConfigFile config = ((BaseUnityPlugin)this).Config;
			IDictionary dictionary = ((config != null) ? (((object)config).GetType().GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(config) as IDictionary) : null);
			string[] array = new string[5] { "Recoil", "Enable Recoil", "Recoil Pitch", "Recoil Yaw", "Recoil Max Angle" };
			foreach (string text in array)
			{
				foreach (ConfigDefinition item in BuildConfigDefinitionAliases("Weapon", text))
				{
					config?.Remove(item);
					dictionary?.Remove(item);
				}
				foreach (ConfigDefinition item2 in BuildConfigDefinitionAliases("Features", text))
				{
					config?.Remove(item2);
					dictionary?.Remove(item2);
				}
			}
			config?.Save();
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] RemoveLegacyRecoilConfig failed: " + DescribeReflectionException(ex)));
		}
	}

	private bool ShouldShowWeaponLobbyNotice()
	{
		Scene activeScene = SceneManager.GetActiveScene();
		if (IsLobbyScene(activeScene) && IsWeaponFeatureEnabled())
		{
			return !IsTitleScene(activeScene);
		}
		return false;
	}

	private void CheckSpawnWeaponKey()
	{
		if (IsWeaponFeatureEnabled() && SpawnWeaponKey != null && (int)SpawnWeaponKey.Value != 0 && Input.GetKeyDown(SpawnWeaponKey.Value))
		{
			SpawnWeaponAtPlayer();
		}
	}

	private static void SetLayerRecursively(Transform root, int layer)
	{
		if (!((Object)root == (Object)null))
		{
			((Component)root).gameObject.layer = layer;
			for (int i = 0; i < root.childCount; i++)
			{
				SetLayerRecursively(root.GetChild(i), layer);
			}
		}
	}

	private void LogLocalHeldDebugSphereState(string phase, Item sourceItem, Transform viewAnchor, int resolvedLayer)
	{
		if ((Object)_localHeldDebugSphereRoot == (Object)null)
		{
			return;
		}
		Camera val = ResolveCameraForAnchor(viewAnchor);
		int instanceID = (((Object)sourceItem != (Object)null) ? ((Object)sourceItem).GetInstanceID() : 0);
		LogDiagnosticOnce("local-held-debug-sphere:" + phase + ":" + instanceID + ":" + ((Object)_localHeldDebugSphereRoot).GetInstanceID(), $"Local held debug sphere: phase={phase}, item={FormatHeldItemForDiagnostics(sourceItem)}, view={FormatTransformPath(viewAnchor)}, camera={(((Object)val != (Object)null) ? ((Object)val).name : "null")}, resolvedLayer={resolvedLayer}, rootPath={FormatTransformPath(_localHeldDebugSphereRoot)}, localPos={_localHeldDebugSphereRoot.localPosition}, localRot={_localHeldDebugSphereRoot.localRotation.eulerAngles}, localScale={_localHeldDebugSphereRoot.localScale}, bounds={DescribeLocalBoundsForDiagnostics(_localHeldDebugSphereRoot)}, renderers={DescribeLocalRendererCollectionForDiagnostics(_localHeldDebugSphereRoot)}");
	}

	private static int GetPreferredWeaponSlot(Player player)
	{
		if ((Object)player == (Object)null || player.itemSlots == null || player.itemSlots.Length == 0)
		{
			return -1;
		}
		if (player.itemSlots[0].IsEmpty())
		{
			return 0;
		}
		for (int i = 1; i < player.itemSlots.Length; i++)
		{
			if (player.itemSlots[i].IsEmpty())
			{
				return i;
			}
		}
		return -1;
	}

	private static Dictionary<string, string> ParseRoomConfigPayload(string payload)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(payload))
		{
			return dictionary;
		}
		string[] array = payload.Split(new char[1] { '|' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			int num = text.IndexOf('=');
			if (num > 0 && num < text.Length - 1)
			{
				string key = text.Substring(0, num);
				string value = Uri.UnescapeDataString(text.Substring(num + 1));
				dictionary[key] = value;
			}
		}
		return dictionary;
	}

	private void InitializeRoomConfigCallbacks()
	{
		if (!_roomConfigCallbacksRegistered)
		{
			if (_roomConfigCallbackProxy == null)
			{
				_roomConfigCallbackProxy = new RoomConfigCallbackProxy();
			}
			PhotonNetwork.AddCallbackTarget((object)_roomConfigCallbackProxy);
			_roomConfigCallbacksRegistered = true;
		}
	}

	private void ReleaseRoomConfigCallbacks()
	{
		if (_roomConfigCallbacksRegistered && _roomConfigCallbackProxy != null)
		{
			PhotonNetwork.RemoveCallbackTarget((object)_roomConfigCallbackProxy);
			_roomConfigCallbacksRegistered = false;
		}
	}

	private void SubscribeOwnedConfigEntryChanges()
	{
		try
		{
			ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config);
			foreach (ConfigEntryBase val in configEntriesSnapshot)
			{
				if (val == null || _observedConfigEntries.ContainsKey(val))
				{
					continue;
				}
				EventInfo eventInfo = ((object)val).GetType().GetEvent("SettingChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (!(eventInfo == null))
				{
					MethodInfo method = ((object)this).GetType().GetMethod("OnOwnedConfigEntryChanged", BindingFlags.Instance | BindingFlags.NonPublic);
					Delegate obj = ((method != null) ? Delegate.CreateDelegate(eventInfo.EventHandlerType, this, method, throwOnBindFailure: false) : null);
					if ((object)obj != null)
					{
						eventInfo.AddEventHandler(val, obj);
						_observedConfigEntries[val] = obj;
					}
				}
			}
		}
		catch
		{
		}
	}

	private void UnsubscribeOwnedConfigEntryChanges()
	{
		foreach (KeyValuePair<ConfigEntryBase, Delegate> observedConfigEntry in _observedConfigEntries)
		{
			if (observedConfigEntry.Key != null && (object)observedConfigEntry.Value != null)
			{
				((object)observedConfigEntry.Key).GetType().GetEvent("SettingChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.RemoveEventHandler(observedConfigEntry.Key, observedConfigEntry.Value);
			}
		}
		_observedConfigEntries.Clear();
	}

	private static void ApplyBoolRoomConfig(IReadOnlyDictionary<string, string> values, string key, ConfigEntry<bool> entry)
	{
		if (entry != null && values.TryGetValue(key, out var value))
		{
			bool result;
			if (string.Equals(value, "1", StringComparison.Ordinal))
			{
				entry.Value = true;
			}
			else if (string.Equals(value, "0", StringComparison.Ordinal))
			{
				entry.Value = false;
			}
			else if (bool.TryParse(value, out result))
			{
				entry.Value = result;
			}
		}
	}

	private static void ApplyIntRoomConfig(IReadOnlyDictionary<string, string> values, string key, ConfigEntry<int> entry)
	{
		if (entry != null && values.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
		{
			entry.Value = result;
		}
	}

	private static void ApplyFloatRoomConfig(IReadOnlyDictionary<string, string> values, string key, ConfigEntry<float> entry)
	{
		if (entry != null && values.TryGetValue(key, out var value) && float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var result))
		{
			entry.Value = result;
		}
	}

	private static void ApplyStringRoomConfig(IReadOnlyDictionary<string, string> values, string key, ConfigEntry<string> entry, Func<string, string> normalizer = null)
	{
		if (entry != null && values.TryGetValue(key, out var value))
		{
			entry.Value = ((normalizer != null) ? normalizer(value) : value);
		}
	}


	private bool IsGameplayScene()
	{
		return IsGameplayScene(SceneManager.GetActiveScene());
	}

	private static string FormatHeldItemForDiagnostics(Item sourceItem)
	{
		if ((Object)sourceItem == (Object)null)
		{
			return "null";
		}
		string text = ((((Object)sourceItem.holderCharacter != (Object)null) ? ((Object)sourceItem.holderCharacter).name : null) ?? ((((Object)sourceItem.trueHolderCharacter != (Object)null) ? ((Object)sourceItem.trueHolderCharacter).name : null) ?? "null"));
		return $"name={((Object)sourceItem).name},id={sourceItem.itemID},state={(int)sourceItem.itemState},holder={text}";
	}
}
