using System;
using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShootZombies;

public static class ZombieSpawner
{
	private struct VanillaZombieBehaviorDefaults
	{
		public float InitialWakeUpTime;

		public float LookAngleBeforeWakeup;

		public float DistanceBeforeWakeup;

		public float DistanceBeforeChase;

		public float ZombieSprintDistance;

		public float ChaseTimeBeforeSprint;

		public float ZombieLungeDistance;

		public float LungeTime;

		public float LungeRecoveryTime;
	}

	private struct ZombieTimer
	{
		public GameObject zombie;

		public float deathTime;
	}

	private const float MinSpawnRadiusFraction = 0.45f;

	private const float MinSpawnRadiusFloor = 6.5f;

	private const float MinSpawnRadiusFloorFraction = 0.75f;

	private const int SpawnPositionAttempts = 40;

	private const int SpawnDirectionAttemptsPerTarget = 6;

	private const float FrontSpawnPreferenceFraction = 0.6f;

	private const float RearSpawnDotThreshold = 0.05f;

	private const float FrontSpawnConeHalfAngle = 70f;

	private const float RearSpawnConeHalfAngle = 75f;

	private const float ViewConeRejectAngle = 55f;

	private const float VisibilityTargetHeight = 1.2f;

	private const float VisibilityPadding = 0.2f;

	private const float SpawnGroundProbeHeight = 12f;

	private const float SpawnGroundProbeDistance = 48f;

	private const float MaxSpawnSlopeAngle = 47f;

	private const float MaxSpawnBelowPlayer = 2.75f;

	private const float MaxSpawnAbovePlayer = 6.5f;

	private const float SpawnCapsuleBottomHeight = 0.55f;

	private const float SpawnCapsuleTopHeight = 1.55f;

	private const float SpawnCapsuleRadius = 0.24f;

	private const float SpawnSupportSampleRadius = 0.85f;

	private const int SpawnSupportSampleCount = 6;

	private const float MaxSpawnSupportHeightDelta = 1.1f;

	private const float SpawnGroundLift = 0.08f;

	private const float ZombieLifetimeCheckInterval = 0.25f;

	private const float ZombieDistanceCheckInterval = 0.75f;

	private static Coroutine _spawnCoroutine;

	private static HashSet<GameObject> _liveZombies = new HashSet<GameObject>();

	private static List<ZombieTimer> _zombieTimers = new List<ZombieTimer>();

	private static readonly List<GameObject> _staleZombieBuffer = new List<GameObject>();

	private static readonly List<Character> _alivePlayersBuffer = new List<Character>();

	private static GameObject _cachedLocalZombiePrefab;

	private static bool _cachedVanillaZombieBehaviorDefaultsResolved;

	private static VanillaZombieBehaviorDefaults _cachedVanillaZombieBehaviorDefaults;

	private static bool _loggedMissingLocalZombiePrefab;

	private static float _nextZombieLifetimeCheckTime;

	private static float _nextZombieDistanceCheckTime;

	private static bool IsGameplaySpawnScene()
	{
		Scene activeScene = SceneManager.GetActiveScene();
		if (!activeScene.IsValid() || !activeScene.isLoaded)
		{
			return false;
		}
		return Plugin.IsGameplaySceneRuntime(activeScene);
	}

	public static void StartZombieSpawning()
	{
		if (!Plugin.IsZombieSpawnFeatureEnabledRuntime() || !IsGameplaySpawnScene())
		{
			StopZombieSpawning();
			DestroyAllZombies();
			return;
		}
		if (Plugin.IsZombieSpawnFeatureEnabledRuntime())
		{
			if (_spawnCoroutine != null)
			{
				((MonoBehaviour)Plugin.Instance).StopCoroutine(_spawnCoroutine);
			}
			_spawnCoroutine = ((MonoBehaviour)Plugin.Instance).StartCoroutine(ZombieSpawnCoroutine());
		}
	}

