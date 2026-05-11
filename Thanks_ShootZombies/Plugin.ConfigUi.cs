﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
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
	private void CheckConfigChanges()
	{
		bool flag = false;
		bool flag2 = false;
		if (ModEnabled != null && ModEnabled.Value != _lastModEnabled)
		{
			_lastModEnabled = ModEnabled.Value;
			if (_lastModEnabled)
			{
				if (IsZombieSpawnFeatureEnabled())
				{
					ZombieSpawner.StartZombieSpawning();
				}
			}
			else
			{
				ZombieSpawner.StopZombieSpawning();
				RestoreVanillaBlowgunFeatureState();
			}
			UpdateWeaponLobbyNotice();
		}
		if (WeaponEnabled != null && WeaponEnabled.Value != _lastWeaponEnabled)
		{
			_lastWeaponEnabled = WeaponEnabled.Value;
			if (!_lastWeaponEnabled)
			{
				RestoreVanillaBlowgunFeatureState();
			}
			UpdateWeaponLobbyNotice();
		}
		if (WeaponSelection != null)
		{
			string text = NormalizeWeaponSelection(WeaponSelection.Value);
			if (!string.Equals(WeaponSelection.Value, text, StringComparison.Ordinal))
			{
				WeaponSelection.Value = text;
				SavePluginConfigQuietly();
			}
			if (!string.Equals(text, _lastWeaponSelection, StringComparison.Ordinal))
			{
				_lastWeaponSelection = text;
				ApplySelectedWeaponAssets();
				PublishLocalWeaponSelectionToPlayerProperties(force: true);
				_pendingLocalWeaponVisualModelRefresh = true;
				RequestAkVisualRefresh(includeUiRefresh: true, forceRefresh: true);
				UpdateWeaponLobbyNotice();
			}
		}
		if (WeaponModelPitch != null && !Mathf.Approximately(WeaponModelPitch.Value, _lastWeaponModelPitch))
		{
			_lastWeaponModelPitch = WeaponModelPitch.Value;
			flag = true;
		}
		if (WeaponModelYaw != null && !Mathf.Approximately(WeaponModelYaw.Value, _lastWeaponModelYaw))
		{
			_lastWeaponModelYaw = WeaponModelYaw.Value;
			flag = true;
		}
		if (WeaponModelRoll != null && !Mathf.Approximately(WeaponModelRoll.Value, _lastWeaponModelRoll))
		{
			_lastWeaponModelRoll = WeaponModelRoll.Value;
			flag = true;
		}
		if (WeaponModelScale != null && !Mathf.Approximately(WeaponModelScale.Value, _lastWeaponModelScale))
		{
			_lastWeaponModelScale = WeaponModelScale.Value;
			flag = true;
		}
		if (WeaponModelOffsetX != null && !Mathf.Approximately(WeaponModelOffsetX.Value, _lastWeaponModelOffsetX))
		{
			_lastWeaponModelOffsetX = WeaponModelOffsetX.Value;
			flag = true;
		}
		if (WeaponModelOffsetY != null && !Mathf.Approximately(WeaponModelOffsetY.Value, _lastWeaponModelOffsetY))
		{
			_lastWeaponModelOffsetY = WeaponModelOffsetY.Value;
			flag = true;
		}
		if (WeaponModelOffsetZ != null && !Mathf.Approximately(WeaponModelOffsetZ.Value, _lastWeaponModelOffsetZ))
		{
			_lastWeaponModelOffsetZ = WeaponModelOffsetZ.Value;
			flag = true;
		}
		if (ZombieBehaviorDifficulty != null)
		{
			string text2 = NormalizeZombieBehaviorDifficultySelection(ZombieBehaviorDifficulty.Value);
			if (!string.Equals(ZombieBehaviorDifficulty.Value, text2, StringComparison.Ordinal))
			{
				ZombieBehaviorDifficulty.Value = text2;
				SavePluginConfigQuietly();
			}
			if (!string.Equals(text2, _lastZombieBehaviorDifficulty, StringComparison.Ordinal))
			{
				_lastZombieBehaviorDifficulty = text2;
				ApplyZombieBehaviorDifficultyPreset(text2);
				ApplySimplifiedZombieDerivedValues();
			}
		}
		ZombieBehaviorDifficultyPreset currentZombieBehaviorDifficultyPresetRuntime = GetCurrentZombieBehaviorDifficultyPresetRuntime();
		float zombieMoveSpeedMultiplierRuntime = GetZombieMoveSpeedMultiplierRuntime();
		if (!Mathf.Approximately(zombieMoveSpeedMultiplierRuntime, _lastZombieMoveSpeed))
		{
			_lastZombieMoveSpeed = zombieMoveSpeedMultiplierRuntime;
			_pendingZombieSpeedRefresh = true;
			UpdateZombieSpeed(forceRefresh: true);
		}
		if (!Mathf.Approximately(GetZombieKnockbackForceRuntime(), _lastZombieKnockbackForce))
		{
			_lastZombieKnockbackForce = GetZombieKnockbackForceRuntime();
		}
		if (!Mathf.Approximately(currentZombieBehaviorDifficultyPresetRuntime.DistanceBeforeWakeup, _lastDistanceBeforeWakeup))
		{
			_lastDistanceBeforeWakeup = currentZombieBehaviorDifficultyPresetRuntime.DistanceBeforeWakeup;
			flag2 = true;
		}
		if (!Mathf.Approximately(currentZombieBehaviorDifficultyPresetRuntime.SprintDistance, _lastZombieSprintDistance))
		{
			_lastZombieSprintDistance = currentZombieBehaviorDifficultyPresetRuntime.SprintDistance;
			flag2 = true;
		}
		if (!Mathf.Approximately(currentZombieBehaviorDifficultyPresetRuntime.ChaseTimeBeforeSprint, _lastChaseTimeBeforeSprint))
		{
			_lastChaseTimeBeforeSprint = currentZombieBehaviorDifficultyPresetRuntime.ChaseTimeBeforeSprint;
			flag2 = true;
		}
		if (!Mathf.Approximately(currentZombieBehaviorDifficultyPresetRuntime.LungeDistance, _lastZombieLungeDistance))
		{
			_lastZombieLungeDistance = currentZombieBehaviorDifficultyPresetRuntime.LungeDistance;
			flag2 = true;
		}
		if (!Mathf.Approximately(currentZombieBehaviorDifficultyPresetRuntime.BiteRecoveryTime, _lastZombieBiteRecoveryTime))
		{
			_lastZombieBiteRecoveryTime = currentZombieBehaviorDifficultyPresetRuntime.BiteRecoveryTime;
			flag2 = true;
		}
		if (!Mathf.Approximately(currentZombieBehaviorDifficultyPresetRuntime.LungeTime, _lastZombieLungeTime))
		{
			_lastZombieLungeTime = currentZombieBehaviorDifficultyPresetRuntime.LungeTime;
			flag2 = true;
		}
		if (!Mathf.Approximately(currentZombieBehaviorDifficultyPresetRuntime.LungeRecoveryTime, _lastZombieLungeRecoveryTime))
		{
			_lastZombieLungeRecoveryTime = currentZombieBehaviorDifficultyPresetRuntime.LungeRecoveryTime;
			flag2 = true;
		}
		if (!Mathf.Approximately(currentZombieBehaviorDifficultyPresetRuntime.LookAngleBeforeWakeup, _lastZombieLookAngleBeforeWakeup))
		{
			_lastZombieLookAngleBeforeWakeup = currentZombieBehaviorDifficultyPresetRuntime.LookAngleBeforeWakeup;
			flag2 = true;
		}
		bool zombieSpawnFeatureEnabled = IsZombieSpawnFeatureEnabled();
		if (zombieSpawnFeatureEnabled != _lastZombieSpawnEnabled)
		{
			_lastZombieSpawnEnabled = zombieSpawnFeatureEnabled;
			if (zombieSpawnFeatureEnabled)
			{
				ZombieSpawner.StartZombieSpawning();
			}
			else
			{
				ZombieSpawner.StopZombieSpawning();
			}
		}
		if (flag)
		{
			RefreshWeaponModelVisuals();
		}
		if (flag2)
		{
			ZombieSpawner.RefreshLiveZombieProperties();
		}
	}

	private void RemoveLegacyInventorySlotConfig()
	{
		try
		{
			RemoveConfigDefinition("Inventory", "Slot");
			RemoveConfigDefinition("物品栏", "槽位");
			ConfigFile config = ((BaseUnityPlugin)this).Config;
			if (config != null)
			{
				config.Save();
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] RemoveLegacyInventorySlotConfig failed: " + DescribeReflectionException(ex)));
		}
	}

	private void RemoveLegacyFogModeConfig()
	{
		try
		{
			string[] array = new string[2] { "Fog", "毒雾" };
			string[] array2 = new string[10]
			{
				"##FogMode##",
				"Fog Mode",
				"Spawn Compass",
				"Speed",
				"Start Delay",
				"Fog UI",
				"UI X Position",
				"UI Y Position",
				"UI Scale",
				"Pause Fog"
			};
			string[] array3 = new string[2] { "Features", "功能" };
			string[] array4 = new string[1] { "Night Cold" };
			foreach (string section in array)
			{
				foreach (string key in array2)
				{
					RemoveConfigDefinition(section, key);
				}
			}
			foreach (string section2 in array3)
			{
				foreach (string key2 in array4)
				{
					RemoveConfigDefinition(section2, key2);
				}
			}
			ConfigFile config = ((BaseUnityPlugin)this).Config;
			if (config != null)
			{
				config.Save();
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] RemoveLegacyFogModeConfig failed: " + DescribeReflectionException(ex)));
		}
	}

	private void RemoveLegacyPlayerShotConfig()
	{
		try
		{
			ConfigFile config = ((BaseUnityPlugin)this).Config;
			IDictionary dictionary = ((config != null) ? (((object)config).GetType().GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(config) as IDictionary) : null);
			foreach (ConfigDefinition item in BuildConfigDefinitionAliases("Weapon", "Player Shot Drowsy"))
			{
				config?.Remove(item);
				dictionary?.Remove(item);
			}
			if (config != null)
			{
				config.Save();
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] RemoveLegacyPlayerShotConfig failed: " + DescribeReflectionException(ex)));
		}
	}

	private void RemoveConfigDefinition(string section, string key)
	{
		if (((BaseUnityPlugin)this).Config != null && !string.IsNullOrWhiteSpace(section) && !string.IsNullOrWhiteSpace(key))
		{
			ConfigDefinition val = new ConfigDefinition(section, key);
			((BaseUnityPlugin)this).Config.Remove(val);
			(((object)((BaseUnityPlugin)this).Config).GetType().GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(((BaseUnityPlugin)this).Config) as IDictionary)?.Remove(val);
		}
	}

	private void ApplyLocalizedConfigMetadata(bool isChinese)
	{
		try
		{
			ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config);
			if (configEntriesSnapshot.Length == 0)
			{
				return;
			}
			ConfigEntryBase[] array = configEntriesSnapshot;
			foreach (ConfigEntryBase val in array)
			{
				if (val != null && !(val.Definition == (ConfigDefinition)null))
				{
					string localizedDescription = GetLocalizedDescription(val.Definition.Key, isChinese);
					if (val.Description != null && !string.IsNullOrEmpty(localizedDescription))
					{
						SetPrivateField(val.Description, "<Description>k__BackingField", localizedDescription);
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] ApplyLocalizedConfigMetadata failed: " + ex.Message));
		}
	}

	private void MigrateLegacyLocalizedConfigEntries()
	{
		try
		{
			ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config);
			IDictionary dictionary = ((object)((BaseUnityPlugin)this).Config)?.GetType().GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(((BaseUnityPlugin)this).Config) as IDictionary;
			if (configEntriesSnapshot.Length == 0 || dictionary == null || dictionary.Count == 0)
			{
				return;
			}
			bool flag = false;
			ConfigEntryBase[] array = configEntriesSnapshot;
			foreach (ConfigEntryBase val in array)
			{
				if (((val != null) ? val.Definition : null) == (ConfigDefinition)null)
				{
					continue;
				}
				string text = GetLegacyConfigSectionAlias(val);
				string localizedSectionName = GetLocalizedSectionName(val.Definition.Section, isChinese: true);
				string localizedKeyName = GetLocalizedKeyName(val.Definition.Key, isChinese: true);
				List<ConfigDefinition> list = new List<ConfigDefinition>(6)
				{
					new ConfigDefinition(localizedSectionName, val.Definition.Key),
					new ConfigDefinition(val.Definition.Section, localizedKeyName),
					new ConfigDefinition(localizedSectionName, localizedKeyName)
				};
				if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, val.Definition.Section, StringComparison.Ordinal))
				{
					string localizedSectionName2 = GetLocalizedSectionName(text, isChinese: true);
					list.Add(new ConfigDefinition(text, val.Definition.Key));
					list.Add(new ConfigDefinition(text, localizedKeyName));
					if (!string.IsNullOrWhiteSpace(localizedSectionName2))
					{
						list.Add(new ConfigDefinition(localizedSectionName2, val.Definition.Key));
						list.Add(new ConfigDefinition(localizedSectionName2, localizedKeyName));
					}
				}
				foreach (ConfigDefinition key in list)
				{
					if (dictionary.Contains(key))
					{
						object obj = dictionary[key];
						if (obj != null)
						{
							val.SetSerializedValue(obj.ToString());
							flag = true;
						}
						dictionary.Remove(key);
					}
				}
			}
			if (flag)
			{
				ConfigFile config = ((BaseUnityPlugin)this).Config;
				if (config != null)
				{
					config.Save();
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] MigrateLegacyLocalizedConfigEntries failed: " + DescribeReflectionException(ex)));
		}
	}

	private static string GetLegacyConfigSectionAlias(ConfigEntryBase entry)
	{
		if (entry == null)
		{
			return string.Empty;
		}
		return GetModConfigSectionForEntry(entry);
	}

	private static void SetPrivateField(object target, string fieldName, object value)
	{
		if (target != null && !string.IsNullOrEmpty(fieldName))
		{
			target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(target, value);
		}
	}

	private static ConfigEntryBase[] GetConfigEntriesSnapshot(ConfigFile configFile)
	{
		if (configFile == null)
		{
			return Array.Empty<ConfigEntryBase>();
		}
		if (!(((object)configFile).GetType().GetProperty("Entries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(configFile) is IDictionary { Count: not 0 } dictionary))
		{
			return Array.Empty<ConfigEntryBase>();
		}
		return (from entry in dictionary.Values.Cast<object>().OfType<ConfigEntryBase>()
			where entry != null
			select entry).ToArray();
	}

	internal static ConfigEntryBase[] GetConfigEntriesSnapshotRuntime(ConfigFile configFile)
	{
		return GetConfigEntriesSnapshot(configFile);
	}

	internal static string GetLocalizedConfigKeyDisplayRuntime(string key)
	{
		return GetLocalizedKeyName(key, Instance?.GetCachedChineseLanguageSetting() ?? false);
	}

	internal static string GetLocalizedConfigSectionDisplayRuntime(string section)
	{
		return GetLocalizedSectionName(section, Instance?.GetCachedChineseLanguageSetting() ?? false);
	}

	internal static string GetLocalizedConfigDescriptionRuntime(string key)
	{
		return GetLocalizedDescription(key, Instance?.GetCachedChineseLanguageSetting() ?? false);
	}

	internal static int GetOwnedConfigEntrySortIndexRuntime(ConfigEntryBase entry)
	{
		return GetModConfigEntrySortIndex(entry);
	}

	internal static int GetOwnedConfigSectionSortIndexRuntime(string section)
	{
		return GetModConfigSectionSortIndex(section);
	}

	internal static string GetOwnedConfigSectionRuntime(ConfigEntryBase entry)
	{
		return GetModConfigSectionForEntry(entry);
	}

	internal static bool ShouldExposeOwnedConfigEntryRuntime(ConfigEntryBase entry)
	{
		return ShouldExposeOwnedConfigEntry(entry);
	}

	internal static string[] GetOwnedSelectableConfigValuesRuntime(ConfigEntryBase entry)
	{
		if ((object)entry == ConfigPanelTheme)
		{
			return ConfigPanelThemeValues.ToArray();
		}
		if ((object)entry == WeaponSelection)
		{
			return WeaponSelectionValues.ToArray();
		}
		if ((object)entry == AkSoundSelection)
		{
			return AkSoundSelectionValues.ToArray();
		}
		if ((object)entry == ZombieBehaviorDifficulty)
		{
			return ZombieBehaviorDifficultyValues.ToArray();
		}
		return Array.Empty<string>();
	}

	internal static bool ShouldEmitVerboseInfoLogsRuntime()
	{
		return EnableVerboseInfoLogs;
	}

	private static string GetLocalizedSectionName(string section, bool isChinese)
	{
		return NormalizeLocalizedText(GetLocalizedSectionNameCore(NormalizeLocalizedText(section), isChinese));
	}

	private static string GetLocalizedSectionDescription(string section, bool isChinese)
	{
		return NormalizeLocalizedText(GetLocalizedSectionDescriptionCore(NormalizeLocalizedText(section), isChinese));
	}

	private static string GetLocalizedKeyName(string key, bool isChinese)
	{
		return NormalizeLocalizedText(GetLocalizedKeyNameCore(NormalizeLocalizedText(key), isChinese));
	}

	private static string GetLocalizedDescription(string key, bool isChinese)
	{
		return NormalizeLocalizedText(GetLocalizedDescriptionCore(NormalizeLocalizedText(key), isChinese));
	}

	private static string GetLocalizedModDisplayName(bool isChinese)
	{
		return NormalizeLocalizedText(GetLocalizedModDisplayNameCore(isChinese));
	}

	private static string GetLobbyWeaponNoticeTextCore(bool isChinese)
	{
		List<string> list = new List<string>(1);
		if (IsWeaponFeatureEnabled())
		{
			string spawnWeaponKeyLabel = GetSpawnWeaponKeyLabel();
			string text = "<color=" + LobbyNoticeKeyColor + ">" + spawnWeaponKeyLabel + "</color>";
			string currentWeaponDisplayName = GetCurrentWeaponDisplayName();
			list.Add(isChinese ? ("按 " + text + " 获取 " + currentWeaponDisplayName) : ("Press " + text + " to get " + currentWeaponDisplayName));
		}
		if (list.Count == 0)
		{
			return string.Empty;
		}
		return string.Join("\n", list);
	}

	private static string GetSpawnWeaponKeyLabel()
	{
		KeyCode keyCode = (SpawnWeaponKey != null && (int)SpawnWeaponKey.Value != 0) ? SpawnWeaponKey.Value : ((KeyCode)116);
		string text = keyCode.ToString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return "T";
		}
		return text.ToUpperInvariant();
	}

	private static string NormalizeLocalizedText(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		string text = value;
		if (LooksLikeMojibake(text))
		{
			text = TryRepairMojibakeUtf8(text, Encoding.GetEncoding(1252));
			if (LooksLikeMojibake(text))
			{
				text = TryRepairMojibakeUtf8(text, Encoding.GetEncoding(28591));
			}
		}
		if (ContainsUnexpectedControlCharacters(text))
		{
			text = new string((from c in text
				where c == '\r' || c == '\n' || c == '\t' || !char.IsControl(c)
				select c).ToArray());
		}
		return text;
	}

	private static string TryRepairMojibakeUtf8(string value, Encoding sourceEncoding)
	{
		if (string.IsNullOrEmpty(value) || sourceEncoding == null)
		{
			return value;
		}
		try
		{
			string @string = Encoding.UTF8.GetString(sourceEncoding.GetBytes(value));
			if (IsBetterLocalizedText(value, @string))
			{
				return @string;
			}
		}
		catch
		{
		}
		return value;
	}

	private static bool IsBetterLocalizedText(string original, string candidate)
	{
		if (string.IsNullOrWhiteSpace(candidate) || string.Equals(original, candidate, StringComparison.Ordinal))
		{
			return false;
		}
		return GetLocalizationNoiseScore(candidate) < GetLocalizationNoiseScore(original);
	}

	private static bool LooksLikeMojibake(string value)
	{
		return GetLocalizationNoiseScore(value) > 0;
	}

	private static int GetLocalizationNoiseScore(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return 0;
		}
		int num = 0;
		foreach (char c in value)
		{
			if (c >= '\u0080' && c <= '\u009f')
			{
				num += 6;
			}
			else if (c == '\ufffd')
			{
				num += 8;
			}
			else if ("ÃƒÃ‚Ã…Ã†Ã‡ÃˆÃ‰ÃŠÃ‹ÃŒÃÃŽÃÃÃ‘Ã’Ã“Ã”Ã•Ã–Ã˜Ã™ÃšÃ›ÃœÃÃžÃŸÃ Ã¡Ã¢Ã£Ã¤Ã¥Ã¦Ã§Ã¨Ã©ÃªÃ«Ã¬Ã­Ã®Ã¯Ã°Ã±Ã²Ã³Ã´ÃµÃ¶Ã¸Ã¹ÃºÃ»Ã¼Ã½Ã¾Ã¿â‚¬Å’Å“Å Å¡Å¸Å½Å¾".IndexOf(c) >= 0)
			{
				num += 2;
			}
			else if (c >= '\u4e00' && c <= '\u9fff')
			{
				num--;
			}
		}
		return num;
	}

	private static bool ContainsUnexpectedControlCharacters(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}
		foreach (char c in value)
		{
			if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
			{
				return true;
			}
		}
		return false;
	}

	private static string StripDisplayOrderPrefix(string value)
{
	string text = NormalizeLocalizedText(value).Trim();
	if (string.IsNullOrWhiteSpace(text) || !char.IsDigit(text[0]))
	{
		return text;
	}
	int num = 0;
	while (num < text.Length && char.IsDigit(text[num]))
	{
		num++;
	}
	while (num < text.Length && (char.IsWhiteSpace(text[num]) || text[num] == '.' || text[num] == ')' || text[num] == ':' || text[num] == '-'))
	{
		num++;
	}
	return (num >= text.Length) ? text : text.Substring(num).Trim();
}

