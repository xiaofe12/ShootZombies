using System;
using HarmonyLib;
using UnityEngine;
using Zorro.Core;

namespace ShootZombies;

public partial class Plugin
{
	internal static bool TryProtectWeaponItemRuntime(Item item)
	{
		if (!IsWeaponFeatureEnabled() || (Object)item == (Object)null || !ItemPatch.IsBlowgunLike(item))
		{
			return false;
		}
		ApplyWeaponItemInteractionRestrictions(item.UIData);
		if ((Object)item.isSecretlyOtherItemPrefab != (Object)null)
		{
			ApplyWeaponItemInteractionRestrictions(item.isSecretlyOtherItemPrefab.UIData);
		}
		return true;
	}

	private static void ApplyWeaponItemInteractionRestrictions(ItemUIData uiData)
	{
		if (uiData == null)
		{
			return;
		}
		uiData.canDrop = false;
		uiData.canThrow = false;
		uiData.canBackpack = false;
	}

	internal static bool IsProtectedWeaponItem(Item item)
	{
		return IsWeaponFeatureEnabled() && (Object)item != (Object)null && ItemPatch.IsBlowgunLike(item);
	}

	internal static bool IsProtectedWeaponSlot(ItemSlot slot)
	{
		if (slot == null || slot.IsEmpty())
		{
			return false;
		}
		return IsProtectedWeaponItem(slot.prefab);
	}

	internal static bool CharacterHasShootZombiesWeaponRuntime(Character c)
	{
		return CharacterAlreadyHasShootZombiesWeapon(c);
	}

	private static void NormalizeLocalWeaponOwnership()
	{
		if (!IsWeaponFeatureEnabled())
		{
			return;
		}
		Character c = Character.localCharacter ?? _localCharacter;
		EnforceSingleWeaponForCharacter(c, equipIfHandsFree: true);
	}

	internal static bool EnforceSingleWeaponForCharacter(Character c, bool equipIfHandsFree)
	{
		if (!IsWeaponFeatureEnabled() || (Object)c == (Object)null || c.isBot || c.isZombie)
		{
			return false;
		}
		Player player = c.player;
		if ((Object)player == (Object)null || player.itemSlots == null)
		{
			return false;
		}
		Item currentItem = null;
		try
		{
			if ((Object)c.data != (Object)null)
			{
				currentItem = c.data.currentItem;
			}
			TryProtectWeaponItemRuntime(currentItem);
		}
		catch
		{
		}

		ItemSlot keptSlot = null;
		bool changed = false;
		bool currentItemIsWeapon = IsProtectedWeaponItem(currentItem);
		Optionable<byte> selectedSlot = GetSelectedSlot(c);
		if (selectedSlot.IsSome)
		{
			ItemSlot selectedItemSlot = player.GetItemSlot(selectedSlot.Value);
			if (IsProtectedWeaponSlot(selectedItemSlot))
			{
				keptSlot = selectedItemSlot;
			}
		}

		for (byte i = 0; i < player.itemSlots.Length; i++)
		{
			ItemSlot slot = player.itemSlots[i];
			if (!IsProtectedWeaponSlot(slot))
			{
				continue;
			}
			TryProtectWeaponItemRuntime(slot.prefab);
			if (keptSlot == null)
			{
				keptSlot = slot;
			}
			else if (slot != keptSlot)
			{
				slot.EmptyOut();
				changed = true;
			}
		}

		ItemSlot tempFullSlot = player.tempFullSlot;
		if (IsProtectedWeaponSlot(tempFullSlot))
		{
			TryProtectWeaponItemRuntime(tempFullSlot.prefab);
			if (keptSlot == null)
			{
				keptSlot = tempFullSlot;
			}
			else if (tempFullSlot != keptSlot)
			{
				tempFullSlot.EmptyOut();
				changed = true;
			}
		}

		if (changed)
		{
			TrySyncInventoryRpc(c, player, "single-weapon");
		}
		return keptSlot != null || currentItemIsWeapon;
	}

	internal static bool TryEquipExistingWeaponSlot(Character c)
	{
		if (!IsWeaponFeatureEnabled() || (Object)c == (Object)null || c.isBot || c.isZombie)
		{
			return false;
		}
		Player player = c.player;
		if ((Object)player == (Object)null || player.itemSlots == null)
		{
			return false;
		}
		Optionable<byte> selectedSlot = GetSelectedSlot(c);
		if (selectedSlot.IsSome && IsProtectedWeaponSlot(player.GetItemSlot(selectedSlot.Value)))
		{
			return TryEquipWeaponSlot(c, selectedSlot.Value);
		}
		for (byte i = 0; i < player.itemSlots.Length; i++)
		{
			if (IsProtectedWeaponSlot(player.itemSlots[i]))
			{
				return TryEquipWeaponSlot(c, i);
			}
		}
		if (IsProtectedWeaponSlot(player.tempFullSlot))
		{
			return TryEquipWeaponSlot(c, player.tempFullSlot.itemSlotID);
		}
		return false;
	}

	private static Optionable<byte> GetSelectedSlot(Character c)
	{
		try
		{
			if ((Object)c != (Object)null && c.refs != null && (Object)c.refs.items != (Object)null)
			{
				return c.refs.items.currentSelectedSlot;
			}
		}
		catch
		{
		}
		return Optionable<byte>.None;
	}