	public static void StopZombieSpawning()
	{
		if (_spawnCoroutine != null)
		{
			((MonoBehaviour)Plugin.Instance).StopCoroutine(_spawnCoroutine);
			_spawnCoroutine = null;
		}
	}

	private static IEnumerator ZombieSpawnCoroutine()
	{
		yield return (object)new WaitForSeconds(GetNextSpawnDelay(firstWave: true));
		if (!IsGameplaySpawnScene())
		{
			yield break;
		}
		int nextSpawnCount = GetNextSpawnCount();
		SpawnZombiesAroundPlayers(nextSpawnCount);
		while (Plugin.IsZombieSpawnFeatureEnabledRuntime())
		{
			float nextSpawnDelay = GetNextSpawnDelay();
			yield return (object)new WaitForSeconds(nextSpawnDelay);
			if (!IsGameplaySpawnScene())
			{
				DestroyAllZombies();
				continue;
			}
			if (Plugin.HasGameplayAuthority())
			{
				int nextSpawnCount2 = GetNextSpawnCount();
				SpawnZombiesAroundPlayers(nextSpawnCount2);
			}
		}
	}

	private static void SpawnZombiesAroundPlayers(int count)
	{
		if (!Plugin.HasGameplayAuthority() || !IsGameplaySpawnScene())
		{
			return;
		}
		try
		{
			int currentZombieCount = GetCurrentZombieCount();
			int value = Plugin.MaxZombies.Value;
			if (currentZombieCount >= value || CollectAlivePlayers(_alivePlayersBuffer) == 0)
			{
				return;
			}
			int num = Mathf.Min(count, value - currentZombieCount);
			for (int i = 0; i < num; i++)
			{
				if (TryGetSpawnPosition(out var position, out var targetPlayer))
				{
					Quaternion rotation = GetSpawnRotation(position, targetPlayer);
					GameObject val = TryCreateZombieInstance(position, rotation);
					if ((Object)val != (Object)null)
					{
						_liveZombies.Add(val);
						float value2 = Plugin.ZombieMaxLifetime.Value;
						_zombieTimers.Add(new ZombieTimer
						{
							zombie = val,
							deathTime = Time.time + value2
						});
						ApplyZombieProperties(val);
					}
				}
			}
		}
		catch (Exception)
		{
		}
	}

	public static bool ContainsZombie(GameObject zombie)
	{
		return _liveZombies.Contains(zombie);
	}

	public static void RemoveZombie(GameObject zombie)
	{
		_liveZombies.Remove(zombie);
		_zombieTimers.RemoveAll((ZombieTimer t) => (Object)t.zombie == (Object)zombie);
	}

	public static bool TryReduceZombieLifetime(GameObject zombie, float seconds, out bool expired)
	{
		expired = false;
		if ((Object)zombie == (Object)null || seconds <= 0f)
		{
			return false;
		}
		for (int i = 0; i < _zombieTimers.Count; i++)
		{
			ZombieTimer value = _zombieTimers[i];
			if (!((Object)value.zombie != (Object)zombie))
			{
				value.deathTime -= seconds;
				if (value.deathTime <= Time.time)
				{
					expired = true;
					_zombieTimers.RemoveAt(i);
					DestroyZombie(zombie);
				}
				else
				{
					_zombieTimers[i] = value;
				}
				return true;
			}
		}
		return false;
	}

	public static void RefreshLiveZombieProperties()
	{
		if (!Plugin.HasGameplayAuthority() || !IsGameplaySpawnScene())
		{
			return;
		}
		CompactLiveZombieSet();
		foreach (GameObject liveZombie in _liveZombies)
		{
			if (!((Object)liveZombie == (Object)null))
			{
				ApplyZombieProperties(liveZombie);
			}
		}
	}