private static string NormalizeSectionAlias(string section)
{
	string text = NormalizeLocalizedText(StripDisplayOrderPrefix(section)).Trim();
	string text2 = text.ToLowerInvariant();
	switch (text2)
	{
	case "weapon":
	case "weapons":
	case "weapon settings":
	case "武器":
		return "Weapon";
	case "zombie":
	case "zombie behavior":
	case "僵尸":
	case "僵尸行为":
		return "Zombie";
	case "zombie spawn":
	case "zombie spawning":
	case "zombie spawn settings":
	case "僵尸生成":
		return "Zombie Spawn";
	case "features":
	case "feature":
	case "hotkeys":
	case "功能":
	case "快捷键":
		return "Features";
	case "general":
	case "通用":
		return "General";
	default:
		return text;
	}
}

private static string NormalizeConfigKeyAlias(string key)
{
	switch (StripDisplayOrderPrefix(key))
	{
	case "Fire Interval":
	case "射击间隔":
	case "射击间隔（秒）":
		return "Fire Interval";
	case "Fire Volume":
	case "射击音量":
		return "Fire Volume";
	case "Weapon Model X Rotation":
	case "武器X轴旋转":
	case "手持模型X轴旋转":
		return "Weapon Model X Rotation";
	case "Weapon Model Y Rotation":
	case "武器Y轴旋转":
	case "手持模型Y轴旋转":
		return "Weapon Model Y Rotation";
	case "Weapon Model Z Rotation":
	case "武器Z轴旋转":
	case "手持模型Z轴旋转":
		return "Weapon Model Z Rotation";
	case "Weapon Model Scale":
	case "武器模型缩放":
	case "手持模型缩放":
		return "Weapon Model Scale";
	case "Weapon Model X Position":
	case "武器X轴位置":
	case "手持模型X位置":
		return "Weapon Model X Position";
	case "Weapon Model Y Position":
	case "武器Y轴位置":
	case "手持模型Y位置":
		return "Weapon Model Y Position";
	case "Weapon Model Z Position":
	case "武器Z轴位置":
	case "手持模型Z位置":
		return "Weapon Model Z Position";
	case "AK Sound":
	case "AK47音效":
	case "AK射击音效":
		return "AK Sound";
	case "Weapon Selection":
	case "武器选择":
	case "武器模型选择":
		return "Weapon Selection";
	case "default":
	case "Default":
	case "默认":
		return "default";
	case "Max Distance":
	case "最大射程":
		return "Max Distance";
	case "Bullet Size":
	case "子弹大小":
		return "Bullet Size";
	case "Zombie Time Reduction":
	case "Damage":
	case "伤害":
	case "命中僵尸减少的存活时间":
		return "Zombie Time Reduction";
	case "Mod Enabled":
	case "Mod":
	case "模组":
	case "模组总开关":
	case "启用模组":
		return "Mod";
	case "Weapon Enabled":
	case "Weapon":
	case "武器生成":
	case "武器生成启用":
	case "启用武器发放":
		return "Weapon";
	case "Spawn Weapon":
	case "生成武器":
	case "生成武器按键":
		return "Spawn Weapon";
	case "Open Config Panel":
	case "打开配置面板":
	case "打开配置面板按键":
	case "配置面板":
		return "Open Config Panel";
	case "Config Panel Theme":
	case "面板主题":
	case "配置面板主题":
		return "Config Panel Theme";
	case "Zombie Spawn Enabled":
	case "Zombie Spawn":
	case "Enabled":
	case "僵尸生成":
	case "僵尸生成启用":
	case "启用":
		return "Zombie Spawn";
	case "Move Speed":
	case "移动速度":
		return "Move Speed";
	case "Aggressiveness":
	case "进攻欲望":
		return "Aggressiveness";
	case "Knockback Force":
	case "击退力度":
		return "Knockback Force";
	case "Max Count":
	case "Zombie Count":
	case "Zombie Max Count":
	case "Max Zombie Count":
	case "最大数量":
	case "僵尸数量":
	case "僵尸最大数量":
		return "Max Count";
	case "Spawn Interval":
	case "生成间隔":
	case "两次僵尸生成波之间的时间间隔":
		return "Spawn Interval";
	case "Interval Random":
	case "间隔随机":
		return "Interval Random";
	case "Spawn Count":
	case "每次生成数量":
	case "每波生成数量":
		return "Spawn Count";
	case "Count Random":
	case "数量随机":
		return "Count Random";
	case "Spawn Radius":
	case "生成半径":
		return "Spawn Radius";
	case "Max Lifetime":
	case "Lifetime":
	case "Health":
	case "最大存活时间":
	case "生命值":
	case "存活时间":
	case "僵尸最大存活时间":
		return "Max Lifetime";
	case "Destroy Distance":
	case "Despawn Range":
	case "销毁距离":
	case "僵尸销毁范围":
		return "Destroy Distance";
	case "Behavior Difficulty":
	case "Zombie Difficulty":
	case "Difficulty":
	case "僵尸难度":
	case "难度":
	case "难度预设":
		return "Behavior Difficulty";
	case "Wakeup Distance":
	case "唤醒距离":
		return "Wakeup Distance";
	case "Chase Distance":
	case "追击距离":
		return "Chase Distance";
	case "Sprint Distance":
	case "冲刺距离":
		return "Sprint Distance";
	case "Chase Time":
	case "追击时间":
		return "Chase Time";
	case "Lunge Distance":
	case "猛扑距离":
		return "Lunge Distance";
	case "Lunge Time":
	case "猛扑持续时间":
		return "Lunge Time";
	case "Lunge Recovery Time":
	case "猛扑恢复时间":
		return "Lunge Recovery Time";
	case "Wakeup Look Angle":
	case "唤醒视角":
		return "Wakeup Look Angle";
	case "Target Search Interval":
	case "索敌刷新间隔":
		return "Target Search Interval";
	case "Bite Recovery Time":
	case "咬后恢复时间":
		return "Bite Recovery Time";
	case "Same Player Bite Cooldown":
	case "同玩家重复咬击冷却":
		return "Same Player Bite Cooldown";
	default:
		return StripDisplayOrderPrefix(key);
	}
}

private static string GetLocalizedSectionNameCore(string section, bool isChinese)
{
	switch (NormalizeSectionAlias(section))
	{
	case "General":
		return isChinese ? "通用" : "General";
	case "Weapon":
		return isChinese ? "武器" : "Weapon";
	case "Zombie":
		return isChinese ? "僵尸" : "Zombie";
	case "Zombie Spawn":
		return isChinese ? "僵尸生成（自动）" : "Zombie Spawn (Auto)";
	case "Features":
		return isChinese ? "功能" : "Features";
	default:
		return section;
	}
}

private static bool TryGetLocalizedSectionDisplayName(string section, bool isChinese, out string displayName)
{
	switch (NormalizeSectionAlias(section))
	{
	case "Weapon":
		displayName = isChinese ? "武器" : "Weapon";
		return true;
	case "Zombie":
		displayName = GetLocalizedSectionName("Zombie", isChinese);
		return true;
	case "Zombie Spawn":
		displayName = isChinese ? "僵尸生成（自动）" : "Zombie Spawn (Auto)";
		return true;
	case "Features":
		displayName = isChinese ? "功能" : "Features";
		return true;
	default:
		displayName = GetLocalizedSectionName(section, isChinese);
		return !string.IsNullOrWhiteSpace(displayName) && !string.Equals(displayName, section, StringComparison.Ordinal);
	}
}
	private void MigrateFeatureConfigDefinitions()
	{
		try
		{
			// Migrate legacy keys into the current canonical keys and remove retired entries.
			if (((object)((BaseUnityPlugin)this).Config)?.GetType().GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(((BaseUnityPlugin)this).Config) is not IDictionary { Count: not 0 } dictionary)
			{
				return;
			}
			bool flag = false;
			flag |= MigrateCurrentConfigEntryAliases(dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ModEnabled, "General", "Mod", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)OpenConfigPanelKey, "General", "Open Config Panel", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ConfigPanelTheme, "General", "Config Panel Theme", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponEnabled, "General", "Weapon", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponSelection, "General", "Weapon Selection", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)SpawnWeaponKey, "General", "Spawn Weapon", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)FireInterval, "General", "Fire Interval", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)FireVolume, "General", "Fire Volume", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)AkSoundSelection, "General", "AK Sound", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieTimeReduction, "General", "Zombie Time Reduction", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelScale, "General", "Weapon Model Scale", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelPitch, "General", "Weapon Model X Rotation", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelYaw, "General", "Weapon Model Y Rotation", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelRoll, "General", "Weapon Model Z Rotation", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelOffsetX, "General", "Weapon Model X Position", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelOffsetY, "General", "Weapon Model Y Position", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelOffsetZ, "General", "Weapon Model Z Position", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)MaxZombies, "General", "Max Count", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieSpawnCount, "General", "Spawn Count", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieSpawnInterval, "General", "Spawn Interval", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieMaxLifetime, "General", "Max Lifetime", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieBehaviorDifficulty, "General", "Behavior Difficulty", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieKnockbackForce, "General", "Knockback Force", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)SpawnWeaponKey, "Hotkeys", "Spawn Weapon", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)SpawnWeaponKey, "Features", "Spawn Weapon", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Weapon", "Max Distance", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Weapon", "Bullet Size", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie Spawn", "Zombie Spawn", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie Spawn", "Enabled", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Features", "Zombie Spawn Enabled", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie Spawn", "Zombie Spawn Enabled", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)MaxZombies, "Zombie Spawn", "Max Count", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieSpawnInterval, "Zombie Spawn", "Spawn Interval", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieSpawnCount, "Zombie Spawn", "Spawn Count", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieMaxLifetime, "Zombie Spawn", "Max Lifetime", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie Spawn", "Destroy Distance", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ModEnabled, "Hotkeys", "Mod Enabled", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ModEnabled, "Features", "Mod Enabled", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponEnabled, "Features", "Weapon Enabled", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponEnabled, "Features", "Weapon", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelPitch, "Weapon", "Weapon Model X Rotation", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelYaw, "Weapon", "Weapon Model Y Rotation", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelRoll, "Weapon", "Weapon Model Z Rotation", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelOffsetX, "Weapon", "Weapon Model X Position", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelOffsetY, "Weapon", "Weapon Model Y Position", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)WeaponModelOffsetZ, "Weapon", "Weapon Model Z Position", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie Spawn", "Interval Random", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie Spawn", "Spawn Count", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie Spawn", "Count Random", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie Spawn", "Spawn Radius", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Move Speed", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Aggressiveness", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieKnockbackForce, "Zombie", "Knockback Force", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Wakeup Distance", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Chase Distance", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Sprint Distance", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Chase Time", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Lunge Distance", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Target Search Interval", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Bite Recovery Time", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Same Player Bite Cooldown", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Lunge Time", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Lunge Recovery Time", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Wakeup Look Angle", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Destroy Distance", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Wakeup Distance", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Chase Distance", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Sprint Distance", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Chase Time", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Lunge Distance", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Target Search Interval", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Bite Recovery Time", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Same Player Bite Cooldown", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Lunge Time", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Lunge Recovery Time", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Wakeup Look Angle", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Move Speed", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Aggressiveness", dictionary);
			flag |= MigrateLegacyConfigEntryValue((ConfigEntryBase)(object)ZombieKnockbackForce, "Zombie AI", "Knockback Force", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Hotkeys", "Spawn Compass", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Features", "Night Cold Enabled", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Features", "Night Cold", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Hotkeys", "Pause Fog", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Features", "Pause Fog", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Fog", "Fog Mode", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Features", "Fog Mode", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Fog", "UI Enabled", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Features", "Fog UI Enabled", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Fog", "Fog UI Enabled", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Weapon", "Player Shot Drowsy", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Weapon", "Recoil", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Weapon", "Enable Recoil", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Weapon", "Recoil Pitch", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Weapon", "Recoil Yaw", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Weapon", "Recoil Max Angle", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Features", "Recoil", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Features", "Enable Recoil", dictionary);
			flag |= RemoveLegacyConfigEntryValue("Hotkeys", "Toggle Mod", dictionary);
			if (flag)
			{
				ConfigFile config = ((BaseUnityPlugin)this).Config;
				if (config != null)
				{
					config.Save();
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] MigrateFeatureConfigDefinitions failed: " + DescribeReflectionException(ex)));
		}
	}

	private bool MigrateCurrentConfigEntryAliases(IDictionary orphanedEntries)
	{
		if (orphanedEntries == null)
		{
			return false;
		}
		bool flag = false;
		ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config);
		foreach (ConfigEntryBase item in configEntriesSnapshot)
		{
			if (item == null || item.Definition == (ConfigDefinition)null)
			{
				continue;
			}
			string modConfigSectionForEntry = GetModConfigSectionForEntry(item);
			string text = NormalizeConfigKeyAlias(item.Definition.Key);
			if (!string.IsNullOrWhiteSpace(modConfigSectionForEntry) && !string.IsNullOrWhiteSpace(text))
			{
				flag |= MigrateLegacyConfigEntryValue(item, modConfigSectionForEntry, text, orphanedEntries);
			}
		}
		return flag;
	}

	private static bool MigrateLegacyConfigEntryValue(ConfigEntryBase target, string legacySection, string legacyKey, IDictionary orphanedEntries)
	{
		if (target == null || orphanedEntries == null || string.IsNullOrWhiteSpace(legacySection) || string.IsNullOrWhiteSpace(legacyKey))
		{
			return false;
		}
		foreach (ConfigDefinition item in BuildConfigDefinitionAliases(legacySection, legacyKey))
		{
			if (orphanedEntries.Contains(item))
			{
				object obj = orphanedEntries[item];
				if (obj != null)
				{
					target.SetSerializedValue(obj.ToString());
				}
				orphanedEntries.Remove(item);
				return true;
			}
		}
		return false;
	}

	private static bool RemoveLegacyConfigEntryValue(string legacySection, string legacyKey, IDictionary orphanedEntries)
	{
		if (orphanedEntries == null)
		{
			return false;
		}
		bool result = false;
		foreach (ConfigDefinition item in BuildConfigDefinitionAliases(legacySection, legacyKey))
		{
			if (orphanedEntries.Contains(item))
			{
				orphanedEntries.Remove(item);
				result = true;
			}
		}
		return result;
	}

	private static IEnumerable<ConfigDefinition> BuildConfigDefinitionAliases(string section, string key)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		string[] array = new string[2]
		{
			section,
			GetLocalizedSectionName(section, isChinese: true)
		};
		string[] array2 = new string[2]
		{
			key,
			GetLocalizedKeyName(key, isChinese: true)
		};
		string[] array3 = array;
		foreach (string text in array3)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			string[] array4 = array2;
			foreach (string text2 in array4)
			{
				if (!string.IsNullOrWhiteSpace(text2))
				{
					string item = text + "\n" + text2;
					if (hashSet.Add(item))
					{
						yield return new ConfigDefinition(text, text2);
					}
				}
			}
		}
	}

