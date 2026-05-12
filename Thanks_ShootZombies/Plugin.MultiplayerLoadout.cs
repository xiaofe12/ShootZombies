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
	private IEnumerator PeriodicPlayerScan()
	{
		yield return (object)new WaitForSeconds(PlayerScanInitialDelay);
		while (true)
		{
			if (HasGameplayAuthority() && IsGameplayScene(SceneManager.GetActiveScene()) && IsWeaponFeatureEnabled())
			{
				try
				{
					foreach (Character allCharacter in Character.AllCharacters)
					{
						if ((Object)allCharacter == (Object)null || allCharacter.isBot || allCharacter.isZombie)
						{
							continue;
						}
						int ownerActorNr = GetCharacterGrantTrackingId(allCharacter);
						int firstAidGrantTrackingId = GetFirstAidGrantTrackingId(allCharacter);
						if (ownerActorNr == int.MinValue && firstAidGrantTrackingId == int.MinValue)
						{
							continue;
						}
						if (!IsLoadoutGrantEligible(allCharacter))
						{
							continue;
						}
						if (ownerActorNr != int.MinValue)
						{
							RepairMissingWeaponGrantRecord(allCharacter, ownerActorNr);
						}
						bool flag4 = ownerActorNr != int.MinValue && HasWeaponGrantRecord(ownerActorNr);
						bool flag5 = firstAidGrantTrackingId != int.MinValue && HasFirstAidGrantRecord(firstAidGrantTrackingId);
						if (flag4 && flag5)
						{
							continue;
						}
						if (!flag5 && firstAidGrantTrackingId != int.MinValue)
						{
							TryGrantFirstAidWithAuthority(allCharacter, firstAidGrantTrackingId);
						}
						if (!flag4 && ownerActorNr != int.MinValue)
						{
							TryGrantWeaponWithAuthority(allCharacter, ownerActorNr);
						}
					}
				}
				catch (Exception)
				{
				}
			}
			yield return (object)new WaitForSeconds(PlayerScanInterval);
		}
	}

	private static bool TryGrantWeaponWithAuthority(Character c, int ownerActorNr)
	{
		if ((Object)c == (Object)null || ownerActorNr == int.MinValue)
		{
			return false;
		}
		if (CharacterAlreadyHasShootZombiesWeapon(c))
		{
			MarkWeaponGrantedForActor(ownerActorNr);
			return true;
		}
		if (ShouldGrantLoadoutDirectly(c))
		{
			if (!TryGiveItemTo(c))
			{
				return false;
			}
			MarkWeaponGrantedForActor(ownerActorNr);
			return true;
		}
		return TryRequestRemoteLoadoutGrant(c, RemoteLoadoutGrantWeapon);
	}

	private static bool TryGrantFirstAidWithAuthority(Character c, int ownerActorNr)
	{
		if ((Object)c == (Object)null || ownerActorNr == int.MinValue)
		{
			return false;
		}
		if (CharacterAlreadyHasFirstAid(c))
		{
			MarkFirstAidGrantedForActor(ownerActorNr);
			return true;
		}
		if (ShouldGrantLoadoutDirectly(c))
		{
			if (!TryGiveFirstAidTo(c))
			{
				return false;
			}
			MarkFirstAidGrantedForActor(ownerActorNr);
			return true;
		}
		return TryRequestRemoteLoadoutGrant(c, RemoteLoadoutGrantFirstAid);
	}

	private static bool ShouldGrantLoadoutDirectly(Character c)
	{
		if ((Object)c == (Object)null)
		{
			return false;
		}
		if (!HasOnlineRoomSession())
		{
			return true;
		}
		if ((Object)c == (Object)Character.localCharacter || (Object)c == (Object)_localCharacter)
		{
			return true;
		}
		PhotonView val = c.refs?.view;
		if ((Object)val == (Object)null)
		{
			val = ((Component)c).GetComponent<PhotonView>() ?? ((Component)c).GetComponentInParent<PhotonView>();
		}
		if ((Object)val == (Object)null)
		{
			return false;
		}
		return val.IsMine || val.OwnerActorNr <= 0;
	}

	private static bool IsLoadoutGrantEligible(Character c)
	{
		if ((Object)c == (Object)null || c.isBot || c.isZombie)
		{
			return false;
		}
		try
		{
			CharacterData data = c.data;
			if ((Object)data != (Object)null && (data.dead || data.passedOut || data.fullyPassedOut))
			{
				return false;
			}
			if (c.warping)
			{
				return false;
			}
			return true;
		}
		catch
		{
			return IsAlive(c);
		}
	}

	private static bool IsAlive(Character c)
	{
		if ((Object)c == (Object)null)
		{
			return false;
		}
		try
		{
			CharacterData data = c.data;
			if ((Object)data == (Object)null)
			{
				return true;
			}
			FieldInfo field = ((object)data).GetType().GetField("dead", BindingFlags.Instance | BindingFlags.Public);
			if (field != null)
			{
				return !(bool)field.GetValue(data);
			}
			return true;
		}
		catch
		{
			return true;
		}
	}

	private static bool TryGiveItemTo(Character c, bool ignoreFeatureGate = false)
	{
		try
		{
			if (!ignoreFeatureGate && !IsWeaponFeatureEnabled())
			{
				return false;
			}
			if ((Object)c == (Object)null || c.isBot || c.isZombie)
			{
				return false;
			}
			if (!IsLoadoutGrantEligible(c))
			{
				return false;
			}
			Player player = c.player;
			if ((Object)player == (Object)null || player.itemSlots == null)
			{
				return false;
			}
			if (CharacterAlreadyHasShootZombiesWeapon(c))
			{
				EnforceSingleWeaponForCharacter(c, equipIfHandsFree: true);
				TryEquipExistingWeaponSlot(c);
				MarkWeaponGrantedForCharacter(c);
				return true;
			}
			int preferredWeaponSlot = GetPreferredWeaponSlot(player);
			if (preferredWeaponSlot < 0)
			{
				return false;
			}
			if (!TryResolveBaseWeaponItem(out var item))
			{
				Log.LogError((object)"[ShootZombies] Failed to resolve base blowgun item from ItemDatabase");
				return false;
			}
			if (!TrySetItemIntoSlot(player, preferredWeaponSlot, item, 999))
			{
				return false;
			}
			TryProtectWeaponItemRuntime(item);
			TrySyncInventoryRpc(c, player, "weapon-slot");
			EnforceSingleWeaponForCharacter(c, equipIfHandsFree: true);
			TryEquipExistingWeaponSlot(c);
			if ((Object)(c.refs?.view) != (Object)null)
			{
				_ = c.refs.view.IsMine;
			}
			if (!c.isBot && !c.isZombie)
			{
				_localCharacter = c;
				_hasWeapon = true;
				_cachedHeldBlowgunItem = null;
				_lastHeldBlowgunSearchTime = -10f;
				_lastHeldWeaponSeenTime = Time.time;
			}
			MarkWeaponGrantedForCharacter(c);
			TryScheduleDelayedBlowgunReplacement();
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] TryGiveItemTo failed: " + ex.Message));
			return false;
		}
	}

	private static bool CharacterAlreadyHasShootZombiesWeapon(Character c)
	{
		if ((Object)c == (Object)null)
		{
			return false;
		}
		try
		{
			CharacterData data = c.data;
			Item val = ((data != null) ? data.currentItem : null);
			if ((Object)val != (Object)null && ItemPatch.IsBlowgunLike(val))
			{
				return true;
			}
		}
		catch
		{
		}
		Player player = c.player;
		if (PlayerInventoryContains(player, (Item itemFromSlot) => (Object)itemFromSlot != (Object)null && ItemPatch.IsBlowgunLike(itemFromSlot)))
		{
			return true;
		}
		return false;
	}

	private static Item GetHeldBlowgunItemForCharacter(Character c)
	{
		if ((Object)c == (Object)null || c.isBot || c.isZombie)
		{
			return null;
		}
		try
		{
			CharacterData data = c.data;
			Item val = ((data != null) ? data.currentItem : null);
			if (IsHeldBlowgunOwnedByCharacter(val, c))
			{
				return val;
			}
		}
		catch
		{
		}
		Player player = c.player;
		if ((Object)player != (Object)null && player.itemSlots != null)
		{
			ItemSlot[] itemSlots = player.itemSlots;
			for (int i = 0; i < itemSlots.Length; i++)
			{
				Item itemFromSlot = GetItemFromSlot(itemSlots[i]);
				if (IsHeldBlowgunOwnedByCharacter(itemFromSlot, c))
				{
					return itemFromSlot;
				}
			}
		}
		Item[] array = Object.FindObjectsByType<Item>((FindObjectsSortMode)0);
		foreach (Item val2 in array)
		{
			if (IsHeldBlowgunOwnedByCharacter(val2, c))
			{
				return val2;
			}
		}
		return null;
	}

	private static Item GetVisualSourceItemForCharacter(Character c)
	{
		Item heldBlowgunItemForCharacter = GetHeldBlowgunItemForCharacter(c);
		if ((Object)heldBlowgunItemForCharacter != (Object)null)
		{
			return heldBlowgunItemForCharacter;
		}
		if (TryResolveBaseWeaponItem(out var item))
		{
			return item;
		}
		return null;
	}

	private static bool CharacterAlreadyHasFirstAid(Character c)
	{
		if ((Object)c == (Object)null)
		{
			return false;
		}
		try
		{
			CharacterData data = c.data;
			Item val = ((data != null) ? data.currentItem : null);
			if (IsFirstAidLike(val))
			{
				return true;
			}
		}
		catch
		{
		}
		Player player = c.player;
		if (PlayerInventoryContains(player, IsFirstAidLike))
		{
			return true;
		}
		return false;
	}

	private static bool IsFirstAidLike(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		try
		{
			if (item.itemID == 29)
			{
				return true;
			}
			if ((Object)item.isSecretlyOtherItemPrefab != (Object)null && item.isSecretlyOtherItemPrefab.itemID == 29)
			{
				return true;
			}
			string text = (((item.UIData?.itemName ?? item.GetName()) ?? string.Empty) + "|" + (((Object)item).name ?? string.Empty)).ToLowerInvariant();
			return text.Contains("firstaid") || text.Contains("first aid") || text.Contains("medkit") || text.Contains("bandage") || text.Contains("急救") || text.Contains("医疗");
		}
		catch
		{
			return false;
		}
	}

	private static bool PlayerInventoryContains(Player player, Func<Item, bool> matcher)
	{
		if ((Object)player == (Object)null || matcher == null)
		{
			return false;
		}
		try
		{
			if (player.itemSlots != null)
			{
				ItemSlot[] itemSlots = player.itemSlots;
				for (int i = 0; i < itemSlots.Length; i++)
				{
					if (matcher(GetItemFromSlot(itemSlots[i])))
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		if (matcher(GetSpecialPlayerSlotItem(player, "backpackSlot")))
		{
			return true;
		}
		if (matcher(GetSpecialPlayerSlotItem(player, "tempFullSlot")))
		{
			return true;
		}
		return false;
	}

	private static Item GetSpecialPlayerSlotItem(Player player, string memberName)
	{
		if ((Object)player == (Object)null || string.IsNullOrWhiteSpace(memberName))
		{
			return null;
		}
		try
		{
			Type type = ((object)player).GetType();
			BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			object obj = type.GetField(memberName, bindingFlags)?.GetValue(player);
			if (obj == null)
			{
				obj = type.GetProperty(memberName, bindingFlags)?.GetValue(player);
			}
			return GetItemFromSlot(obj);
		}
		catch
		{
			return null;
		}
	}

	private static Item GetItemFromSlot(object slot)
	{
		try
		{
			if (slot == null)
			{
				return null;
			}
			if (slot is Item item)
			{
				return item;
			}
			object obj = ((object)slot).GetType().GetProperty("item")?.GetValue(slot);
			if ((Object)((obj is Item) ? obj : null) != (Object)null)
			{
				return (Item)((obj is Item) ? obj : null);
			}
			FieldInfo field = ((object)slot).GetType().GetField("item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				object value = field.GetValue(slot);
				return (Item)((value is Item) ? value : null);
			}
		}
		catch
		{
		}
		return null;
	}

	private static bool TryGiveCompassTo(Character c)
	{
		if (!IsFogModeEnabled() || (Object)c == (Object)null || c.isBot || c.isZombie)
		{
			return false;
		}
		try
		{
			Player player = c.player;
			if ((Object)player == (Object)null || player.itemSlots == null)
			{
				return false;
			}
			if (CharacterAlreadyHasNormalCompass(c))
			{
				return true;
			}
			if (!TryResolveNormalCompassItem(out var item))
			{
				return false;
			}
			int preferredSlot = GetPreferredCompassSlot(player);
			if (preferredSlot >= 0)
			{
				if (!TrySetItemIntoSlot(player, preferredSlot, item, 999))
				{
					return false;
				}
				if (EnableVerboseInfoLogs)
				{
					Log.LogInfo((object)("[ShootZombies] Compass granted to player: " + c.name));
				}
				return true;
			}
			return TrySpawnCompassAboveHead(c, item);
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] TryGiveCompassTo failed: " + ex.Message));
			return false;
		}
	}

	private static int GetPreferredCompassSlot(Player player)
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

	private static bool CharacterAlreadyHasNormalCompass(Character c)
	{
		if ((Object)c == (Object)null)
		{
			return false;
		}
		try
		{
			CharacterData data = c.data;
			Item val = ((data != null) ? data.currentItem : null);
			if (IsNormalCompassLike(val))
			{
				return true;
			}
		}
		catch
		{
		}
		Player player = c.player;
		if (PlayerInventoryContains(player, IsNormalCompassLike))
		{
			return true;
		}
		return false;
	}

	private static bool IsNormalCompassLike(Item item)
	{
		return ScoreNormalCompassItem(item) > 0;
	}

	private static bool TryResolveNormalCompassItem(out Item item)
	{
		item = null;
		int num = 0;
		try
		{
			Object[] array = Resources.FindObjectsOfTypeAll(typeof(Object));
			foreach (Object val in array)
			{
				if (val == (Object)null || ((object)val).GetType().Name != "ItemDatabase" || !(((object)val).GetType().GetField("itemLookup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(val) is IDictionary dictionary))
				{
					continue;
				}
				foreach (DictionaryEntry item2 in dictionary)
				{
					object value = item2.Value;
					Item val2 = (Item)((value is Item) ? value : null);
					int num2 = ScoreNormalCompassItem(val2);
					if (num2 > num)
					{
						num = num2;
						item = val2;
					}
				}
				break;
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] TryResolveNormalCompassItem database scan failed: " + ex.Message));
		}
		if ((Object)item == (Object)null)
		{
			try
			{
				Item[] array2 = Resources.FindObjectsOfTypeAll<Item>();
				foreach (Item val3 in array2)
				{
					int num3 = ScoreNormalCompassItem(val3);
					if (num3 > num)
					{
						num = num3;
						item = val3;
					}
				}
			}
			catch (Exception ex2)
			{
				Log.LogWarning((object)("[ShootZombies] TryResolveNormalCompassItem resource scan failed: " + ex2.Message));
			}
		}
		return (Object)item != (Object)null;
	}

	private static int ScoreNormalCompassItem(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return 0;
		}
		string text = (((Object)((Component)item).gameObject).name ?? string.Empty).ToLowerInvariant();
		string text2 = (item.GetName() ?? string.Empty).ToLowerInvariant();
		string text3 = (item.UIData?.itemName ?? string.Empty).ToLowerInvariant();
		string text4 = text + "|" + text2 + "|" + text3;
		if (!text4.Contains("compass") && !text4.Contains("指南针") && !text4.Contains("罗盘"))
		{
			return 0;
		}
		if (text4.Contains("warp") || text4.Contains("pirate") || text4.Contains("传送") || text4.Contains("海盗"))
		{
			return 0;
		}
		if (((Component)item).GetComponentInChildren(typeof(WarpCompassVFX), true) != null)
		{
			return 0;
		}
		int num = 1000;
		if (text4.Contains("normal") || text4.Contains("普通"))
		{
			num += 300;
		}
		if (TryIsNormalCompassPointer(item))
		{
			num += 800;
		}
		return num;
	}

	private static bool TryIsNormalCompassPointer(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		try
		{
			Type type = typeof(Item).Assembly.GetType("CompassPointer");
			if (type == null)
			{
				return false;
			}
			Component componentInChildren = ((Component)item).GetComponentInChildren(type, true);
			if (componentInChildren == null)
			{
				return false;
			}
			FieldInfo field = type.GetField("compassType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				return false;
			}
			object value = field.GetValue(componentInChildren);
			return value != null && Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0;
		}
		catch
		{
			return false;
		}
	}

	private static bool TrySpawnCompassAboveHead(Character c, Item item)
	{
		if ((Object)c == (Object)null || (Object)item == (Object)null)
		{
			return false;
		}
		string text = ((Object)((Component)item).gameObject).name;
		Vector3 position = c.Center + Vector3.up * 3f;
		Quaternion rotation = Quaternion.identity;
		try
		{
			Item val = null;
			if (PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode && !string.IsNullOrWhiteSpace(text))
			{
				GameObject val2 = PhotonNetwork.Instantiate("0_Items/" + text, position, rotation, 0, null);
				if ((Object)val2 != (Object)null)
				{
					val = val2.GetComponent<Item>();
				}
			}
			if ((Object)val == (Object)null)
			{
				GameObject gameObject = ((Component)item).gameObject;
				if ((Object)gameObject != (Object)null)
				{
					GameObject val3 = Object.Instantiate(gameObject, position, rotation);
					val = val3.GetComponent<Item>();
				}
			}
			if ((Object)val != (Object)null)
			{
				TrySetItemGroundState(val);
				if (EnableVerboseInfoLogs)
				{
					Log.LogInfo((object)("[ShootZombies] Compass spawned above head for player: " + c.name));
				}
				return true;
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] TrySpawnCompassAboveHead failed: " + ex.Message));
		}
		return false;
	}

	private static void TrySetItemGroundState(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return;
		}
		try
		{
			Type type = ((object)item).GetType();
			MethodInfo method = type.GetMethod("SetState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[2]
			{
				type.Assembly.GetType("ItemState") ?? typeof(int),
				typeof(Character)
			}, null);
			if (method != null)
			{
				Type type2 = type.Assembly.GetType("ItemState");
				object obj = (type2 != null && type2.IsEnum) ? Enum.ToObject(type2, 0) : 0;
				method.Invoke(item, new object[2] { obj, null });
				return;
			}
			FieldInfo field = type.GetField("<itemState>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetField("itemState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				object value = ((field.FieldType.IsEnum && field.FieldType.FullName == "ItemState") ? Enum.ToObject(field.FieldType, 0) : 0);
				field.SetValue(item, value);
			}
		}
		catch
		{
		}
	}

	private static bool TrySpawnCompassViaCharacterItems(Character c, string itemObjectName)
	{
		return TrySpawnItemInHandViaCharacterItems(c, itemObjectName, "compass");
	}

	private static bool TrySpawnWeaponViaCharacterItems(Character c, Item item)
	{
		if ((Object)c == (Object)null || (Object)item == (Object)null)
		{
			return false;
		}
		string text = ((Object)((Component)item).gameObject).name;
		if (!TrySpawnItemInHandViaCharacterItems(c, text, "weapon"))
		{
			return false;
		}
		return CharacterAlreadyHasShootZombiesWeapon(c);
	}

	private static bool TrySpawnItemInHandViaCharacterItems(Character c, string itemObjectName, string context)
	{
		if ((Object)c == (Object)null || string.IsNullOrWhiteSpace(itemObjectName))
		{
			return false;
		}
		try
		{
			object obj = c.refs?.items;
			MethodInfo method = obj?.GetType().GetMethod("SpawnItemInHand", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				return false;
			}
			method.Invoke(obj, new object[1] { itemObjectName });
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] TrySpawnItemInHandViaCharacterItems failed (" + context + "): " + ex.Message));
			return false;
		}
	}

	private static bool TryGiveFirstAidTo(Character c, bool ignoreFeatureGate = false)
	{
		try
		{
			if (!ignoreFeatureGate && !IsWeaponFeatureEnabled())
			{
				return false;
			}
			if ((Object)c == (Object)null || c.isBot || c.isZombie)
			{
				return false;
			}
			if (!IsLoadoutGrantEligible(c))
			{
				return false;
			}
			Player player = c.player;
			if ((Object)player == (Object)null || player.itemSlots == null)
			{
				return false;
			}
			if (CharacterAlreadyHasFirstAid(c))
			{
				MarkFirstAidGrantedForCharacter(c);
				return true;
			}
			int num = -1;
			for (int i = 0; i < player.itemSlots.Length; i++)
			{
				if (player.itemSlots[i].IsEmpty())
				{
					num = i;
					break;
				}
			}
			if (num == -1)
			{
				return false;
			}
			if (!TryResolveFirstAidItem(out var val))
			{
				return false;
			}
			if ((Object)val == (Object)null)
			{
				return false;
			}
			if (!TrySetItemIntoSlot(player, num, val, 1))
			{
				return false;
			}
			MarkFirstAidGrantedForCharacter(c);
			TrySyncInventoryRpc(c, player, "first-aid");
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] TryGiveFirstAidTo failed: " + ex.Message));
			return false;
		}
	}

	private static void TrySyncInventoryRpc(Character c, Player player, string context)
	{
		if ((Object)c == (Object)null || (Object)player == (Object)null || (Object)(c.refs?.view) == (Object)null)
		{
			return;
		}
		try
		{
			Type type = typeof(Item).Assembly.GetType("InventorySyncData");
			Type type2 = typeof(Item).Assembly.GetType("InventorySyncData+SlotData");
			if (type == null || type2 == null)
			{
				return;
			}
			List<object> list = new List<object>();
			ItemSlot[] itemSlots = player.itemSlots;
			foreach (ItemSlot val in itemSlots)
			{
				object obj = Activator.CreateInstance(type2);
				FieldInfo field = type2.GetField("item", BindingFlags.Instance | BindingFlags.Public);
				FieldInfo field2 = type2.GetField("data", BindingFlags.Instance | BindingFlags.Public);
				object value = ((object)val).GetType().GetProperty("item")?.GetValue(val);
				object value2 = ((object)val).GetType().GetProperty("data")?.GetValue(val);
				if (field != null)
				{
					field.SetValue(obj, value);
				}
				if (field2 != null)
				{
					field2.SetValue(obj, value2);
				}
				list.Add(obj);
			}
			Array array = Array.CreateInstance(type2, list.Count);
			for (int i = 0; i < list.Count; i++)
			{
				array.SetValue(list[i], i);
			}
			object value3 = ((object)player).GetType().GetField("backpackSlot")?.GetValue(player);
			object value4 = ((object)player).GetType().GetField("tempFullSlot")?.GetValue(player);
			object obj2 = Activator.CreateInstance(type);
			FieldInfo field3 = type.GetField("slots", BindingFlags.Instance | BindingFlags.Public);
			FieldInfo field4 = type.GetField("backpackSlot", BindingFlags.Instance | BindingFlags.Public);
			FieldInfo field5 = type.GetField("tempFullSlot", BindingFlags.Instance | BindingFlags.Public);
			if (field3 != null)
			{
				field3.SetValue(obj2, array);
			}
			if (field4 != null)
			{
				field4.SetValue(obj2, value3);
			}
			if (field5 != null)
			{
				field5.SetValue(obj2, value4);
			}
			MethodInfo methodInfo = typeof(Item).Assembly.GetType("Zorro.Core.Serizalization.IBinarySerializable")?.GetMethod("ToManagedArray")?.MakeGenericMethod(type);
			if (methodInfo == null)
			{
				return;
			}
			object obj3 = methodInfo.Invoke(null, new object[1] { obj2 });
			c.refs.view.RPC("SyncInventoryRPC", (RpcTarget)0, new object[2] { obj3, false });
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] Inventory sync failed (" + context + "): " + ex.Message));
		}
	}

	private static void TryScheduleDelayedBlowgunReplacement()
	{
		try
		{
			if (!((Object)(object)Instance == (Object)null))
			{
				RequestAkVisualRefresh(includeUiRefresh: true);
				((MonoBehaviour)Instance).StartCoroutine(DelayedReplaceBlowgunModel());
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] Delayed blowgun replacement failed: " + ex.Message));
		}
	}

	private static void MarkWeaponGrantedForCharacter(Character c)
	{
		int characterGrantTrackingId = GetCharacterGrantTrackingId(c);
		if (characterGrantTrackingId == int.MinValue)
		{
			return;
		}
		MarkWeaponGrantedForActor(characterGrantTrackingId);
	}

	private static void MarkWeaponGrantedForActor(int actorNr)
	{
		_receivedItem.Add(actorNr);
		_persistentReceivedItem.Add(actorNr);
		_pendingRemoteWeaponGrantActors.Remove(actorNr);
		_lastWeaponGrantTimeByActor[actorNr] = Time.unscaledTime;
		_weaponMissingSinceByActor.Remove(actorNr);
		_recentWeaponDropTimeByActor.Remove(actorNr);
	}

	internal static void NotifyWeaponDropped(Character c)
	{
		int characterGrantTrackingId = GetCharacterGrantTrackingId(c);
		if (characterGrantTrackingId == int.MinValue)
		{
			return;
		}
		if (ShouldSuppressWeaponRegrantAfterDrop(c))
		{
			_recentWeaponDropTimeByActor[characterGrantTrackingId] = Time.unscaledTime;
		}
		else
		{
			_recentWeaponDropTimeByActor.Remove(characterGrantTrackingId);
		}
		_weaponMissingSinceByActor.Remove(characterGrantTrackingId);
		_pendingRemoteWeaponGrantActors.Remove(characterGrantTrackingId);
	}

	private static void MarkFirstAidGrantedForCharacter(Character c)
	{
		int characterGrantTrackingId = GetFirstAidGrantTrackingId(c);
		if (characterGrantTrackingId == int.MinValue)
		{
			return;
		}
		MarkFirstAidGrantedForActor(characterGrantTrackingId);
	}

	private static void MarkFirstAidGrantedForActor(int actorNr)
	{
		_receivedFirstAid.Add(actorNr);
		_persistentReceivedFirstAid.Add(actorNr);
		_pendingRemoteFirstAidGrantActors.Remove(actorNr);
		_lastFirstAidGrantTimeByActor[actorNr] = Time.unscaledTime;
	}

	private static bool HasWeaponGrantRecord(int actorNr)
	{
		return _receivedItem.Contains(actorNr) || _persistentReceivedItem.Contains(actorNr);
	}

	private static bool HasRecentWeaponDrop(int actorNr)
	{
		if (!_recentWeaponDropTimeByActor.TryGetValue(actorNr, out var value))
		{
			return false;
		}
		if (Time.unscaledTime - value <= WeaponDropGrantSuppressDuration)
		{
			return true;
		}
		_recentWeaponDropTimeByActor.Remove(actorNr);
		return false;
	}

	private static bool ShouldSuppressWeaponRegrantAfterDrop(Character c)
	{
		if ((Object)c == (Object)null)
		{
			return true;
		}
		try
		{
			CharacterData data = c.data;
			if ((Object)data != (Object)null && (data.dead || data.passedOut || data.fullyPassedOut))
			{
				return false;
			}
			return true;
		}
		catch
		{
			return true;
		}
	}

	private static bool HasFirstAidGrantRecord(int actorNr)
	{
		return _receivedFirstAid.Contains(actorNr) || _persistentReceivedFirstAid.Contains(actorNr);
	}

	private static bool IsPendingRemoteWeaponGrant(int actorNr)
	{
		if (!_pendingRemoteWeaponGrantActors.Contains(actorNr))
		{
			return false;
		}
		if (_lastWeaponGrantTimeByActor.TryGetValue(actorNr, out var value) && Time.unscaledTime - value >= RemoteLoadoutGrantRetryTimeout)
		{
			_pendingRemoteWeaponGrantActors.Remove(actorNr);
			return false;
		}
		return true;
	}

	private static bool IsPendingRemoteFirstAidGrant(int actorNr)
	{
		if (!_pendingRemoteFirstAidGrantActors.Contains(actorNr))
		{
			return false;
		}
		if (_lastFirstAidGrantTimeByActor.TryGetValue(actorNr, out var value) && Time.unscaledTime - value >= RemoteLoadoutGrantRetryTimeout)
		{
			_pendingRemoteFirstAidGrantActors.Remove(actorNr);
			return false;
		}
		return true;
	}

	private static void ClearWeaponGrantRecordForActor(int actorNr)
	{
		_receivedItem.Remove(actorNr);
		_persistentReceivedItem.Remove(actorNr);
		_pendingRemoteWeaponGrantActors.Remove(actorNr);
		_lastWeaponGrantTimeByActor.Remove(actorNr);
		_weaponMissingSinceByActor.Remove(actorNr);
	}

	private static void RepairMissingWeaponGrantRecord(Character c, int actorNr)
	{
		if (actorNr == int.MinValue || (Object)c == (Object)null || c.isBot || c.isZombie)
		{
			return;
		}
		if (!HasWeaponGrantRecord(actorNr) || IsPendingRemoteWeaponGrant(actorNr))
		{
			_weaponMissingSinceByActor.Remove(actorNr);
			return;
		}
		if (HasRecentWeaponDrop(actorNr))
		{
			_weaponMissingSinceByActor.Remove(actorNr);
			return;
		}
		if (CharacterAlreadyHasShootZombiesWeapon(c))
		{
			_weaponMissingSinceByActor.Remove(actorNr);
			return;
		}
		if (_lastWeaponGrantTimeByActor.TryGetValue(actorNr, out var value) && Time.unscaledTime - value < LoadoutRepairGracePeriod)
		{
			return;
		}
		if (!_weaponMissingSinceByActor.TryGetValue(actorNr, out var value2))
		{
			_weaponMissingSinceByActor[actorNr] = Time.unscaledTime;
			return;
		}
		if (Time.unscaledTime - value2 >= LoadoutMissingConfirmDuration)
		{
			ClearWeaponGrantRecordForActor(actorNr);
		}
	}

	private static bool TryRequestRemoteLoadoutGrant(Character c, int grantType)
	{
		if (!HasOnlineRoomSession() || !PhotonNetwork.IsMasterClient || (Object)c == (Object)null || c.isBot || c.isZombie || (Object)(c.refs?.view) == (Object)null || c.refs.view.IsMine || c.refs.view.OwnerActorNr <= 0)
		{
			return false;
		}
		int ownerActorNr = c.refs.view.OwnerActorNr;
		if ((grantType == RemoteLoadoutGrantWeapon && IsPendingRemoteWeaponGrant(ownerActorNr)) || (grantType == RemoteLoadoutGrantFirstAid && IsPendingRemoteFirstAidGrant(ownerActorNr)))
		{
			return true;
		}
		object[] customEventContent = new object[1] { grantType };
		RaiseEventOptions raiseEventOptions = new RaiseEventOptions
		{
			TargetActors = new int[1] { ownerActorNr }
		};
		bool flag = PhotonNetwork.RaiseEvent(RemoteLoadoutGrantEventCode, customEventContent, raiseEventOptions, SendOptions.SendReliable);
		if (flag)
		{
			if (grantType == RemoteLoadoutGrantWeapon)
			{
				_pendingRemoteWeaponGrantActors.Add(ownerActorNr);
				_lastWeaponGrantTimeByActor[ownerActorNr] = Time.unscaledTime;
			}
			else if (grantType == RemoteLoadoutGrantFirstAid)
			{
				_pendingRemoteFirstAidGrantActors.Add(ownerActorNr);
				_lastFirstAidGrantTimeByActor[ownerActorNr] = Time.unscaledTime;
			}
		}
		return flag;
	}

	private static bool TrySendRemoteLoadoutGrantAck(int grantType)
	{
		if (!HasOnlineRoomSession() || PhotonNetwork.OfflineMode || PhotonNetwork.IsMasterClient || PhotonNetwork.MasterClient == null || PhotonNetwork.MasterClient.ActorNumber <= 0)
		{
			return false;
		}
		object[] customEventContent = new object[1] { grantType };
		RaiseEventOptions raiseEventOptions = new RaiseEventOptions
		{
			TargetActors = new int[1] { PhotonNetwork.MasterClient.ActorNumber }
		};
		return PhotonNetwork.RaiseEvent(RemoteLoadoutGrantAckEventCode, customEventContent, raiseEventOptions, SendOptions.SendReliable);
	}

	private void HandleRemoteLoadoutGrantEvent(object[] payload)
	{
		if (payload == null || payload.Length == 0 || !(payload[0] is int num))
		{
			return;
		}
		switch (num)
		{
		case RemoteLoadoutGrantWeapon:
			_pendingRemoteWeaponGrant = true;
			break;
		case RemoteLoadoutGrantFirstAid:
			_pendingRemoteFirstAidGrant = true;
			break;
		default:
			return;
		}
		ProcessPendingRemoteGrantRequests(force: true);
	}

	private static void HandleRemoteLoadoutGrantAckEvent(EventData photonEvent)
	{
		if (!PhotonNetwork.IsMasterClient || photonEvent == null || photonEvent.Sender <= 0)
		{
			return;
		}
		object[] array = photonEvent.CustomData as object[];
		if (array == null || array.Length == 0 || !(array[0] is int num))
		{
			return;
		}
		switch (num)
		{
		case RemoteLoadoutGrantWeapon:
			MarkWeaponGrantedForActor(photonEvent.Sender);
			break;
		case RemoteLoadoutGrantFirstAid:
			MarkFirstAidGrantedForActor(photonEvent.Sender);
			break;
		}
	}

	private void HandleRemoteShotEffectsEvent(object[] payload)
	{
		if (payload == null || payload.Length < 6)
		{
			return;
		}
		if (!(payload[0] is int shooterViewId) || !(payload[1] is Vector3 muzzlePosition) || !(payload[2] is Vector3 muzzleDirection) || !(payload[3] is Vector3 soundPosition) || !(payload[4] is bool hasImpact) || !(payload[5] is Vector3 impactPosition))
		{
			return;
		}
		string selection = ((payload.Length >= 7 && payload[6] is string text) ? text : null);
		Character character = ResolveCharacterFromPhotonViewId(shooterViewId);
		PhotonView val = ((Object)character != (Object)null) ? (character.refs?.view) : null;
		if ((Object)val == (Object)null)
		{
			val = PhotonView.Find(shooterViewId);
		}
		if ((Object)character != (Object)null && (Object)character == (Object)_localCharacter)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(selection) && (Object)val != (Object)null && val.OwnerActorNr > 0)
		{
			selection = GetAkSoundSelectionForActor(val.OwnerActorNr);
		}
		if (soundPosition == Vector3.zero)
		{
			soundPosition = muzzlePosition;
		}
		if (muzzleDirection.sqrMagnitude <= 1E-06f)
		{
			muzzleDirection = (((Object)character != (Object)null && (Object)(character.refs?.view) != (Object)null) ? ((Component)character.refs.view).transform.forward : Vector3.forward);
		}
		CreateMuzzleFlash(muzzlePosition, muzzleDirection);
		PlayRemoteGunshotSound(soundPosition, selection);
		if (hasImpact)
		{
			SpawnRemoteShotImpactVisual(character, impactPosition);
		}
	}

	private static void SpawnRemoteShotImpactVisual(Character shooter, Vector3 endpoint)
	{
		try
		{
			Item visualSourceItemForCharacter = GetVisualSourceItemForCharacter(shooter);
			if ((Object)visualSourceItemForCharacter != (Object)null)
			{
				SpawnDartImpactVisualOnly(visualSourceItemForCharacter, endpoint);
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] SpawnRemoteShotImpactVisual failed: " + ex.Message));
		}
	}

	private void ProcessPendingRemoteGrantRequests(bool force = false)
	{
		if ((!_pendingRemoteWeaponGrant && !_pendingRemoteFirstAidGrant) || PhotonNetwork.IsMasterClient || !HasOnlineRoomSession())
		{
			return;
		}
		if (!force && Time.unscaledTime - _lastPendingRemoteGrantAttemptTime < PendingRemoteGrantRetryInterval)
		{
			return;
		}
		_lastPendingRemoteGrantAttemptTime = Time.unscaledTime;
		Character val = Character.localCharacter ?? _localCharacter;
		if ((Object)val == (Object)null || val.isZombie || val.isBot)
		{
			return;
		}
		if (!IsLoadoutGrantEligible(val))
		{
			return;
		}
		Player player = val.player;
		if ((Object)player == (Object)null || player.itemSlots == null)
		{
			return;
		}
		if (_pendingRemoteWeaponGrant && (CharacterAlreadyHasShootZombiesWeapon(val) || TryGiveItemTo(val, ignoreFeatureGate: true)))
		{
			_pendingRemoteWeaponGrant = false;
			TrySendRemoteLoadoutGrantAck(RemoteLoadoutGrantWeapon);
		}
		if (_pendingRemoteFirstAidGrant && (CharacterAlreadyHasFirstAid(val) || TryGiveFirstAidTo(val, ignoreFeatureGate: true)))
		{
			_pendingRemoteFirstAidGrant = false;
			TrySendRemoteLoadoutGrantAck(RemoteLoadoutGrantFirstAid);
		}
	}

	private static int GetCharacterGrantTrackingId(Character c)
	{
		if ((Object)c == (Object)null || c.isBot || c.isZombie)
		{
			return int.MinValue;
		}
		PhotonView val = c.refs?.view;
		if ((Object)val != (Object)null && val.OwnerActorNr > 0)
		{
			return val.OwnerActorNr;
		}
		Player player = c.player;
		if (HasOnlineRoomSession())
		{
			if ((Object)c == (Object)Character.localCharacter || (Object)c == (Object)_localCharacter || ((Object)val != (Object)null && val.IsMine))
			{
				RoomPlayer localPlayer = PhotonNetwork.LocalPlayer;
				if (localPlayer != null && localPlayer.ActorNumber > 0)
				{
					return localPlayer.ActorNumber;
				}
			}
			return int.MinValue;
		}
		if ((Object)player != (Object)null)
		{
			return -Mathf.Abs(((Object)player).GetInstanceID());
		}
		if ((Object)c == (Object)Character.localCharacter || (Object)c == (Object)_localCharacter)
		{
			return -Mathf.Abs(((Object)c).GetInstanceID());
		}
		return int.MinValue;
	}

	private static int GetFirstAidGrantTrackingId(Character c)
	{
		if ((Object)c == (Object)null || c.isBot || c.isZombie)
		{
			return int.MinValue;
		}
		if (HasOnlineRoomSession())
		{
			int characterGrantTrackingId = GetCharacterGrantTrackingId(c);
			if (characterGrantTrackingId != int.MinValue)
			{
				return characterGrantTrackingId;
			}
			if ((Object)c == (Object)Character.localCharacter || (Object)c == (Object)_localCharacter)
			{
				RoomPlayer localPlayer = PhotonNetwork.LocalPlayer;
				if (localPlayer != null && localPlayer.ActorNumber > 0)
				{
					return localPlayer.ActorNumber;
				}
			}
			return int.MinValue;
		}
		if ((Object)c == (Object)Character.localCharacter || (Object)c == (Object)_localCharacter)
		{
			return -1;
		}
		Player player = c.player;
		if ((Object)player != (Object)null)
		{
			return -Mathf.Abs(((Object)player).GetInstanceID());
		}
		return int.MinValue;
	}

	private static void ApplyWarmBabyEffect(Character c)
	{
		try
		{
			if ((Object)c == (Object)null || c.isZombie || c.isBot)
			{
				return;
			}
			CharacterAfflictions val = c.refs?.afflictions;
			if (!((Object)val != (Object)null))
			{
				return;
			}
			Type type = Type.GetType("Peak.Afflictions.Affliction_AdjustColdOverTime");
			if (type != null)
			{
				object obj = Activator.CreateInstance(type);
				FieldInfo field = type.GetField("statusPerSecond", BindingFlags.Instance | BindingFlags.Public);
				FieldInfo field2 = type.GetField("totalTime", BindingFlags.Instance | BindingFlags.Public);
				FieldInfo field3 = type.GetField("character", BindingFlags.Instance | BindingFlags.Public);
				if (field != null)
				{
					field.SetValue(obj, -10f);
				}
				if (field2 != null)
				{
					field2.SetValue(obj, 10f);
				}
				if (field3 != null)
				{
					field3.SetValue(obj, c);
				}
				MethodInfo method = ((object)val).GetType().GetMethod("AddAffliction", new Type[2]
				{
					type,
					typeof(bool)
				});
				if (method != null)
				{
					method.Invoke(val, new object[2] { obj, false });
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private void Start()
	{
		ZombieBehaviorDifficultyPreset currentZombieBehaviorDifficultyPresetRuntime = GetCurrentZombieBehaviorDifficultyPresetRuntime();
		_cachedIsChineseLanguage = IsChineseLanguage();
		_lastLanguageSetting = _cachedIsChineseLanguage;
		_lastLanguagePollTime = Time.unscaledTime;
		_lastZombieMoveSpeed = GetZombieMoveSpeedMultiplierRuntime();
		_lastZombieKnockbackForce = GetZombieKnockbackForceRuntime();
		_lastZombieSpawnEnabled = IsZombieSpawnFeatureEnabled();
		_lastZombieSpawnInterval = ZombieSpawnInterval.Value;
		_lastZombieSpawnIntervalRandom = GetDerivedZombieSpawnIntervalRandomRangeRuntime();
		_lastZombieSpawnCount = GetDerivedZombieWaveMaxCount();
		_lastZombieSpawnCountRandom = Mathf.Max(GetDerivedZombieWaveMaxCount() - GetDerivedZombieWaveMinCount(), 0);
		_lastZombieSpawnRadius = GetDerivedZombieSpawnRadiusRuntime();
		_lastMaxZombies = MaxZombies.Value;
		_lastZombieMaxLifetime = ZombieMaxLifetime.Value;
		_lastDistanceBeforeWakeup = currentZombieBehaviorDifficultyPresetRuntime.DistanceBeforeWakeup;
		_lastZombieSprintDistance = currentZombieBehaviorDifficultyPresetRuntime.SprintDistance;
		_lastChaseTimeBeforeSprint = currentZombieBehaviorDifficultyPresetRuntime.ChaseTimeBeforeSprint;
		_lastZombieLungeDistance = currentZombieBehaviorDifficultyPresetRuntime.LungeDistance;
		_lastZombieBiteRecoveryTime = currentZombieBehaviorDifficultyPresetRuntime.BiteRecoveryTime;
		_lastZombieLungeTime = currentZombieBehaviorDifficultyPresetRuntime.LungeTime;
		_lastZombieLungeRecoveryTime = currentZombieBehaviorDifficultyPresetRuntime.LungeRecoveryTime;
		_lastZombieLookAngleBeforeWakeup = currentZombieBehaviorDifficultyPresetRuntime.LookAngleBeforeWakeup;
		_lastZombieBehaviorDifficulty = NormalizeZombieBehaviorDifficultySelection(ZombieBehaviorDifficulty.Value);
		_lastModEnabled = ModEnabled?.Value ?? true;
		_lastWeaponEnabled = WeaponEnabled?.Value ?? true;
		EnsureVanillaBlowgunFunctionalityWhenWeaponDisabled();
		_lastWeaponSelection = GetCurrentWeaponSelection();
		ApplySelectedWeaponAssets();
		_lastWeaponModelPitch = GetWeaponModelPitch();
		_lastWeaponModelYaw = GetWeaponModelYaw();
		_lastWeaponModelRoll = GetWeaponModelRoll();
		_lastWeaponModelScale = GetWeaponModelScale();
		_lastWeaponModelOffsetX = GetWeaponModelOffsetX();
		_lastWeaponModelOffsetY = GetWeaponModelOffsetY();
		_lastWeaponModelOffsetZ = GetWeaponModelOffsetZ();
		_pendingZombieSpeedRefresh = true;
		_lastFireInterval = FireInterval.Value;
		_lastFireVolume = FireVolume.Value;
		_lastZombieTimeReduction = ZombieTimeReduction.Value;
		ResetScheduledUpdateState();
		LogNightTestHotkeyHint();
		if (IsModConfigUiRuntimeSafe())
		{
			((MonoBehaviour)this).StartCoroutine(RefreshLocalizedUiAfterStartup());
		}
		_lobbyConfigPanel = new LobbyConfigPanel(this);
	}

	private IEnumerator RefreshLocalizedUiAfterStartup()
	{
		yield return null;
		yield return (object)new WaitForSeconds(0.2f);
		for (int i = 0; i < 120; i++)
		{
			if (TryResolveGameLanguage(out var _, out var _, out var _))
			{
				break;
			}
			yield return null;
		}
		if (!IsModConfigUiRuntimeSafe())
		{
			yield break;
		}
		ReinitializeConfig();
	}

	private void Update()
	{
		UpdateMuzzleFlashPool();
		UpdateRemoteGunshotAudioPool();
		UpdateDartImpactVfxPool();
		if (!_resourcesLoaded)
		{
			LoadResources();
		}
			bool flag = GetCachedChineseLanguageSetting();
			if (flag != _lastLanguageSetting)
			{
				_lastLanguageSetting = flag;
				if (IsModConfigUiRuntimeSafe())
				{
					ReinitializeConfig();
				}
				_lobbyConfigPanel?.NotifyLanguageChanged(flag);
			}
		RunAlwaysScheduledTasks();
		EnsureVanillaBlowgunFunctionalityWhenWeaponDisabled();
		_lobbyConfigPanel?.Tick();
		if (!IsModFeatureEnabled())
		{
			_hasWeapon = false;
			return;
		}
		if (IsRuntimeVisualRefreshBlocked())
		{
			CleanupLocalWeaponVisual();
			_hasWeapon = false;
			return;
		}
		RunFeatureScheduledTasks();
		Item heldBlowgunItem = GetHeldBlowgunItem();
		CheckSpawnWeaponKey();
		CheckNightTestHotkey();
		_hasWeapon = IsWeaponFeatureEnabled() && (Object)heldBlowgunItem != (Object)null;
		if ((Object)heldBlowgunItem != (Object)null)
		{
			_lastHeldWeaponSeenTime = Time.time;
			if (IsWeaponFeatureEnabled())
			{
				SyncBlowgunChargeState(heldBlowgunItem);
			}
		}
		else
		{
			_lastChargeSyncItemId = int.MinValue;
		}
		bool flag2 = _pendingAkVisualRefresh;
		if (flag2)
		{
			try
			{
				ItemPatch.EnsureAkVisualOnAllItems(_pendingAkVisualForceRefresh);
				if (_pendingAkUiRefresh)
				{
					ItemUIDataPatch.ForceRefreshVisibleUi();
				}
			}
			catch (Exception ex)
			{
				Log.LogWarning((object)("[ShootZombies] Periodic AK visual refresh failed: " + ex.Message));
			}
			_pendingAkVisualRefresh = false;
			_pendingAkVisualForceRefresh = false;
			_pendingAkUiRefresh = false;
		}
		if (IsWeaponFeatureEnabled() && EnableLocalWeaponVisualFollower)
		{
			EnsureLocalWeaponVisual(heldBlowgunItem);
		}
		else if (IsWeaponFeatureEnabled())
		{
			CleanupLocalWeaponVisual();
		}
		if (IsWeaponFeatureEnabled() && _hasWeapon && (Object)heldBlowgunItem != (Object)null)
		{
			EnsureLocalHeldDebugSphere(heldBlowgunItem);
		}
		else
		{
			CleanupLocalHeldDebugSphere();
		}
		if (IsWeaponFeatureEnabled() && _hasWeapon && (Object)_localCharacter != (Object)null && CanProcessLocalWeaponFireInput(_localCharacter) && Input.GetMouseButton(0) && Time.time - _lastFireTime >= FireInterval.Value)
		{
			TryFire();
		}
	}

	private void ResetScheduledUpdateState()
	{
		float time = Time.time;
		float unscaledTime = Time.unscaledTime;
		_nextRoomConfigSyncTime = unscaledTime;
		_nextConfigCheckTime = unscaledTime;
		_nextLobbyNoticeUpdateTime = unscaledTime;
		_nextZombieTimerUpdateTime = time;
		_nextZombieSpeedUpdateCheckTime = time;
		_nextZombieHealthBarRefreshTime = time;
		_nextLocalCharacterRefreshTime = unscaledTime;
		_nextPendingRemoteGrantProcessTime = unscaledTime;
		_nextWeaponOwnershipUpdateTime = unscaledTime;
	}

	private void RunAlwaysScheduledTasks()
	{
		float unscaledTime = Time.unscaledTime;
		if (ShouldRunScheduledTask(ref _nextRoomConfigSyncTime, unscaledTime, ScheduledRoomConfigSyncInterval))
		{
			UpdateRoomConfigSynchronization();
		}
		if (ShouldRunScheduledTask(ref _nextConfigCheckTime, unscaledTime, ScheduledConfigCheckInterval))
		{
			CheckConfigChanges();
		}
		if (ShouldRunScheduledTask(ref _nextLobbyNoticeUpdateTime, unscaledTime, ScheduledLobbyNoticeInterval))
		{
			UpdateWeaponLobbyNotice();
		}
	}

	private void RunFeatureScheduledTasks()
	{
		float time = Time.time;
		float unscaledTime = Time.unscaledTime;
		if (ShouldRunScheduledTask(ref _nextZombieTimerUpdateTime, time, ScheduledZombieTimerInterval))
		{
			ZombieSpawner.UpdateZombieTimers();
		}
		if (ShouldRunScheduledTask(ref _nextZombieSpeedUpdateCheckTime, time, ScheduledZombieSpeedInterval))
		{
			UpdateZombieSpeed();
		}
		if (ShouldRunScheduledTask(ref _nextZombieHealthBarRefreshTime, time, ScheduledZombieHealthBarInterval))
		{
			ZombieHealthBar.RefreshAll();
		}
		if (ShouldRunScheduledTask(ref _nextLocalCharacterRefreshTime, unscaledTime, ScheduledLocalCharacterRefreshInterval))
		{
			RefreshLocalCharacterReference();
		}
		if (ShouldRunScheduledTask(ref _nextPendingRemoteGrantProcessTime, unscaledTime, ScheduledPendingRemoteGrantInterval))
		{
			ProcessPendingRemoteGrantRequests();
		}
		if (ShouldRunScheduledTask(ref _nextWeaponOwnershipUpdateTime, unscaledTime, ScheduledWeaponOwnershipInterval))
		{
			NormalizeLocalWeaponOwnership();
		}
	}

	private static bool ShouldRunScheduledTask(ref float nextRunTime, float now, float interval)
	{
		if (now < nextRunTime)
		{
			return false;
		}
		nextRunTime = now + interval;
		return true;
	}

	private void UpdateRoomConfigSynchronization()
	{
		if (!HasOnlineRoomSession())
		{
			RestoreLocalRoomConfigBackupIfNeeded();
			ResetRoomConfigSynchronizationState();
			return;
		}
		string text = PhotonNetwork.CurrentRoom.Name ?? string.Empty;
		bool flag = false;
		if (!string.Equals(_activeRoomConfigRoomName, text, StringComparison.Ordinal))
		{
			RestoreLocalRoomConfigBackupIfNeeded();
			ResetRoomConfigSynchronizationState(clearBackup: false);
			_activeRoomConfigRoomName = text;
			flag = true;
		}
		RefreshPlayerWeaponSelectionCache();
		RefreshPlayerAkSoundSelectionCache();
		PublishLocalWeaponSelectionToPlayerProperties(flag || string.IsNullOrEmpty(_lastPublishedLocalWeaponSelection));
		PublishLocalAkSoundSelectionToPlayerProperties(flag || string.IsNullOrEmpty(_lastPublishedLocalAkSoundSelection));
		if (PhotonNetwork.IsMasterClient)
		{
			if (!_wasRoomMasterClient)
			{
				RestoreLocalRoomConfigBackupIfNeeded();
				MarkRoomConfigDirty(forceImmediate: true);
			}
			if ((_roomConfigDirty || string.IsNullOrEmpty(_lastPublishedRoomConfigPayload)) && Time.unscaledTime - _lastRoomConfigPublishTime >= 0.35f)
			{
				PublishHostConfigToRoom(string.IsNullOrEmpty(_lastPublishedRoomConfigPayload));
			}
		}
		else
		{
			CaptureLocalRoomConfigBackupIfNeeded();
			if (flag || string.IsNullOrEmpty(_lastAppliedRoomConfigPayload))
			{
				ApplyHostRoomConfigIfNeeded();
			}
			if (Time.unscaledTime - _lastRoomConfigPollTime >= 1f)
			{
				_lastRoomConfigPollTime = Time.unscaledTime;
				ApplyHostRoomConfigIfNeeded();
			}
		}
		_wasRoomMasterClient = PhotonNetwork.IsMasterClient;
	}

	private void ResetRoomConfigSynchronizationState(bool clearBackup = true)
	{
		_activeRoomConfigRoomName = string.Empty;
		_lastPublishedRoomConfigPayload = string.Empty;
		_lastAppliedRoomConfigPayload = string.Empty;
		_lastPublishedLocalWeaponSelection = string.Empty;
		_lastPublishedLocalAkSoundSelection = string.Empty;
		_lastRoomConfigPublishTime = -10f;
		_lastRoomConfigPollTime = -10f;
		_wasRoomMasterClient = false;
		_roomConfigDirty = true;
		_pendingRemoteWeaponGrant = false;
		_pendingRemoteFirstAidGrant = false;
		_lastPendingRemoteGrantAttemptTime = -10f;
		_playerWeaponSelectionsByActor.Clear();
		_playerAkSoundSelectionsByActor.Clear();
		if (clearBackup)
		{
			_localRoomConfigBackupPayload = string.Empty;
			_hasLocalRoomConfigBackup = false;
		}
	}

	private void MarkRoomConfigDirty(bool forceImmediate = false)
	{
		_roomConfigDirty = true;
		if (forceImmediate)
		{
			_lastRoomConfigPublishTime = -10f;
		}
	}

	private void MarkRoomConfigDirtyIfPublishedPayloadChanged()
	{
		if (_applyingRoomConfigPayload || !HasOnlineRoomSession() || !PhotonNetwork.IsMasterClient)
		{
			return;
		}
		string text = BuildRoomConfigPayload();
		if (!string.IsNullOrWhiteSpace(text) && !string.Equals(text, _lastPublishedRoomConfigPayload, StringComparison.Ordinal))
		{
			MarkRoomConfigDirty(forceImmediate: true);
		}
	}

	private void CaptureLocalRoomConfigBackupIfNeeded()
	{
		if (!_hasLocalRoomConfigBackup)
		{
			_localRoomConfigBackupPayload = BuildRoomConfigPayload();
			_hasLocalRoomConfigBackup = !string.IsNullOrWhiteSpace(_localRoomConfigBackupPayload);
		}
	}

	private void RestoreLocalRoomConfigBackupIfNeeded()
	{
		if (_hasLocalRoomConfigBackup && !string.IsNullOrWhiteSpace(_localRoomConfigBackupPayload))
		{
			ApplyRoomConfigPayload(_localRoomConfigBackupPayload);
			_localRoomConfigBackupPayload = string.Empty;
			_hasLocalRoomConfigBackup = false;
		}
	}

	private void PublishHostConfigToRoom(bool force = false)
	{
		if (!_applyingRoomConfigPayload && PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom != null)
		{
			string text = BuildRoomConfigPayload();
			if (string.IsNullOrWhiteSpace(text))
			{
				_roomConfigDirty = false;
				_lastRoomConfigPublishTime = Time.unscaledTime;
				return;
			}
			if (!force && string.Equals(text, _lastPublishedRoomConfigPayload, StringComparison.Ordinal))
			{
				_roomConfigDirty = false;
				_lastRoomConfigPublishTime = Time.unscaledTime;
				return;
			}
			PhotonHashtable val = new PhotonHashtable();
			((Dictionary<object, object>)val).Add((object)RoomConfigPropertyKey, (object)text);
			PhotonHashtable val2 = val;
			PhotonNetwork.CurrentRoom.SetCustomProperties(val2, (PhotonHashtable)null, (WebFlags)null);
			_lastPublishedRoomConfigPayload = text;
			_lastAppliedRoomConfigPayload = text;
			_roomConfigDirty = false;
			_lastRoomConfigPublishTime = Time.unscaledTime;
		}
	}

	private void ApplyHostRoomConfigIfNeeded()
	{
		if (!_applyingRoomConfigPayload && !PhotonNetwork.IsMasterClient && TryGetRoomConfigPayload(out var payload) && (!string.Equals(payload, _lastAppliedRoomConfigPayload, StringComparison.Ordinal) || !IsCurrentRoomConfigPayload(payload)))
		{
			ApplyRoomConfigPayload(payload);
			_lastAppliedRoomConfigPayload = payload;
		}
	}

	private static bool TryGetRoomConfigPayload(out string payload)
	{
		payload = null;
		Room currentRoom = PhotonNetwork.CurrentRoom;
		PhotonHashtable customProperties = ((currentRoom != null) ? ((RoomInfo)currentRoom).CustomProperties : null);
		if (customProperties == null)
		{
			return false;
		}
		if (!((Dictionary<object, object>)customProperties).TryGetValue((object)RoomConfigPropertyKey, out var value))
		{
			return false;
		}
		payload = value as string;
		return !string.IsNullOrWhiteSpace(payload);
	}

	private bool IsCurrentRoomConfigPayload(string payload)
	{
		if (!string.IsNullOrWhiteSpace(payload))
		{
			return string.Equals(payload, BuildRoomConfigPayload(), StringComparison.Ordinal);
		}
		return false;
	}

	private void HandleRoomPropertiesUpdated(PhotonHashtable propertiesThatChanged)
	{
		if (propertiesThatChanged != null && ((Dictionary<object, object>)(object)propertiesThatChanged).ContainsKey((object)RoomConfigPropertyKey) && HasOnlineRoomSession() && !PhotonNetwork.IsMasterClient)
		{
			_lastRoomConfigPollTime = Time.unscaledTime;
			ApplyHostRoomConfigIfNeeded();
		}
	}

	private static void RefreshPlayerWeaponSelectionCache()
	{
		if (!PhotonNetwork.InRoom)
		{
			_playerWeaponSelectionsByActor.Clear();
			return;
		}
		foreach (RoomPlayer player in PhotonNetwork.PlayerList ?? Array.Empty<RoomPlayer>())
		{
			if (player != null && player.ActorNumber > 0)
			{
				_playerWeaponSelectionsByActor[player.ActorNumber] = GetWeaponSelectionForPlayer(player);
			}
		}
	}

	private static void RefreshPlayerAkSoundSelectionCache()
	{
		if (!PhotonNetwork.InRoom)
		{
			_playerAkSoundSelectionsByActor.Clear();
			return;
		}
		foreach (RoomPlayer player in PhotonNetwork.PlayerList ?? Array.Empty<RoomPlayer>())
		{
			if (player != null && player.ActorNumber > 0)
			{
				_playerAkSoundSelectionsByActor[player.ActorNumber] = GetAkSoundSelectionForPlayer(player);
			}
		}
	}

	private void PublishLocalWeaponSelectionToPlayerProperties(bool force = false)
	{
		string currentWeaponSelection = GetCurrentWeaponSelection();
		if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber > 0)
		{
			_playerWeaponSelectionsByActor[PhotonNetwork.LocalPlayer.ActorNumber] = currentWeaponSelection;
		}
		if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
		{
			_lastPublishedLocalWeaponSelection = string.Empty;
			return;
		}
		if (PhotonNetwork.OfflineMode)
		{
			_lastPublishedLocalWeaponSelection = currentWeaponSelection;
			return;
		}
		if (!force && string.Equals(currentWeaponSelection, _lastPublishedLocalWeaponSelection, StringComparison.Ordinal))
		{
			return;
		}
		PhotonHashtable val = new PhotonHashtable();
		((Dictionary<object, object>)val).Add((object)PlayerWeaponSelectionPropertyKey, (object)currentWeaponSelection);
		PhotonNetwork.LocalPlayer.SetCustomProperties(val);
		if (PhotonNetwork.LocalPlayer.ActorNumber > 0)
		{
			_playerWeaponSelectionsByActor[PhotonNetwork.LocalPlayer.ActorNumber] = currentWeaponSelection;
		}
		_lastPublishedLocalWeaponSelection = currentWeaponSelection;
	}

	private void PublishLocalAkSoundSelectionToPlayerProperties(bool force = false)
	{
		string currentAkSoundSelection = GetCurrentAkSoundSelection();
		if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber > 0)
		{
			_playerAkSoundSelectionsByActor[PhotonNetwork.LocalPlayer.ActorNumber] = currentAkSoundSelection;
		}
		if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
		{
			_lastPublishedLocalAkSoundSelection = string.Empty;
			return;
		}
		if (PhotonNetwork.OfflineMode)
		{
			_lastPublishedLocalAkSoundSelection = currentAkSoundSelection;
			return;
		}
		if (!force && string.Equals(currentAkSoundSelection, _lastPublishedLocalAkSoundSelection, StringComparison.Ordinal))
		{
			return;
		}
		PhotonHashtable val = new PhotonHashtable();
		((Dictionary<object, object>)val).Add((object)PlayerAkSoundSelectionPropertyKey, (object)currentAkSoundSelection);
		PhotonNetwork.LocalPlayer.SetCustomProperties(val);
		if (PhotonNetwork.LocalPlayer.ActorNumber > 0)
		{
			_playerAkSoundSelectionsByActor[PhotonNetwork.LocalPlayer.ActorNumber] = currentAkSoundSelection;
		}
		_lastPublishedLocalAkSoundSelection = currentAkSoundSelection;
	}

	private void HandlePlayerPropertiesUpdated(RoomPlayer targetPlayer, PhotonHashtable changedProps)
	{
		if (targetPlayer == null || changedProps == null)
		{
			return;
		}
		bool flag = ((Dictionary<object, object>)(object)changedProps).ContainsKey((object)PlayerWeaponSelectionPropertyKey);
		bool flag2 = ((Dictionary<object, object>)(object)changedProps).ContainsKey((object)PlayerAkSoundSelectionPropertyKey);
		if (!flag && !flag2)
		{
			return;
		}
		if (targetPlayer.ActorNumber > 0)
		{
			if (flag)
			{
				_playerWeaponSelectionsByActor[targetPlayer.ActorNumber] = GetWeaponSelectionForPlayer(targetPlayer);
			}
			if (flag2)
			{
				_playerAkSoundSelectionsByActor[targetPlayer.ActorNumber] = GetAkSoundSelectionForPlayer(targetPlayer);
			}
		}
		if (flag)
		{
			RequestAkVisualRefresh(includeUiRefresh: true, forceRefresh: true);
		}
	}

	private void HandleRoomPlayerListChanged()
	{
		TrimStalePendingRemoteGrantActors();
		RefreshPlayerWeaponSelectionCache();
		RefreshPlayerAkSoundSelectionCache();
		if (PhotonNetwork.IsMasterClient)
		{
			MarkRoomConfigDirty(forceImmediate: true);
		}
		else if (PhotonNetwork.InRoom)
		{
			ApplyHostRoomConfigIfNeeded();
		}
	}

	private void HandleMasterClientChanged()
	{
		_lastRoomConfigPollTime = -10f;
		TrimStalePendingRemoteGrantActors();
		if (PhotonNetwork.IsMasterClient)
		{
			RestoreLocalRoomConfigBackupIfNeeded();
			ZombieSpawner.RebuildLiveZombieRegistryFromScene();
			MarkRoomConfigDirty(forceImmediate: true);
		}
		else
		{
			CaptureLocalRoomConfigBackupIfNeeded();
			ApplyHostRoomConfigIfNeeded();
		}
	}

	private void TrimStalePendingRemoteGrantActors()
	{
		if (!PhotonNetwork.InRoom)
		{
			_pendingRemoteWeaponGrantActors.Clear();
			_pendingRemoteFirstAidGrantActors.Clear();
			return;
		}
		HashSet<int> hashSet = new HashSet<int>((from player in PhotonNetwork.PlayerList
			where player != null && player.ActorNumber > 0
			select player.ActorNumber));
		_pendingRemoteWeaponGrantActors.RemoveWhere((int actorNr) => !hashSet.Contains(actorNr));
		_pendingRemoteFirstAidGrantActors.RemoveWhere((int actorNr) => !hashSet.Contains(actorNr));
		PruneGrantTrackingDictionary(_lastWeaponGrantTimeByActor, hashSet);
		PruneGrantTrackingDictionary(_lastFirstAidGrantTimeByActor, hashSet);
		PruneGrantTrackingDictionary(_weaponMissingSinceByActor, hashSet);
		PruneGrantTrackingDictionary(_recentWeaponDropTimeByActor, hashSet);
	}

	private static void PruneGrantTrackingDictionary(Dictionary<int, float> tracking, HashSet<int> liveActors)
	{
		if (tracking == null || tracking.Count == 0)
		{
			return;
		}
		List<int> list = null;
		foreach (int key in tracking.Keys)
		{
			if (!liveActors.Contains(key))
			{
				(list ?? (list = new List<int>())).Add(key);
			}
		}
		if (list == null)
		{
			return;
		}
		foreach (int item in list)
		{
			tracking.Remove(item);
		}
	}

	private void HandlePhotonEvent(EventData photonEvent)
	{
		if (photonEvent == null || photonEvent.CustomData == null)
		{
			return;
		}
		switch (photonEvent.Code)
		{
		case ZombieHitEventCode:
			HandleZombieHitEvent(photonEvent.CustomData as object[]);
			break;
		case RemoteLoadoutGrantEventCode:
			HandleRemoteLoadoutGrantEvent(photonEvent.CustomData as object[]);
			break;
		case RemoteLoadoutGrantAckEventCode:
			HandleRemoteLoadoutGrantAckEvent(photonEvent);
			break;
		case RemoteShotEffectsEventCode:
			HandleRemoteShotEffectsEvent(photonEvent.CustomData as object[]);
			break;
		case PlayerShotStatusEventCode:
			HandlePlayerShotStatusEvent(photonEvent.CustomData as object[]);
			break;
		case ZombieHealthEventCode:
			HandleZombieHealthEvent(photonEvent.CustomData as object[]);
			break;
		}
	}

	private void HandleZombieHitEvent(object[] payload)
	{
		if (!HasGameplayAuthority() || !TryGetZombieFromEventPayload(payload, out var zombie, out var origin))
		{
			return;
		}
		ApplyZombieHitLocal(zombie, origin);
	}

	private static void HandleZombieHealthEvent(object[] payload)
	{
		if (payload == null || payload.Length < 2 || !(payload[0] is int viewId))
		{
			return;
		}
		float health01;
		if (payload[1] is float value)
		{
			health01 = value;
		}
		else if (payload[1] is double doubleValue)
		{
			health01 = (float)doubleValue;
		}
		else
		{
			return;
		}
		Character zombie = ResolveCharacterFromPhotonViewId(viewId);
		if ((Object)zombie == (Object)null || (!zombie.isZombie && (Object)((Component)zombie).GetComponentInParent<MushroomZombie>() == (Object)null && (Object)((Component)zombie).GetComponentInChildren<MushroomZombie>(true) == (Object)null))
		{
			return;
		}
		ZombieHealthBar.SetHealth(zombie, health01);
	}

	private static bool TryGetZombieFromEventPayload(object[] payload, out Character zombie, out Vector3? origin)
	{
		zombie = null;
		origin = null;
		if (payload == null || payload.Length < 3 || !(payload[0] is int viewId) || !(payload[1] is bool hasOrigin))
		{
			return false;
		}
		zombie = ResolveCharacterFromPhotonViewId(viewId);
		if ((Object)zombie == (Object)null || (!zombie.isZombie && !zombie.isBot))
		{
			zombie = null;
			return false;
		}
		if (hasOrigin && payload[2] is Vector3 value && IsFiniteVector(value))
		{
			origin = value;
		}
		return true;
	}

	private void HandlePlayerShotStatusEvent(object[] payload)
	{
		if (!TryGetPlayerCharacterFromEventPayload(payload, out var playerCharacter))
		{
			return;
		}
		ApplyPlayerShotStatusIfOwnedLocal(playerCharacter);
	}

	private static bool TryGetPlayerCharacterFromEventPayload(object[] payload, out Character playerCharacter)
	{
		playerCharacter = null;
		if (payload == null || payload.Length < 1 || !(payload[0] is int viewId))
		{
			return false;
		}
		playerCharacter = ResolveCharacterFromPhotonViewId(viewId);
		if ((Object)playerCharacter == (Object)null || playerCharacter.isZombie || playerCharacter.isBot)
		{
			playerCharacter = null;
			return false;
		}
		return true;
	}

	private static Character ResolveCharacterFromPhotonViewId(int viewId)
	{
		if (viewId <= 0)
		{
			return null;
		}
		PhotonView val = PhotonView.Find(viewId);
		if ((Object)val == (Object)null)
		{
			return null;
		}
		return ((Component)val).GetComponent<Character>() ?? ((Component)val).GetComponentInParent<Character>() ?? ((Component)val).GetComponentInChildren<Character>(true);
	}

	private static string BuildRoomConfigPayload()
	{
		StringBuilder stringBuilder = new StringBuilder(512);
		AppendRoomConfigValue(stringBuilder, "Version", 2);
		AppendRoomConfigValue(stringBuilder, "ModEnabled", ModEnabled?.Value ?? true);
		AppendRoomConfigValue(stringBuilder, "WeaponEnabled", WeaponEnabled?.Value ?? true);
		AppendRoomConfigValue(stringBuilder, "FireInterval", FireInterval?.Value ?? 0.4f);
		AppendRoomConfigValue(stringBuilder, "ZombieTimeReduction", ZombieTimeReduction?.Value ?? DefaultZombieTimeReductionSeconds);
		AppendRoomConfigValue(stringBuilder, "ZombieSpawnInterval", ZombieSpawnInterval?.Value ?? 15f);
		AppendRoomConfigValue(stringBuilder, "MaxZombies", MaxZombies?.Value ?? DefaultMaxZombieCount);
		AppendRoomConfigValue(stringBuilder, "ZombieSpawnCount", ZombieSpawnCount?.Value ?? DefaultZombieSpawnCount);
		AppendRoomConfigValue(stringBuilder, "ZombieMaxLifetime", ZombieMaxLifetime?.Value ?? 120f);
		AppendRoomConfigValue(stringBuilder, "ZombieBehaviorDifficulty", ZombieBehaviorDifficulty?.Value ?? DefaultZombieBehaviorDifficulty);
		AppendRoomConfigValue(stringBuilder, "ZombieKnockbackForce", GetZombieKnockbackForceRuntime());
		return stringBuilder.ToString();
	}

	private static void AppendRoomConfigValue(StringBuilder builder, string key, bool value)
	{
		AppendRoomConfigValue(builder, key, value ? "1" : "0");
	}

	private static void AppendRoomConfigValue(StringBuilder builder, string key, int value)
	{
		AppendRoomConfigValue(builder, key, value.ToString(CultureInfo.InvariantCulture));
	}

	private static void AppendRoomConfigValue(StringBuilder builder, string key, float value)
	{
		AppendRoomConfigValue(builder, key, value.ToString("R", CultureInfo.InvariantCulture));
	}

	private static void AppendRoomConfigValue(StringBuilder builder, string key, string value)
	{
		if (builder.Length > 0)
		{
			builder.Append('|');
		}
		builder.Append(key).Append('=').Append(Uri.EscapeDataString(value ?? string.Empty));
	}

	private void ApplyRoomConfigPayload(string payload)
	{
		if (string.IsNullOrWhiteSpace(payload))
		{
			return;
		}
		Dictionary<string, string> dictionary = ParseRoomConfigPayload(payload);
		if (dictionary.Count == 0)
		{
			return;
		}
		_applyingRoomConfigPayload = true;
		try
		{
			ApplyBoolRoomConfig(dictionary, "ModEnabled", ModEnabled);
			ApplyBoolRoomConfig(dictionary, "WeaponEnabled", WeaponEnabled);
			ApplyFloatRoomConfig(dictionary, "FireInterval", FireInterval);
			ApplyFloatRoomConfig(dictionary, "ZombieTimeReduction", ZombieTimeReduction);
			ApplyFloatRoomConfig(dictionary, "ZombieSpawnInterval", ZombieSpawnInterval);
			ApplyIntRoomConfig(dictionary, "MaxZombies", MaxZombies);
			ApplyIntRoomConfig(dictionary, "ZombieSpawnCount", ZombieSpawnCount);
			ApplyFloatRoomConfig(dictionary, "ZombieMaxLifetime", ZombieMaxLifetime);
			ApplyStringRoomConfig(dictionary, "ZombieBehaviorDifficulty", ZombieBehaviorDifficulty, NormalizeZombieBehaviorDifficultySelection);
			ApplyFloatRoomConfig(dictionary, "ZombieKnockbackForce", ZombieKnockbackForce);
			ApplyZombieBehaviorDifficultyPreset();
			ApplySimplifiedZombieDerivedValues();
			if (ZombieBehaviorDifficulty != null)
			{
				_lastZombieBehaviorDifficulty = NormalizeZombieBehaviorDifficultySelection(ZombieBehaviorDifficulty.Value);
			}
			NormalizeConfigRanges();
		}
		finally
		{
			_applyingRoomConfigPayload = false;
		}
	}
}