	private static GameObject TryCreateZombieInstance(Vector3 position, Quaternion rotation)
	{
		if (!IsGameplaySpawnScene())
		{
			return null;
		}
		if (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode)
		{
			try
			{
				return PhotonNetwork.Instantiate("MushroomZombie", position, rotation, (byte)0, (object[])null);
			}
			catch (Exception)
			{
			}
		}
		GameObject localZombiePrefab = ResolveLocalZombiePrefab();
		if ((Object)localZombiePrefab == (Object)null)
		{
			if (!_loggedMissingLocalZombiePrefab && Plugin.Log != null)
			{
				_loggedMissingLocalZombiePrefab = true;
				Plugin.Log.LogWarning((object)"[ShootZombies] Local zombie prefab could not be resolved for non-network gameplay.");
			}
			return null;
		}
		return Object.Instantiate<GameObject>(localZombiePrefab, position, rotation);
	}

	private static GameObject ResolveLocalZombiePrefab()
	{
		if ((Object)_cachedLocalZombiePrefab != (Object)null)
		{
			return _cachedLocalZombiePrefab;
		}
		try
		{
			_cachedLocalZombiePrefab = Resources.Load<GameObject>("MushroomZombie");
		}
		catch
		{
			_cachedLocalZombiePrefab = null;
		}
		if ((Object)_cachedLocalZombiePrefab == (Object)null)
		{
			try
			{
				GameObject[] array = Resources.FindObjectsOfTypeAll<GameObject>();
				GameObject val2 = null;
				foreach (GameObject val in array)
				{
					if ((Object)val == (Object)null || !string.Equals(((Object)val).name, "MushroomZombie", StringComparison.Ordinal))
					{
						continue;
					}
					if (!val.scene.IsValid())
					{
						_cachedLocalZombiePrefab = val;
						break;
					}
					if ((Object)val2 == (Object)null)
					{
						val2 = val;
					}
				}
				if ((Object)_cachedLocalZombiePrefab == (Object)null)
				{
					_cachedLocalZombiePrefab = val2;
				}
			}
			catch
			{
				_cachedLocalZombiePrefab = null;
			}
		}
		return _cachedLocalZombiePrefab;
	}

	private static bool TryGetVanillaZombieBehaviorDefaults(out VanillaZombieBehaviorDefaults defaults)
	{
		defaults = default(VanillaZombieBehaviorDefaults);
		if (_cachedVanillaZombieBehaviorDefaultsResolved)
		{
			defaults = _cachedVanillaZombieBehaviorDefaults;
			return true;
		}
		GameObject localZombiePrefab = ResolveLocalZombiePrefab();
		if ((Object)localZombiePrefab == (Object)null)
		{
			return false;
		}
		MushroomZombie component = localZombiePrefab.GetComponent<MushroomZombie>();
		if ((Object)component == (Object)null)
		{
			return false;
		}
		_cachedVanillaZombieBehaviorDefaults = new VanillaZombieBehaviorDefaults
		{
			InitialWakeUpTime = component.initialWakeUpTime,
			LookAngleBeforeWakeup = component.lookAngleBeforeWakeup,
			DistanceBeforeWakeup = component.distanceBeforeWakeup,
			DistanceBeforeChase = component.distanceBeforeChase,
			ZombieSprintDistance = component.zombieSprintDistance,
			ChaseTimeBeforeSprint = component.chaseTimeBeforeSprint,
			ZombieLungeDistance = component.zombieLungeDistance,
			LungeTime = component.lungeTime,
			LungeRecoveryTime = component.lungeRecoveryTime
		};
		_cachedVanillaZombieBehaviorDefaultsResolved = true;
		defaults = _cachedVanillaZombieBehaviorDefaults;
		return true;
	}

