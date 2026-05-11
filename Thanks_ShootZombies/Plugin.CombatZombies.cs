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
	private void CheckNightTestHotkey()
	{
		if (!IsModFeatureEnabled() || !IsGameplayScene())
		{
			ResetNightTestHotkeyState();
			return;
		}
		if (!Input.GetKey(NightTestKey))
		{
			ResetNightTestHotkeyState();
			return;
		}
		if (_nightTestHoldStartTime < 0f)
		{
			_nightTestHoldStartTime = Time.unscaledTime;
		}
		if (_nightTestTriggeredThisHold || Time.unscaledTime - _nightTestHoldStartTime < NightTestHoldDuration)
		{
			return;
		}
		_nightTestTriggeredThisHold = true;
		SwitchToNightForTesting();
	}

	private void ResetNightTestHotkeyState()
	{
		_nightTestHoldStartTime = -1f;
		_nightTestTriggeredThisHold = false;
	}

	private void SwitchToNightForTesting()
	{
		if (!HasGameplayAuthority())
		{
			if (EnableVerboseInfoLogs)
			{
				bool cachedChineseLanguageSetting3 = GetCachedChineseLanguageSetting();
				Log?.LogInfo((object)(cachedChineseLanguageSetting3 ? "[ShootZombies] 夜晚测试按键仅支持单机或房主使用。" : "[ShootZombies] The night test hotkey only works offline or for the room host."));
			}
			return;
		}
		DayNightManager instance = DayNightManager.instance;
		if ((Object)(object)instance == (Object)null)
		{
			bool cachedChineseLanguageSetting2 = GetCachedChineseLanguageSetting();
			Log?.LogWarning((object)(cachedChineseLanguageSetting2 ? "[ShootZombies] 未找到 DayNightManager，无法切换到夜晚。" : "[ShootZombies] DayNightManager was not found, so night switching could not be applied."));
			return;
		}
		instance.setTimeOfDay(NightTestTargetTimeOfDay);
		instance.UpdateCycle();
		PhotonView component = ((Component)instance).GetComponent<PhotonView>();
		if ((Object)(object)component != (Object)null && HasOnlineRoomSession() && PhotonNetwork.IsMasterClient)
		{
			component.RPC("RPCA_SyncTime", (RpcTarget)0, new object[1] { NightTestTargetTimeOfDay });
		}
		if (EnableVerboseInfoLogs)
		{
			bool cachedChineseLanguageSetting = GetCachedChineseLanguageSetting();
			Log?.LogInfo((object)(cachedChineseLanguageSetting ? "[ShootZombies] 已切换到夜晚测试时间。长按 \\ 键 5 秒可再次触发。" : "[ShootZombies] Switched to night test time. Hold \\ for 5 seconds to trigger it again."));
		}
	}

	private void LogNightTestHotkeyHint()
	{
		if (EnableVerboseInfoLogs)
		{
			bool cachedIsChineseLanguage = _cachedIsChineseLanguage;
			Log?.LogInfo((object)(cachedIsChineseLanguage ? "[ShootZombies] 测试按键：在游戏内长按 \\ 键 5 秒可切换到夜晚。" : "[ShootZombies] Test hotkey: hold \\ for 5 seconds during gameplay to switch to night."));
		}
	}

	private void SpawnWeaponAtPlayer()
	{
		Character val = _localCharacter ?? Character.localCharacter;
		if ((Object)val == (Object)null || val.isBot || val.isZombie || CharacterAlreadyHasShootZombiesWeapon(val))
		{
			return;
		}
		if (!TryGiveItemTo(val))
		{
			if (TryResolveBaseWeaponItem(out var item) && TrySpawnWeaponViaCharacterItems(val, item))
			{
				return;
			}
			Log.LogWarning((object)("[ShootZombies] Hotkey weapon grant failed for " + val.characterName));
		}
	}

	private void SpawnCompassAtPlayer()
	{
	}

	private void UpdateZombieSpeed(bool forceRefresh = false)
	{
		float multiplier = GetZombieMoveSpeedMultiplierRuntime();
		bool flag = Mathf.Approximately(multiplier, 1f);
		if (!HasGameplayAuthority() || !IsGameplayScene(SceneManager.GetActiveScene()))
		{
			return;
		}
		if (flag && _zombieBaseSpeeds.Count == 0 && !_pendingZombieSpeedRefresh && !forceRefresh)
		{
			return;
		}
		if (!forceRefresh && !_pendingZombieSpeedRefresh && Time.time - _lastZombieSpeedUpdateTime < ZombieSpeedRefreshInterval)
		{
			return;
		}
		_lastZombieSpeedUpdateTime = Time.time;
		_pendingZombieSpeedRefresh = false;
		Character[] array = Object.FindObjectsByType<Character>((FindObjectsSortMode)0);
		List<Character> list = new List<Character>();
		foreach (KeyValuePair<Character, float> zombieBaseSpeed in _zombieBaseSpeeds)
		{
			if ((Object)zombieBaseSpeed.Key == (Object)null)
			{
				list.Add(zombieBaseSpeed.Key);
			}
		}
		foreach (Character item in list)
		{
			_zombieBaseSpeeds.Remove(item);
		}
		Character[] array2 = array;
		foreach (Character val in array2)
		{
			if ((Object)val == (Object)null || (!val.isZombie && !val.isBot))
			{
				continue;
			}
			if (!TryGetZombieSpeedReflectionCache(val, out var reflectionCache))
			{
				continue;
			}
			object value = reflectionCache.AgentField.GetValue(val);
			if (value == null)
			{
				continue;
			}
			if (!TryGetZombieSpeedProperty(reflectionCache, value, out var speedProperty))
			{
				continue;
			}
			float value2 = 3.5f;
			if (!_zombieBaseSpeeds.TryGetValue(val, out value2))
			{
				if (reflectionCache.BaseSpeedField != null)
				{
					value2 = (float)reflectionCache.BaseSpeedField.GetValue(val);
				}
				_zombieBaseSpeeds[val] = value2;
			}
			speedProperty.SetValue(value, flag ? value2 : (value2 * multiplier));
			if (flag)
			{
				_zombieBaseSpeeds.Remove(val);
			}
		}
	}

	private static bool TryGetZombieSpeedReflectionCache(Character zombie, out ZombieSpeedReflectionCache cache)
	{
		cache = null;
		if ((Object)zombie == (Object)null)
		{
			return false;
		}
		Type type = zombie.GetType();
		if (!_zombieSpeedReflectionCache.TryGetValue(type, out cache))
		{
			cache = new ZombieSpeedReflectionCache
			{
				AgentField = type.GetField("agent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetField("_agent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetField("navMeshAgent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
				BaseSpeedField = type.GetField("baseSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? type.GetField("_baseSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
			};
			_zombieSpeedReflectionCache[type] = cache;
		}
		return cache != null && cache.AgentField != null;
	}

	private static bool TryGetZombieSpeedProperty(ZombieSpeedReflectionCache cache, object agent, out PropertyInfo property)
	{
		property = null;
		if (cache == null || agent == null)
		{
			return false;
		}
		Type type = agent.GetType();
		if (cache.SpeedProperty == null || cache.AgentType != type)
		{
			cache.AgentType = type;
			cache.SpeedProperty = type.GetProperty("speed");
		}
		property = cache.SpeedProperty;
		return property != null;
	}

	private void UpdateDartEffectColorsOnce()
	{
		if (Time.time - _lastMaterialUpdateTime < 5f)
		{
			return;
		}
		_lastMaterialUpdateTime = Time.time;
		try
		{
			Renderer[] array = Object.FindObjectsByType<Renderer>((FindObjectsSortMode)0);
			foreach (Renderer val in array)
			{
				if ((Object)val == (Object)null || (Object)val.sharedMaterial == (Object)null)
				{
					continue;
				}
				string name = ((Object)((Component)val).gameObject).name;
				if ((!name.Contains("Dart") && !name.Contains("Trail") && !name.Contains("Projectile")) || name.Contains("HealingDart") || name.Contains("Hand") || name.Contains("Blowgun"))
				{
					continue;
				}
				Material sharedMaterial = val.sharedMaterial;
				if (!_processedMaterials.Contains(sharedMaterial) && sharedMaterial.HasProperty("_Color") && sharedMaterial.GetColor("_Color") != Color.black)
				{
					Material val2 = new Material(sharedMaterial);
					val2.SetColor("_Color", Color.black);
					val.sharedMaterial = val2;
					_processedMaterials.Add(val2);
				}
			}
		}
		catch
		{
		}
	}

	private void ReplaceBlowgunInstanceSound()
	{
		try
		{
			if ((Object)_localCharacter != (Object)null)
			{
				FieldInfo field = ((object)_localCharacter).GetType().GetField("poofSFX", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null && field.GetValue(_localCharacter) is Array array)
				{
					foreach (object item in array)
					{
						if (item == null)
						{
							continue;
						}
						Type type = item.GetType();
						FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						foreach (FieldInfo fieldInfo in fields)
						{
							if (fieldInfo.FieldType == typeof(AudioClip[]))
							{
								if (fieldInfo.GetValue(item) is AudioClip[] array2 && (Object)_gunshotSound != (Object)null)
								{
									for (int j = 0; j < array2.Length; j++)
									{
										array2[j] = _gunshotSound;
									}
								}
							}
							else if (fieldInfo.FieldType == typeof(AudioClip))
							{
								object value = fieldInfo.GetValue(item);
								_ = (AudioClip)((value is AudioClip) ? value : null);
								if ((Object)_gunshotSound != (Object)null)
								{
									fieldInfo.SetValue(item, _gunshotSound);
								}
							}
						}
						PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						foreach (PropertyInfo propertyInfo in properties)
						{
							if (!(propertyInfo.PropertyType == typeof(AudioClip)))
							{
								continue;
							}
							try
							{
								object value2 = propertyInfo.GetValue(item);
								_ = (AudioClip)((value2 is AudioClip) ? value2 : null);
								if ((Object)_gunshotSound != (Object)null && propertyInfo.CanWrite)
								{
									propertyInfo.SetValue(item, _gunshotSound);
								}
							}
							catch
							{
							}
						}
					}
				}
			}
			Item[] array3 = Object.FindObjectsByType<Item>((FindObjectsSortMode)0);
			foreach (Item val in array3)
			{
				string name = val.GetName();
				if (!ItemPatch.IsBlowgunLike(val, name))
				{
					continue;
				}
				AudioSource[] componentsInChildren = ((Component)val).GetComponentsInChildren<AudioSource>(true);
				foreach (AudioSource val2 in componentsInChildren)
				{
					if ((Object)val2.clip != (Object)null && (Object)_gunshotSound != (Object)null)
					{
						val2.clip = _gunshotSound;
					}
				}
				Type type2 = typeof(Item).Assembly.GetType("SFX_Instance");
				if (!(type2 != null))
				{
					continue;
				}
				Component[] componentsInChildren2 = ((Component)val).GetComponentsInChildren(type2, true);
				foreach (Component val3 in componentsInChildren2)
				{
					FieldInfo[] fields = ((object)val3).GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					foreach (FieldInfo fieldInfo2 in fields)
					{
						if (fieldInfo2.FieldType == typeof(AudioClip))
						{
							object value3 = fieldInfo2.GetValue(val3);
							_ = (AudioClip)((value3 is AudioClip) ? value3 : null);
							if ((Object)_gunshotSound != (Object)null)
							{
								fieldInfo2.SetValue(val3, _gunshotSound);
							}
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] ReplaceBlowgunInstanceSound error: " + ex.Message));
		}
	}

	private bool IsHoldingBlowgun()
	{
		try
		{
			return (Object)GetHeldBlowgunItem() != (Object)null;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] IsHoldingBlowgun error: " + ex.Message));
		}
		return false;
	}

	private static bool CanProcessLocalWeaponFireInput(Character character)
	{
		if ((Object)character == (Object)null)
		{
			return false;
		}
		if (GUIManager.InPauseMenu)
		{
			return false;
		}
		try
		{
			if (!_characterCanDoInputMethodInitialized)
			{
				_characterCanDoInputMethod = typeof(Character).GetMethod("CanDoInput", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				_characterCanDoInputMethodInitialized = true;
			}
			if (_characterCanDoInputMethod != null)
			{
				object obj = _characterCanDoInputMethod.Invoke(character, null);
				if (obj is bool)
				{
					return (bool)obj;
				}
			}
		}
		catch
		{
		}
		return true;
	}

	private void TryFire()
	{
		if (!IsWeaponFeatureEnabled() || !IsHoldingBlowgun() || !CanProcessLocalWeaponFireInput(_localCharacter))
		{
			return;
		}
		_lastFireTime = Time.time;
		Item heldBlowgunItem = GetHeldBlowgunItem();
		if ((Object)heldBlowgunItem != (Object)null)
		{
			FieldInfo field = ((object)heldBlowgunItem).GetType().GetField("<castProgress>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				field.SetValue(heldBlowgunItem, 0f);
			}
		}
		Vector3 val2;
		Vector3 val3;
		if ((Object)Camera.main != (Object)null)
		{
			Ray val = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
			val2 = val.origin + val.direction * 0.08f;
			val3 = val.direction;
		}
		else
		{
			val2 = ((Component)_localCharacter.refs.view).transform.position;
			val3 = ((Component)_localCharacter.refs.view).transform.forward;
		}
		UpdateLocalWeaponMuzzle();
		Transform val4 = ResolveCurrentWeaponMuzzle(heldBlowgunItem);
		Vector3 val5 = (((Object)val4 != (Object)null) ? val4.position : GetWeaponPosition());
		if (val5 == Vector3.zero)
		{
			val5 = val2;
		}
		Vector3 val6 = (((Object)val4 != (Object)null) ? val4.forward : val3);
		RaycastHit hit = default(RaycastHit);
		Character hitCharacter = null;
		bool num = TryGetAimHit(val2, val2, val3, heldBlowgunItem, out hit, out hitCharacter);
		if (num)
		{
			Vector3 val7 = hit.point - val5;
			if (val7.sqrMagnitude > 1E-06f)
			{
				val6 = val7.normalized;
			}
		}
		bool flag = num && (Object)hit.collider != (Object)null;
		Component val8 = TryGetRaycastDartComponent(heldBlowgunItem);
		if (num && (Object)hitCharacter == (Object)null && flag)
		{
			SpawnDartImpactVisualOnly(heldBlowgunItem, hit.point);
		}
		if (num && flag)
		{
			if ((Object)hitCharacter == (Object)null)
			{
				hitCharacter = ResolveHitCharacter(hit.collider);
			}
			if ((Object)hitCharacter != (Object)null && (Object)hitCharacter != (Object)_localCharacter)
			{
				if (hitCharacter.isZombie || hitCharacter.isBot)
				{
					if (!TriggerDartImpactEffect(heldBlowgunItem, hit, val5, hitCharacter))
					{
						if ((Object)val8 != (Object)null)
						{
							HandleZombieDartImpactVisual(val8, hit.point);
						}
						HitZombie(hitCharacter, val5);
					}
				}
				else
				{
					SpawnDartImpactVisualOnly(heldBlowgunItem, hit.point);
					HandlePlayerShotStatus(hitCharacter);
				}
			}
		}
		Vector3 preferredSoundPosition = GetPreferredSoundPosition(val5, val6);
		if ((Object)_localCharacter != (Object)null)
		{
			CreateMuzzleFlash(val5, val6);
			if ((Object)_gunshotSound != (Object)null)
			{
				PlayGunshotSound(preferredSoundPosition);
			}
			else
			{
				PlayFallbackGunshotSound(preferredSoundPosition);
			}
		}
		TrySendRemoteShotEffects(val5, val6, preferredSoundPosition, num && flag, (num && flag) ? hit.point : Vector3.zero);
	}

	private bool TryGetAimHit(Vector3 aimOrigin, Vector3 weaponOrigin, Vector3 direction, Item heldBlowgunItem, out RaycastHit hit, out Character hitCharacter)
	{
		hit = default(RaycastHit);
		hitCharacter = null;
		try
		{
			Component obj = TryGetRaycastDartComponent(heldBlowgunItem);
			float num = GetConfiguredHitScanDistance(obj);
			float num2 = Mathf.Max(GetConfiguredBulletSize(obj), 0.01f);
			List<RaycastHit> list = new List<RaycastHit>();
			RaycastHit[] array = Physics.RaycastAll(aimOrigin, direction, num, -5, (QueryTriggerInteraction)1);
			if (array != null && array.Length != 0)
			{
				list.AddRange(array);
			}
			RaycastHit[] array2 = Physics.SphereCastAll(aimOrigin, num2, direction, num, -5, (QueryTriggerInteraction)1);
			if (array2 != null && array2.Length != 0)
			{
				list.AddRange(array2);
			}
			if (list.Count > 0)
			{
				list.Sort((RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance));
				foreach (RaycastHit item in list)
				{
					RaycastHit current = item;
					if (!((Object)current.collider == (Object)null) && !IsSelfHit(current.collider))
					{
						hit = current;
						hitCharacter = ResolveHitCharacter(current.collider);
						return true;
					}
				}
			}
			RaycastHit val = default(RaycastHit);
			if (!Physics.Raycast(aimOrigin, direction, out val, num, HelperFunctions.terrainMapMask, (QueryTriggerInteraction)1))
			{
				val.distance = num;
				val.point = aimOrigin + direction * num;
			}
			hit = val;
			return true;
		}
		catch
		{
			if (Physics.Raycast(aimOrigin, direction, out hit, DefaultHitScanDistance) && !IsSelfHit(hit.collider))
			{
				hitCharacter = ResolveHitCharacter(hit.collider);
				return true;
			}
		}
		return false;
	}

	private static Character ResolveHitCharacter(Collider collider)
	{
		if (!((Object)(object)collider != (Object)null))
		{
			return null;
		}
		return ((Component)collider).GetComponentInParent<Character>();
	}

	private bool IsSelfHit(Collider collider)
	{
		if ((Object)(object)collider == (Object)null || (Object)_localCharacter == (Object)null)
		{
			return false;
		}
		Item componentInParent = ((Component)collider).GetComponentInParent<Item>();
		if ((Object)componentInParent != (Object)null && ((Object)componentInParent.holderCharacter == (Object)_localCharacter || (Object)componentInParent.trueHolderCharacter == (Object)_localCharacter))
		{
			return true;
		}
		Character componentInParent2 = ((Component)collider).GetComponentInParent<Character>();
		if ((Object)componentInParent2 != (Object)null && (Object)componentInParent2 == (Object)_localCharacter)
		{
			return true;
		}
		if ((Object)componentInParent2 != (Object)null && (componentInParent2.isZombie || componentInParent2.isBot))
		{
			return false;
		}
		return ((Component)collider).transform.IsChildOf(((Component)_localCharacter).transform);
	}

	private static bool TriggerDartImpactEffect(Item heldBlowgunItem, RaycastHit hit, Vector3 origin, Character hitCharacter)
	{
		try
		{
			if ((Object)heldBlowgunItem == (Object)null)
			{
				return false;
			}
			Component val = TryGetRaycastDartComponent(heldBlowgunItem);
			if ((Object)val == (Object)null || _actionRaycastDartType == null)
			{
				return false;
			}
			if (_rpcDartImpactMethod == null || _rpcDartImpactMethod.DeclaringType != ((object)val).GetType())
			{
				_rpcDartImpactMethod = ((object)val).GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(delegate(MethodInfo method)
				{
					if (!string.Equals(method.Name, "RPC_DartImpact", StringComparison.Ordinal))
					{
						return false;
					}
					ParameterInfo[] parameters3 = method.GetParameters();
					if (parameters3.Length != 3 && parameters3.Length != 4)
					{
						return false;
					}
					return parameters3[0].ParameterType == typeof(int) && parameters3[1].ParameterType == typeof(Vector3) && parameters3[2].ParameterType == typeof(Vector3);
				});
			}
			if (_rpcDartImpactMethod == null)
			{
				TrySpawnDartVfxFromAction(val, hit.point);
				return false;
			}
			int num = -1;
			if ((Object)hitCharacter != (Object)null && (Object)((MonoBehaviourPun)hitCharacter).photonView != (Object)null)
			{
				num = ((MonoBehaviourPun)hitCharacter).photonView.ViewID;
			}
			ParameterInfo[] parameters = _rpcDartImpactMethod.GetParameters();
			object[] parameters2 = ((parameters.Length != 4) ? new object[3]
			{
				num,
				origin,
				hit.point
			} : new object[4]
			{
				num,
				origin,
				hit.point,
				Activator.CreateInstance(parameters[3].ParameterType)
			});
			_rpcDartImpactMethod.Invoke(val, parameters2);
			return true;
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogWarning((object)("[ShootZombies] TriggerDartImpactEffect failed: " + ex.Message));
			}
		}
		return false;
	}

	private static void SpawnDartImpactVisualOnly(Item heldBlowgunItem, Vector3 endpoint)
	{
		try
		{
			Component val = TryGetRaycastDartComponent(heldBlowgunItem);
			if ((Object)val != (Object)null)
			{
				TrySpawnDartVfxFromAction(val, endpoint);
			}
		}
		catch
		{
		}
	}

	private static Component TryGetRaycastDartComponent(Item heldBlowgunItem)
	{
		if ((Object)heldBlowgunItem == (Object)null)
		{
			return null;
		}
		if (_actionRaycastDartType == null)
		{
			_actionRaycastDartType = typeof(Item).Assembly.GetType("Action_RaycastDart");
		}
		if (_actionRaycastDartType == null)
		{
			return null;
		}
		NormalizeRaycastDartComponents(heldBlowgunItem);
		NormalizeRaycastDartComponents(heldBlowgunItem.isSecretlyOtherItemPrefab);
		Component val = ((Component)heldBlowgunItem).GetComponentInChildren(_actionRaycastDartType, true);
		if ((Object)val == (Object)null)
		{
			val = ((Component)heldBlowgunItem).GetComponent(_actionRaycastDartType);
		}
		return val;
	}

	private static void NormalizeRaycastDartComponents(Item heldBlowgunItem)
	{
		try
		{
			if ((Object)heldBlowgunItem == (Object)null)
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
			Component[] components = ((Component)heldBlowgunItem).GetComponentsInChildren(_actionRaycastDartType, true);
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

	private static void TrySpawnDartVfxFromAction(Component actionRaycastDart, Vector3 endpoint)
	{
		try
		{
			if (!((Object)actionRaycastDart == (Object)null) && !(_actionRaycastDartType == null))
			{
				if (_dartVfxField == null)
				{
					_dartVfxField = _actionRaycastDartType.GetField("dartVFX", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}
				object obj = _dartVfxField?.GetValue(actionRaycastDart);
				GameObject val = (GameObject)((obj is GameObject) ? obj : null);
				if ((Object)val != (Object)null)
				{
					PlayDartImpactVfx(val, endpoint, Quaternion.identity);
				}
			}
		}
		catch
		{
		}
	}

	public static void HandleZombieDartImpactVisual(Component actionRaycastDart, Vector3 endpoint)
	{
		try
		{
			if (!((Object)actionRaycastDart == (Object)null))
			{
				if (_actionRaycastDartType == null)
				{
					_actionRaycastDartType = ((object)actionRaycastDart).GetType();
				}
				TrySpawnDartVfxFromAction(actionRaycastDart, endpoint);
				if ((Object)GamefeelHandler.instance != (Object)null)
				{
					GamefeelHandler.instance.AddPerlinShakeProximity(endpoint, 5f, 0.2f, 15f, 10f);
				}
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogWarning((object)("[ShootZombies] HandleZombieDartImpactVisual failed: " + ex.Message));
			}
		}
	}

	private void CreateMuzzleFlash(Vector3 position, Vector3 direction)
	{
		try
		{
			if (direction.sqrMagnitude < 1E-06f)
			{
				direction = (((Object)_localWeaponMuzzle != (Object)null) ? _localWeaponMuzzle.forward : (((Object)(_localCharacter?.refs?.view) != (Object)null) ? ((Component)_localCharacter.refs.view).transform.forward : Vector3.forward));
			}
			MuzzleFlashInstance muzzleFlashInstance = AcquireMuzzleFlashInstance();
			if (muzzleFlashInstance == null || (Object)muzzleFlashInstance.Root == (Object)null)
			{
				return;
			}
			muzzleFlashInstance.Root.transform.position = position;
			muzzleFlashInstance.Root.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
			if ((Object)muzzleFlashInstance.Light != (Object)null)
			{
				muzzleFlashInstance.Light.enabled = true;
				muzzleFlashInstance.Light.intensity = 6f;
				muzzleFlashInstance.Light.range = 2f;
				muzzleFlashInstance.Light.color = new Color(1f, 0.72f, 0.35f);
			}
			muzzleFlashInstance.DisableAtTime = Time.time + MuzzleFlashDuration;
			if (!muzzleFlashInstance.Root.activeSelf)
			{
				muzzleFlashInstance.Root.SetActive(true);
			}
		}
		catch (Exception)
		{
		}
	}

	private static void UpdateMuzzleFlashPool()
	{
		if (_muzzleFlashPool.Count == 0)
		{
			return;
		}
		float time = Time.time;
		for (int num = _muzzleFlashPool.Count - 1; num >= 0; num--)
		{
			MuzzleFlashInstance muzzleFlashInstance = _muzzleFlashPool[num];
			if (muzzleFlashInstance == null || (Object)muzzleFlashInstance.Root == (Object)null)
			{
				_muzzleFlashPool.RemoveAt(num);
				if (_nextMuzzleFlashPoolIndex > num)
				{
					_nextMuzzleFlashPoolIndex--;
				}
				continue;
			}
			if (muzzleFlashInstance.Root.activeSelf && time >= muzzleFlashInstance.DisableAtTime)
			{
				if ((Object)muzzleFlashInstance.Light != (Object)null)
				{
					muzzleFlashInstance.Light.enabled = false;
				}
				muzzleFlashInstance.Root.SetActive(false);
			}
		}
		if (_nextMuzzleFlashPoolIndex < 0)
		{
			_nextMuzzleFlashPoolIndex = 0;
		}
		if (_muzzleFlashPool.Count > 0)
		{
			_nextMuzzleFlashPoolIndex %= _muzzleFlashPool.Count;
		}
		else
		{
			_nextMuzzleFlashPoolIndex = 0;
		}
	}

	private static MuzzleFlashInstance AcquireMuzzleFlashInstance()
	{
		for (int i = 0; i < _muzzleFlashPool.Count; i++)
		{
			MuzzleFlashInstance muzzleFlashInstance = _muzzleFlashPool[i];
			if (muzzleFlashInstance == null || (Object)muzzleFlashInstance.Root == (Object)null)
			{
				continue;
			}
			if (!muzzleFlashInstance.Root.activeSelf)
			{
				return muzzleFlashInstance;
			}
		}
		if (_muzzleFlashPool.Count < MaxMuzzleFlashPoolSize)
		{
			MuzzleFlashInstance muzzleFlashInstance2 = CreateMuzzleFlashInstance();
			if (muzzleFlashInstance2 != null)
			{
				_muzzleFlashPool.Add(muzzleFlashInstance2);
			}
			return muzzleFlashInstance2;
		}
		if (_muzzleFlashPool.Count == 0)
		{
			return null;
		}
		int num = Mathf.Clamp(_nextMuzzleFlashPoolIndex, 0, _muzzleFlashPool.Count - 1);
		MuzzleFlashInstance result = _muzzleFlashPool[num];
		_nextMuzzleFlashPoolIndex = (num + 1) % _muzzleFlashPool.Count;
		return result;
	}

	private static MuzzleFlashInstance CreateMuzzleFlashInstance()
	{
		try
		{
			GameObject val = new GameObject("MuzzleFlash");
			val.SetActive(false);
			Light obj = val.AddComponent<Light>();
			obj.type = (LightType)2;
			obj.intensity = 6f;
			obj.range = 2f;
			obj.color = new Color(1f, 0.72f, 0.35f);
			obj.enabled = false;
			return new MuzzleFlashInstance
			{
				Root = val,
				Light = obj,
				DisableAtTime = 0f
			};
		}
		catch
		{
			return null;
		}
	}

	private static void CleanupMuzzleFlashPool()
	{
		for (int i = 0; i < _muzzleFlashPool.Count; i++)
		{
			MuzzleFlashInstance muzzleFlashInstance = _muzzleFlashPool[i];
			if (muzzleFlashInstance != null && (Object)muzzleFlashInstance.Root != (Object)null)
			{
				Object.Destroy((Object)muzzleFlashInstance.Root);
			}
		}
		_muzzleFlashPool.Clear();
		_nextMuzzleFlashPoolIndex = 0;
	}

	private static void UpdateRemoteGunshotAudioPool()
	{
		if (_remoteGunshotAudioPool.Count == 0)
		{
			return;
		}
		float time = Time.time;
		for (int num = _remoteGunshotAudioPool.Count - 1; num >= 0; num--)
		{
			RemoteGunshotAudioInstance remoteGunshotAudioInstance = _remoteGunshotAudioPool[num];
			if (remoteGunshotAudioInstance == null || (Object)remoteGunshotAudioInstance.Root == (Object)null || (Object)remoteGunshotAudioInstance.AudioSource == (Object)null)
			{
				_remoteGunshotAudioPool.RemoveAt(num);
				if (_nextRemoteGunshotAudioPoolIndex > num)
				{
					_nextRemoteGunshotAudioPoolIndex--;
				}
				continue;
			}
			if (remoteGunshotAudioInstance.Root.activeSelf && time >= remoteGunshotAudioInstance.ReleaseAtTime)
			{
				remoteGunshotAudioInstance.AudioSource.Stop();
				remoteGunshotAudioInstance.AudioSource.clip = null;
				remoteGunshotAudioInstance.Root.SetActive(false);
			}
		}
		if (_nextRemoteGunshotAudioPoolIndex < 0)
		{
			_nextRemoteGunshotAudioPoolIndex = 0;
		}
		if (_remoteGunshotAudioPool.Count > 0)
		{
			_nextRemoteGunshotAudioPoolIndex %= _remoteGunshotAudioPool.Count;
		}
		else
		{
			_nextRemoteGunshotAudioPoolIndex = 0;
		}
	}

	private static RemoteGunshotAudioInstance AcquireRemoteGunshotAudioInstance()
	{
		for (int i = 0; i < _remoteGunshotAudioPool.Count; i++)
		{
			RemoteGunshotAudioInstance remoteGunshotAudioInstance = _remoteGunshotAudioPool[i];
			if (remoteGunshotAudioInstance == null || (Object)remoteGunshotAudioInstance.Root == (Object)null || (Object)remoteGunshotAudioInstance.AudioSource == (Object)null)
			{
				continue;
			}
			if (!remoteGunshotAudioInstance.Root.activeSelf)
			{
				return remoteGunshotAudioInstance;
			}
		}
		if (_remoteGunshotAudioPool.Count < MaxRemoteGunshotAudioPoolSize)
		{
			RemoteGunshotAudioInstance remoteGunshotAudioInstance2 = CreateRemoteGunshotAudioInstance();
			if (remoteGunshotAudioInstance2 != null)
			{
				_remoteGunshotAudioPool.Add(remoteGunshotAudioInstance2);
			}
			return remoteGunshotAudioInstance2;
		}
		if (_remoteGunshotAudioPool.Count == 0)
		{
			return null;
		}
		int num = Mathf.Clamp(_nextRemoteGunshotAudioPoolIndex, 0, _remoteGunshotAudioPool.Count - 1);
		RemoteGunshotAudioInstance result = _remoteGunshotAudioPool[num];
		_nextRemoteGunshotAudioPoolIndex = (num + 1) % _remoteGunshotAudioPool.Count;
		return result;
	}

	private static RemoteGunshotAudioInstance CreateRemoteGunshotAudioInstance()
	{
		try
		{
			GameObject val = new GameObject("AK47_RemoteGunshotSound");
			val.SetActive(false);
			AudioSource obj = val.AddComponent<AudioSource>();
			obj.spatialBlend = 1f;
			obj.rolloffMode = AudioRolloffMode.Linear;
			obj.minDistance = 1.5f;
			obj.maxDistance = 100f;
			obj.dopplerLevel = 0f;
			obj.spread = 10f;
			obj.priority = 32;
			obj.playOnAwake = false;
			return new RemoteGunshotAudioInstance
			{
				Root = val,
				AudioSource = obj,
				ReleaseAtTime = 0f
			};
		}
		catch
		{
			return null;
		}
	}

	private static void CleanupRemoteGunshotAudioPool()
	{
		for (int i = 0; i < _remoteGunshotAudioPool.Count; i++)
		{
			RemoteGunshotAudioInstance remoteGunshotAudioInstance = _remoteGunshotAudioPool[i];
			if (remoteGunshotAudioInstance != null && (Object)remoteGunshotAudioInstance.Root != (Object)null)
			{
				Object.Destroy((Object)remoteGunshotAudioInstance.Root);
			}
		}
		_remoteGunshotAudioPool.Clear();
		_nextRemoteGunshotAudioPoolIndex = 0;
	}

	private static void PlayDartImpactVfx(GameObject prefab, Vector3 endpoint, Quaternion rotation)
	{
		if ((Object)prefab == (Object)null)
		{
			return;
		}
		DartImpactVfxInstance dartImpactVfxInstance = AcquireDartImpactVfxInstance(prefab);
		if (dartImpactVfxInstance == null || (Object)dartImpactVfxInstance.Root == (Object)null)
		{
			return;
		}
		RestartDartImpactVfxInstance(dartImpactVfxInstance, endpoint, rotation);
		dartImpactVfxInstance.ReleaseAtTime = Time.time + Mathf.Max(dartImpactVfxInstance.EstimatedLifetime, DefaultDartImpactVfxLifetime);
	}

	private static void UpdateDartImpactVfxPool()
	{
		if (_dartImpactVfxPool.Count == 0)
		{
			return;
		}
		float time = Time.time;
		for (int num = _dartImpactVfxPool.Count - 1; num >= 0; num--)
		{
			DartImpactVfxInstance dartImpactVfxInstance = _dartImpactVfxPool[num];
			if (dartImpactVfxInstance == null || (Object)dartImpactVfxInstance.Root == (Object)null)
			{
				_dartImpactVfxPool.RemoveAt(num);
				if (_nextDartImpactVfxPoolIndex > num)
				{
					_nextDartImpactVfxPoolIndex--;
				}
				continue;
			}
			if (dartImpactVfxInstance.Root.activeSelf && time >= dartImpactVfxInstance.ReleaseAtTime)
			{
				if (dartImpactVfxInstance.AudioSources != null)
				{
					AudioSource[] audioSources = dartImpactVfxInstance.AudioSources;
					foreach (AudioSource val in audioSources)
					{
						if (!((Object)val == (Object)null))
						{
							val.Stop();
						}
					}
				}
				dartImpactVfxInstance.Root.SetActive(false);
			}
		}
		if (_nextDartImpactVfxPoolIndex < 0)
		{
			_nextDartImpactVfxPoolIndex = 0;
		}
		if (_dartImpactVfxPool.Count > 0)
		{
			_nextDartImpactVfxPoolIndex %= _dartImpactVfxPool.Count;
		}
		else
		{
			_nextDartImpactVfxPoolIndex = 0;
		}
	}

	private static DartImpactVfxInstance AcquireDartImpactVfxInstance(GameObject prefab)
	{
		int instanceID = ((Object)prefab).GetInstanceID();
		DartImpactVfxInstance dartImpactVfxInstance = null;
		int num = 0;
		for (int i = 0; i < _dartImpactVfxPool.Count; i++)
		{
			DartImpactVfxInstance dartImpactVfxInstance2 = _dartImpactVfxPool[i];
			if (dartImpactVfxInstance2 == null || (Object)dartImpactVfxInstance2.Root == (Object)null || dartImpactVfxInstance2.PrefabId != instanceID)
			{
				continue;
			}
			num++;
			if (!dartImpactVfxInstance2.Root.activeSelf)
			{
				return dartImpactVfxInstance2;
			}
			if (dartImpactVfxInstance == null || dartImpactVfxInstance2.ReleaseAtTime < dartImpactVfxInstance.ReleaseAtTime)
			{
				dartImpactVfxInstance = dartImpactVfxInstance2;
			}
		}
		if (num < MaxDartImpactVfxPoolSizePerPrefab)
		{
			DartImpactVfxInstance dartImpactVfxInstance3 = CreateDartImpactVfxInstance(prefab);
			if (dartImpactVfxInstance3 != null)
			{
				_dartImpactVfxPool.Add(dartImpactVfxInstance3);
			}
			return dartImpactVfxInstance3;
		}
		if (dartImpactVfxInstance != null)
		{
			return dartImpactVfxInstance;
		}
		if (_dartImpactVfxPool.Count == 0)
		{
			return null;
		}
		int num2 = Mathf.Clamp(_nextDartImpactVfxPoolIndex, 0, _dartImpactVfxPool.Count - 1);
		DartImpactVfxInstance result = _dartImpactVfxPool[num2];
		_nextDartImpactVfxPoolIndex = (num2 + 1) % _dartImpactVfxPool.Count;
		return result;
	}

	private static DartImpactVfxInstance CreateDartImpactVfxInstance(GameObject prefab)
	{
		try
		{
			GameObject val = Object.Instantiate<GameObject>(prefab);
			val.SetActive(false);
			return new DartImpactVfxInstance
			{
				Root = val,
				PrefabId = ((Object)prefab).GetInstanceID(),
				ReleaseAtTime = 0f,
				EstimatedLifetime = EstimateDartImpactVfxLifetime(val),
				AudioSources = val.GetComponentsInChildren<AudioSource>(true)
			};
		}
		catch
		{
			return null;
		}
	}

	private static float EstimateDartImpactVfxLifetime(GameObject root)
	{
		if ((Object)root == (Object)null)
		{
			return DefaultDartImpactVfxLifetime;
		}
		float num = 0f;
		AudioSource[] componentsInChildren2 = root.GetComponentsInChildren<AudioSource>(true);
		if (componentsInChildren2 != null)
		{
			foreach (AudioSource val2 in componentsInChildren2)
			{
				if (!((Object)val2 == (Object)null) && (Object)val2.clip != (Object)null)
				{
					num = Mathf.Max(num, val2.clip.length);
				}
			}
		}
		return Mathf.Max(num, DefaultDartImpactVfxLifetime);
	}

	private static void RestartDartImpactVfxInstance(DartImpactVfxInstance instance, Vector3 endpoint, Quaternion rotation)
	{
		if (instance == null || (Object)instance.Root == (Object)null)
		{
			return;
		}
		instance.Root.SetActive(false);
		instance.Root.transform.SetPositionAndRotation(endpoint, rotation);
		if (instance.AudioSources != null)
		{
			AudioSource[] audioSources = instance.AudioSources;
			foreach (AudioSource val in audioSources)
			{
				if (!((Object)val == (Object)null))
				{
					val.Stop();
					val.time = 0f;
				}
			}
		}
		instance.Root.SetActive(true);
		if (instance.AudioSources != null)
		{
			AudioSource[] audioSources2 = instance.AudioSources;
			foreach (AudioSource val4 in audioSources2)
			{
				if (!((Object)val4 == (Object)null))
				{
					val4.Play();
				}
			}
		}
	}

	private static void CleanupDartImpactVfxPool()
	{
		for (int i = 0; i < _dartImpactVfxPool.Count; i++)
		{
			DartImpactVfxInstance dartImpactVfxInstance = _dartImpactVfxPool[i];
			if (dartImpactVfxInstance != null && (Object)dartImpactVfxInstance.Root != (Object)null)
			{
				Object.Destroy((Object)dartImpactVfxInstance.Root);
			}
		}
		_dartImpactVfxPool.Clear();
		_nextDartImpactVfxPoolIndex = 0;
	}

	private static Material GetMuzzleFlashMaterial()
	{
		if ((Object)_muzzleFlashMaterial != (Object)null)
		{
			return _muzzleFlashMaterial;
		}
		_muzzleFlashMaterial = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Standard"));
		((Object)_muzzleFlashMaterial).name = "ShootZombies_MuzzleFlash";
		if (_muzzleFlashMaterial.HasProperty("_Surface"))
		{
			_muzzleFlashMaterial.SetFloat("_Surface", 1f);
		}
		if (_muzzleFlashMaterial.HasProperty("_Blend"))
		{
			_muzzleFlashMaterial.SetFloat("_Blend", 1f);
		}
		if (_muzzleFlashMaterial.HasProperty("_BaseColor"))
		{
			_muzzleFlashMaterial.SetColor("_BaseColor", new Color(1f, 0.75f, 0.3f, 1f));
		}
		if (_muzzleFlashMaterial.HasProperty("_Color"))
		{
			_muzzleFlashMaterial.SetColor("_Color", new Color(1f, 0.75f, 0.3f, 1f));
		}
		return _muzzleFlashMaterial;
	}

	private Item GetHeldBlowgunItem()
	{
		if ((Object)_localCharacter == (Object)null)
		{
			_cachedHeldBlowgunItem = null;
			return null;
		}
		CharacterData data = _localCharacter.data;
		Item val = ((data != null) ? data.currentItem : null);
		if (IsHeldBlowgunOwnedByCharacter(val, _localCharacter))
		{
			_cachedHeldBlowgunItem = val;
			return val;
		}
		Item item = _cachedHeldBlowgunItem;
		if (IsHeldBlowgunOwnedByCharacter(item, _localCharacter))
		{
			return item;
		}
		if (Time.time - _lastHeldBlowgunSearchTime < HeldBlowgunSearchInterval)
		{
			return null;
		}
		_lastHeldBlowgunSearchTime = Time.time;
		Item[] array = Object.FindObjectsByType<Item>((FindObjectsSortMode)0);
		foreach (Item val2 in array)
		{
			if (IsHeldBlowgunOwnedByCharacter(val2, _localCharacter))
			{
				_cachedHeldBlowgunItem = val2;
				return val2;
			}
		}
		_cachedHeldBlowgunItem = null;
		return null;
	}

	private static bool IsHeldBlowgunOwnedByCharacter(Item item, Character character)
	{
		if ((Object)item == (Object)null || (Object)character == (Object)null || !ItemPatch.IsBlowgunLike(item) || (int)item.itemState != 1)
		{
			return false;
		}
		if ((Object)item.holderCharacter == (Object)character)
		{
			return true;
		}
		return (Object)item.trueHolderCharacter == (Object)character;
	}

	private void SyncBlowgunChargeState(Item heldBlowgunItem)
	{
		if ((Object)heldBlowgunItem == (Object)null)
		{
			_lastChargeSyncItemId = int.MinValue;
			return;
		}
		int instanceID = ((Object)heldBlowgunItem).GetInstanceID();
		if (_lastChargeSyncItemId == instanceID && Time.time - _lastChargeSyncTime < ChargeStateSyncInterval)
		{
			return;
		}
		_lastChargeSyncItemId = instanceID;
		_lastChargeSyncTime = Time.time;
		try
		{
			float value = Mathf.Max(FireInterval?.Value ?? 0.4f, 0.1f);
			float num = 0f;
			if (_castProgressField == null)
			{
				_castProgressField = ((object)heldBlowgunItem).GetType().GetField("<castProgress>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			_castProgressField?.SetValue(heldBlowgunItem, num);
			TrySetFloatFieldOrProperty(heldBlowgunItem, "castProgress", num);
			TrySetFloatFieldOrProperty(heldBlowgunItem, "chargeProgress", num);
			TrySetBoolFieldOrProperty(heldBlowgunItem, "showChargeBar", value2: false);
			TrySetBoolFieldOrProperty(heldBlowgunItem, "showCastBar", value2: false);
			TrySetBoolFieldOrProperty(heldBlowgunItem, "needsCharge", value2: false);
			TrySetBoolFieldOrProperty(heldBlowgunItem, "requiresCharge", value2: false);
			EnsureInfiniteUses(heldBlowgunItem);
			DisableConsumableActions(heldBlowgunItem);
			NormalizeRaycastDartComponents(heldBlowgunItem);
			NormalizeRaycastDartComponents(heldBlowgunItem.isSecretlyOtherItemPrefab);
			if (_actionRaycastDartType == null)
			{
				_actionRaycastDartType = typeof(Item).Assembly.GetType("Action_RaycastDart");
			}
			if (_actionRaycastDartType == null)
			{
				return;
			}
			Component val = ((Component)heldBlowgunItem).GetComponentInChildren(_actionRaycastDartType, true);
			if ((Object)val == (Object)null)
			{
				val = ((Component)heldBlowgunItem).GetComponent(_actionRaycastDartType);
			}
			if (!((Object)val == (Object)null))
			{
				Behaviour val2 = (Behaviour)(object)((val is Behaviour) ? val : null);
				if (val2 != null)
				{
					val2.enabled = false;
				}
				TrySetFloatFieldOrProperty(val, "castProgress", num);
				TrySetFloatFieldOrProperty(val, "chargeProgress", num);
				TrySetFloatFieldOrProperty(val, "castTime", value);
				TrySetFloatFieldOrProperty(val, "castDuration", value);
				TrySetFloatFieldOrProperty(val, "chargeTime", value);
				TrySetFloatFieldOrProperty(val, "chargeDuration", value);
				TrySetBoolFieldOrProperty(val, "showChargeBar", value2: false);
				TrySetBoolFieldOrProperty(val, "showCastBar", value2: false);
				TrySetBoolFieldOrProperty(val, "needsCharge", value2: false);
				TrySetBoolFieldOrProperty(val, "requiresCharge", value2: false);
			}
		}
		catch
		{
		}
	}

	private static void TrySetFloatFieldOrProperty(object target, string memberName, float value)
	{
		if (target == null || string.IsNullOrEmpty(memberName))
		{
			return;
		}
		ReflectionMemberCacheEntry cachedMemberAccessor = GetCachedMemberAccessor(target.GetType(), memberName, typeof(float));
		FieldInfo field = cachedMemberAccessor.Field;
		if (field != null)
		{
			field.SetValue(target, value);
			return;
		}
		PropertyInfo property = cachedMemberAccessor.Property;
		if (property != null && property.CanWrite)
		{
			property.SetValue(target, value);
		}
	}

	private static bool TryFireViaOriginalDartAction(Item heldBlowgunItem, Vector3 spawnPosition, Vector3 fallbackDirection, out Vector3 shotDirection)
	{
		shotDirection = fallbackDirection;
		GameObject val = null;
		Transform val2 = null;
		Transform val3 = null;
		Component val4 = null;
		float? num = null;
		try
		{
			val4 = TryGetRaycastDartComponent(heldBlowgunItem);
			if ((Object)val4 == (Object)null || _actionRaycastDartType == null)
			{
				return false;
			}
			if (_actionRunActionMethod == null)
			{
				_actionRunActionMethod = _actionRaycastDartType.GetMethod("RunAction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			if (_actionRunActionMethod == null)
			{
				return false;
			}
			if (_actionRaycastDartSpawnTransformField == null)
			{
				_actionRaycastDartSpawnTransformField = _actionRaycastDartType.GetField("spawnTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			val2 = ((_actionRaycastDartSpawnTransformField != null) ? (_actionRaycastDartSpawnTransformField.GetValue(val4) as Transform) : null);
			val3 = ResolveDartSpawnTransform(heldBlowgunItem);
			if ((Object)val3 != (Object)null && _actionRaycastDartSpawnTransformField != null)
			{
				Vector3 val5 = ((fallbackDirection.sqrMagnitude > 1E-06f) ? fallbackDirection.normalized : val3.forward);
				Vector3 val6 = spawnPosition;
				if (val6 == Vector3.zero)
				{
					val6 = val3.position;
				}
				val = new GameObject("AK47_RuntimeSpawn");
				((Object)val).hideFlags = (HideFlags)61;
				val.transform.position = val6;
				val.transform.rotation = Quaternion.LookRotation(val5, ResolveShotUpVector(val3, val5));
				_actionRaycastDartSpawnTransformField.SetValue(val4, val.transform);
				shotDirection = val.transform.forward;
			}
			else if ((Object)val2 != (Object)null)
			{
				shotDirection = val2.forward;
			}
			TrySetFloatFieldOrProperty(val4, "maxDistance", Mathf.Max(DefaultHitScanDistance, 1f));
			num = TryGetOptionalFloatFieldOrProperty(val4, "dartCollisionSize");
			TrySetFloatFieldOrProperty(val4, "dartCollisionSize", GetConfiguredBulletSize(val4));
			_actionRunActionMethod.Invoke(val4, null);
			return true;
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogWarning((object)("[ShootZombies] TryFireViaOriginalDartAction failed: " + ex.Message));
			}
		}
		finally
		{
			if ((Object)(object)val4 != (Object)null && num.HasValue)
			{
				TrySetFloatFieldOrProperty(val4, "dartCollisionSize", num.Value);
			}
			if (_actionRaycastDartSpawnTransformField != null && (Object)(object)val4 != (Object)null)
			{
				_actionRaycastDartSpawnTransformField.SetValue(val4, val2);
			}
			if ((Object)val != (Object)null)
			{
				Object.Destroy((Object)(object)val);
			}
		}
		return false;
	}

	private static Vector3 ResolveShotUpVector(Transform referenceTransform, Vector3 forward)
	{
		Vector3 val = ((forward.sqrMagnitude > 1E-06f) ? forward.normalized : Vector3.forward);
		Vector3 val2 = (((Object)referenceTransform != (Object)null) ? referenceTransform.up : Vector3.up);
		if (Mathf.Abs(Vector3.Dot(val, val2.normalized)) > 0.98f)
		{
			val2 = (((Object)referenceTransform != (Object)null) ? referenceTransform.right : Vector3.right);
		}
		if (val2.sqrMagnitude <= 1E-06f)
		{
			val2 = Vector3.up;
		}
		return val2.normalized;
	}

	private static Transform ResolveDartSpawnTransform(Item heldBlowgunItem)
	{
		Transform val = ResolveCurrentWeaponMuzzle(heldBlowgunItem);
		if ((Object)val != (Object)null)
		{
			return val;
		}
		if (!((Object)heldBlowgunItem != (Object)null))
		{
			return null;
		}
		return ((Component)heldBlowgunItem).transform;
	}

	private static Transform ResolveCurrentWeaponMuzzle(Item heldBlowgunItem)
	{
		if ((Object)_localWeaponMuzzle != (Object)null)
		{
			return _localWeaponMuzzle;
		}
		Transform val = ItemPatch.TryGetMuzzleMarker(heldBlowgunItem);
		if ((Object)val != (Object)null)
		{
			return val;
		}
		return null;
	}

	private static float TryGetFloatFieldOrProperty(object target, string memberName, float fallbackValue)
	{
		if (target == null || string.IsNullOrEmpty(memberName))
		{
			return fallbackValue;
		}
		Type type = target.GetType();
		FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field != null && field.FieldType == typeof(float))
		{
			return (float)field.GetValue(target);
		}
		PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property != null && property.CanRead && property.PropertyType == typeof(float))
		{
			return (float)property.GetValue(target);
		}
		return fallbackValue;
	}

	private static float? TryGetOptionalFloatFieldOrProperty(object target, string memberName)
	{
		if (target == null || string.IsNullOrEmpty(memberName))
		{
			return null;
		}
		ReflectionMemberCacheEntry cachedMemberAccessor = GetCachedMemberAccessor(target.GetType(), memberName, typeof(float));
		FieldInfo field = cachedMemberAccessor.Field;
		if (field != null)
		{
			return (float)field.GetValue(target);
		}
		PropertyInfo property = cachedMemberAccessor.Property;
		if (property != null && property.CanRead)
		{
			return (float)property.GetValue(target);
		}
		return null;
	}

	private static float GetConfiguredHitScanDistance(object raycastDartComponent = null)
	{
		return Mathf.Max(TryGetFloatFieldOrProperty(raycastDartComponent, "maxDistance", DefaultHitScanDistance), 0.25f);
	}

	private static float GetConfiguredBulletSize(object raycastDartComponent = null)
	{
		return Mathf.Max(DefaultHitScanRadius, 0.01f);
	}

	private static void TrySetBoolFieldOrProperty(object target, string memberName, bool value2)
	{
		if (target == null || string.IsNullOrEmpty(memberName))
		{
			return;
		}
		ReflectionMemberCacheEntry cachedMemberAccessor = GetCachedMemberAccessor(target.GetType(), memberName, typeof(bool));
		FieldInfo field = cachedMemberAccessor.Field;
		if (field != null)
		{
			field.SetValue(target, value2);
			return;
		}
		PropertyInfo property = cachedMemberAccessor.Property;
		if (property != null && property.CanWrite)
		{
			property.SetValue(target, value2);
		}
	}

	private static ReflectionMemberCacheEntry GetCachedMemberAccessor(Type type, string memberName, Type valueType)
	{
		if (type == null)
		{
			return new ReflectionMemberCacheEntry();
		}
		MemberAccessorCacheKey key = new MemberAccessorCacheKey(type, memberName, valueType);
		if (_reflectionMemberCache.TryGetValue(key, out var value))
		{
			return value;
		}
		value = new ReflectionMemberCacheEntry
		{
			Field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
			Property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
		};
		if (value.Field != null && value.Field.FieldType != valueType)
		{
			value.Field = null;
		}
		if (value.Property != null && value.Property.PropertyType != valueType)
		{
			value.Property = null;
		}
		_reflectionMemberCache[key] = value;
		return value;
	}

	private static void EnsureInfiniteUses(Item item)
	{
		if ((Object)item == (Object)null || Time.time - _lastUseSyncTime < 0.1f)
		{
			return;
		}
		_lastUseSyncTime = Time.time;
		try
		{
			if (_itemGetDataGenericMethod == null)
			{
				_itemGetDataGenericMethod = typeof(Item).GetMethods(BindingFlags.Instance | BindingFlags.Public).FirstOrDefault((MethodInfo m) => m.Name == "GetData" && m.IsGenericMethod);
			}
			if (_optionableIntItemDataType == null)
			{
				_optionableIntItemDataType = typeof(Item).Assembly.GetTypes().FirstOrDefault((Type t) => t.Name == "OptionableIntItemData");
			}
			if (_itemGetDataGenericMethod == null || _optionableIntItemDataType == null)
			{
				return;
			}
			if (_usesDataKey == null)
			{
				_usesDataKey = typeof(Item).GetField("UsesKey", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
				if (_usesDataKey == null)
				{
					_usesDataKey = (object)(DataEntryKey)2;
				}
			}
			MethodInfo methodInfo = _itemGetDataGenericMethod.MakeGenericMethod(_optionableIntItemDataType);
			ParameterInfo[] parameters = methodInfo.GetParameters();
			object[] array = new object[parameters.Length];
			for (int num = 0; num < parameters.Length; num++)
			{
				if (parameters[num].ParameterType == typeof(DataEntryKey))
				{
					array[num] = _usesDataKey;
				}
				else
				{
					array[num] = null;
				}
			}
			object obj = methodInfo.Invoke(item, array);
			if (obj != null)
			{
				FieldInfo fieldInfo = obj.GetType().GetField("HasData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (fieldInfo != null)
				{
					fieldInfo.SetValue(obj, true);
				}
				if (_optionableIntValueField == null)
				{
					_optionableIntValueField = obj.GetType().GetField("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}
				if (_optionableIntValueField != null)
				{
					_optionableIntValueField.SetValue(obj, 9999);
				}
			}
		}
		catch
		{
		}
	}

	private static void DisableConsumableActions(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return;
		}
		try
		{
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
		foreach (Component val in componentsInChildren)
		{
			if (!((Object)val == (Object)null))
			{
				Object.Destroy((Object)val);
			}
		}
	}

	private float GetBlowgunCastProgress()
	{
		if ((Object)_localCharacter == (Object)null)
		{
			return 0f;
		}
		Item heldBlowgunItem = GetHeldBlowgunItem();
		if ((Object)heldBlowgunItem != (Object)null)
		{
			FieldInfo field = ((object)heldBlowgunItem).GetType().GetField("<castProgress>k__BackingField", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				return (float)field.GetValue(heldBlowgunItem);
			}
		}
		return 0f;
	}

	private Vector3 GetWeaponPosition()
	{
		if ((Object)_localCharacter == (Object)null)
		{
			return Vector3.zero;
		}
		Item heldBlowgunItem = GetHeldBlowgunItem();
		Transform val = ResolveCurrentWeaponMuzzle(heldBlowgunItem);
		if ((Object)val != (Object)null)
		{
			return val.position;
		}
		if ((Object)heldBlowgunItem != (Object)null)
		{
			if ((Object)heldBlowgunItem.mainRenderer != (Object)null)
			{
				Bounds bounds = heldBlowgunItem.mainRenderer.bounds;
				return bounds.center + ((Component)heldBlowgunItem.mainRenderer).transform.forward * Mathf.Max(bounds.extents.z, 0.05f);
			}
			return ((Component)heldBlowgunItem).transform.position;
		}
		if ((Object)(_localCharacter.refs?.view) != (Object)null)
		{
			return ((Component)_localCharacter.refs.view).transform.position + ((Component)_localCharacter.refs.view).transform.forward * 0.45f;
		}
		return ((Component)_localCharacter).transform.position + ((Component)_localCharacter).transform.forward * 0.45f;
	}

	private Vector3 GetPreferredSoundPosition(Vector3 weaponPosition, Vector3 aimForward)
	{
		Transform val = null;
		if ((Object)Camera.main != (Object)null)
		{
			val = ((Component)Camera.main).transform;
		}
		else if ((Object)(_localCharacter?.refs?.view) != (Object)null)
		{
			val = ((Component)_localCharacter.refs.view).transform;
		}
		if ((Object)val == (Object)null)
		{
			return weaponPosition;
		}
		Vector3 val2 = weaponPosition - val.position;
		if (val2.sqrMagnitude > 1E-06f && Vector3.Dot(val.forward, val2.normalized) >= 0.12f)
		{
			return weaponPosition;
		}
		Vector3 val3 = ((aimForward.sqrMagnitude > 1E-06f) ? aimForward.normalized : val.forward);
		return val.position + val3 * 0.55f;
	}

	private bool TrySendRemoteShotEffects(Vector3 muzzlePosition, Vector3 muzzleDirection, Vector3 soundPosition, bool hasImpact, Vector3 impactPosition)
	{
		Character val = Character.localCharacter ?? _localCharacter;
		if (!HasOnlineRoomSession() || PhotonNetwork.OfflineMode || (Object)val == (Object)null)
		{
			return false;
		}
		PhotonView val2 = val.refs?.view;
		if ((Object)val2 == (Object)null)
		{
			val2 = ((Component)val).GetComponent<PhotonView>() ?? ((Component)val).GetComponentInParent<PhotonView>();
		}
		if (soundPosition == Vector3.zero)
		{
			soundPosition = muzzlePosition;
		}
		object[] customEventContent = new object[7]
		{
			((Object)val2 != (Object)null) ? val2.ViewID : 0,
			muzzlePosition,
			muzzleDirection,
			soundPosition,
			hasImpact,
			impactPosition,
			NormalizeAkSoundSelection(AkSoundSelection?.Value)
		};
		RaiseEventOptions raiseEventOptions = new RaiseEventOptions
		{
			Receivers = ReceiverGroup.Others
		};
		return PhotonNetwork.RaiseEvent(RemoteShotEffectsEventCode, customEventContent, raiseEventOptions, SendOptions.SendReliable);
	}

	public void HitZombie(Character zombie, Vector3? origin = null)
	{
		if ((Object)zombie == (Object)null)
		{
			return;
		}
		if (TrySendZombieHitEvent(zombie, origin))
		{
			return;
		}
		ApplyZombieHitLocal(zombie, origin);
	}

	private static bool TrySendZombieHitEvent(Character zombie, Vector3? origin)
	{
		if (!ShouldRelayZombieHitToHost())
		{
			return false;
		}
		PhotonView val = ResolveZombiePhotonView(zombie);
		if ((Object)val == (Object)null)
		{
			return false;
		}
		bool flag = origin.HasValue && IsFiniteVector(origin.Value);
		object[] customEventContent = new object[3]
		{
			val.ViewID,
			flag,
			flag ? ((object)origin.Value) : ((object)Vector3.zero)
		};
		RaiseEventOptions raiseEventOptions = new RaiseEventOptions
		{
			Receivers = (ReceiverGroup)1
		};
		return PhotonNetwork.RaiseEvent(ZombieHitEventCode, customEventContent, raiseEventOptions, SendOptions.SendReliable);
	}

	private static PhotonView ResolveZombiePhotonView(Character zombie)
	{
		if ((Object)zombie == (Object)null)
		{
			return null;
		}
		MushroomZombie val = ((Component)zombie).GetComponent<MushroomZombie>() ?? ((Component)zombie).GetComponentInParent<MushroomZombie>() ?? ((Component)zombie).GetComponentInChildren<MushroomZombie>(true);
		GameObject val2 = (((Object)val != (Object)null) ? ((Component)val).gameObject : ((Component)((Component)zombie).transform.root).gameObject);
		return ((Component)zombie).GetComponent<PhotonView>() ?? ((Component)zombie).GetComponentInParent<PhotonView>() ?? (((Object)val2 != (Object)null) ? val2.GetComponent<PhotonView>() : null);
	}

	private void HandlePlayerShotStatus(Character targetCharacter)
	{
		if ((Object)targetCharacter == (Object)null || targetCharacter.isZombie || targetCharacter.isBot)
		{
			return;
		}
		if (TrySendPlayerShotStatusEvent(targetCharacter))
		{
			return;
		}
		ApplyPlayerShotStatusIfOwnedLocal(targetCharacter);
	}

	private static bool TrySendPlayerShotStatusEvent(Character targetCharacter)
	{
		if (!HasOnlineRoomSession() || PhotonNetwork.OfflineMode || (Object)targetCharacter == (Object)null || targetCharacter.isZombie || targetCharacter.isBot)
		{
			return false;
		}
		PhotonView val = targetCharacter.refs?.view ?? ((Component)targetCharacter).GetComponent<PhotonView>() ?? ((Component)targetCharacter).GetComponentInParent<PhotonView>();
		if ((Object)val == (Object)null || val.IsMine || val.OwnerActorNr <= 0)
		{
			return false;
		}
		object[] customEventContent = new object[1] { val.ViewID };
		RaiseEventOptions raiseEventOptions = new RaiseEventOptions
		{
			TargetActors = new int[1] { val.OwnerActorNr }
		};
		return PhotonNetwork.RaiseEvent(PlayerShotStatusEventCode, customEventContent, raiseEventOptions, SendOptions.SendReliable);
	}

	private static void ApplyPlayerShotStatusIfOwnedLocal(Character targetCharacter)
	{
		if ((Object)targetCharacter == (Object)null || targetCharacter.isZombie || targetCharacter.isBot)
		{
			return;
		}
		PhotonView val = targetCharacter.refs?.view ?? ((Component)targetCharacter).GetComponent<PhotonView>() ?? ((Component)targetCharacter).GetComponentInParent<PhotonView>();
		if ((Object)val != (Object)null && !val.IsMine)
		{
			return;
		}
		CharacterAfflictions afflictions = targetCharacter.refs?.afflictions;
		if (afflictions == null)
		{
			return;
		}
		float currentStatus = afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE.Spores);
		if (currentStatus > 0f)
		{
			float amount = Mathf.Max(afflictions.GetStatusCap(CharacterAfflictions.STATUSTYPE.Spores) * PlayerShotSporeReductionFraction, 0.025f);
			afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Spores, amount);
		}
		else
		{
			float amount2 = Mathf.Max(afflictions.GetStatusCap(CharacterAfflictions.STATUSTYPE.Cold) * PlayerShotColdFraction, 0.025f);
			afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Cold, amount2);
		}
	}

	private void ApplyZombieHitLocal(Character zombie, Vector3? origin = null)
	{
		MushroomZombie val = ((Component)zombie).GetComponent<MushroomZombie>() ?? ((Component)zombie).GetComponentInParent<MushroomZombie>() ?? ((Component)zombie).GetComponentInChildren<MushroomZombie>(true);
		GameObject val2 = (((Object)val != (Object)null) ? ((Component)val).gameObject : ((Component)((Component)zombie).transform.root).gameObject);
		if (!CanMutateZombie(((Component)zombie).GetComponent<PhotonView>() ?? ((Component)zombie).GetComponentInParent<PhotonView>() ?? (((Object)val2 != (Object)null) ? val2.GetComponent<PhotonView>() : null)))
		{
			return;
		}
		if ((Object)zombie != (Object)null && (Object)((Component)zombie).gameObject != (Object)null)
		{
			((MonoBehaviour)this).StartCoroutine(ZombieHitEffect(zombie));
		}
		ApplyZombieKnockback(zombie, origin);
		float num = Mathf.Max(ZombieTimeReduction.Value, 0f);
		bool flag = false;
		bool expired = false;
		if ((Object)val2 != (Object)null && num > 0f)
		{
			flag = ZombieSpawner.TryReduceZombieLifetime(val2, num, out expired);
			if (flag)
			{
				float health01 = expired ? 0f : (ZombieSpawner.TryGetZombieHealth01(val2, out var currentHealth01) ? currentHealth01 : 1f);
				ZombieHealthBar.SetHealth(val2, health01);
				BroadcastZombieHealth(zombie, health01);
			}
		}
		if (!((Object)val != (Object)null))
		{
			return;
		}
		FieldInfo field = ((object)val).GetType().GetField("survivalTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field == null)
		{
			field = ((object)val).GetType().GetField("lifetime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		}
		if (field == null)
		{
			field = ((object)val).GetType().GetField("_survivalTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		}
		if (field == null)
		{
			field = ((object)val).GetType().GetField("_lifetime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		}
		if (field != null)
		{
			float num2 = Mathf.Max((float)field.GetValue(val) - num, 0f);
			field.SetValue(val, num2);
			if (!flag)
			{
				float health01 = Mathf.Clamp01(num2 / Mathf.Max(ZombieMaxLifetime.Value, 1f));
				ZombieHealthBar.SetHealth(zombie, health01);
				BroadcastZombieHealth(zombie, health01);
			}
			if (!flag && !(num2 > 0f))
			{
				ZombieSpawner.ExpireZombie(val2);
			}
		}
	}

	private static void BroadcastZombieHealth(Character zombie, float health01)
	{
		if (!HasOnlineRoomSession() || PhotonNetwork.OfflineMode)
		{
			return;
		}
		PhotonView view = ResolveZombiePhotonView(zombie);
		if ((Object)view == (Object)null)
		{
			return;
		}
		object[] customEventContent = new object[2]
		{
			view.ViewID,
			Mathf.Clamp01(health01)
		};
		RaiseEventOptions raiseEventOptions = new RaiseEventOptions
		{
			Receivers = ReceiverGroup.Others
		};
		PhotonNetwork.RaiseEvent(ZombieHealthEventCode, customEventContent, raiseEventOptions, SendOptions.SendUnreliable);
	}

	private static void ApplyZombieKnockback(Character zombie, Vector3? origin = null)
	{
		if ((Object)zombie == (Object)null)
		{
			return;
		}
		float num = GetZombieKnockbackForceRuntime();
		if (num <= 0f)
		{
			return;
		}
		try
		{
			Vector3 center = zombie.Center;
			Vector3 val = center;
			val = ((origin.HasValue && IsFiniteVector(origin.Value)) ? origin.Value : (((Object)_localWeaponMuzzle != (Object)null) ? _localWeaponMuzzle.position : ((!((Object)_localCharacter != (Object)null)) ? (center - ((Component)zombie).transform.forward * 0.35f) : _localCharacter.Center)));
			Vector3 val2 = center - val;
			val2.y = Mathf.Max(val2.y, 0.18f);
			if (!IsFiniteVector(val2) || val2.sqrMagnitude < 1E-06f)
			{
				val2 = ((Component)zombie).transform.forward;
				val2.y = 0.18f;
			}
			val2.Normalize();
			Vector3 val3 = val2 * num;
			if (_characterAddForceMethod == null)
			{
				_characterAddForceMethod = typeof(Character).GetMethod("AddForce", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[3]
				{
					typeof(Vector3),
					typeof(float),
					typeof(float)
				}, null);
			}
			if (_characterAddForceMethod != null)
			{
				_characterAddForceMethod.Invoke(zombie, new object[3] { val3, 1f, 1f });
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogWarning((object)("[ShootZombies] ApplyZombieKnockback failed: " + ex.Message));
			}
		}
	}

	private static bool CanMutateZombie(PhotonView zombieView)
	{
		if (!HasOnlineRoomSession())
		{
			return true;
		}
		if ((Object)zombieView == (Object)null)
		{
			return PhotonNetwork.IsMasterClient;
		}
		if (!zombieView.IsMine)
		{
			return PhotonNetwork.IsMasterClient;
		}
		return true;
	}

	private IEnumerator ZombieHitEffect(Character zombie)
	{
		if ((Object)zombie == (Object)null)
		{
			yield break;
		}
		Vector3 originalPosition = ((Component)zombie).transform.position;
		Vector3 val = default(Vector3);
		for (int i = 0; i < 3; i++)
		{
			if ((Object)zombie == (Object)null)
			{
				break;
			}
			val = new Vector3(Random.Range(-0.1f, 0.1f), 0f, Random.Range(-0.1f, 0.1f));
			((Component)zombie).transform.position = originalPosition + val;
			yield return (object)new WaitForSeconds(0.05f);
			if (!((Object)zombie == (Object)null))
			{
				((Component)zombie).transform.position = originalPosition;
				yield return (object)new WaitForSeconds(0.05f);
				continue;
			}
			break;
		}
	}

	private void TriggerZombieFall(Character zombie)
	{
		if ((Object)zombie == (Object)null)
		{
			return;
		}
		try
		{
			MushroomZombie component = ((Component)zombie).GetComponent<MushroomZombie>();
			if ((Object)component != (Object)null)
			{
				((Behaviour)component).enabled = false;
			}
			FieldInfo field = ((object)zombie).GetType().GetField("agent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field == null)
			{
				field = ((object)zombie).GetType().GetField("_agent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			if (field == null)
			{
				field = ((object)zombie).GetType().GetField("navMeshAgent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			}
			if (field != null)
			{
				object value = field.GetValue(zombie);
				if (value != null)
				{
					PropertyInfo property = value.GetType().GetProperty("enabled");
					if (property != null)
					{
						property.SetValue(value, false);
					}
				}
			}
			if ((Object)zombie.data != (Object)null)
			{
				FieldInfo field2 = ((object)zombie.data).GetType().GetField("dead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field2 != null)
				{
					field2.SetValue(zombie.data, true);
				}
			}
			MethodInfo method = ((object)zombie).GetType().GetMethod("Fall", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method != null)
			{
				try
				{
					ParameterInfo[] parameters = method.GetParameters();
					if (parameters.Length == 0)
					{
						method.Invoke(zombie, new object[0]);
					}
					else
					{
						object[] array = new object[parameters.Length];
						for (int i = 0; i < parameters.Length; i++)
						{
							if (parameters[i].HasDefaultValue)
							{
								array[i] = parameters[i].DefaultValue;
							}
							else if (parameters[i].ParameterType == typeof(bool))
							{
								array[i] = true;
							}
							else if (parameters[i].ParameterType == typeof(float))
							{
								array[i] = 0f;
							}
							else
							{
								array[i] = null;
							}
						}
						method.Invoke(zombie, array);
					}
				}
				catch (Exception)
				{
				}
			}
			Character.CharacterRefs refs = zombie.refs;
			if (refs == null)
			{
				return;
			}
			PropertyInfo property2 = ((object)refs).GetType().GetProperty("ragdoll", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property2 != null)
			{
				object value2 = property2.GetValue(refs);
				if (value2 != null)
				{
					MethodInfo method2 = value2.GetType().GetMethod("ToggleRagdoll", BindingFlags.Instance | BindingFlags.Public);
					if (method2 != null)
					{
						method2.Invoke(value2, new object[1] { true });
					}
					PropertyInfo property3 = value2.GetType().GetProperty("ragdollEnabled", BindingFlags.Instance | BindingFlags.Public);
					if (property3 != null)
					{
						property3.SetValue(value2, true);
					}
					FieldInfo field3 = value2.GetType().GetField("enabled", BindingFlags.Instance | BindingFlags.Public);
					if (field3 != null)
					{
						field3.SetValue(value2, true);
					}
				}
			}
			FieldInfo field4 = ((object)refs).GetType().GetField("partList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (!(field4 != null))
			{
				return;
			}
			object value3 = field4.GetValue(refs);
			if (value3 == null || !(value3 is IList list))
			{
				return;
			}
			int num = 0;
			foreach (object item in list)
			{
				if (item == null)
				{
					continue;
				}
				GameObject val = (GameObject)((item is GameObject) ? item : null);
				if ((Object)val != (Object)null)
				{
					Rigidbody component2 = val.GetComponent<Rigidbody>();
					if ((Object)component2 != (Object)null)
					{
						component2.isKinematic = false;
						component2.useGravity = true;
						num++;
					}
					continue;
				}
				Component val2 = (Component)((item is Component) ? item : null);
				if ((Object)val2 != (Object)null)
				{
					Rigidbody component3 = val2.GetComponent<Rigidbody>();
					if ((Object)component3 != (Object)null)
					{
						component3.isKinematic = false;
						component3.useGravity = true;
						num++;
					}
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private IEnumerator DestroyZombieAfterDelay(Character zombie, float delay)
	{
		yield return (object)new WaitForSeconds(delay);
		if (!((Object)zombie != (Object)null) || !((Object)((Component)zombie).gameObject != (Object)null))
		{
			yield break;
		}
		PhotonView component = ((Component)zombie).GetComponent<PhotonView>();
		if ((Object)component != (Object)null)
		{
			if (component.IsMine)
			{
				PhotonNetwork.Destroy(((Component)zombie).gameObject);
			}
			else if (PhotonNetwork.IsMasterClient)
			{
				PhotonNetwork.Destroy(component);
			}
		}
		else
		{
			Object.Destroy((Object)((Component)zombie).gameObject);
		}
	}
}
