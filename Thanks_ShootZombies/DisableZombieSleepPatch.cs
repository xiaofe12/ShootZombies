using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;

namespace ShootZombies;

[HarmonyPatch]
public static class DisableZombieSleepPatch
{
	private const float DefaultTargetSearchInterval = 10f;

	private const float DefaultSamePlayerBiteCooldown = 5f;

	private const float DefaultPostBiteRecoveryTime = 3f;

	private const int LungeRecoveryState = 5;

	private const float PostBiteRecoverySyncThreshold = PostBiteRecoveryFallWindow + 0.01f;

	internal const float PostBiteRecoveryFallWindow = 0.45f;

	private const float CloseRangeLungeDistanceScale = 0.35f;

	private const float CloseRangeLungeDistanceMin = 2.75f;

	private const float CloseRangeLungeDistanceMax = 4.25f;

	private const float MinimumChaseBeforeLunge = 0.65f;

	private const float MaximumLungeVerticalDelta = 2.25f;

	private static readonly MethodInfo CharacterFallMethod = AccessTools.Method(typeof(Character), "Fall", new System.Type[2]
	{
		typeof(float),
		typeof(float)
	});

	private static readonly MethodInfo StartChasingMethod = AccessTools.Method(typeof(MushroomZombie), "StartChasing", System.Type.EmptyTypes);

	private static readonly MethodInfo StartLungingMethod = AccessTools.Method(typeof(MushroomZombie), "StartLunging", System.Type.EmptyTypes);

	private static readonly MethodInfo CanSeeTargetMethod = AccessTools.Method(typeof(MushroomZombie), "CanSeeTarget", new System.Type[1] { typeof(Character) });

	private static readonly FieldInfo LungeRecoveryTimerField = AccessTools.Field(typeof(MushroomZombie), "timeSpentRecoveringFromLunge");

	private static readonly FieldInfo TimeSpentChasingField = AccessTools.Field(typeof(MushroomZombie), "timeSpentChasing");

	private static readonly Dictionary<int, float> PostBiteRecoveryUntilByZombieViewId = new Dictionary<int, float>();

	private static bool UseVanillaBehavior()
	{
		return Plugin.IsVanillaZombieBehaviorDifficultyRuntime();
	}

	private static float GetTargetSearchInterval()
	{
		return Plugin.GetZombieTargetSearchIntervalRuntime();
	}

	private static float GetSamePlayerBiteCooldown()
	{
		return Plugin.GetZombieSamePlayerBiteCooldownRuntime();
	}

	private static float GetPostBiteRecoveryTime()
	{
		return Plugin.GetZombieBiteRecoveryTimeRuntime();
	}

	private static int GetZombieViewId(MushroomZombie zombie)
	{
		if ((Object)zombie == (Object)null || (Object)zombie.photonView == (Object)null)
		{
			return -1;
		}
		return zombie.photonView.ViewID;
	}

	private static void ResetLungeRecoveryTimer(MushroomZombie zombie)
	{
		if ((Object)zombie != (Object)null && LungeRecoveryTimerField != null)
		{
			LungeRecoveryTimerField.SetValue(zombie, 0f);
		}
	}

	private static void ClearPostBiteRecoveryOverride(MushroomZombie zombie)
	{
		int zombieViewId = GetZombieViewId(zombie);
		if (zombieViewId >= 0)
		{
			PostBiteRecoveryUntilByZombieViewId.Remove(zombieViewId);
		}
		ResetLungeRecoveryTimer(zombie);
	}

	private static void SetPostBiteRecoveryOverride(MushroomZombie zombie, float duration)
	{
		int zombieViewId = GetZombieViewId(zombie);
		if (zombieViewId < 0)
		{
			return;
		}
		ResetLungeRecoveryTimer(zombie);
		if (duration <= 0f)
		{
			PostBiteRecoveryUntilByZombieViewId.Remove(zombieViewId);
			return;
		}
		PostBiteRecoveryUntilByZombieViewId[zombieViewId] = Time.time + duration;
	}

	private static bool TryGetPostBiteRecoveryOverride(MushroomZombie zombie, out float recoverAtTime)
	{
		recoverAtTime = 0f;
		int zombieViewId = GetZombieViewId(zombie);
		return zombieViewId >= 0 && PostBiteRecoveryUntilByZombieViewId.TryGetValue(zombieViewId, out recoverAtTime);
	}