	public static void DestroyZombie(GameObject zombie)
	{
		if ((Object)zombie == (Object)null)
		{
			return;
		}
		_liveZombies.Remove(zombie);
		_zombieTimers.RemoveAll((ZombieTimer t) => (Object)t.zombie == (Object)zombie);
		if (!Plugin.HasOnlineRoomSession() && !PhotonNetwork.OfflineMode)
		{
			Object.Destroy((Object)zombie);
			return;
		}
		PhotonView component = zombie.GetComponent<PhotonView>();
		if ((Object)component != (Object)null)
		{
			if (component.IsMine || PhotonNetwork.IsMasterClient)
			{
				PhotonNetwork.Destroy(component);
			}
		}
		else if (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode)
		{
			PhotonNetwork.Destroy(zombie);
		}
		else
		{
			Object.Destroy((Object)zombie);
		}
	}

	private static void ApplyZombieProperties(GameObject zombieObj)
	{
		try
		{
			MushroomZombie component = zombieObj.GetComponent<MushroomZombie>();
			if ((Object)component != (Object)null)
			{
				if (Plugin.IsVanillaZombieBehaviorDifficultyRuntime())
				{
					if (TryGetVanillaZombieBehaviorDefaults(out var defaults))
					{
						component.initialWakeUpTime = 0f;
						component.lookAngleBeforeWakeup = defaults.LookAngleBeforeWakeup;
						component.distanceBeforeWakeup = defaults.DistanceBeforeWakeup;
						component.distanceBeforeChase = 0f;
						component.zombieSprintDistance = defaults.ZombieSprintDistance;
						component.chaseTimeBeforeSprint = defaults.ChaseTimeBeforeSprint;
						component.zombieLungeDistance = defaults.ZombieLungeDistance;
						component.lungeTime = defaults.LungeTime;
						component.lungeRecoveryTime = defaults.LungeRecoveryTime;
						component.sinceLookForTarget = Mathf.Max(component.sinceLookForTarget, Plugin.GetZombieTargetSearchIntervalRuntime());
					}
					return;
				}
				component.initialWakeUpTime = 0f;
				float zombieLookAngleBeforeWakeupRuntime = Plugin.GetZombieLookAngleBeforeWakeupRuntime();
				float zombieDistanceBeforeWakeupRuntime = Plugin.GetZombieDistanceBeforeWakeupRuntime();
				float zombieSprintDistanceRuntime = Plugin.GetZombieSprintDistanceRuntime();
				component.lookAngleBeforeWakeup = zombieLookAngleBeforeWakeupRuntime;
				if (zombieDistanceBeforeWakeupRuntime > 0f)
				{
					component.distanceBeforeWakeup = zombieDistanceBeforeWakeupRuntime;
				}
				component.distanceBeforeChase = 0f;
				if (zombieSprintDistanceRuntime > 0f)
				{
					component.zombieSprintDistance = Mathf.Max(zombieSprintDistanceRuntime, 3f);
				}
				component.chaseTimeBeforeSprint = Plugin.GetZombieChaseTimeBeforeSprintRuntime();
				component.zombieLungeDistance = Plugin.GetZombieLungeDistanceRuntime();
				component.lungeTime = Plugin.GetZombieLungeTimeRuntime();
				component.lungeRecoveryTime = Plugin.GetZombieLungeRecoveryTimeRuntime();
			}
		}
		catch (Exception)
		{
		}
	}

	public static void ClearZombies()
	{
		_liveZombies.Clear();
		_zombieTimers.Clear();
		_staleZombieBuffer.Clear();
		_alivePlayersBuffer.Clear();
		_nextZombieLifetimeCheckTime = 0f;
		_nextZombieDistanceCheckTime = 0f;
	}