	private static bool TryEquipWeaponSlot(Character c, byte slotId)
	{
		try
		{
			if ((Object)c == (Object)null || c.refs == null || (Object)c.refs.items == (Object)null)
			{
				return false;
			}
			Player player = c.player;
			if ((Object)player == (Object)null)
			{
				return false;
			}
			ItemSlot slot = player.GetItemSlot(slotId);
			if (!IsProtectedWeaponSlot(slot))
			{
				return false;
			}
			Item currentItem = null;
			if ((Object)c.data != (Object)null)
			{
				currentItem = c.data.currentItem;
			}
			if (c.refs.items.currentSelectedSlot.IsSome && c.refs.items.currentSelectedSlot.Value == slotId && IsProtectedWeaponItem(currentItem))
			{
				return true;
			}
			c.refs.items.EquipSlot(Optionable<byte>.Some(slotId));
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] TryEquipWeaponSlot failed: " + ex.Message));
			return false;
		}
	}
}

[HarmonyPatch(typeof(CharacterItems), "DropItemRpc")]
public static class WeaponDropRpcGuardPatch
{
	public static bool Prefix(CharacterItems __instance, byte slotID)
	{
		try
		{
			if (!Plugin.IsWeaponFeatureEnabled())
			{
				return true;
			}
			Character character = ((Component)__instance).GetComponent<Character>();
			Item currentItem = null;
			Player player = null;
			if ((Object)character != (Object)null)
			{
				player = character.player;
				if ((Object)character.data != (Object)null)
				{
					currentItem = character.data.currentItem;
				}
			}
			ItemSlot slot = ((Object)player != (Object)null) ? player.GetItemSlot(slotID) : null;
			if (Plugin.IsProtectedWeaponItem(currentItem) || Plugin.IsProtectedWeaponSlot(slot))
			{
				Plugin.TryProtectWeaponItemRuntime(currentItem);
				Plugin.TryProtectWeaponItemRuntime(slot != null ? slot.prefab : null);
				Plugin.EnforceSingleWeaponForCharacter(character, equipIfHandsFree: true);
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] WeaponDropRpcGuardPatch failed: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(CharacterItems), "DropItemFromSlotRPC")]
public static class WeaponDropFromSlotRpcGuardPatch
{
	public static bool Prefix(CharacterItems __instance, byte slotID)
	{
		try
		{
			if (!Plugin.IsWeaponFeatureEnabled())
			{
				return true;
			}
			Character character = ((Component)__instance).GetComponent<Character>();
			Player player = ((Object)character != (Object)null) ? character.player : null;
			ItemSlot slot = ((Object)player != (Object)null) ? player.GetItemSlot(slotID) : null;
			if (Plugin.IsProtectedWeaponSlot(slot))
			{
				Plugin.TryProtectWeaponItemRuntime(slot.prefab);
				Plugin.EnforceSingleWeaponForCharacter(character, equipIfHandsFree: true);
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] WeaponDropFromSlotRpcGuardPatch failed: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(CharacterItems), "DestroyHeldItemRpc")]
public static class WeaponDestroyHeldGuardPatch
{
	public static bool Prefix(CharacterItems __instance)
	{
		try
		{
			if (!Plugin.IsWeaponFeatureEnabled())
			{
				return true;
			}
			Character character = ((Component)__instance).GetComponent<Character>();
			Item currentItem = null;
			if ((Object)character != (Object)null && (Object)character.data != (Object)null)
			{
				currentItem = character.data.currentItem;
			}
			if (Plugin.IsProtectedWeaponItem(currentItem))
			{
				Plugin.TryProtectWeaponItemRuntime(currentItem);
				Plugin.EnforceSingleWeaponForCharacter(character, equipIfHandsFree: true);
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] WeaponDestroyHeldGuardPatch failed: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(Player), "EmptySlot")]
public static class WeaponEmptySlotGuardPatch
{
	public static bool Prefix(Player __instance, Optionable<byte> slot)
	{
		try
		{
			if (!Plugin.IsWeaponFeatureEnabled() || slot.IsNone)
			{
				return true;
			}
			ItemSlot itemSlot = __instance.GetItemSlot(slot.Value);
			if (Plugin.IsProtectedWeaponSlot(itemSlot))
			{
				Plugin.TryProtectWeaponItemRuntime(itemSlot.prefab);
				Plugin.EnforceSingleWeaponForCharacter(__instance.character, equipIfHandsFree: true);
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] WeaponEmptySlotGuardPatch failed: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(Player), "RPCRemoveItemFromSlot")]
public static class WeaponRpcRemoveItemFromSlotGuardPatch
{
	public static bool Prefix(Player __instance, byte slotID)
	{
		try
		{
			if (!Plugin.IsWeaponFeatureEnabled())
			{
				return true;
			}
			ItemSlot itemSlot = __instance.GetItemSlot(slotID);
			if (Plugin.IsProtectedWeaponSlot(itemSlot))
			{
				Plugin.TryProtectWeaponItemRuntime(itemSlot.prefab);
				Plugin.EnforceSingleWeaponForCharacter(__instance.character, equipIfHandsFree: true);
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] WeaponRpcRemoveItemFromSlotGuardPatch failed: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(Item), "Interact")]
public static class WeaponPickupDuplicateGuardPatch
{
	public static bool Prefix(Item __instance, Character interactor)
	{
		try
		{
			if (!Plugin.IsProtectedWeaponItem(__instance) || (Object)interactor == (Object)null)
			{
				return true;
			}
			if (Plugin.CharacterHasShootZombiesWeaponRuntime(interactor))
			{
				Plugin.EnforceSingleWeaponForCharacter(interactor, equipIfHandsFree: true);
				return false;
			}
			Plugin.TryProtectWeaponItemRuntime(__instance);
		}
		catch (Exception ex)
		{
			Plugin.Log.LogWarning((object)("[ShootZombies] WeaponPickupDuplicateGuardPatch failed: " + ex.Message));
		}
		return true;
	}
}