	private static float GetCloseRangeLungeDistance(MushroomZombie zombie)
	{
		if ((Object)zombie == (Object)null)
		{
			return CloseRangeLungeDistanceMin;
		}
		return Mathf.Clamp(zombie.zombieLungeDistance * CloseRangeLungeDistanceScale, CloseRangeLungeDistanceMin, CloseRangeLungeDistanceMax);
	}

	private static float GetTimeSpentChasing(MushroomZombie zombie)
	{
		if ((Object)zombie == (Object)null || TimeSpentChasingField == null)
		{
			return 0f;
		}
		object value = TimeSpentChasingField.GetValue(zombie);
		return (value is float result) ? result : 0f;
	}

	private static bool CanSeeTarget(MushroomZombie zombie, Character target)
	{
		if ((Object)zombie == (Object)null || (Object)target == (Object)null || CanSeeTargetMethod == null)
		{
			return false;
		}
		object obj = CanSeeTargetMethod.Invoke(zombie, new object[1] { target });
		return obj is bool flag && flag;
	}

	internal static void ResetRuntimeState()
	{
		PostBiteRecoveryUntilByZombieViewId.Clear();
	}

	[HarmonyPatch(typeof(MushroomZombie), "TryLookForTarget")]
	[HarmonyPrefix]
	private static bool TryLookForTargetPrefix(MushroomZombie __instance)
	{
		if ((Object)__instance == (Object)null || (Object)__instance.photonView == (Object)null || !__instance.photonView.IsMine)
		{
			return true;
		}
		float targetSearchInterval = GetTargetSearchInterval();
		if (__instance.sinceLookForTarget < targetSearchInterval)
		{
			return false;
		}
		Character component = ((Component)__instance).GetComponent<Character>();
		if ((Object)component == (Object)null)
		{
			__instance.sinceLookForTarget = 0f;
			return false;
		}
		Character character = null;
		float num = float.MaxValue;
		foreach (Character allCharacter in Character.AllCharacters)
		{
			if ((Object)allCharacter == (Object)null || (Object)allCharacter == (Object)component || allCharacter.isBot || allCharacter.data.dead || allCharacter.data.fullyPassedOut)
			{
				continue;
			}
			float num2 = Vector3.Distance(allCharacter.Center, component.Center);
			if ((Object)character == (Object)null || num2 < num)
			{
				character = allCharacter;
				num = num2;
			}
		}
		int num3 = ((Object)character == (Object)null) ? (-1) : character.photonView.ViewID;
		__instance.photonView.RPC("RPCA_SetCurrentTarget", RpcTarget.All, num3, 0f);
		__instance.sinceLookForTarget = 0f;
		return false;
	}

	[HarmonyPatch(typeof(MushroomZombie), "TryLunge")]
	[HarmonyPrefix]
	private static bool TryLungePrefix(MushroomZombie __instance)
	{
		if (UseVanillaBehavior())
		{
			return true;
		}
		if ((Object)__instance == (Object)null || StartLungingMethod == null)
		{
			return true;
		}
		Character currentTarget = __instance.currentTarget;
		Character component = ((Component)__instance).GetComponent<Character>();
		if ((Object)component == (Object)null || (Object)currentTarget == (Object)null)
		{
			return false;
		}
		if (__instance.currentState != MushroomZombie.State.Chasing || !component.data.isGrounded || !component.input.sprintIsPressed || !CanSeeTarget(__instance, currentTarget))
		{
			return false;
		}
		float timeSpentChasing = GetTimeSpentChasing(__instance);
		if (timeSpentChasing < Mathf.Max(__instance.chaseTimeBeforeSprint, MinimumChaseBeforeLunge))
		{
			return false;
		}
		Vector3 val = component.Center;
		Vector3 val2 = currentTarget.Center;
		val.y = 0f;
		val2.y = 0f;
		float num = Vector3.Distance(val, val2);
		float num2 = Mathf.Abs(currentTarget.Center.y - component.Center.y);
		if (num > GetCloseRangeLungeDistance(__instance) || num2 > MaximumLungeVerticalDelta)
		{
			return false;
		}
		StartLungingMethod.Invoke(__instance, null);
		return false;
	}