	public static void UpdateZombieTimers()
	{
		if (!Plugin.HasGameplayAuthority())
		{
			return;
		}
		if (!IsGameplaySpawnScene())
		{
			DestroyAllZombies();
			return;
		}
		float time = Time.time;
		bool flag = time >= _nextZombieLifetimeCheckTime;
		bool flag2 = time >= _nextZombieDistanceCheckTime;
		if (!flag && !flag2)
		{
			return;
		}
		if (flag)
		{
			_nextZombieLifetimeCheckTime = time + ZombieLifetimeCheckInterval;
		}
		if (flag2)
		{
			_nextZombieDistanceCheckTime = time + ZombieDistanceCheckInterval;
		}
		CompactLiveZombieSet();
		float num = float.PositiveInfinity;
		if (flag2)
		{
			num = Mathf.Pow(Mathf.Clamp(Plugin.GetDerivedZombieDestroyDistanceRuntime(), 50f, 150f), 2f);
			CollectAlivePlayers(_alivePlayersBuffer);
		}
		for (int num2 = _zombieTimers.Count - 1; num2 >= 0; num2--)
		{
			ZombieTimer zombieTimer = _zombieTimers[num2];
			if ((Object)zombieTimer.zombie == (Object)null)
			{
				_zombieTimers.RemoveAt(num2);
			}
			else if (flag && time >= zombieTimer.deathTime)
			{
				_zombieTimers.RemoveAt(num2);
				DestroyZombie(zombieTimer.zombie);
			}
			else if (flag2 && GetClosestAlivePlayerDistanceSqr(zombieTimer.zombie.transform.position, _alivePlayersBuffer) > num)
			{
				_zombieTimers.RemoveAt(num2);
				DestroyZombie(zombieTimer.zombie);
			}
		}
	}

	private static int GetCurrentZombieCount()
	{
		CompactLiveZombieSet();
		return _liveZombies.Count;
	}

	private static float GetClosestAlivePlayerDistanceSqr(Vector3 position, List<Character> alivePlayers)
	{
		if (alivePlayers == null || alivePlayers.Count == 0)
		{
			return float.PositiveInfinity;
		}
		float num = float.PositiveInfinity;
		foreach (Character item in alivePlayers)
		{
			if (!((Object)item == (Object)null))
			{
				float num2 = (item.Center - position).sqrMagnitude;
				if (num2 < num)
				{
					num = num2;
				}
			}
		}
		return num;
	}

	private static Vector3 GetPlayersCenter()
	{
		if (Character.AllCharacters == null || Character.AllCharacters.Count == 0)
		{
			return Vector3.zero;
		}
		Vector3 val = Vector3.zero;
		int num = 0;
		foreach (Character allCharacter in Character.AllCharacters)
		{
			if ((Object)allCharacter != (Object)null && !allCharacter.isBot)
			{
				val += allCharacter.Center;
				num++;
			}
		}
		if (num <= 0)
		{
			return Vector3.zero;
		}
		return val / (float)num;
	}

	private static int CollectAlivePlayers(List<Character> buffer)
	{
		if (buffer == null)
		{
			return 0;
		}
		buffer.Clear();
		if (Character.AllCharacters == null)
		{
			return 0;
		}
		foreach (Character allCharacter in Character.AllCharacters)
		{
			if ((Object)allCharacter != (Object)null && !allCharacter.isBot && !allCharacter.isZombie)
			{
				buffer.Add(allCharacter);
			}
		}
		return buffer.Count;
	}

	private static float GetNextSpawnDelay(bool firstWave = false)
	{
		float num = Mathf.Max(Plugin.ZombieSpawnInterval.Value, 1f);
		float num2 = Mathf.Max(Plugin.GetDerivedZombieSpawnIntervalRandomRangeRuntime(), 0f);
		float num3 = Mathf.Max(num + Random.Range(0f - num2, num2), 1f);
		if (!firstWave)
		{
			return num3;
		}
		return Mathf.Clamp(num3 * 0.75f, 3f, 30f);
	}

	private static int GetNextSpawnCount()
	{
		return Mathf.Max(Plugin.GetDerivedZombieWaveSpawnCountRuntime(), 0);
	}