private static string GetLocalizedSectionDescriptionCore(string section, bool isChinese)
{
	switch (NormalizeSectionAlias(section))
	{
	case "General":
		return isChinese ? "通用设置。" : "General settings.";
	case "Weapon":
		return isChinese ? "AK 外观替换、射击、音效与手持模型设置。" : "AK presentation, firing, audio, and held-model settings.";
	case "Zombie":
		return isChinese ? "僵尸最大数量、每波生成数量、生命值、生成间隔、销毁范围，以及索敌、冲刺、猛扑和唤醒相关设置。" : "Zombie max count, per-wave spawn count, health, spawn timing, destroy range, and behavior settings including targeting, sprinting, lunging, and wake-up rules.";
	case "Zombie Spawn":
		return isChinese ? "僵尸生成系统自动派生出的内部参数缓存。正常不需要调整，也不代表第二套玩家配置。" : "Internal auto-derived spawn parameters used by the zombie system. They are not meant to be a second player-facing configuration set.";
	case "Features":
		return isChinese ? "模组总开关与共享环境规则。" : "Master toggles and shared world-rule settings.";
	default:
		return string.Empty;
	}
}
private static string GetLocalizedKeyNameCore(string key, bool isChinese)
{
	switch (NormalizeConfigKeyAlias(key))
	{
	case "Fire Interval":
		return isChinese ? "射击间隔（秒）" : "Fire Interval (s)";
	case "Fire Volume":
		return isChinese ? "射击音量" : "Shot Volume";
	case "Weapon Model X Rotation":
		return isChinese ? "手持模型X轴旋转" : "Held Model X Rotation";
	case "Weapon Model Y Rotation":
		return isChinese ? "手持模型Y轴旋转" : "Held Model Yaw";
	case "Weapon Model Z Rotation":
		return isChinese ? "手持模型Z轴旋转" : "Held Model Z Rotation";
	case "Weapon Model Scale":
		return isChinese ? "手持模型缩放" : "Held Model Scale";
	case "Weapon Model X Position":
		return isChinese ? "手持模型X位置" : "Held Model X";
	case "Weapon Model Y Position":
		return isChinese ? "手持模型Y位置" : "Held Model Y";
	case "Weapon Model Z Position":
		return isChinese ? "手持模型Z位置" : "Held Model Z";
	case "AK Sound":
		return isChinese ? "AK射击音效" : "AK Fire Sound";
	case "Weapon Selection":
		return isChinese ? "武器选择" : "Weapon Selection";
	case "Max Distance":
		return isChinese ? "命中检测距离" : "Hit Scan Distance";
	case "Bullet Size":
		return isChinese ? "命中检测半径" : "Hit Scan Radius";
	case "Zombie Time Reduction":
		return isChinese ? "伤害" : "Damage";
	case "Mod":
		return isChinese ? "启用模组" : "Enable Mod";
	case "Weapon":
		return isChinese ? "启用武器发放" : "Enable Weapon Grants";
	case "Spawn Weapon":
		return isChinese ? "生成武器按键" : "Spawn Weapon Key";
	case "Open Config Panel":
		return isChinese ? "打开配置面板按键" : "Open Config Panel Key";
	case "Config Panel Theme":
		return isChinese ? "面板主题" : "Panel Theme";
	case "Zombie Spawn":
		return isChinese ? "启用僵尸生成" : "Enable Zombie Spawns";
	case "Move Speed":
		return isChinese ? "移动速度倍率" : "Move Speed Multiplier";
	case "Aggressiveness":
		return isChinese ? "进攻欲望倍率" : "Aggro Multiplier";
	case "Knockback Force":
		return isChinese ? "命中击退力度" : "Hit Knockback Force";
	case "Max Count":
		return isChinese ? "僵尸最大数量" : "Zombie Max Count";
	case "Spawn Interval":
		return isChinese ? "生成间隔" : "Spawn Interval";
	case "Interval Random":
		return isChinese ? "生成间隔浮动（自动）" : "Spawn Interval Jitter (Auto)";
	case "Spawn Count":
		return isChinese ? "每波生成数量" : "Spawn Count Per Wave";
	case "Count Random":
		return isChinese ? "每波数量浮动（自动）" : "Per-Wave Count Jitter (Auto)";
	case "Spawn Radius":
		return isChinese ? "生成搜索半径（自动）" : "Spawn Search Radius (Auto)";
	case "Max Lifetime":
		return isChinese ? "生命值" : "Health";
	case "Destroy Distance":
		return isChinese ? "销毁范围" : "Despawn Range";
	case "Behavior Difficulty":
		return isChinese ? "难度" : "Difficulty";
	case "Wakeup Distance":
		return isChinese ? "唤醒距离" : "Wakeup Distance";
	case "Chase Distance":
		return isChinese ? "追击距离" : "Chase Distance";
	case "Sprint Distance":
		return isChinese ? "冲刺距离" : "Sprint Distance";
	case "Chase Time":
		return isChinese ? "冲刺延迟（秒）" : "Sprint Delay (s)";
	case "Lunge Distance":
		return isChinese ? "猛扑距离" : "Lunge Distance";
	case "Lunge Time":
		return isChinese ? "猛扑持续时间（秒）" : "Lunge Duration (s)";
	case "Lunge Recovery Time":
		return isChinese ? "猛扑恢复时间（秒）" : "Lunge Recovery (s)";
	case "Wakeup Look Angle":
		return isChinese ? "唤醒视角（度）" : "Wakeup View Angle (deg)";
	case "Target Search Interval":
		return isChinese ? "索敌刷新间隔（秒）" : "Target Refresh Interval (s)";
	case "Bite Recovery Time":
		return isChinese ? "咬后恢复时间（秒）" : "Post-Bite Recovery (s)";
	case "Same Player Bite Cooldown":
		return isChinese ? "同玩家重复咬击冷却（秒）" : "Repeat Bite Cooldown (s)";
	default:
		return key;
	}
}
private static string GetLocalizedDescriptionCore(string key, bool isChinese)
{
	switch (NormalizeConfigKeyAlias(key))
	{
	case "Fire Interval":
		return isChinese ? "每次开火之间的最短时间，数值越小，射速越快。" : "Minimum time between shots. Lower values produce a faster fire rate.";
	case "Fire Volume":
		return isChinese ? "AK 射击音效的播放音量，0 为静音。" : "Playback volume for the AK firing sound. Use 0 to mute it.";
	case "Weapon Model X Rotation":
		return isChinese ? "调整手持 AK 围绕握持点的 X 轴旋转，用于修正上下翻转和俯仰方向。" : "Adjusts the held AK rotation around the X axis to correct pitch and upside-down orientation.";
	case "Weapon Model Y Rotation":
		return isChinese ? "调整手持 AK 围绕握持点的 Y 轴朝向，用于修正枪口方向。" : "Adjusts the local AK yaw around the held anchor to correct weapon facing.";
	case "Weapon Model Z Rotation":
		return isChinese ? "调整手持 AK 围绕握持点的 Z 轴旋转，用于修正左右倾斜方向。" : "Adjusts the held AK rotation around the Z axis to correct roll and sideways tilt.";
	case "Weapon Model Scale":
		return isChinese ? "调整手持与本地显示用 AK 模型的整体大小。" : "Scales the locally displayed AK model used in hand and local presentation.";
	case "Weapon Model X Position":
		return isChinese ? "调整手持 AK 相对握持点的左右位置偏移。" : "Adjusts the left-right local position offset of the held AK.";
	case "Weapon Model Y Position":
		return isChinese ? "调整手持 AK 相对握持点的上下位置偏移。" : "Adjusts the up-down local position offset of the held AK.";
	case "Weapon Model Z Position":
		return isChinese ? "调整手持 AK 相对握持点的前后位置偏移。" : "Adjusts the forward-back local position offset of the held AK.";
	case "AK Sound":
		return isChinese ? "从 AK_Sounds 文件夹中的 ak_sound1、ak_sound2、ak_sound3 里切换射击音效。更改后会立即影响后续射击声音。" : "Selects the AK firing sound from ak_sound1, ak_sound2, and ak_sound3 inside the AK_Sounds folder. Changes apply to the next shot immediately.";
	case "Weapon Selection":
		return isChinese ? "选择替换吹箭筒时使用的武器模型与图标。该选项只在本地生效，不会同步给其他玩家。" : "Selects which weapon model and icon replace the blowgun. This option is local only and is not synchronized to other players.";
	case "default":
		return string.Empty;
	case "Max Distance":
		return isChinese ? "射击命中检测的最大距离。" : "Maximum distance used by the weapon hit scan.";
	case "Bullet Size":
		return isChinese ? "射击检测半径。略微增大可以让近距离手感更稳定。" : "Hit-scan radius. Slightly larger values make close-range shots feel more forgiving.";
	case "Zombie Time Reduction":
		return isChinese ? "每次命中会削减僵尸的剩余生命值。本模组内部用存活时间来近似生命值，所以数值越大，僵尸死得越快。" : "Each hit reduces a zombie's remaining health. Internally this mod models health through remaining lifetime, so higher values kill zombies faster.";
	case "Mod":
		return isChinese ? "当前模组总开关。关闭后武器和僵尸相关逻辑都会停止接管。" : "Master switch for the whole mod. Turning it off disables the weapon and zombie systems.";
	case "Weapon":
		return isChinese ? "控制大厅/游戏内是否发放吹箭筒与急救包，以及是否启用 AK 替换逻辑。" : "Controls automatic blowgun and first-aid grants and whether the AK replacement logic is active.";
	case "Spawn Weapon":
		return isChinese ? "本地备用发枪按键，用于测试或在自动发枪失效时补发。" : "Local backup hotkey for spawning the weapon if automatic grants fail or for testing.";
	case "Open Config Panel":
		return isChinese ? "在大厅中打开或关闭自定义配置面板的按键。" : "Hotkey used in the lobby to open or close the custom configuration panel.";
	case "Config Panel Theme":
		return isChinese ? "选择配置面板的外观主题。支持黑色、白色和透明三种版本，并会保存你的本地选择。" : "Selects the configuration panel appearance. Supports dark, light, and transparent themes and saves your local choice.";
	case "Zombie Spawn":
		return isChinese ? "控制僵尸生成系统是否运行。关闭后不会继续刷出新的僵尸。" : "Turns the zombie spawn system on or off. Disabling it stops new zombie waves from spawning.";
	case "Move Speed":
		return isChinese ? "僵尸基础移动速度倍率。1 为原版速度。" : "Multiplier for zombie base movement speed. A value of 1 matches vanilla speed.";
	case "Aggressiveness":
		return isChinese ? "提高僵尸更积极追击与维持仇恨的倾向。" : "Scales how aggressively zombies commit to chasing and keeping pressure on players.";
	case "Knockback Force":
		return isChinese ? "子弹命中僵尸时施加的击退力度。" : "Knockback force applied when a zombie is hit by a shot.";
	case "Max Count":
		return isChinese ? "同一时间允许存在的僵尸上限。无论每波生成多少，场上总数都不会超过这个值。" : "Maximum number of zombies allowed to stay alive at the same time. No matter how many a wave tries to spawn, the live total never exceeds this cap.";
	case "Spawn Interval":
		return isChinese ? "两次僵尸生成波之间的基础时间间隔。系统会围绕这个值自动上下浮动几秒。" : "Base delay between zombie spawn waves. The system automatically adds a few seconds of jitter around this value.";
	case "Interval Random":
		return isChinese ? "由生成间隔自动推导出的内部浮动值，用来让刷怪节奏不那么死板；它不是单独的玩家配置项。" : "An internal jitter value derived from the spawn interval so waves do not feel too rigid. It is not a separate player-facing setting.";
	case "Spawn Count":
		return isChinese ? "每一波会尝试生成该数量的僵尸。若当前场上僵尸加上下一波会超过僵尸最大数量，则会自动把本波数量压到剩余容量。" : "Each wave attempts to spawn this many zombies. If the current live total plus the next wave would exceed the zombie max count, the wave is automatically capped to the remaining capacity.";
	case "Count Random":
		return isChinese ? "由每波生成数量自动推导出的内部浮动值，用来避免每一波都完全固定；它不是第二套数量配置。" : "An internal jitter value derived from the per-wave spawn count so waves are not perfectly fixed. It is not a second count setting.";
	case "Spawn Radius":
		return isChinese ? "由当前僵尸设置自动推导出的内部搜索半径，用来寻找合适刷怪点；它不是单独的玩家配置项。" : "An internal search radius derived from the current zombie settings and used to find suitable spawn points. It is not a separate player-facing setting.";
	case "Max Lifetime":
		return isChinese ? "单个僵尸的基础生命值。本模组内部用最长存活时间来表示，所以数值越大，僵尸越耐打。" : "Base health for an individual zombie. Internally this mod represents it through maximum lifetime, so higher values make zombies harder to kill.";
	case "Destroy Distance":
		return isChinese ? "当僵尸与所有玩家的距离都超过该值时，会被直接销毁。" : "A zombie is destroyed when it is farther than this distance from every player.";
	case "Behavior Difficulty":
		return isChinese ? "五档行为预设。第一档简单保留原版扑击、咬后恢复和咬人节奏，但对刷出的僵尸保持及时唤醒与追击，避免长时间站立。后四档会逐步提高索敌频率、咬后恢复、追击冲刺、猛扑距离/持续/恢复，以及唤醒角度和唤醒距离；这些增强档仍会把猛扑收紧在近身范围，避免远距离滑扑。默认是第一档简单。" : "Five behavior presets. The first Easy preset keeps vanilla lunge, post-bite recovery, and bite cadence, while spawned zombies still wake and pursue promptly so they do not stand idle for long periods. The remaining four presets progressively tighten target-search cadence, post-bite recovery, sprint and chase timing, lunge distance/duration/recovery, plus wake-up angle and distance; those enhanced presets still clamp lunges back down to close range so zombies do not keep sliding into long-range pounces. Easy is the default.";
	case "Wakeup Distance":
		return isChinese ? "僵尸开始被激活的距离。" : "Distance at which zombies wake up and become active.";
	case "Chase Distance":
		return isChinese ? "僵尸开始稳定追击玩家的距离。设为 0 表示一旦锁定目标就立刻追击。" : "Distance at which zombies commit to chasing players. Set this to 0 for immediate pursuit once a target is found.";
	case "Sprint Distance":
		return isChinese ? "僵尸进入冲刺追击所需的距离阈值。" : "Distance threshold that allows zombies to enter sprint pursuit.";
	case "Chase Time":
		return isChinese ? "僵尸追击玩家多久后允许进入冲刺。设为 0 可在满足距离条件时立即冲刺。" : "How long a zombie must chase before sprinting is allowed. Set this to 0 to allow immediate sprinting once distance conditions are met.";
	case "Lunge Distance":
		return isChinese ? "僵尸触发近身猛扑攻击的距离。默认值已调回接近原版，常用范围为 4 到 20。" : "Distance at which zombies can trigger their close-range lunge attack. The default has been restored near vanilla, with a practical range of 4 to 20.";
	case "Lunge Time":
		return isChinese ? "僵尸维持猛扑动作的持续时间。数值越大，突进压迫感越强。" : "How long zombies stay in the lunging state. Higher values extend the forward pressure window.";
	case "Lunge Recovery Time":
		return isChinese ? "普通猛扑结束后，僵尸恢复到继续追击所需的时间。" : "How long regular lunge recovery lasts before the zombie can resume chasing.";
	case "Wakeup Look Angle":
		return isChinese ? "玩家面向僵尸时，允许触发唤醒判定的最大视角。" : "Maximum facing angle that still allows players looking toward the zombie to trigger wake-up.";
	case "Target Search Interval":
		return isChinese ? "僵尸重新搜索最近目标的时间间隔。数值越小，越容易快速重新锁定或换目标。" : "How often zombies refresh their nearest-target search. Lower values make them reacquire and swap targets more quickly.";
	case "Bite Recovery Time":
		return isChinese ? "僵尸成功咬到玩家后，自身恢复到继续追击所需的时间。数值越小，连续压迫越强。" : "How long a zombie takes to recover after successfully biting a player. Lower values keep pressure on players much more consistently.";
	case "Same Player Bite Cooldown":
		return isChinese ? "同一只僵尸再次咬到同一名玩家前必须等待的时间。数值越小，连咬威胁越高。" : "How long the same zombie must wait before it can bite the same player again. Lower values make repeated bites much more dangerous.";
	default:
		return string.Empty;
	}
}
private void RefreshRealModConfigUi()
	{
		try
		{
			// GameHandler æœªåˆå§‹åŒ–æ—¶ï¼ŒModConfig çš„ç¼“å­˜åˆ·æ–°é“¾è·¯è¿˜ä¸å®Œæ•´ï¼Œè¿‡æ—©è°ƒç”¨ä¼šæŠ¥ç©ºå¼•ç”¨ã€‚
			// Skip cache refresh until GameHandler is ready, otherwise ModConfig can throw during startup.
			if ((Object)(object)GameHandler.Instance == (Object)null)
			{
				return;
			}
			if (Chainloader.PluginInfos.TryGetValue("com.github.PEAKModding.PEAKLib.ModConfig", out var value) && (Object)(object)((value != null) ? value.Instance : null) != (Object)null)
			{
				Type type = ((object)value.Instance).GetType();
				(type.GetProperty("EntriesProcessed", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as IList)?.Clear();
				(type.GetProperty("ModdedKeys", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as IList)?.Clear();
				(type.GetProperty("GetValidKeyPaths", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as IList)?.Clear();
				type.GetMethod("GenerateValidKeyPaths", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
				type.GetMethod("ProcessModEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
				type.GetMethod("LoadModSettings", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] RefreshRealModConfigUi cache refresh failed: " + DescribeReflectionException(ex)));
		}
	}

	private void PatchModConfigUiMethods(Harmony harmony)
	{
		if (harmony == null)
		{
			return;
		}
		try
		{
			Type type = ResolveLoadedType("PEAKLib.ModConfig.Components.ModdedSettingsMenu", "com.github.PEAKModding.PEAKLib.ModConfig");
			if (type == null)
			{
				return;
			}
			HarmonyMethod val = new HarmonyMethod(AccessTools.Method(typeof(Plugin), "ModConfigMenuUiChangedPostfix", (Type[])null, (Type[])null));
			string[] array = new string[1] { "OnEnable" };
			foreach (string name in array)
			{
				MethodInfo[] array2 = (from m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
					where string.Equals(m.Name, name, StringComparison.Ordinal)
					select m).ToArray();
				foreach (MethodInfo methodInfo in array2)
				{
					harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, val, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				}
			}
			MethodInfo methodInfo2 = AccessTools.Method(type, "ShowSettings", new Type[1] { typeof(string) }, (Type[])null);
			HarmonyMethod val2 = new HarmonyMethod(AccessTools.Method(typeof(Plugin), "ModConfigShowSettingsPrefix", (Type[])null, (Type[])null));
			HarmonyMethod val3 = new HarmonyMethod(AccessTools.Method(typeof(Plugin), "ModConfigShowSettingsPostfix", (Type[])null, (Type[])null));
			if (methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo2, val2, val3, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
			MethodInfo methodInfo3 = AccessTools.Method(type, "UpdateSectionTabs", new Type[1] { typeof(string) }, (Type[])null);
			HarmonyMethod val4 = new HarmonyMethod(AccessTools.Method(typeof(Plugin), "ModConfigUpdateSectionTabsPrefix", (Type[])null, (Type[])null));
			if (methodInfo3 != null)
			{
				harmony.Patch((MethodBase)methodInfo3, val4, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
			MethodInfo methodInfo4 = AccessTools.Method(type, "SetSection", new Type[1] { typeof(string) }, (Type[])null);
			HarmonyMethod val5 = new HarmonyMethod(AccessTools.Method(typeof(Plugin), "ModConfigSetSectionPrefix", (Type[])null, (Type[])null));
			if (methodInfo4 != null)
			{
				harmony.Patch((MethodBase)methodInfo4, val5, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
			Type type2 = type.Assembly.GetType("PEAKLib.ModConfig.Components.ModdedTABSButton");
			MethodInfo methodInfo5 = AccessTools.Method(type2, "Update", Type.EmptyTypes, (Type[])null);
			HarmonyMethod val6 = new HarmonyMethod(AccessTools.Method(typeof(Plugin), "ModConfigTabsButtonUpdatePostfix", (Type[])null, (Type[])null));
			if (methodInfo5 != null)
			{
				harmony.Patch((MethodBase)methodInfo5, (HarmonyMethod)null, val6, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
			PatchModConfigSettingOptionMethods(harmony, type.Assembly);
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] PatchModConfigUiMethods failed: " + DescribeReflectionException(ex)));
		}
	}

	private void PatchModConfigSettingOptionMethods(Harmony harmony, Assembly assembly)
	{
		if (harmony == null || assembly == null)
		{
			return;
		}
		HarmonyMethod val = new HarmonyMethod(AccessTools.Method(typeof(Plugin), "ModConfigGetDisplayNamePostfix", (Type[])null, (Type[])null));
		foreach (Type item in from t in GetLoadableTypes(assembly)
			where t != null && !t.IsAbstract && !t.IsInterface && !string.IsNullOrWhiteSpace(t.FullName) && t.FullName.StartsWith("PEAKLib.ModConfig.SettingOptions.BepInEx", StringComparison.Ordinal)
			select t)
		{
			MethodInfo methodInfo = AccessTools.Method(item, "GetDisplayName", Type.EmptyTypes, (Type[])null);
			if (methodInfo != null)
			{
				harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, val, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
	}

	private static void ModConfigMenuUiChangedPostfix(object __instance)
	{
		if ((Object)(object)Instance != (Object)null && IsModConfigUiRuntimeSafe())
		{
			Instance.LocalizeModConfigUiNextFrame(__instance);
		}
	}

	private static void ModConfigShowSettingsPostfix(object __instance, string __0)
	{
		if (!((Object)(object)Instance == (Object)null) && IsModConfigUiRuntimeSafe())
		{
			string text = NormalizeModConfigContextName(__0);
			if (!string.IsNullOrWhiteSpace(__0))
			{
				Instance._activeModConfigName = text;
			}
			else if (__instance != null)
			{
				string selectedModConfigCategory = GetSelectedModConfigCategory(__instance, __instance.GetType());
				if (!string.IsNullOrWhiteSpace(selectedModConfigCategory))
				{
					Instance._activeModConfigName = selectedModConfigCategory;
				}
			}
			string text2 = string.IsNullOrWhiteSpace(text) ? NormalizeModConfigContextName(Instance._activeModConfigName) : text;
			if (!Instance.IsOwnedModConfigName(text2))
			{
				Instance.StopOwnedModConfigStabilizer(restoreRuntimeState: true);
				return;
			}
			Instance.LocalizeModConfigUiNextFrame(__instance);
		}
	}

	private static void ModConfigShowSettingsPrefix(object __instance, string __0)
	{
		if ((Object)(object)Instance == (Object)null || __instance == null)
		{
			return;
		}
		try
		{
			string text = NormalizeModConfigContextName(__0);
			if (!string.IsNullOrWhiteSpace(text))
			{
				Instance._activeModConfigName = text;
				if (!Instance.IsOwnedModConfigName(text))
				{
					Instance.StopOwnedModConfigStabilizer(restoreRuntimeState: true);
					return;
				}
			}
			Type type = __instance.GetType();
			CleanupDestroyedModConfigCells(__instance, type);
			if (Instance.IsOwnedModConfigName(Instance._activeModConfigName))
			{
				Instance.NormalizeOwnedModConfigSections(type.Assembly, __instance, type);
				Instance.EnsureOwnedModConfigEntriesRegistered(type);
			}
			else
			{
				Instance.StopOwnedModConfigStabilizer(restoreRuntimeState: true);
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] ModConfigShowSettingsPrefix cleanup failed: " + DescribeReflectionException(ex)));
		}
	}

	private static bool ModConfigUpdateSectionTabsPrefix(object __instance, string __0)
	{
		if ((Object)(object)Instance == (Object)null)
		{
			return true;
		}
		try
		{
			string text = NormalizeLocalizedText(__0).Trim();
			if (!string.IsNullOrWhiteSpace(text))
			{
				Instance._activeModConfigName = text;
			}
			else if (__instance != null)
			{
				string selectedModConfigCategory = GetSelectedModConfigCategory(__instance, __instance.GetType());
				if (!string.IsNullOrWhiteSpace(selectedModConfigCategory))
				{
					Instance._activeModConfigName = selectedModConfigCategory;
				}
			}
			if (!Instance.IsOwnedModConfigName(Instance._activeModConfigName))
			{
				Instance.StopOwnedModConfigStabilizer(restoreRuntimeState: true);
				return true;
			}
			if (__instance != null && Instance.IsOwnedModConfigName(Instance._activeModConfigName))
			{
				Instance.NormalizeOwnedModConfigSections(__instance.GetType().Assembly, __instance, __instance.GetType());
			}
		}
		catch
		{
		}
		return true;
	}

	private static bool ModConfigSetSectionPrefix(object __instance, ref string __0)
	{
		if ((Object)(object)Instance == (Object)null || __instance == null)
		{
			return true;
		}
		try
		{
			Type type = __instance.GetType();
			string text = GetSelectedModConfigCategory(__instance, type);
			if (!string.IsNullOrWhiteSpace(text))
			{
				Instance._activeModConfigName = text;
			}
			if (!Instance.IsOwnedModConfigName(Instance._activeModConfigName))
			{
				Instance.StopOwnedModConfigStabilizer(restoreRuntimeState: true);
				return true;
			}
			if (Instance.IsOwnedModConfigName(Instance._activeModConfigName) && TryGetOwnedCanonicalSectionName(__0, out var canonicalSectionName) && !string.IsNullOrWhiteSpace(canonicalSectionName))
			{
				__0 = canonicalSectionName;
				FieldInfo field = type.GetField("selectedSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null)
				{
					field.SetValue(__instance, canonicalSectionName);
				}
				else
				{
					PropertyInfo propertyInfo = type.GetProperty("selectedSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetProperty("SelectedSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (propertyInfo != null && propertyInfo.CanWrite)
					{
						propertyInfo.SetValue(__instance, canonicalSectionName, null);
					}
				}
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] ModConfigSetSectionPrefix failed: " + DescribeReflectionException(ex)));
			return true;
		}
	}

	private static void CleanupDestroyedModConfigCells(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null)
		{
			return;
		}
		try
		{
			IList list = GetFieldInHierarchy(menuType, "m_spawnedCells")?.GetValue(menuInstance) as IList;
			if (list == null)
			{
				return;
			}
			for (int num = list.Count - 1; num >= 0; num--)
			{
				object obj = list[num];
				if (obj == null)
				{
					list.RemoveAt(num);
					continue;
				}
				if (obj is Object @object && (Object)@object == (Object)null)
				{
					list.RemoveAt(num);
				}
			}
		}
		catch
		{
		}
	}

	private static void ModConfigGetDisplayNamePostfix(object __instance, ref string __result)
	{
		if (IsOwnedConfigEntry(TryGetConfigEntryBaseFromSettingOption(__instance)))
		{
			bool flag = (Object)(object)Instance != (Object)null && Instance.IsChineseLanguage();
			string canonicalConfigKey = GetCanonicalConfigKey(__instance);
			if (!string.IsNullOrWhiteSpace(canonicalConfigKey))
			{
				__result = GetLocalizedKeyName(canonicalConfigKey, flag);
			}
			else if (TryGetLocalizedOwnedConfigDisplayName(__result, flag, out var displayName))
			{
				__result = displayName;
			}
			else
			{
				__result = LocalizeModConfigText(__result);
			}
		}
	}

	private static void ModConfigTabsButtonUpdatePostfix(object __instance)
	{
		if ((Object)(object)Instance == (Object)null || __instance == null)
		{
			return;
		}
		try
		{
			Type type = __instance.GetType();
			FieldInfo field = type.GetField("category", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			FieldInfo field2 = type.GetField("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			string text = field?.GetValue(__instance) as string;
			object obj = field2?.GetValue(__instance);
			TMP_Text val = (TMP_Text)((obj is TMP_Text) ? obj : null);
			Component component = (Component)((__instance is Component value) ? value : null);
			if ((Object)val == (Object)null)
			{
				return;
			}
			string text2 = (!string.IsNullOrWhiteSpace(text) ? text : val.text);
			bool flag = false;
			if (TryGetModConfigMenuInstance(out var menuType, out var menuInstance))
			{
				flag = IsOwnedModConfigContext(menuInstance, menuType);
			}
			if (!ShouldLocalizeOwnedModConfigButton(text2, flag))
			{
				return;
			}
			if (flag && TryGetOwnedCanonicalSectionName(text2, out var canonicalSectionName) && !string.IsNullOrWhiteSpace(canonicalSectionName))
			{
				if (field != null && !string.Equals(text, canonicalSectionName, StringComparison.Ordinal))
				{
					field.SetValue(__instance, canonicalSectionName);
					text = canonicalSectionName;
				}
				if ((Object)component != (Object)null && !string.Equals(((Object)component.gameObject).name, canonicalSectionName, StringComparison.Ordinal))
				{
					((Object)component.gameObject).name = canonicalSectionName;
				}
				text2 = canonicalSectionName;
			}
			string displayName;
			string text3 = (TryGetLocalizedSectionDisplayName(text2, Instance.IsChineseLanguage(), out displayName) ? displayName : LocalizeModConfigText(text2));
			if (!string.IsNullOrWhiteSpace(text3) && !string.Equals(val.text, text3, StringComparison.Ordinal))
			{
				val.text = text3;
			}
		}
		catch
		{
		}
	}

	private void LocalizeModConfigUiNextFrame(object menuInstance)
	{
		if (!IsModConfigUiRuntimeSafe())
		{
			return;
		}
		string text = NormalizeModConfigContextName(_activeModConfigName);
		if (!IsOwnedModConfigName(text))
		{
			StopOwnedModConfigStabilizer();
			return;
		}
		if (_lastLocalizedModConfigUiFrame != Time.frameCount)
		{
			_lastLocalizedModConfigUiFrame = Time.frameCount;
			((MonoBehaviour)this).StartCoroutine(LocalizeModConfigUiCoroutine(menuInstance, text));
		}
	}

	private IEnumerator LocalizeModConfigUiCoroutine(object menuInstance, string ownerModName)
	{
		for (int i = 0; i < 4; i++)
		{
			yield return null;
			if (!IsOwnedModConfigContextStillValid(ownerModName))
			{
				yield break;
			}
			TryLocalizeVisibleModConfigUi(menuInstance, ownerModName);
		}
	}

	private void TryLocalizeVisibleModConfigUi(object menuInstance = null, string ownerModName = null)
	{
		if (!IsModConfigUiRuntimeSafe())
		{
			return;
		}
		if (!TryGetModConfigMenuInstance(out var menuType, out var menuInstance2))
		{
			if (menuInstance == null)
			{
				return;
			}
			menuInstance2 = menuInstance;
			menuType = menuInstance.GetType();
		}
		Behaviour val = (Behaviour)((menuInstance2 is Behaviour) ? menuInstance2 : null);
		if (val == null || (Object)val == (Object)null)
		{
			return;
		}
		try
		{
			if (!val.isActiveAndEnabled || !((Component)val).gameObject.activeInHierarchy)
			{
				return;
			}
		}
		catch
		{
			return;
		}
		bool isChinese = IsChineseLanguage();
		SyncActiveModConfigName(menuInstance2, menuType);
		if (!string.IsNullOrWhiteSpace(ownerModName) && !IsOwnedModConfigContextStillValid(ownerModName))
		{
			StopOwnedModConfigStabilizer();
			return;
		}
		if (!IsOwnedModConfigName(_activeModConfigName))
		{
			StopOwnedModConfigStabilizer();
			return;
		}
		EnsureOwnedModConfigEntriesRegistered(menuType);
		EnsureOwnedModConfigStabilizer(menuInstance2);
		if (NeedsOwnedModConfigSectionRebuild(menuInstance2, menuType))
		{
			RepairOwnedModConfigSections(menuInstance2, menuType);
		}
		RepairOwnedModConfigState(menuInstance2, menuType);
		NormalizeOwnedSectionTabCategories(menuInstance2, menuType, isChinese);
		LocalizeOwnedModConfigSectionTabs(menuInstance2, menuType, isChinese);
		Dictionary<string, string> map = BuildModConfigUiLocalizationMap(isChinese);
		foreach (Transform item in EnumerateModConfigUiRoots(menuInstance2, menuType))
		{
			ApplyTextLocalizationToRoot(item, map);
		}
	}

	private void EnsureOwnedModConfigStabilizer(object menuInstance)
	{
		string text = NormalizeModConfigContextName(_activeModConfigName);
		if (!IsOwnedModConfigName(text))
		{
			StopOwnedModConfigStabilizer();
			return;
		}
		if (_modConfigStabilizeCoroutine != null)
		{
			if (string.Equals(_modConfigStabilizeOwnerName, text, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			StopOwnedModConfigStabilizer();
		}
		_modConfigStabilizeOwnerName = text;
		_modConfigStabilizeCoroutine = ((MonoBehaviour)this).StartCoroutine(StabilizeOwnedModConfigUiCoroutine(menuInstance, text));
	}

	private static string NormalizeModConfigContextName(string modName)
	{
		return NormalizeLocalizedText(modName).Trim();
	}

	private bool IsOwnedModConfigContextStillValid(string ownerModName)
	{
		ownerModName = NormalizeModConfigContextName(ownerModName);
		if (string.IsNullOrWhiteSpace(ownerModName) || !IsOwnedModConfigName(ownerModName))
		{
			return false;
		}
		string text = NormalizeModConfigContextName(_activeModConfigName);
		return !string.IsNullOrWhiteSpace(text) && string.Equals(text, ownerModName, StringComparison.OrdinalIgnoreCase);
	}

	private void StopOwnedModConfigStabilizer(bool restoreRuntimeState = false)
	{
		bool flag = _modConfigStabilizeCoroutine != null || !string.IsNullOrWhiteSpace(_modConfigStabilizeOwnerName);
		if (_modConfigStabilizeCoroutine != null)
		{
			((MonoBehaviour)this).StopCoroutine(_modConfigStabilizeCoroutine);
			_modConfigStabilizeCoroutine = null;
		}
		_modConfigStabilizeOwnerName = string.Empty;
		if (restoreRuntimeState && flag)
		{
			RefreshRealModConfigUi();
		}
	}

	private IEnumerator StabilizeOwnedModConfigUiCoroutine(object menuInstance, string ownerModName)
	{
		try
		{
			for (int i = 0; i < 20; i++)
			{
				yield return null;
				if (!IsOwnedModConfigContextStillValid(ownerModName))
				{
					yield break;
				}
				if (!IsModConfigUiRuntimeSafe())
				{
					continue;
				}
				if (!TryGetModConfigMenuInstance(out var menuType, out var menuInstance2))
				{
					if (menuInstance == null)
					{
						continue;
					}
					menuInstance2 = menuInstance;
					menuType = menuInstance.GetType();
				}
				Behaviour val = (Behaviour)((menuInstance2 is Behaviour) ? menuInstance2 : null);
				if (val == null || (Object)val == (Object)null)
				{
					continue;
				}
				bool flag;
				try
				{
					flag = val.isActiveAndEnabled && ((Component)val).gameObject.activeInHierarchy;
				}
				catch
				{
					flag = false;
				}
				if (!flag)
				{
					continue;
				}
				SyncActiveModConfigName(menuInstance2, menuType);
				if (!IsOwnedModConfigContextStillValid(ownerModName))
				{
					yield break;
				}
				EnsureOwnedModConfigEntriesRegistered(menuType);
				bool flag2 = NeedsOwnedModConfigSectionRebuild(menuInstance2, menuType);
				bool isChinese = IsChineseLanguage();
				if (flag2)
				{
					RepairOwnedModConfigSections(menuInstance2, menuType);
				}
				RepairOwnedModConfigState(menuInstance2, menuType);
				NormalizeOwnedSectionTabCategories(menuInstance2, menuType, isChinese);
				LocalizeOwnedModConfigSectionTabs(menuInstance2, menuType, isChinese);
				Dictionary<string, string> map = BuildModConfigUiLocalizationMap(isChinese);
				foreach (Transform item in EnumerateModConfigUiRoots(menuInstance2, menuType))
				{
					ApplyTextLocalizationToRoot(item, map);
				}
				if (!NeedsOwnedModConfigSectionRebuild(menuInstance2, menuType) && AreOwnedSectionTabLabelsStable(menuInstance2, menuType, isChinese))
				{
					break;
				}
			}
		}
		finally
		{
			_modConfigStabilizeCoroutine = null;
			if (string.Equals(_modConfigStabilizeOwnerName, NormalizeModConfigContextName(ownerModName), StringComparison.OrdinalIgnoreCase))
			{
				_modConfigStabilizeOwnerName = string.Empty;
			}
		}
	}

	private void RepairOwnedModConfigSections(object menuInstance, Type menuType)
	{
		if (_repairingModConfigUi || menuInstance == null || menuType == null || !IsOwnedModConfigName(_activeModConfigName))
		{
			return;
		}
		try
		{
			string text = ResolveOwnedModConfigLookupName(menuType.Assembly, menuInstance, menuType);
			bool flag = NormalizeOwnedModConfigSections(menuType.Assembly, menuInstance, menuType);
			bool flag2 = GetOwnedSectionTabCount(menuInstance, menuType) != GetCurrentCanonicalSections().Count;
			if (flag || flag2)
			{
				_repairingModConfigUi = true;
				_activeModConfigName = text ?? _activeModConfigName;
				MethodInfo method = menuType.GetMethod("UpdateSectionTabs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(string) }, null);
				if (method != null && !string.IsNullOrWhiteSpace(_activeModConfigName))
				{
					method.Invoke(menuInstance, new object[1] { _activeModConfigName });
					ForceOwnedSectionTabLayoutRefresh(menuInstance, menuType);
					NormalizeOwnedSectionTabCategories(menuInstance, menuType, IsChineseLanguage());
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] RepairOwnedModConfigSections failed: " + ex.Message));
		}
		finally
		{
			_repairingModConfigUi = false;
		}
	}

	private static bool TryGetModConfigMenuInstance(out Type menuType, out object menuInstance)
	{
		menuType = null;
		menuInstance = null;
		if (!Chainloader.PluginInfos.TryGetValue("com.github.PEAKModding.PEAKLib.ModConfig", out var value) || (Object)(object)((value != null) ? value.Instance : null) == (Object)null)
		{
			return false;
		}
		Assembly assembly = ((object)value.Instance).GetType().Assembly;
		menuType = assembly.GetType("PEAKLib.ModConfig.Components.ModdedSettingsMenu") ?? ResolveLoadedType("PEAKLib.ModConfig.Components.ModdedSettingsMenu", "com.github.PEAKModding.PEAKLib.ModConfig");
		menuInstance = menuType?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
		if (menuType != null)
		{
			return menuInstance != null;
		}
		return false;
	}

	private IEnumerable<Transform> EnumerateModConfigUiRoots(object menuInstance, Type menuType)
	{
		HashSet<int> hashSet = new HashSet<int>();
		foreach (Transform item in EnumerateCandidateTransforms(menuInstance, menuType))
		{
			if (!((Object)item == (Object)null) && hashSet.Add(((Object)item).GetInstanceID()))
			{
				yield return item;
			}
		}
	}

	private IEnumerable<Transform> EnumerateCandidateTransforms(object menuInstance, Type menuType)
	{
		object obj = menuType.GetProperty("Content", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance);
		Transform val = (Transform)((obj is Transform) ? obj : null);
		if ((Object)val != (Object)null)
		{
			yield return val;
		}
	}

	private bool NeedsOwnedModConfigSectionRebuild(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null || !IsOwnedModConfigName(_activeModConfigName))
		{
			return false;
		}
		int count = GetCurrentCanonicalSections().Count;
		if (count <= 0)
		{
			return false;
		}
		int ownedSectionTabCount = GetOwnedSectionTabCount(menuInstance, menuType);
		return ownedSectionTabCount <= 0 || ownedSectionTabCount < count || ownedSectionTabCount > count;
	}

	private static int GetOwnedSectionTabCount(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null)
		{
			return 0;
		}
		try
		{
			object value = menuType.GetField("SectionTabController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance);
			Component val = (Component)((value is Component) ? value : null);
			if ((Object)val == (Object)null)
			{
				return 0;
			}
			Type type = menuType.Assembly.GetType("PEAKLib.ModConfig.Components.ModdedTABSButton");
			return (type != null) ? ((Component)val).GetComponentsInChildren(type, includeInactive: true).Length : 0;
		}
		catch
		{
			return 0;
		}
	}

	private static void ForceOwnedSectionTabLayoutRefresh(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null)
		{
			return;
		}
		try
		{
			object value = menuType.GetField("SectionTabController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance);
			Component val = (Component)((value is Component) ? value : null);
			if ((Object)val == (Object)null)
			{
				return;
			}
			Canvas.ForceUpdateCanvases();
			foreach (RectTransform item in ((Component)val).GetComponentsInParent<RectTransform>(includeInactive: true))
			{
				if (!((Object)item == (Object)null))
				{
					LayoutRebuilder.ForceRebuildLayoutImmediate(item);
				}
			}
			RectTransform component = ((Component)val).GetComponent<RectTransform>();
			if (!((Object)component == (Object)null))
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(component);
			}
			Canvas.ForceUpdateCanvases();
		}
		catch
		{
		}
	}

	private static void LocalizeOwnedModConfigSectionTabs(object menuInstance, Type menuType, bool isChinese)
	{
		if (menuInstance == null || menuType == null)
		{
			return;
		}
		try
		{
			object obj = menuType.GetField("SectionTabController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance);
			Component val = (Component)((obj is Component) ? obj : null);
			if ((Object)val == (Object)null)
			{
				return;
			}
			TMP_Text[] componentsInChildren = ((Component)val).GetComponentsInChildren<TMP_Text>(true);
			foreach (TMP_Text val2 in componentsInChildren)
			{
				if ((Object)val2 == (Object)null)
				{
					continue;
				}
				string text = NormalizeLocalizedText(val2.text).Trim();
				if (string.IsNullOrWhiteSpace(text) || !TryGetOwnedCanonicalSectionName(text, out var canonicalSectionName))
				{
					continue;
				}
				string displayName;
				string text2 = (TryGetLocalizedSectionDisplayName(canonicalSectionName, isChinese, out displayName) ? displayName : GetLocalizedSectionName(canonicalSectionName, isChinese));
				if (!string.IsNullOrWhiteSpace(text2) && !string.Equals(val2.text, text2, StringComparison.Ordinal))
				{
					val2.text = text2;
				}
			}
		}
		catch
		{
		}
	}

	private void RepairOwnedModConfigState(object menuInstance, Type menuType)
	{
		if (_repairingModConfigUi || menuInstance == null || menuType == null || !IsOwnedModConfigName(_activeModConfigName))
		{
			return;
		}
		try
		{
			bool isChinese = IsChineseLanguage();
			bool flag = FilterHiddenOwnedModConfigRows(menuInstance, menuType);
			DeduplicateVisibleOwnedModConfigRows(menuInstance, menuType);
			bool flag2 = NormalizeOwnedSectionTabCategories(menuInstance, menuType, isChinese);
			bool flag3 = NormalizeOwnedModConfigSelectedSection(menuInstance, menuType);
			bool flag4 = NeedsOwnedModConfigContentRefresh(menuInstance, menuType);
			if (flag || flag2 || flag3 || flag4)
			{
				_repairingModConfigUi = true;
				string selectedModConfigCategory = GetSelectedModConfigCategory(menuInstance, menuType);
				if (string.IsNullOrWhiteSpace(selectedModConfigCategory))
				{
					selectedModConfigCategory = _activeModConfigName;
				}
				string selectedModConfigSection = GetSelectedModConfigSection(menuInstance, menuType);
				if (!TryGetOwnedCanonicalSectionName(selectedModConfigSection, out var canonicalSectionName))
				{
					canonicalSectionName = GetCurrentCanonicalSections().FirstOrDefault();
				}
				if (flag4)
				{
					menuType.GetMethod("RefreshSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(menuInstance, null);
					FieldInfo field = menuType.GetField("selectedSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (!string.IsNullOrWhiteSpace(canonicalSectionName) && field != null)
					{
						field.SetValue(menuInstance, canonicalSectionName);
					}
					MethodInfo method = menuType.GetMethod("ShowSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(string) }, null);
					if (method != null && !string.IsNullOrWhiteSpace(selectedModConfigCategory))
					{
						method.Invoke(menuInstance, new object[1] { selectedModConfigCategory });
					}
					else if (!string.IsNullOrWhiteSpace(canonicalSectionName))
					{
						SetSelectedModConfigSection(menuInstance, menuType, canonicalSectionName);
					}
				}
				else if (!string.IsNullOrWhiteSpace(canonicalSectionName))
				{
					SetSelectedModConfigSection(menuInstance, menuType, canonicalSectionName);
				}
				CleanupDestroyedModConfigCells(menuInstance, menuType);
				FilterHiddenOwnedModConfigRows(menuInstance, menuType);
				DeduplicateVisibleOwnedModConfigRows(menuInstance, menuType);
				NormalizeOwnedSectionTabCategories(menuInstance, menuType, isChinese);
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] RepairOwnedModConfigState failed: " + ex.Message));
		}
		finally
		{
			_repairingModConfigUi = false;
		}
	}

	private bool NeedsOwnedModConfigContentRefresh(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null || !IsOwnedModConfigName(_activeModConfigName))
		{
			return false;
		}
		string selectedModConfigSection = GetSelectedModConfigSection(menuInstance, menuType);
		if (!TryGetOwnedCanonicalSectionName(selectedModConfigSection, out var canonicalSectionName))
		{
			canonicalSectionName = GetCurrentCanonicalSections().FirstOrDefault();
		}
		int expectedOwnedModConfigEntryCount = GetExpectedOwnedModConfigEntryCount(menuInstance, menuType);
		if (expectedOwnedModConfigEntryCount <= 0)
		{
			return false;
		}
		int contentVisibleOwnedSettingsCellCount = GetContentVisibleOwnedSettingsCellCount(menuInstance, menuType, canonicalSectionName);
		int modConfigContentChildCount = GetModConfigContentChildCount(menuInstance, menuType);
		if (contentVisibleOwnedSettingsCellCount <= 0)
		{
			return true;
		}
		if (contentVisibleOwnedSettingsCellCount != expectedOwnedModConfigEntryCount)
		{
			return true;
		}
		if (modConfigContentChildCount < expectedOwnedModConfigEntryCount)
		{
			return true;
		}
		return false;
	}

	private bool FilterHiddenOwnedModConfigRows(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null || !IsOwnedModConfigName(_activeModConfigName))
		{
			return false;
		}
		bool flag = false;
		try
		{
			Transform modConfigContentTransform = GetModConfigContentTransform(menuInstance, menuType);
			IList list = GetFieldInHierarchy(menuType, "m_spawnedCells")?.GetValue(menuInstance) as IList;
			if (list != null)
			{
				for (int num = list.Count - 1; num >= 0; num--)
				{
					object obj = list[num];
					if (obj == null)
					{
						list.RemoveAt(num);
						flag = true;
						continue;
					}
					if (obj is Object @object && (Object)@object == (Object)null)
					{
						list.RemoveAt(num);
						flag = true;
						continue;
					}
					ConfigEntryBase configEntryBaseFromModConfigCell = TryGetConfigEntryBaseFromSettingOption(obj);
					if (configEntryBaseFromModConfigCell != null && IsOwnedConfigEntry(configEntryBaseFromModConfigCell) && !ShouldExposeOwnedConfigEntry(configEntryBaseFromModConfigCell))
					{
						list.RemoveAt(num);
						flag = true;
						GameObject directModConfigCellGameObject = TryGetDirectModConfigCellGameObject(obj);
						if ((Object)(object)directModConfigCellGameObject != (Object)null)
						{
							Object.Destroy((Object)(object)directModConfigCellGameObject);
						}
					}
				}
			}
			HashSet<string> hiddenOwnedConfigDisplayNames = GetHiddenOwnedConfigDisplayNames();
			if ((Object)modConfigContentTransform != (Object)null)
			{
				for (int num2 = modConfigContentTransform.childCount - 1; num2 >= 0; num2--)
				{
					Transform child = modConfigContentTransform.GetChild(num2);
					if ((Object)child == (Object)null)
					{
						continue;
					}
					if (ShouldRemoveOwnedModConfigRowByDisplayName(child, hiddenOwnedConfigDisplayNames))
					{
						Object.Destroy((Object)(object)((Component)child).gameObject);
						flag = true;
					}
				}
			}
			if (flag)
			{
				CleanupDestroyedModConfigCells(menuInstance, menuType);
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] FilterHiddenOwnedModConfigRows failed: " + DescribeReflectionException(ex)));
		}
		return flag;
	}

	private bool EnsureOwnedModConfigEntriesRegistered(Type menuType)
	{
		if (menuType == null)
		{
			return false;
		}
		try
		{
			Type type = menuType.Assembly.GetType("PEAKLib.ModConfig.ModConfigPlugin");
			if (type == null || SettingsHandler.Instance == null)
			{
				return false;
			}
			ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config);
			if (configEntriesSnapshot.Length == 0)
			{
				return false;
			}
			HashSet<string> hashSet = GetOwnedRegisteredConfigEntryIdentitySet();
			List<ConfigEntryBase> list = (from entry in configEntriesSnapshot
				where entry != null && ShouldExposeOwnedConfigEntry(entry) && !hashSet.Contains(GetConfigEntryIdentity(entry))
				orderby GetModConfigSectionSortIndex(GetModConfigSectionForEntry(entry)), GetModConfigEntrySortIndex(entry)
				select entry).ToList();
			if (list.Count == 0)
			{
				return false;
			}
			IList list2 = type.GetProperty("EntriesProcessed", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as IList;
			if (list2 != null)
			{
				foreach (ConfigEntryBase item2 in list)
				{
					if (list2.Contains(item2))
					{
						list2.Remove(item2);
					}
				}
			}
			type.GetMethod("ProcessModEntries", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
			HashSet<string> hashSet2 = GetOwnedRegisteredConfigEntryIdentitySet();
			List<string> list3 = list.Select(GetConfigEntryIdentity).Where((string identity) => !hashSet2.Contains(identity)).ToList();
			if (list3.Count != 0)
			{
				Log.LogWarning((object)$"[ShootZombies] ModConfig entries still missing after restore: {string.Join(", ", list3)}");
			}
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] EnsureOwnedModConfigEntriesRegistered failed: " + DescribeReflectionException(ex)));
			return false;
		}
	}

	private static HashSet<string> GetOwnedRegisteredConfigEntryIdentitySet()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		if (SettingsHandler.Instance == null)
		{
			return hashSet;
		}
		IEnumerable enumerable = typeof(SettingsHandler).GetMethod("GetAllSettings", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(SettingsHandler.Instance, null) as IEnumerable;
		if (enumerable == null)
		{
			return hashSet;
		}
		foreach (object item in enumerable)
		{
			ConfigEntryBase val = TryGetConfigEntryBaseFromSettingOption(item);
			if (val != null && IsOwnedConfigEntry(val))
			{
				hashSet.Add(GetConfigEntryIdentity(val));
			}
		}
		return hashSet;
	}

	private static string GetConfigEntryIdentity(ConfigEntryBase entry)
	{
		if (entry == null || entry.Definition == (ConfigDefinition)null)
		{
			return string.Empty;
		}
		return entry.Definition.Section + "\u0001" + entry.Definition.Key;
	}

	private int GetExpectedOwnedModConfigEntryCount(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null)
		{
			return 0;
		}
		string selectedModConfigSection = GetSelectedModConfigSection(menuInstance, menuType);
		if (!TryGetOwnedCanonicalSectionName(selectedModConfigSection, out var canonicalSectionName))
		{
			canonicalSectionName = GetCurrentCanonicalSections().FirstOrDefault();
		}
		if (string.IsNullOrWhiteSpace(canonicalSectionName))
		{
			return 0;
		}
		ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config);
		return configEntriesSnapshot.Count((ConfigEntryBase entry) => ShouldExposeOwnedConfigEntry(entry) && string.Equals(GetModConfigSectionForEntry(entry), canonicalSectionName, StringComparison.Ordinal));
	}

	private static int GetSpawnedVisibleOwnedSettingsCellCount(object menuInstance, Type menuType, string canonicalSectionName)
	{
		if (menuInstance == null || menuType == null || string.IsNullOrWhiteSpace(canonicalSectionName))
		{
			return 0;
		}
		IList list = menuType.GetField("m_spawnedCells", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance) as IList;
		if (list == null)
		{
			return 0;
		}
		int num = 0;
		foreach (object item in list)
		{
			if (item == null)
			{
				continue;
			}
			if (item is Object @object && (Object)@object == (Object)null)
			{
				continue;
			}
			ConfigEntryBase configEntryBaseFromModConfigCell = TryGetConfigEntryBaseFromSettingOption(item);
			if (configEntryBaseFromModConfigCell != null && ShouldExposeOwnedConfigEntry(configEntryBaseFromModConfigCell) && string.Equals(GetModConfigSectionForEntry(configEntryBaseFromModConfigCell), canonicalSectionName, StringComparison.Ordinal))
			{
				num++;
			}
		}
		return num;
	}

	private static int GetContentVisibleOwnedSettingsCellCount(object menuInstance, Type menuType, string canonicalSectionName)
	{
		if (menuInstance == null || menuType == null || string.IsNullOrWhiteSpace(canonicalSectionName))
		{
			return 0;
		}
		Transform modConfigContentTransform = GetModConfigContentTransform(menuInstance, menuType);
		if ((Object)modConfigContentTransform == (Object)null)
		{
			return 0;
		}
		HashSet<string> visibleOwnedConfigDisplayNamesForSection = GetVisibleOwnedConfigDisplayNamesForSection(canonicalSectionName);
		if (visibleOwnedConfigDisplayNamesForSection.Count == 0)
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < modConfigContentTransform.childCount; i++)
		{
			Transform child = modConfigContentTransform.GetChild(i);
			if ((Object)child == (Object)null)
			{
				continue;
			}
			if (DoesModConfigRowMatchDisplayNames(child, visibleOwnedConfigDisplayNamesForSection))
			{
				num++;
			}
		}
		return num;
	}

	private static int GetSpawnedSettingsCellCount(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null)
		{
			return 0;
		}
		IList list = menuType.GetField("m_spawnedCells", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance) as IList;
		if (list != null)
		{
			return list.Cast<object>().Count((object item) => item != null);
		}
		Transform val = (Transform)((menuType.GetProperty("Content", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance) is Transform transform) ? transform : null);
		if ((Object)val == (Object)null)
		{
			return 0;
		}
		return val.childCount;
	}

	private static int GetModConfigContentChildCount(object menuInstance, Type menuType)
	{
		Transform modConfigContentTransform = GetModConfigContentTransform(menuInstance, menuType);
		if ((Object)modConfigContentTransform == (Object)null)
		{
			return 0;
		}
		return modConfigContentTransform.childCount;
	}

	private static Transform GetModConfigContentTransform(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null)
		{
			return null;
		}
		object value = menuType.GetProperty("Content", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance);
		return (Transform)((value is Transform) ? value : null);
	}

	private bool NormalizeOwnedModConfigSelectedSection(object menuInstance, Type menuType)
	{
		List<string> list = GetCurrentCanonicalSections();
		if (list.Count == 0)
		{
			return false;
		}
		string text = NormalizeLocalizedText(GetSelectedModConfigSection(menuInstance, menuType)).Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			return SetSelectedModConfigSection(menuInstance, menuType, list[0]);
		}
		if (TryGetOwnedCanonicalSectionName(text, out var canonicalSectionName) && list.Contains(canonicalSectionName))
		{
			if (string.Equals(text, canonicalSectionName, StringComparison.Ordinal))
			{
				return false;
			}
			return SetSelectedModConfigSection(menuInstance, menuType, canonicalSectionName);
		}
		return SetSelectedModConfigSection(menuInstance, menuType, list[0]);
	}

	private static string GetSelectedModConfigSection(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null)
		{
			return string.Empty;
		}
		FieldInfo field = menuType.GetField("selectedSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field != null && field.GetValue(menuInstance) is string result)
		{
			return result;
		}
		PropertyInfo propertyInfo = menuType.GetProperty("selectedSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? menuType.GetProperty("SelectedSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (propertyInfo != null && propertyInfo.CanRead)
		{
			return (propertyInfo.GetValue(menuInstance, null) as string) ?? string.Empty;
		}
		return string.Empty;
	}

	private static bool SetSelectedModConfigSection(object menuInstance, Type menuType, string section)
	{
		if (menuInstance == null || menuType == null || string.IsNullOrWhiteSpace(section))
		{
			return false;
		}
		if (string.Equals(GetSelectedModConfigSection(menuInstance, menuType), section, StringComparison.Ordinal))
		{
			return false;
		}
		MethodInfo method = menuType.GetMethod("SetSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { typeof(string) }, null);
		if (method != null)
		{
			method.Invoke(menuInstance, new object[1] { section });
			return true;
		}
		FieldInfo field = menuType.GetField("selectedSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field != null)
		{
			field.SetValue(menuInstance, section);
			return true;
		}
		PropertyInfo propertyInfo = menuType.GetProperty("selectedSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? menuType.GetProperty("SelectedSection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (propertyInfo != null && propertyInfo.CanWrite)
		{
			propertyInfo.SetValue(menuInstance, section, null);
			return true;
		}
		return false;
	}

	private static bool TryGetOwnedCanonicalSectionName(string section, out string canonicalSectionName)
	{
		canonicalSectionName = string.Empty;
		section = NormalizeLocalizedText(section).Trim();
		if (string.IsNullOrWhiteSpace(section))
		{
			return false;
		}
		foreach (string item in GetDesiredModConfigSectionOrder())
		{
			if (MatchesOwnedSectionAlias(section, item))
			{
				canonicalSectionName = item;
				return true;
			}
		}
		return false;
	}

	private void SyncActiveModConfigName(object menuInstance, Type menuType)
	{
		string selectedModConfigCategory = GetSelectedModConfigCategory(menuInstance, menuType);
		if (!string.IsNullOrWhiteSpace(selectedModConfigCategory))
		{
			_activeModConfigName = selectedModConfigCategory;
		}
	}

	private static bool NormalizeOwnedSectionTabCategories(object menuInstance, Type menuType, bool isChinese)
	{
		if (menuInstance == null || menuType == null)
		{
			return false;
		}
		try
		{
			object value = menuType.GetField("SectionTabController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance);
			Component val = (Component)((value is Component) ? value : null);
			if ((Object)val == (Object)null)
			{
				return false;
			}
			Type type = menuType.Assembly.GetType("PEAKLib.ModConfig.Components.ModdedTABSButton");
			if (type == null)
			{
				return false;
			}
			FieldInfo field = type.GetField("category", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			FieldInfo field2 = type.GetField("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				return false;
			}
			bool result = false;
			foreach (Component item in ((Component)val).GetComponentsInChildren(type, includeInactive: true).OfType<Component>())
			{
				if ((Object)item == (Object)null)
				{
					continue;
				}
				string text = NormalizeLocalizedText(field.GetValue(item) as string).Trim();
				TMP_Text val2 = (TMP_Text)((field2 != null) ? field2.GetValue(item) : null);
				string text2 = (!string.IsNullOrWhiteSpace(text)) ? text : NormalizeLocalizedText((val2 != null) ? val2.text : string.Empty).Trim();
				if (!TryGetOwnedCanonicalSectionName(text2, out var canonicalSectionName))
				{
					continue;
				}
				if (!string.Equals(text, canonicalSectionName, StringComparison.Ordinal))
				{
					field.SetValue(item, canonicalSectionName);
					result = true;
				}
				if ((Object)((Component)item).gameObject != (Object)null && !string.Equals(((Object)((Component)item).gameObject).name, canonicalSectionName, StringComparison.Ordinal))
				{
					((Object)((Component)item).gameObject).name = canonicalSectionName;
					result = true;
				}
				string displayName;
				string text3 = (TryGetLocalizedSectionDisplayName(canonicalSectionName, isChinese, out displayName) ? displayName : GetLocalizedSectionName(canonicalSectionName, isChinese));
				if ((Object)val2 != (Object)null && !string.IsNullOrWhiteSpace(text3) && !string.Equals(val2.text, text3, StringComparison.Ordinal))
				{
					val2.text = text3;
					result = true;
				}
			}
			return result;
		}
		catch
		{
			return false;
		}
	}

	private static bool AreOwnedSectionTabLabelsStable(object menuInstance, Type menuType, bool isChinese)
	{
		if (menuInstance == null || menuType == null)
		{
			return false;
		}
		try
		{
			object value = menuType.GetField("SectionTabController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance);
			Component val = (Component)((value is Component) ? value : null);
			if ((Object)val == (Object)null)
			{
				return false;
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (TMP_Text item in ((Component)val).GetComponentsInChildren<TMP_Text>(true))
			{
				if ((Object)item == (Object)null)
				{
					continue;
				}
				string text = NormalizeLocalizedText(item.text).Trim();
				if (string.IsNullOrWhiteSpace(text) || !TryGetOwnedCanonicalSectionName(text, out var canonicalSectionName))
				{
					continue;
				}
				hashSet.Add(canonicalSectionName);
				string displayName;
				string text2 = (TryGetLocalizedSectionDisplayName(canonicalSectionName, isChinese, out displayName) ? displayName : GetLocalizedSectionName(canonicalSectionName, isChinese));
				if (!string.IsNullOrWhiteSpace(text2) && !string.Equals(text, text2, StringComparison.Ordinal))
				{
					return false;
				}
			}
			return hashSet.Count == Instance?.GetCurrentCanonicalSections().Count;
		}
		catch
		{
			return false;
		}
	}

	private static string GetSelectedModConfigCategory(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null)
		{
			return string.Empty;
		}
		try
		{
			object value = menuType.GetProperty("Tabs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance);
			if (value == null)
			{
				return string.Empty;
			}
			FieldInfo fieldInfo = GetFieldInHierarchy(value.GetType(), "selectedButton");
			object value2 = fieldInfo?.GetValue(value);
			if (value2 == null)
			{
				return string.Empty;
			}
			return NormalizeLocalizedText(value2.GetType().GetField("category", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value2) as string).Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	private static FieldInfo GetFieldInHierarchy(Type type, string fieldName)
	{
		for (Type type2 = type; type2 != null; type2 = type2.BaseType)
		{
			FieldInfo field = type2.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				return field;
			}
		}
		return null;
	}

	private static string FindFallbackAkSoundSelection(string currentSelection)
	{
		foreach (string akSoundSelectionValue in AkSoundSelectionValues)
		{
			if (_externalGunshotSounds.TryGetValue(akSoundSelectionValue, out var value) && (Object)value != (Object)null)
			{
				return akSoundSelectionValue;
			}
		}
		foreach (string akSoundSelectionValue2 in AkSoundSelectionValues)
		{
			string externalAkSoundPath = GetExternalAkSoundPath(akSoundSelectionValue2);
			if (!string.IsNullOrWhiteSpace(externalAkSoundPath) && File.Exists(externalAkSoundPath))
			{
				return akSoundSelectionValue2;
			}
		}
		return NormalizeAkSoundSelection(currentSelection);
	}

	private static bool MatchesOwnedSectionAlias(string value, string canonicalSectionName)
	{
		if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(canonicalSectionName))
		{
			return false;
		}
		string text = NormalizeSectionAlias(value);
		string text2 = NormalizeSectionAlias(canonicalSectionName);
		if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2) && string.Equals(text, text2, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (string.Equals(value, canonicalSectionName, StringComparison.OrdinalIgnoreCase) || string.Equals(value, GetLocalizedSectionName(canonicalSectionName, isChinese: false), StringComparison.OrdinalIgnoreCase) || string.Equals(value, GetLocalizedSectionName(canonicalSectionName, isChinese: true), StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string displayName;
		if (TryGetLocalizedSectionDisplayName(canonicalSectionName, isChinese: false, out displayName) && string.Equals(value, displayName, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (TryGetLocalizedSectionDisplayName(canonicalSectionName, isChinese: true, out displayName) && string.Equals(value, displayName, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return false;
	}

	private bool NormalizeOwnedModConfigSections(Assembly assembly, object menuInstance = null, Type menuType = null)
	{
		if (assembly == null)
		{
			return false;
		}
		Type type = assembly.GetType("PEAKLib.ModConfig.Components.ModSectionNames");
		if (type == null)
		{
			return false;
		}
		PropertyInfo property = type.GetProperty("SectionNames", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		PropertyInfo property2 = type.GetProperty("ModName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		PropertyInfo property3 = type.GetProperty("Sections", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property == null || property2 == null || property3 == null)
		{
			return false;
		}
		if (!(property.GetValue(null) is IList list))
		{
			return false;
		}
		// SectionNames æ˜¯ ModConfig çš„ç« èŠ‚æ•°æ®æºï¼Œè¿™é‡Œåªä¿ç•™å½“å‰æ¨¡ç»„å®žé™…å­˜åœ¨çš„ç« èŠ‚å¹¶æŒ‰é¢„æœŸé¡ºåºé‡æŽ’ã€‚
		// SectionNames is ModConfig's source of truth for section tabs; keep only our live sections in the desired order.
		List<string> list2 = (from section in GetCurrentCanonicalSections()
			where !string.IsNullOrWhiteSpace(section)
			select section).Distinct(StringComparer.Ordinal).ToList();
		if (list2.Count == 0)
		{
			return false;
		}
		List<object> list3 = new List<object>();
		foreach (object item in list)
		{
			string modName = property2.GetValue(item, null) as string;
			if (IsOwnedModConfigName(modName))
			{
				list3.Add(item);
			}
		}
		string text = ResolveOwnedModConfigLookupName(assembly, menuInstance, menuType, list3, property2);
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		bool result = false;
		object obj = list3.FirstOrDefault((object entry) => string.Equals(property2.GetValue(entry, null) as string, text, StringComparison.OrdinalIgnoreCase)) ?? list3.FirstOrDefault();
		if (obj == null)
		{
			obj = Activator.CreateInstance(type);
			property2.SetValue(obj, text, null);
			list.Add(obj);
			result = true;
		}
		for (int num = list3.Count - 1; num >= 0; num--)
		{
			if (!ReferenceEquals(list3[num], obj))
			{
				list.Remove(list3[num]);
				result = true;
			}
		}
		if (!string.Equals(property2.GetValue(obj, null) as string, text, StringComparison.Ordinal))
		{
			property2.SetValue(obj, text, null);
			result = true;
		}
		IList list4 = property3.GetValue(obj, null) as IList;
		List<string> list5 = new List<string>();
		if (list4 != null)
		{
			foreach (object item2 in list4)
			{
				if (item2 is string text2 && !string.IsNullOrWhiteSpace(text2))
				{
					list5.Add(text2);
				}
			}
		}
		if (!list5.SequenceEqual(list2))
		{
			property3.SetValue(obj, list2, null);
			result = true;
		}
		return result;
	}

	private string ResolveOwnedModConfigLookupName(Assembly assembly, object menuInstance, Type menuType, IEnumerable<object> existingEntries = null, PropertyInfo modNameProperty = null)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		List<string> list = new List<string>();
		void AddCandidate(string name)
		{
			name = NormalizeLocalizedText(name).Trim();
			if (!string.IsNullOrWhiteSpace(name) && IsOwnedModConfigName(name) && hashSet.Add(name))
			{
				list.Add(name);
			}
		}
		AddCandidate(GetSelectedModConfigCategory(menuInstance, menuType));
		AddCandidate(_activeModConfigName);
		if (existingEntries != null && modNameProperty != null)
		{
			foreach (object existingEntry in existingEntries)
			{
				AddCandidate(modNameProperty.GetValue(existingEntry, null) as string);
			}
		}
		AddCandidate("ShootZombies");
		AddCandidate("Shoot Zombies");
		AddCandidate(Name);
		AddCandidate(GetLocalizedModDisplayName(isChinese: false));
		AddCandidate(GetLocalizedModDisplayName(isChinese: true));
		AddCandidate("打僵尸");
		AddCandidate("僵尸模式");
		return list.FirstOrDefault();
	}

	private bool IsOwnedModConfigName(string modName)
	{
		if (string.IsNullOrWhiteSpace(modName))
		{
			return false;
		}
		if (string.Equals(modName, "Shoot Zombies", StringComparison.OrdinalIgnoreCase) || string.Equals(modName, "ShootZombies", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return string.Equals(modName, GetLocalizedModDisplayName(isChinese: true), StringComparison.OrdinalIgnoreCase) || string.Equals(modName, "僵尸模式", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsOwnedModConfigContext(object menuInstance, Type menuType)
	{
		if ((Object)(object)Instance == (Object)null)
		{
			return false;
		}
		string text = GetSelectedModConfigCategory(menuInstance, menuType);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = NormalizeLocalizedText(Instance._activeModConfigName).Trim();
		}
		return Instance.IsOwnedModConfigName(text);
	}

	private static bool ShouldLocalizeOwnedModConfigButton(string value, bool ownedContext)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		if ((Object)(object)Instance != (Object)null && Instance.IsOwnedModConfigName(value))
		{
			return true;
		}
		if (!ownedContext)
		{
			return false;
		}
		return TryGetOwnedCanonicalSectionName(value, out var _);
	}

	private static bool TryGetLocalizedOwnedConfigDisplayName(string value, bool isChinese, out string displayName)
	{
		displayName = string.Empty;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string localizedKeyName = GetLocalizedKeyName(value, isChinese);
		if (string.IsNullOrWhiteSpace(localizedKeyName))
		{
			return false;
		}
		string text = NormalizeLocalizedText(value).Trim();
		string text2 = NormalizeLocalizedText(localizedKeyName).Trim();
		if (string.IsNullOrWhiteSpace(text2) || string.Equals(text2, text, StringComparison.Ordinal))
		{
			return false;
		}
		displayName = text2;
		return true;
	}

	private static void DeduplicateVisibleOwnedModConfigRows(object menuInstance, Type menuType)
	{
		if (menuInstance == null || menuType == null)
		{
			return;
		}
		try
		{
			Transform val = (Transform)((menuType.GetProperty("Content", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(menuInstance) is Transform transform) ? transform : null);
			if ((Object)val == (Object)null)
			{
				return;
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			for (int num = val.childCount - 1; num >= 0; num--)
			{
				Transform child = val.GetChild(num);
				if ((Object)child == (Object)null)
				{
					continue;
				}
				TMP_Text val2 = ((Component)child).GetComponentsInChildren<TMP_Text>(true).FirstOrDefault((TMP_Text text) => !string.IsNullOrWhiteSpace((text != null) ? text.text : null));
				if ((Object)val2 == (Object)null)
				{
					continue;
				}
				string text2 = NormalizeLocalizedText(((TMP_Text)val2).text).Trim();
				if (string.IsNullOrWhiteSpace(text2))
				{
					continue;
				}
				if (!hashSet.Add(text2))
				{
					Object.Destroy((Object)(object)((Component)child).gameObject);
				}
			}
		}
		catch
		{
		}
	}

	private static GameObject TryGetDirectModConfigCellGameObject(object instance)
	{
		if (instance == null)
		{
			return null;
		}
		GameObject val = (GameObject)((instance is GameObject value) ? value : null);
		if ((Object)(object)val != (Object)null)
		{
			return val;
		}
		Component val2 = (Component)((instance is Component value2) ? value2 : null);
		if ((Object)(object)val2 != (Object)null)
		{
			return ((Component)val2).gameObject;
		}
		return null;
	}

	private HashSet<string> GetHiddenOwnedConfigDisplayNames()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config);
		foreach (ConfigEntryBase item in configEntriesSnapshot)
		{
			if (item == null || !IsOwnedConfigEntry(item) || ShouldExposeOwnedConfigEntry(item))
			{
				continue;
			}
			ConfigDefinition definition = item.Definition;
			string key = ((definition != null) ? definition.Key : null) ?? string.Empty;
			AddNormalizedOwnedConfigDisplayName(hashSet, key);
			AddNormalizedOwnedConfigDisplayName(hashSet, GetLocalizedKeyName(key, isChinese: false));
			AddNormalizedOwnedConfigDisplayName(hashSet, GetLocalizedKeyName(key, isChinese: true));
		}
		return hashSet;
	}

	private static void AddNormalizedOwnedConfigDisplayName(HashSet<string> names, string value)
	{
		if (names != null && !string.IsNullOrWhiteSpace(value))
		{
			names.Add(NormalizeLocalizedText(value).Trim());
		}
	}

	private static HashSet<string> GetVisibleOwnedConfigDisplayNamesForSection(string canonicalSectionName)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Plugin instance = Instance;
		ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot((instance != null) ? ((BaseUnityPlugin)instance).Config : null);
		foreach (ConfigEntryBase item in configEntriesSnapshot)
		{
			if (item == null || !IsOwnedConfigEntry(item) || !ShouldExposeOwnedConfigEntry(item) || !string.Equals(GetModConfigSectionForEntry(item), canonicalSectionName, StringComparison.Ordinal))
			{
				continue;
			}
			ConfigDefinition definition = item.Definition;
			string key = ((definition != null) ? definition.Key : null) ?? string.Empty;
			AddNormalizedOwnedConfigDisplayName(hashSet, key);
			AddNormalizedOwnedConfigDisplayName(hashSet, GetLocalizedKeyName(key, isChinese: false));
			AddNormalizedOwnedConfigDisplayName(hashSet, GetLocalizedKeyName(key, isChinese: true));
		}
		return hashSet;
	}

	private static bool ShouldRemoveOwnedModConfigRowByDisplayName(Transform row, HashSet<string> hiddenOwnedConfigDisplayNames)
	{
		if ((Object)row == (Object)null)
		{
			return false;
		}
		if (hiddenOwnedConfigDisplayNames == null || hiddenOwnedConfigDisplayNames.Count == 0)
		{
			return false;
		}
		return DoesModConfigRowMatchDisplayNames(row, hiddenOwnedConfigDisplayNames);
	}

	private static bool DoesModConfigRowMatchDisplayNames(Transform row, HashSet<string> displayNames)
	{
		if ((Object)row == (Object)null || displayNames == null || displayNames.Count == 0)
		{
			return false;
		}
		TMP_Text[] componentsInChildren = ((Component)row).GetComponentsInChildren<TMP_Text>(true);
		foreach (TMP_Text val in componentsInChildren)
		{
			string text = NormalizeLocalizedText((val != null) ? val.text : null).Trim();
			if (!string.IsNullOrWhiteSpace(text) && displayNames.Contains(text))
			{
				return true;
			}
		}
		return false;
	}

	private void ApplyTextLocalizationToRoot(Transform root, Dictionary<string, string> map)
	{
		if ((Object)root == (Object)null || map == null || map.Count == 0)
		{
			return;
		}
		TMP_Text[] componentsInChildren = ((Component)root).GetComponentsInChildren<TMP_Text>(true);
		foreach (TMP_Text val in componentsInChildren)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			string text = val.text;
			if (!string.IsNullOrWhiteSpace(text))
			{
				string value = null;
				if (map.TryGetValue(text.Trim(), out value) && !string.Equals(value, text, StringComparison.Ordinal))
				{
					val.text = value;
				}
			}
		}
	}

	private Dictionary<string, string> BuildModConfigUiLocalizationMap(bool isChinese)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
		AddUiLocalizationPair(dictionary, "Shoot Zombies", GetLocalizedModDisplayName(isChinese));
		AddUiLocalizationPair(dictionary, "ShootZombies", GetLocalizedModDisplayName(isChinese));
		AddUiLocalizationPair(dictionary, "僵尸模式", GetLocalizedModDisplayName(isChinese));
		AddUiLocalizationPair(dictionary, "打僵尸", GetLocalizedModDisplayName(isChinese));
		string[] array = new string[9] { "General", "Weapon", "Inventory", "Zombie", "Zombie Spawn", "Zombie AI", "Fog", "Features", "Hotkeys" };
		foreach (string text in array)
		{
			string displayName;
			string localized = (TryGetLocalizedSectionDisplayName(text, isChinese, out displayName) ? displayName : GetLocalizedSectionName(text, isChinese));
			AddUiLocalizationPair(dictionary, text, localized);
			string localizedSectionDescription = GetLocalizedSectionDescription(text, isChinese: false);
			string localizedSectionDescription2 = GetLocalizedSectionDescription(text, isChinese);
			if (!string.IsNullOrWhiteSpace(localizedSectionDescription) && !string.IsNullOrWhiteSpace(localizedSectionDescription2))
			{
				AddUiLocalizationPair(dictionary, localizedSectionDescription, localizedSectionDescription2);
			}
		}
		AddUiLocalizationPair(dictionary, "Weapons", GetLocalizedSectionName("Weapon", isChinese));
		AddUiLocalizationPair(dictionary, "Zombie Behavior", GetLocalizedSectionName("Zombie", isChinese));
		AddUiLocalizationPair(dictionary, "Zombie Behaviors", GetLocalizedSectionName("Zombie", isChinese));
		AddUiLocalizationPair(dictionary, "Zombie Spawning", GetLocalizedSectionName("Zombie Spawn", isChinese));
		string[] first = new string[]
		{
			"Weapon Selection", "Fire Interval", "Fire Volume", "Weapon Model Y Rotation", "Weapon Model Scale", "Weapon Model X Position", "Weapon Model Y Position", "Weapon Model Z Position",
			"Max Distance", "Bullet Size", "Zombie Time Reduction", "Move Speed", "Aggressiveness", "Knockback Force", "Enabled", "Zombie Spawn", "Zombie Spawn Enabled", "Max Count",
			"Spawn Interval",
			"Interval Random", "Spawn Count",
			"Count Random", "Spawn Radius", "Max Lifetime", "Destroy Distance", "Wakeup Distance", "Chase Distance", "Sprint Distance", "Chase Time"
		};
		string[] second = new string[]
		{
			"Behavior Difficulty", "Lunge Distance", "Lunge Time", "Lunge Recovery Time", "Wakeup Look Angle", "Target Search Interval", "Bite Recovery Time", "Same Player Bite Cooldown",
			"AK Sound", "Mod Enabled", "Weapon Enabled", "Mod", "Weapon", "Zombie Spawn", "Open Config Panel", "Config Panel Theme"
		};
		string[] second2 = new string[1] { "Spawn Weapon" };
		foreach (string item in first.Concat(second).Concat(second2))
		{
			string localizedKeyName = GetLocalizedKeyName(item, isChinese);
			if (!string.IsNullOrWhiteSpace(localizedKeyName))
			{
				AddUiLocalizationPair(dictionary, item, localizedKeyName);
			}
			string localizedDescription = GetLocalizedDescription(item, isChinese: false);
			string localizedDescription2 = GetLocalizedDescription(item, isChinese);
			if (!string.IsNullOrWhiteSpace(localizedDescription) && !string.IsNullOrWhiteSpace(localizedDescription2))
			{
				AddUiLocalizationPair(dictionary, localizedDescription, localizedDescription2);
			}
		}
		foreach (string item2 in ConfigPanelThemeValues.Concat(WeaponSelectionValues).Concat(AkSoundSelectionValues).Concat(ZombieBehaviorDifficultyValues))
		{
			AddUiLocalizationPair(dictionary, item2, GetLocalizedSelectableValueDisplayName(item2, isChinese));
		}
		return dictionary;
	}

	private static string GetLocalizedSelectableValueDisplayName(string value, bool isChinese)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		switch (NormalizeLookupToken(value))
		{
		case "dark":
		case "black":
		case "黑":
		case "黑色":
			return isChinese ? "黑色" : "Dark";
		case "light":
		case "white":
		case "白":
		case "白色":
			return isChinese ? "白色" : "Light";
		case "transparent":
		case "clear":
		case "translucent":
		case "透":
		case "透明":
			return isChinese ? "透明" : "Transparent";
		case "aksound1":
		case "aksounds1":
		case "sound1":
		case "声音1":
		case "音效1":
			return isChinese ? "音效 1" : "Sound 1";
		case "aksound2":
		case "aksounds2":
		case "sound2":
		case "声音2":
		case "音效2":
			return isChinese ? "音效 2" : "Sound 2";
		case "aksound3":
		case "aksounds3":
		case "sound3":
		case "声音3":
		case "音效3":
			return isChinese ? "音效 3" : "Sound 3";
		case "easy":
		case "simple":
		case "casual":
		case "休闲":
		case "简单":
			return isChinese ? "简单" : "Easy";
		case "standard":
		case "normal":
		case "默认":
		case "标准":
			return isChinese ? "标准" : "Standard";
		case "hard":
		case "困难":
			return isChinese ? "困难" : "Hard";
		case "insane":
		case "brutal":
		case "疯狂":
		case "残酷":
			return isChinese ? "疯狂" : "Insane";
		case "nightmare":
		case "hell":
		case "噩梦":
		case "地狱":
			return isChinese ? "噩梦" : "Nightmare";
		default:
			return value;
		}
	}

	private static void AddUiLocalizationPair(Dictionary<string, string> map, string english, string localized)
	{
		if (map != null && !string.IsNullOrWhiteSpace(english) && !string.IsNullOrWhiteSpace(localized))
		{
			string text = NormalizeLocalizedText(english);
			string text2 = NormalizeLocalizedText(localized);
			map[english] = text2;
			map[text] = text2;
			map[localized] = text2;
			map[text2] = text2;
			string text3 = english.Replace(" ", string.Empty);
			string text4 = text.Replace(" ", string.Empty);
			if (!map.ContainsKey(text3))
			{
				map[text3] = text2;
			}
			if (!map.ContainsKey(text4))
			{
				map[text4] = text2;
			}
			string text5 = text.ToUpperInvariant();
			string text6 = text2.ToUpperInvariant();
			map[text5] = text2;
			map[text6] = text2;
		}
	}

	private static bool IsSectionCanonicalName(string value)
	{
		switch (value)
		{
		case "General":
		case "Hotkeys":
		case "Weapon":
		case "Zombie":
		case "Inventory":
		case "Zombie AI":
		case "Zombie Spawn":
		case "Fog":
		case "Features":
			return true;
		default:
			return false;
		}
	}

	private static string GetLocalizedModDisplayNameCore(bool isChinese)
	{
		if (!isChinese)
		{
			return "ShootZombies";
		}
		return "打僵尸";
	}

	private static Type ResolveLoadedType(string fullName, string preferredAssembly = null)
	{
		if (!string.IsNullOrWhiteSpace(preferredAssembly))
		{
			Type type = Type.GetType(fullName + ", " + preferredAssembly, throwOnError: false);
			if (type != null)
			{
				return type;
			}
		}
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type type2 = null;
			try
			{
				type2 = assembly.GetType(fullName, throwOnError: false);
			}
			catch
			{
			}
			if (type2 != null)
			{
				return type2;
			}
		}
		return null;
	}

	private static Transform FindBestCameraAnchor(Transform root)
	{
		if ((Object)root == (Object)null)
		{
			return null;
		}
		Camera[] componentsInChildren = ((Component)root).GetComponentsInChildren<Camera>(true);
		Camera val = null;
		int num = int.MinValue;
		Camera[] array = componentsInChildren;
		foreach (Camera val2 in array)
		{
			if (!((Object)val2 == (Object)null) && ((Behaviour)val2).isActiveAndEnabled && !((Object)val2.targetTexture != (Object)null))
			{
				int num2 = 0;
				string text = (((Object)val2).name ?? string.Empty).ToLowerInvariant();
				if ((Object)(object)val2 == (Object)(object)Camera.main)
				{
					num2 += 500;
				}
				if (text.Contains("main"))
				{
					num2 += 150;
				}
				if (text.Contains("camera"))
				{
					num2 += 100;
				}
				if (text.Contains("fps") || text.Contains("view") || text.Contains("player"))
				{
					num2 += 80;
				}
				if (val2.depth > 0f)
				{
					num2 += 20;
				}
				if (num2 > num)
				{
					num = num2;
					val = val2;
				}
			}
		}
		if (!((Object)val != (Object)null))
		{
			return null;
		}
		return ((Component)val).transform;
	}

	private bool TryFindBestGlobalCameraAnchor(out Transform cameraTransform)
	{
		cameraTransform = null;
		List<Camera> list = new List<Camera>();
		try
		{
			list.AddRange(Object.FindObjectsByType<Camera>((FindObjectsSortMode)0));
		}
		catch
		{
		}
		try
		{
			Camera[] array = Resources.FindObjectsOfTypeAll<Camera>();
			foreach (Camera val in array)
			{
				if ((Object)val != (Object)null && !list.Contains(val))
				{
					list.Add(val);
				}
			}
		}
		catch
		{
		}
		Camera val2 = null;
		int num = int.MinValue;
		foreach (Camera item in list)
		{
			if ((Object)item == (Object)null || !((Behaviour)item).isActiveAndEnabled || (Object)item.targetTexture != (Object)null)
			{
				continue;
			}
			Scene scene = ((Component)item).gameObject.scene;
			if (scene.IsValid())
			{
				int globalCameraScore = GetGlobalCameraScore(item);
				if (globalCameraScore > num)
				{
					num = globalCameraScore;
					val2 = item;
				}
			}
		}
		if ((Object)val2 == (Object)null)
		{
			return false;
		}
		cameraTransform = ((Component)val2).transform;
		return true;
	}

	private int GetGlobalCameraScore(Camera camera)
	{
		if ((Object)camera == (Object)null)
		{
			return int.MinValue;
		}
		int num = 0;
		string text = ((((Object)camera).name ?? string.Empty) + "/" + (((Object)((Component)camera).transform.parent != (Object)null) ? ((Object)((Component)camera).transform.parent).name : string.Empty)).ToLowerInvariant();
		if ((Object)(object)camera == (Object)(object)Camera.main)
		{
			num += 500;
		}
		if (text.Contains("main"))
		{
			num += 180;
		}
		if (text.Contains("player") || text.Contains("fps") || text.Contains("first") || text.Contains("view") || text.Contains("camera"))
		{
			num += 90;
		}
		if ((Object)_localCharacter != (Object)null)
		{
			float num2 = Vector3.Distance(((Component)camera).transform.position, ((Component)_localCharacter).transform.position);
			num += Mathf.RoundToInt(Mathf.Clamp(40f - num2 * 10f, -40f, 40f));
		}
		if (camera.depth > 0f)
		{
			num += 10;
		}
		return num;
	}

	private static string LocalizeModConfigText(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return value;
		}
		value = NormalizeLocalizedText(value);
		bool isChinese = (Object)(object)Instance != (Object)null && Instance.IsChineseLanguage();
		string displayName;
		if (TryGetLocalizedSectionDisplayName(value, isChinese, out displayName) && !string.Equals(displayName, value, StringComparison.Ordinal))
		{
			return NormalizeLocalizedText(displayName);
		}
		string localizedSectionName = GetLocalizedSectionName(value, isChinese);
		if (!string.Equals(localizedSectionName, value, StringComparison.Ordinal))
		{
			return NormalizeLocalizedText(localizedSectionName);
		}
		string localizedKeyName = GetLocalizedKeyName(value, isChinese);
		if (!string.Equals(localizedKeyName, value, StringComparison.Ordinal))
		{
			return NormalizeLocalizedText(localizedKeyName);
		}
		string localizedDescription = GetLocalizedDescription(value, isChinese);
		if (!string.IsNullOrWhiteSpace(localizedDescription) && !string.Equals(localizedDescription, value, StringComparison.Ordinal))
		{
			return NormalizeLocalizedText(localizedDescription);
		}
		string localizedSelectableValueDisplayName = GetLocalizedSelectableValueDisplayName(value, isChinese);
		if (!string.IsNullOrWhiteSpace(localizedSelectableValueDisplayName) && !string.Equals(localizedSelectableValueDisplayName, value, StringComparison.Ordinal))
		{
			return NormalizeLocalizedText(localizedSelectableValueDisplayName);
		}
		if (string.Equals(value, "default", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
		{
			return NormalizeLocalizedText(GetLocalizedSelectableValueDisplayName(DefaultAkSoundOption, isChinese));
		}
		if (string.Equals(value, "Shoot Zombies", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "ShootZombies", StringComparison.OrdinalIgnoreCase))
		{
			return NormalizeLocalizedText(GetLocalizedModDisplayName(isChinese));
		}
		return NormalizeLocalizedText(value);
	}

	private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
	{
		if (assembly == null)
		{
			return Array.Empty<Type>();
		}
		try
		{
			return (from t in assembly.GetTypes()
				where t != null
				select t).ToArray();
		}
		catch (ReflectionTypeLoadException ex)
		{
			return ex.Types.Where((Type t) => t != null).ToArray();
		}
		catch
		{
			return Array.Empty<Type>();
		}
	}

	private static ConfigEntryBase TryGetConfigEntryBaseFromSettingOption(object instance)
	{
		if (instance == null)
		{
			return null;
		}
		try
		{
			Type type = instance.GetType();
			object obj = (type.GetProperty("PEAKLib.ModConfig.IBepInExProperty.ConfigBase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetProperty("ConfigBase", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(instance);
			ConfigEntryBase val = (ConfigEntryBase)((obj is ConfigEntryBase) ? obj : null);
			if (val != null)
			{
				return val;
			}
			string[] array = new string[4] { "<entryBase>P", "entryBase", "_entryBase", "<ConfigBase>k__BackingField" };
			foreach (string name in array)
			{
				object obj2 = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
				ConfigEntryBase val2 = (ConfigEntryBase)((obj2 is ConfigEntryBase) ? obj2 : null);
				if (val2 != null)
				{
					return val2;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static ConfigEntryBase TryGetConfigEntryBaseFromModConfigCell(object instance)
	{
		return TryResolveConfigEntryBaseFromObjectGraph(instance, 0, new HashSet<object>(ReferenceIdentityComparer.Instance));
	}

	private static ConfigEntryBase TryResolveConfigEntryBaseFromObjectGraph(object instance, int depth, HashSet<object> visited)
	{
		if (instance == null || depth > 2)
		{
			return null;
		}
		if (instance is string)
		{
			return null;
		}
		if (instance is Object @object && (Object)@object == (Object)null)
		{
			return null;
		}
		Type type = instance.GetType();
		if (!type.IsValueType && visited != null && !visited.Add(instance))
		{
			return null;
		}
		ConfigEntryBase val = TryGetConfigEntryBaseFromSettingOption(instance);
		if (val != null)
		{
			return val;
		}
		val = (ConfigEntryBase)((instance is ConfigEntryBase value) ? value : null);
		if (val != null)
		{
			return val;
		}
		if (instance is GameObject val2)
		{
			Component[] components = val2.GetComponents<Component>();
			foreach (Component val3 in components)
			{
				ConfigEntryBase configEntryBase = TryResolveConfigEntryBaseFromObjectGraph(val3, depth + 1, visited);
				if (configEntryBase != null)
				{
					return configEntryBase;
				}
			}
		}
		foreach (object item in EnumerateRelevantModConfigMemberValues(instance))
		{
			ConfigEntryBase configEntryBase2 = TryResolveConfigEntryBaseFromObjectGraph(item, depth + 1, visited);
			if (configEntryBase2 != null)
			{
				return configEntryBase2;
			}
		}
		return null;
	}

	private static GameObject TryGetAssociatedGameObjectFromModConfigCell(object instance)
	{
		return TryResolveGameObjectFromObjectGraph(instance, 0, new HashSet<object>(ReferenceIdentityComparer.Instance));
	}

	private static GameObject TryResolveGameObjectFromObjectGraph(object instance, int depth, HashSet<object> visited)
	{
		if (instance == null || depth > 2)
		{
			return null;
		}
		if (instance is Object @object && (Object)@object == (Object)null)
		{
			return null;
		}
		if (instance is string)
		{
			return null;
		}
		GameObject val = (GameObject)((instance is GameObject value) ? value : null);
		if ((Object)(object)val != (Object)null)
		{
			return val;
		}
		Component val2 = (Component)((instance is Component value2) ? value2 : null);
		if ((Object)(object)val2 != (Object)null)
		{
			return ((Component)val2).gameObject;
		}
		Transform val3 = (Transform)((instance is Transform value3) ? value3 : null);
		if ((Object)(object)val3 != (Object)null)
		{
			return ((Component)val3).gameObject;
		}
		Type type = instance.GetType();
		if (!type.IsValueType && visited != null && !visited.Add(instance))
		{
			return null;
		}
		foreach (object item in EnumerateRelevantModConfigMemberValues(instance))
		{
			GameObject gameObject = TryResolveGameObjectFromObjectGraph(item, depth + 1, visited);
			if ((Object)(object)gameObject != (Object)null)
			{
				return gameObject;
			}
		}
		return null;
	}

	private static IEnumerable<object> EnumerateRelevantModConfigMemberValues(object instance)
	{
		if (instance == null)
		{
			yield break;
		}
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		for (Type current = instance.GetType(); current != null; current = current.BaseType)
		{
			FieldInfo[] fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (fieldInfo == null || !hashSet.Add("F:" + fieldInfo.Name) || !ShouldInspectRelevantModConfigMember(fieldInfo.FieldType, fieldInfo.Name))
				{
					continue;
				}
				object value = null;
				try
				{
					value = fieldInfo.GetValue(instance);
				}
				catch
				{
				}
				if (value != null)
				{
					yield return value;
				}
			}
			PropertyInfo[] properties = current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (PropertyInfo propertyInfo in properties)
			{
				if (propertyInfo == null || !propertyInfo.CanRead || propertyInfo.GetIndexParameters().Length != 0 || !hashSet.Add("P:" + propertyInfo.Name) || !ShouldInspectRelevantModConfigMember(propertyInfo.PropertyType, propertyInfo.Name))
				{
					continue;
				}
				object value2 = null;
				try
				{
					value2 = propertyInfo.GetValue(instance, null);
				}
				catch
				{
				}
				if (value2 != null)
				{
					yield return value2;
				}
			}
		}
	}

	private static bool ShouldInspectRelevantModConfigMember(Type memberType, string memberName)
	{
		if (memberType == null)
		{
			return false;
		}
		if (typeof(ConfigEntryBase).IsAssignableFrom(memberType) || typeof(Component).IsAssignableFrom(memberType) || typeof(GameObject).IsAssignableFrom(memberType) || typeof(Transform).IsAssignableFrom(memberType))
		{
			return true;
		}
		string text = memberName ?? string.Empty;
		if (text.IndexOf("config", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("setting", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("option", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("entry", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("property", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("cell", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("row", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("view", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("item", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		string text2 = memberType.FullName ?? memberType.Name ?? string.Empty;
		if (text2.IndexOf("PEAKLib.ModConfig", StringComparison.OrdinalIgnoreCase) >= 0 || text2.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		return false;
	}

	private static void RefreshOwnedConfigEntryCache()
	{
		_ownedConfigEntries.Clear();
		try
		{
			Plugin instance = Instance;
			ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot((instance != null) ? ((BaseUnityPlugin)instance).Config : null);
			foreach (ConfigEntryBase item in configEntriesSnapshot)
			{
				_ownedConfigEntries.Add(item);
			}
		}
		catch
		{
		}
	}

	private static bool IsOwnedConfigEntry(ConfigEntryBase entry)
	{
		if (entry == null)
		{
			return false;
		}
		if (_ownedConfigEntries.Count == 0)
		{
			RefreshOwnedConfigEntryCache();
		}
		if (_ownedConfigEntries.Contains(entry))
		{
			return true;
		}
		try
		{
			Plugin instance = Instance;
			ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot((instance != null) ? ((BaseUnityPlugin)instance).Config : null);
			foreach (ConfigEntryBase item in configEntriesSnapshot)
			{
				_ownedConfigEntries.Add(item);
			}
		}
		catch
		{
		}
		return _ownedConfigEntries.Contains(entry);
	}

private static string GetCanonicalConfigSection(object instance)
{
	ConfigEntryBase obj = TryGetConfigEntryBaseFromSettingOption(instance);
	if (obj == null)
	{
		return null;
	}
	string modConfigSectionForEntry = GetModConfigSectionForEntry(obj);
	if (!string.IsNullOrWhiteSpace(modConfigSectionForEntry))
	{
		return modConfigSectionForEntry;
	}
	ConfigDefinition definition = obj.Definition;
	if (definition == null)
	{
		return null;
	}
	return definition.Section;
}

	private static string GetCanonicalConfigKey(object instance)
	{
		ConfigEntryBase obj = TryGetConfigEntryBaseFromSettingOption(instance);
		if (obj == null)
		{
			return null;
		}
		ConfigDefinition definition = obj.Definition;
		if (definition == null)
		{
			return null;
		}
		return definition.Key;
	}

	private List<string> GetCurrentLocalizedSections()
	{
		List<string> list = new List<string>();
		foreach (string currentCanonicalSection in GetCurrentCanonicalSections())
		{
			list.Add(GetLocalizedModConfigSectionDisplayName(currentCanonicalSection));
		}
		return list;
	}

	private List<string> GetCurrentCanonicalSections()
	{
		List<string> list = new List<string>();
		ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config);
		if (configEntriesSnapshot.Length == 0)
		{
			return list;
		}
		foreach (string item in GetDesiredModConfigSectionOrder())
		{
			if (configEntriesSnapshot.Any((ConfigEntryBase entry) => ShouldExposeOwnedConfigEntry(entry) && string.Equals(GetModConfigSectionForEntry(entry), item, StringComparison.Ordinal)))
			{
				list.Add(item);
			}
		}
		return list;
	}

	private static bool IsPrimaryZombieConfigEntry(ConfigEntryBase entry)
	{
		return (object)entry == ZombieBehaviorDifficulty || (object)entry == ZombieKnockbackForce || (object)entry == MaxZombies || (object)entry == ZombieSpawnCount || (object)entry == ZombieSpawnInterval || (object)entry == ZombieMaxLifetime;
	}

	private static bool IsDerivedZombieConfigEntry(ConfigEntryBase entry)
	{
		return (object)entry == ZombieSpawnEnabled || (object)entry == ZombieSpawnIntervalRandom || (object)entry == ZombieSpawnRadius || (object)entry == ZombieSpawnCountRandom || (object)entry == ZombieDestroyDistance || (object)entry == ZombieMoveSpeed || (object)entry == ZombieAggressiveness || (object)entry == DistanceBeforeWakeup || (object)entry == DistanceBeforeChase || (object)entry == ZombieSprintDistance || (object)entry == ChaseTimeBeforeSprint || (object)entry == ZombieLungeDistance || (object)entry == ZombieTargetSearchInterval || (object)entry == ZombieBiteRecoveryTime || (object)entry == ZombieSamePlayerBiteCooldown || (object)entry == ZombieLungeTime || (object)entry == ZombieLungeRecoveryTime || (object)entry == ZombieLookAngleBeforeWakeup;
	}

	private static bool IsZombieConfigEntry(ConfigEntryBase entry)
	{
		return IsPrimaryZombieConfigEntry(entry) || IsDerivedZombieConfigEntry(entry);
	}

	private static bool ShouldExposeOwnedConfigEntry(ConfigEntryBase entry)
	{
		if (entry == null)
		{
			return false;
		}
		if (IsZombieConfigEntry(entry))
		{
			return IsPrimaryZombieConfigEntry(entry);
		}
		return true;
	}

	private string GetLocalizedModConfigSectionDisplayName(string section)
	{
		string displayName;
		return TryGetLocalizedSectionDisplayName(section, IsChineseLanguage(), out displayName) ? displayName : GetLocalizedSectionName(section, IsChineseLanguage());
	}

	private static IEnumerable<string> GetDesiredModConfigSectionOrder()
	{
		yield return "Weapon";
		yield return "Zombie";
		yield return "Features";
	}

	private static int GetModConfigSectionSortIndex(string section)
	{
		return NormalizeSectionAlias(section) switch
		{
			"Weapon" => 0, 
			"Zombie" => 1, 
			"Features" => 2, 
			"Zombie Spawn" => 3, 
			_ => 99, 
		};
	}

	private static int GetModConfigEntrySortIndex(ConfigEntryBase entry)
	{
		if ((object)entry == ModEnabled)
		{
			return 0;
		}
		if ((object)entry == WeaponEnabled)
		{
			return 100;
		}
		if ((object)entry == WeaponSelection)
		{
			return 101;
		}
		if ((object)entry == SpawnWeaponKey)
		{
			return 102;
		}
		if ((object)entry == OpenConfigPanelKey)
		{
			return 103;
		}
		if ((object)entry == ConfigPanelTheme)
		{
			return 104;
		}
		if ((object)entry == FireInterval)
		{
			return 105;
		}
		if ((object)entry == FireVolume)
		{
			return 106;
		}
		if ((object)entry == AkSoundSelection)
		{
			return 107;
		}
		if ((object)entry == ZombieTimeReduction)
		{
			return 108;
		}
		if ((object)entry == WeaponModelScale)
		{
			return 190;
		}
		if ((object)entry == WeaponModelPitch)
		{
			return 191;
		}
		if ((object)entry == WeaponModelYaw)
		{
			return 192;
		}
		if ((object)entry == WeaponModelRoll)
		{
			return 193;
		}
		if ((object)entry == WeaponModelOffsetX)
		{
			return 194;
		}
		if ((object)entry == WeaponModelOffsetY)
		{
			return 195;
		}
		if ((object)entry == WeaponModelOffsetZ)
		{
			return 196;
		}
		if ((object)entry == ZombieBehaviorDifficulty)
		{
			return 199;
		}
		if ((object)entry == ZombieMoveSpeed)
		{
			return 200;
		}
		if ((object)entry == ZombieAggressiveness)
		{
			return 201;
		}
		if ((object)entry == ZombieKnockbackForce)
		{
			return 202;
		}
		if ((object)entry == DistanceBeforeWakeup)
		{
			return 203;
		}
		if ((object)entry == DistanceBeforeChase)
		{
			return 204;
		}
		if ((object)entry == ZombieSprintDistance)
		{
			return 205;
		}
		if ((object)entry == ChaseTimeBeforeSprint)
		{
			return 206;
		}
		if ((object)entry == ZombieLungeDistance)
		{
			return 207;
		}
		if ((object)entry == ZombieTargetSearchInterval)
		{
			return 208;
		}
		if ((object)entry == ZombieBiteRecoveryTime)
		{
			return 209;
		}
		if ((object)entry == ZombieSamePlayerBiteCooldown)
		{
			return 210;
		}
		if ((object)entry == ZombieSpawnEnabled)
		{
			return 211;
		}
		if ((object)entry == MaxZombies)
		{
			return 212;
		}
		if ((object)entry == ZombieSpawnCount)
		{
			return 213;
		}
		if ((object)entry == ZombieSpawnInterval)
		{
			return 214;
		}
		if ((object)entry == ZombieMaxLifetime)
		{
			return 215;
		}
		if ((object)entry == ZombieSpawnIntervalRandom)
		{
			return 216;
		}
		if ((object)entry == ZombieSpawnCountRandom)
		{
			return 217;
		}
		if ((object)entry == ZombieSpawnRadius)
		{
			return 218;
		}
		if ((object)entry == ZombieDestroyDistance)
		{
			return 219;
		}
		return int.MaxValue;
	}

	private static string GetModConfigSectionForEntry(ConfigEntryBase entry)
	{
		if (entry == null)
		{
			return string.Empty;
		}
		if ((object)entry == WeaponEnabled || (object)entry == WeaponSelection || (object)entry == WeaponModelScale || (object)entry == SpawnWeaponKey || (object)entry == FireInterval || (object)entry == FireVolume || (object)entry == AkSoundSelection || (object)entry == ZombieTimeReduction)
		{
			return "Weapon";
		}
		if ((object)entry == ZombieBehaviorDifficulty || (object)entry == ZombieMoveSpeed || (object)entry == ZombieAggressiveness || (object)entry == ZombieKnockbackForce || (object)entry == DistanceBeforeWakeup || (object)entry == DistanceBeforeChase || (object)entry == ZombieSprintDistance || (object)entry == ChaseTimeBeforeSprint || (object)entry == ZombieLungeDistance || (object)entry == ZombieTargetSearchInterval || (object)entry == ZombieBiteRecoveryTime || (object)entry == ZombieSamePlayerBiteCooldown || (object)entry == ZombieLungeTime || (object)entry == ZombieLungeRecoveryTime || (object)entry == ZombieLookAngleBeforeWakeup)
		{
			return "Zombie";
		}
		if ((object)entry == MaxZombies || (object)entry == ZombieSpawnInterval || (object)entry == ZombieMaxLifetime || (object)entry == ZombieDestroyDistance)
		{
			return "Zombie";
		}
		if ((object)entry == ZombieSpawnEnabled || (object)entry == ZombieSpawnIntervalRandom || (object)entry == ZombieSpawnCount || (object)entry == ZombieSpawnCountRandom || (object)entry == ZombieSpawnRadius)
		{
			return "Zombie";
		}
		if ((object)entry == ModEnabled || (object)entry == OpenConfigPanelKey || (object)entry == ConfigPanelTheme || (object)entry == WeaponModelPitch || (object)entry == WeaponModelYaw || (object)entry == WeaponModelRoll || (object)entry == WeaponModelOffsetX || (object)entry == WeaponModelOffsetY || (object)entry == WeaponModelOffsetZ)
		{
			return "Features";
		}
		ConfigDefinition definition = entry.Definition;
		return ((definition != null) ? definition.Section : null) ?? string.Empty;
	}

	private static string DescribeReflectionException(Exception ex)
	{
		if (ex == null)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder();
		int num = 0;
		while (ex != null && num < 6)
		{
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append(" --> ");
			}
			stringBuilder.Append(ex.GetType().Name);
			if (!string.IsNullOrWhiteSpace(ex.Message))
			{
				stringBuilder.Append(": ").Append(ex.Message);
			}
			ex = ex.InnerException;
			num++;
		}
		return stringBuilder.ToString();
	}

	private static bool TryCoerceInvokeArgument(Type parameterType, object value, out object coercedValue)
	{
		coercedValue = null;
		if (parameterType == null)
		{
			return false;
		}
		if (parameterType.IsByRef)
		{
			parameterType = parameterType.GetElementType();
			if (parameterType == null)
			{
				return false;
			}
		}
		Type type = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
		if (value == null)
		{
			if (!type.IsValueType || Nullable.GetUnderlyingType(parameterType) != null)
			{
				coercedValue = null;
				return true;
			}
			return false;
		}
		if (type.IsInstanceOfType(value))
		{
			coercedValue = value;
			return true;
		}
		try
		{
			if (type.IsEnum)
			{
				if (value is string value2)
				{
					coercedValue = Enum.Parse(type, value2, ignoreCase: true);
					return true;
				}
				coercedValue = Enum.ToObject(type, value);
				return true;
			}
			if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(type))
			{
				coercedValue = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	private static bool TryBuildCompatibleInvokeArguments(MethodInfo method, object[] suppliedArguments, out object[] invokeArguments)
	{
		invokeArguments = null;
		if (method == null)
		{
			return false;
		}
		suppliedArguments = suppliedArguments ?? Array.Empty<object>();
		ParameterInfo[] parameters = method.GetParameters();
		if (suppliedArguments.Length > parameters.Length)
		{
			return false;
		}
		object[] array = new object[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			ParameterInfo parameterInfo = parameters[i];
			if (i < suppliedArguments.Length)
			{
				if (!TryCoerceInvokeArgument(parameterInfo.ParameterType, suppliedArguments[i], out array[i]))
				{
					return false;
				}
				continue;
			}
			if (parameterInfo.HasDefaultValue)
			{
				array[i] = parameterInfo.DefaultValue;
				continue;
			}
			Type type = parameterInfo.ParameterType.IsByRef ? parameterInfo.ParameterType.GetElementType() : parameterInfo.ParameterType;
			if (type == typeof(PhotonMessageInfo))
			{
				array[i] = Activator.CreateInstance(type);
				continue;
			}
			return false;
		}
		invokeArguments = array;
		return true;
	}

	private static bool TryInvokeCompatibleInstanceMethod(object target, string methodName, out Exception failure, params object[] suppliedArguments)
	{
		failure = null;
		if (target == null || string.IsNullOrWhiteSpace(methodName))
		{
			return false;
		}
		MethodInfo[] array = ((object)target).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where((MethodInfo method) => string.Equals(method.Name, methodName, StringComparison.Ordinal)).OrderBy((MethodInfo method) => Math.Abs(method.GetParameters().Length - (suppliedArguments?.Length ?? 0))).ThenBy((MethodInfo method) => method.GetParameters().Length).ToArray();
		foreach (MethodInfo methodInfo in array)
		{
			if (!TryBuildCompatibleInvokeArguments(methodInfo, suppliedArguments, out var invokeArguments))
			{
				continue;
			}
			try
			{
				methodInfo.Invoke(target, invokeArguments);
				return true;
			}
			catch (Exception ex) when (ex is TargetParameterCountException || ex is ArgumentException)
			{
				failure = ex;
			}
			catch (TargetInvocationException ex2)
			{
				failure = ex2.InnerException ?? ex2;
				return false;
			}
			catch (Exception ex3)
			{
				failure = ex3;
				return false;
			}
		}
		return false;
	}

	private void ReinitializeConfig()
	{
		if (_isRefreshingLanguage)
		{
			return;
		}
		_isRefreshingLanguage = true;
		try
		{
			bool isChinese = (_lastLanguageSetting = IsChineseLanguage());
			ApplyLocalizedConfigMetadata(isChinese);
			RefreshLocalizedConfigFiles(isChinese);
			if (!DisableModConfigRuntimePatches && IsModConfigUiRuntimeSafe())
			{
				TryLocalizeVisibleModConfigUi();
			}
			UpdateWeaponLobbyNotice();
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[ShootZombies] ReinitializeConfig error: " + ex));
		}
		finally
		{
			_isRefreshingLanguage = false;
		}
	}
}