	[HarmonyPatch(typeof(MushroomZombie), "OnBitCharacter")]
	[HarmonyPostfix]
	private static void OnBitCharacterPostfix(MushroomZombie __instance)
	{
		if ((Object)__instance == (Object)null || (Object)__instance.photonView == (Object)null)
		{
			return;
		}
		Character component = ((Component)__instance).GetComponent<Character>();
		if ((Object)component == (Object)null || (Object)component.photonView == (Object)null)
		{
			return;
		}
		float postBiteRecoveryTime = GetPostBiteRecoveryTime();
		SetPostBiteRecoveryOverride(__instance, postBiteRecoveryTime);
		float num = Mathf.Min(PostBiteRecoveryFallWindow, postBiteRecoveryTime);
		component.data.fallSeconds = num;
		component.photonView.RPC("RPCA_Fall", RpcTarget.All, num);
		__instance.photonView.RPC("RPC_SyncState", RpcTarget.All, LungeRecoveryState, false, num, component.data.passedOut);
	}

	[HarmonyPatch(typeof(MushroomZombie), "RPC_SyncState")]
	[HarmonyPostfix]
	private static void RpcSyncStatePostfix(MushroomZombie __instance, int state, bool isSprinting, float fallSeconds, bool passedOut)
	{
		if ((Object)__instance == (Object)null)
		{
			return;
		}
		if (state == LungeRecoveryState)
		{
			if (fallSeconds <= PostBiteRecoverySyncThreshold)
			{
				SetPostBiteRecoveryOverride(__instance, GetPostBiteRecoveryTime());
			}
			else
			{
				ClearPostBiteRecoveryOverride(__instance);
			}
			return;
		}
		ClearPostBiteRecoveryOverride(__instance);
	}

	[HarmonyPatch(typeof(MushroomZombie), "DoLungeRecovery")]
	[HarmonyPrefix]
	private static bool DoLungeRecoveryPrefix(MushroomZombie __instance)
	{
		if ((Object)__instance == (Object)null)
		{
			return true;
		}
		if (!TryGetPostBiteRecoveryOverride(__instance, out var recoverAtTime))
		{
			return true;
		}
		Character component = ((Component)__instance).GetComponent<Character>();
		if ((Object)component == (Object)null)
		{
			ClearPostBiteRecoveryOverride(__instance);
			return true;
		}
		if (component.data.fallSeconds > 0f || component.data.passedOut || component.data.fullyPassedOut)
		{
			return false;
		}
		if (Time.time < recoverAtTime)
		{
			return false;
		}
		ClearPostBiteRecoveryOverride(__instance);
		if (StartChasingMethod == null)
		{
			return true;
		}
		StartChasingMethod.Invoke(__instance, null);
		return false;
	}

	[HarmonyPatch(typeof(MushroomZombieBiteCollider), "OnTriggerEnter")]
	[HarmonyPrefix]
	private static bool BiteColliderPrefix(MushroomZombieBiteCollider __instance, Collider other)
	{
		if (UseVanillaBehavior())
		{
			return true;
		}
		if ((Object)__instance == (Object)null || (Object)__instance.parentZombie == (Object)null)
		{
			return false;
		}
		float samePlayerBiteCooldown = GetSamePlayerBiteCooldown();
		if (Time.time - __instance.lastBitLocalCharacter < samePlayerBiteCooldown || !CharacterRagdoll.TryGetCharacterFromCollider(other, out var character) || !character.IsLocal)
		{
			return false;
		}
		__instance.lastBitLocalCharacter = Time.time;
		if (character.data.isSkeleton)
		{
			character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, __instance.parentZombie.biteInitialInjury / 8f * 2f);
		}
		else
		{
			character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Injury, __instance.parentZombie.biteInitialInjury);
		}
		character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Spores, __instance.parentZombie.biteInitialSpores);
		Affliction_ZombieBite affliction = new Affliction_ZombieBite(__instance.parentZombie.totalBiteSporesTime, __instance.parentZombie.biteDelayBeforeSpores, __instance.parentZombie.biteSporesPerSecond);
		character.refs.afflictions.AddAffliction(affliction);
		CharacterFallMethod?.Invoke(character, new object[2] { __instance.parentZombie.biteStunTime, 0f });
		__instance.parentZombie.OnBitCharacter(character);
		return false;
	}
}