	private static bool TryGetSpawnPosition(out Vector3 position, out Character targetPlayer)
	{
		position = Vector3.zero;
		targetPlayer = null;
		List<Character> alivePlayers = _alivePlayersBuffer;
		CollectAlivePlayers(alivePlayers);
		if (alivePlayers.Count == 0)
		{
			return false;
		}
		float num = Mathf.Max(Plugin.GetDerivedZombieSpawnRadiusRuntime(), 8f);
		float num2 = Mathf.Max(Mathf.Min(num * MinSpawnRadiusFraction, num - 1f), Mathf.Min(MinSpawnRadiusFloor, num * MinSpawnRadiusFloorFraction));
		int num3 = Mathf.CeilToInt((float)SpawnPositionAttempts * FrontSpawnPreferenceFraction);
		for (int i = 0; i < SpawnPositionAttempts; i++)
		{
			Character val = alivePlayers[Random.Range(0, alivePlayers.Count)];
			if (!((Object)val == (Object)null) && TryBuildSpawnCandidate(val, alivePlayers, num2, num, i < num3, out var position2))
			{
				position = position2;
				targetPlayer = val;
				return true;
			}
		}
		return false;
	}

	private static void CompactLiveZombieSet()
	{
		if (_liveZombies.Count == 0)
		{
			return;
		}
		_staleZombieBuffer.Clear();
		foreach (GameObject liveZombie in _liveZombies)
		{
			if ((Object)liveZombie == (Object)null)
			{
				_staleZombieBuffer.Add(liveZombie);
			}
		}
		for (int i = 0; i < _staleZombieBuffer.Count; i++)
		{
			_liveZombies.Remove(_staleZombieBuffer[i]);
		}
		_staleZombieBuffer.Clear();
	}

	private static void DestroyAllZombies()
	{
		if (_liveZombies.Count != 0)
		{
			_staleZombieBuffer.Clear();
			foreach (GameObject liveZombie in _liveZombies)
			{
				if ((Object)liveZombie != (Object)null)
				{
					_staleZombieBuffer.Add(liveZombie);
				}
			}
			for (int i = 0; i < _staleZombieBuffer.Count; i++)
			{
				DestroyZombie(_staleZombieBuffer[i]);
			}
			_staleZombieBuffer.Clear();
		}
		ClearZombies();
	}

	private static bool TryBuildSpawnCandidate(Character targetPlayer, List<Character> players, float minRadius, float maxRadius, bool preferFront, out Vector3 position)
	{
		position = Vector3.zero;
		if ((Object)targetPlayer == (Object)null)
		{
			return false;
		}
		Vector3 playerFacing = GetPlayerFacing(targetPlayer);
		for (int i = 0; i < SpawnDirectionAttemptsPerTarget; i++)
		{
			Vector3 val2 = GetCandidateSpawnDirection(playerFacing, preferFront);
			float num = Random.Range(minRadius, maxRadius);
			Vector3 position2 = targetPlayer.Center + val2 * num + Vector3.up * 2f;
			if (!TryFindSupportedSpawnPosition(position2, targetPlayer, out position2))
			{
				continue;
			}
			if (IsSpawnPositionValid(position2, players, minRadius * 0.8f) && IsSpawnPositionStealthy(position2, players, targetPlayer, preferFront))
			{
				position = position2;
				return true;
			}
		}
		return false;
	}

	private static bool IsSpawnPositionValid(Vector3 position, List<Character> players, float minDistance)
	{
		if (position == Vector3.zero)
		{
			return false;
		}
		foreach (Character player in players)
		{
			if (!((Object)player == (Object)null))
			{
				Vector3 center = player.Center;
				center.y = position.y;
				if (Vector3.Distance(position, center) < minDistance)
				{
					return false;
				}
			}
		}
		foreach (GameObject liveZombie in _liveZombies)
		{
			if ((Object)liveZombie != (Object)null && Vector3.Distance(position, liveZombie.transform.position) < 3f)
			{
				return false;
			}
		}
		return true;
	}

	private static bool IsSpawnPositionStealthy(Vector3 position, List<Character> players, Character primaryPlayer, bool preferFront)
	{
		if ((Object)primaryPlayer == (Object)null)
		{
			return false;
		}
		if (preferFront && IsBehindPlayer(primaryPlayer, position))
		{
			return false;
		}
		if (!preferFront && !IsBehindPlayer(primaryPlayer, position))
		{
			return false;
		}
		return !IsPositionVisibleToAnyPlayer(position, players);
	}

	private static Vector3 GetCandidateSpawnDirection(Vector3 playerFacing, bool preferFront)
	{
		if (playerFacing.sqrMagnitude < 0.0001f)
		{
			playerFacing = Vector3.forward;
		}
		float num = Random.Range(preferFront ? (0f - FrontSpawnConeHalfAngle) : (0f - RearSpawnConeHalfAngle), preferFront ? FrontSpawnConeHalfAngle : RearSpawnConeHalfAngle);
		Vector3 val = preferFront ? playerFacing : (playerFacing * -1f);
		Vector3 normalized = Vector3.ProjectOnPlane(Quaternion.AngleAxis(num, Vector3.up) * val, Vector3.up).normalized;
		if (normalized.sqrMagnitude >= 0.0001f)
		{
			return normalized;
		}
		return preferFront ? Vector3.forward : Vector3.back;
	}

	private static bool IsPositionVisibleToAnyPlayer(Vector3 position, List<Character> players)
	{
		foreach (Character player in players)
		{
			if (!((Object)player == (Object)null) && IsPositionVisibleToPlayer(player, position))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsPositionVisibleToPlayer(Character player, Vector3 position)
	{
		Vector3 playerViewOrigin = GetPlayerViewOrigin(player);
		Vector3 val = position + Vector3.up * VisibilityTargetHeight;
		Vector3 normalized = (val - playerViewOrigin).normalized;
		float num = Vector3.Distance(playerViewOrigin, val);
		if (num <= 0.1f)
		{
			return true;
		}
		if (Vector3.Dot(GetPlayerFacing(player), normalized) < Mathf.Cos(ViewConeRejectAngle * ((float)Math.PI / 180f)))
		{
			return false;
		}
		RaycastHit val2 = default(RaycastHit);
		if (!Physics.Raycast(playerViewOrigin, normalized, out val2, Mathf.Max(num - VisibilityPadding, 0.01f), -1, QueryTriggerInteraction.Ignore))
		{
			return true;
		}
		return IsPartOfCharacter(val2.transform, player);
	}

	private static bool IsPartOfCharacter(Transform hitTransform, Character player)
	{
		if ((Object)hitTransform == (Object)null || (Object)player == (Object)null)
		{
			return false;
		}
		if (hitTransform.IsChildOf(((Component)player).transform))
		{
			return true;
		}
		Character componentInParent = ((Component)hitTransform).GetComponentInParent<Character>();
		return (Object)componentInParent == (Object)player;
	}

	private static bool IsDirectionAccepted(Vector3 playerFacing, Vector3 candidateDirection, bool preferRear)
	{
		float num = Vector3.Dot(playerFacing, candidateDirection.normalized);
		if (preferRear)
		{
			return num <= RearSpawnDotThreshold;
		}
		return num > RearSpawnDotThreshold;
	}

	private static bool IsBehindPlayer(Character player, Vector3 position)
	{
		Vector3 playerFacing = GetPlayerFacing(player);
		Vector3 normalized = Vector3.ProjectOnPlane(position - player.Center, Vector3.up).normalized;
		if (normalized.sqrMagnitude < 0.0001f)
		{
			return false;
		}
		return Vector3.Dot(playerFacing, normalized) <= RearSpawnDotThreshold;
	}

	private static Vector3 GetPlayerFacing(Character player)
	{
		if ((Object)player == (Object)null)
		{
			return Vector3.forward;
		}
		Transform val = (((Object)(player.refs?.view) != (Object)null) ? ((Component)player.refs.view).transform : ((Component)player).transform);
		Vector3 normalized = Vector3.ProjectOnPlane(val.forward, Vector3.up).normalized;
		if (normalized.sqrMagnitude >= 0.0001f)
		{
			return normalized;
		}
		normalized = Vector3.ProjectOnPlane(((Component)player).transform.forward, Vector3.up).normalized;
		if (normalized.sqrMagnitude >= 0.0001f)
		{
			return normalized;
		}
		return Vector3.forward;
	}

	private static Vector3 GetPlayerViewOrigin(Character player)
	{
		if ((Object)player == (Object)null)
		{
			return Vector3.zero;
		}
		if ((Object)(player.refs?.view) != (Object)null)
		{
			return ((Component)player.refs.view).transform.position;
		}
		return player.Center + Vector3.up * 0.35f;
	}

	private static bool TryFindSupportedSpawnPosition(Vector3 probePosition, Character targetPlayer, out Vector3 groundedPosition)
	{
		groundedPosition = Vector3.zero;
		if (!TryGetGroundHit(probePosition, out var hit))
		{
			return false;
		}
		if (!IsGroundStandable(hit.normal))
		{
			return false;
		}
		Vector3 val = hit.point + Vector3.up * SpawnGroundLift;
		if (!IsSpawnHeightCompatible(val, targetPlayer))
		{
			return false;
		}
		if (!HasSpawnHeadroom(val))
		{
			return false;
		}
		if (!HasStableGroundSupport(val))
		{
			return false;
		}
		groundedPosition = val;
		return true;
	}

	private static bool TryGetGroundHit(Vector3 probePosition, out RaycastHit hit)
	{
		return Physics.Raycast(probePosition + Vector3.up * SpawnGroundProbeHeight, Vector3.down, out hit, SpawnGroundProbeHeight + SpawnGroundProbeDistance, -1, QueryTriggerInteraction.Ignore);
	}

	private static bool IsGroundStandable(Vector3 normal)
	{
		if (normal.sqrMagnitude < 0.0001f)
		{
			return false;
		}
		return Vector3.Angle(normal, Vector3.up) <= MaxSpawnSlopeAngle;
	}

	private static bool IsSpawnHeightCompatible(Vector3 position, Character targetPlayer)
	{
		if ((Object)targetPlayer == (Object)null)
		{
			return true;
		}
		float y = position.y - targetPlayer.Center.y;
		return y >= 0f - MaxSpawnBelowPlayer && y <= MaxSpawnAbovePlayer;
	}

	private static bool HasSpawnHeadroom(Vector3 position)
	{
		Vector3 val = position + Vector3.up * SpawnCapsuleBottomHeight;
		Vector3 val2 = position + Vector3.up * SpawnCapsuleTopHeight;
		return !Physics.CheckCapsule(val, val2, SpawnCapsuleRadius, -1, QueryTriggerInteraction.Ignore);
	}

	private static bool HasStableGroundSupport(Vector3 position)
	{
		for (int i = 0; i < SpawnSupportSampleCount; i++)
		{
			float f = (float)i / (float)SpawnSupportSampleCount * (float)Math.PI * 2f;
			Vector3 probePosition = position + new Vector3(Mathf.Cos(f), 0f, Mathf.Sin(f)) * SpawnSupportSampleRadius;
			if (!TryGetGroundHit(probePosition, out var hit))
			{
				return false;
			}
			if (!IsGroundStandable(hit.normal))
			{
				return false;
			}
			if (Mathf.Abs(hit.point.y - position.y) > MaxSpawnSupportHeightDelta)
			{
				return false;
			}
		}
		return true;
	}

	private static Quaternion GetSpawnRotation(Vector3 spawnPosition, Character targetPlayer)
	{
		if ((Object)targetPlayer == (Object)null)
		{
			return Random.rotation;
		}
		Vector3 normalized = Vector3.ProjectOnPlane(targetPlayer.Center - spawnPosition, Vector3.up).normalized;
		if (normalized.sqrMagnitude < 0.0001f)
		{
			return Random.rotation;
		}
		return Quaternion.LookRotation(normalized, Vector3.up);
	}

}
