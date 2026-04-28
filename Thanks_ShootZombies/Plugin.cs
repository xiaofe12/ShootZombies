﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
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

[BepInPlugin("com.github.Thanks.ShootZombies", "ShootZombies", "1.3.0")]
[BepInDependency("com.github.PEAKModding.PEAKLib.Core", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.github.PEAKModding.PEAKLib.Items", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("PEAKModding.ModConfig", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
	private static readonly string[] PreferredAkItemContentAssetNames = new string[1] { "assets/_ak/ic_ak.asset" };

	private static readonly string[] PreferredAkPrefabAssetNames = new string[4] { "assets/codexbuild/generated/weapon_bundle.prefab", "assets/weapon/weapon.prefab", "assets/_ak/ak.prefab", "assets/_ak/prefabs/ak.prefab" };

	private static readonly string[] PreferredAkIconAssetNames = new string[5] { "assets/weapon/ak47_icon.png", "assets/weapon/mpx_icon.png", "assets/weapon/hk416_icon.png", "assets/_ak/textures/ak_icon.asset", "assets/_ak/ak_icon.asset" };

	private static readonly string[] PreferredAkVfxAssetNames = new string[2] { "assets/_ak/vfx_ak.prefab", "VFX_AK" };

	private static readonly bool EnableDiagnosticLogs = false;

	private static readonly bool EnableVerboseInfoLogs = false;

	private sealed class ReferenceIdentityComparer : IEqualityComparer<object>
	{
		public static readonly ReferenceIdentityComparer Instance = new ReferenceIdentityComparer();

		public new bool Equals(object x, object y)
		{
			return ReferenceEquals(x, y);
		}

		public int GetHashCode(object obj)
		{
			return RuntimeHelpers.GetHashCode(obj);
		}
	}

	private sealed class DiagnosticGraphHit
	{
		public string Path;

		public string Summary;

		public int Score;
	}

	private sealed class RoomConfigCallbackProxy : IInRoomCallbacks, IOnEventCallback
	{
		public void OnPlayerEnteredRoom(RoomPlayer newPlayer)
		{
			Instance?.HandleRoomPlayerListChanged();
		}

		public void OnPlayerLeftRoom(RoomPlayer otherPlayer)
		{
			Instance?.HandleRoomPlayerListChanged();
		}

		public void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
		{
			Instance?.HandleRoomPropertiesUpdated(propertiesThatChanged);
		}

		public void OnPlayerPropertiesUpdate(RoomPlayer targetPlayer, PhotonHashtable changedProps)
		{
			Instance?.HandlePlayerPropertiesUpdated(targetPlayer, changedProps);
		}

		public void OnMasterClientSwitched(RoomPlayer newMasterClient)
		{
			Instance?.HandleMasterClientChanged();
		}

		public void OnEvent(EventData photonEvent)
		{
			Instance?.HandlePhotonEvent(photonEvent);
		}
	}

	private sealed class WeaponVariantDefinition
	{
		public string SelectionKey;

		public string DisplayName;

		public string[] PrefabAliases;

		public string[] IconAliases;

		public float ScaleMultiplier = 1f;

		public Vector3 PositionOffset = Vector3.zero;
	}

	private sealed class MuzzleFlashInstance
	{
		public GameObject Root;

		public Light Light;

		public float DisableAtTime;
	}

	private sealed class RemoteGunshotAudioInstance
	{
		public GameObject Root;

		public AudioSource AudioSource;

		public float ReleaseAtTime;
	}

	private readonly struct ZombieBehaviorDifficultyPreset
	{
		// These fields map to real runtime hooks:
		// target search cadence, post-bite recovery, repeat-bite cooldown,
		// authored sprint/lunge values, and wake-up thresholds.
		public readonly float MoveSpeedMultiplier;

		public readonly float HitKnockbackForce;

		public readonly float TargetSearchInterval;

		public readonly float BiteRecoveryTime;

		public readonly float SamePlayerBiteCooldown;

		public readonly float SprintDistance;

		public readonly float ChaseTimeBeforeSprint;

		public readonly float LungeDistance;

		public readonly float LungeTime;

		public readonly float LungeRecoveryTime;

		public readonly float LookAngleBeforeWakeup;

		public readonly float DistanceBeforeWakeup;

		public ZombieBehaviorDifficultyPreset(float moveSpeedMultiplier, float hitKnockbackForce, float targetSearchInterval, float biteRecoveryTime, float samePlayerBiteCooldown, float sprintDistance, float chaseTimeBeforeSprint, float lungeDistance, float lungeTime, float lungeRecoveryTime, float lookAngleBeforeWakeup, float distanceBeforeWakeup)
		{
			MoveSpeedMultiplier = moveSpeedMultiplier;
			HitKnockbackForce = hitKnockbackForce;
			TargetSearchInterval = targetSearchInterval;
			BiteRecoveryTime = biteRecoveryTime;
			SamePlayerBiteCooldown = samePlayerBiteCooldown;
			SprintDistance = sprintDistance;
			ChaseTimeBeforeSprint = chaseTimeBeforeSprint;
			LungeDistance = lungeDistance;
			LungeTime = lungeTime;
			LungeRecoveryTime = lungeRecoveryTime;
			LookAngleBeforeWakeup = lookAngleBeforeWakeup;
			DistanceBeforeWakeup = distanceBeforeWakeup;
		}
	}

	private sealed class DartImpactVfxInstance
	{
		public GameObject Root;

		public int PrefabId;

		public float ReleaseAtTime;

		public float EstimatedLifetime;

		public AudioSource[] AudioSources;
	}

	private readonly struct MemberAccessorCacheKey : IEquatable<MemberAccessorCacheKey>
	{
		public readonly Type OwnerType;

		public readonly string MemberName;

		public readonly Type ValueType;

		public MemberAccessorCacheKey(Type ownerType, string memberName, Type valueType)
		{
			OwnerType = ownerType;
			MemberName = memberName ?? string.Empty;
			ValueType = valueType;
		}

		public bool Equals(MemberAccessorCacheKey other)
		{
			return OwnerType == other.OwnerType && ValueType == other.ValueType && string.Equals(MemberName, other.MemberName, StringComparison.Ordinal);
		}

		public override bool Equals(object obj)
		{
			return obj is MemberAccessorCacheKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			int num = ((OwnerType != null) ? OwnerType.GetHashCode() : 0);
			num = num * 397 ^ ((ValueType != null) ? ValueType.GetHashCode() : 0);
			return num * 397 ^ StringComparer.Ordinal.GetHashCode(MemberName);
		}
	}

	private sealed class ReflectionMemberCacheEntry
	{
		public FieldInfo Field;

		public PropertyInfo Property;
	}

	private sealed class ZombieSpeedReflectionCache
	{
		public FieldInfo AgentField;

		public FieldInfo BaseSpeedField;

		public Type AgentType;

		public PropertyInfo SpeedProperty;
	}

	private const string LegacyPluginId = "com.github.PeakTest.ShootZombies";

	public const string Id = "com.github.Thanks.ShootZombies";

	public const string Name = "ShootZombies";

	public const string Version = "1.3.0";

	private const string CanonicalConfigFileName = "Thanks.ShootZombies.cfg";

	private const string PreviousCanonicalConfigFileName = "com.github.Thanks.ShootZombies.cfg";

	private const string LegacyCanonicalConfigFileName = "com.github.PeakTest.ShootZombies.cfg";

	private const string LocalizedConfigMirrorFileName = "Thanks.ShootZombies.localized.cfg";

	private const string PreviousLocalizedConfigMirrorFileName = "com.github.Thanks.ShootZombies.localized.cfg";

	private const string LegacyLocalizedConfigMirrorFileName = "com.github.PeakTest.ShootZombies.localized.cfg";

	private const string ConfigMetadataVersionPrefix = "## ShootZombiesVersion: ";

	private const string ConfigMetadataSchemaPrefix = "## ShootZombiesSchema: ";

	private const string ConfigMetadataLanguagePrefix = "## ShootZombiesLanguage: ";

	private const string ConfigMetadataZombieDifficultyPrefix = "## ShootZombiesZombieDifficulty: ";

	private const string FeaturesConfigSectionName = "Features";

	private const string WeaponConfigSectionName = "Weapon";

	private const string ZombieConfigSectionName = "Zombie";

	private static HashSet<int> _receivedItem = new HashSet<int>();

	private static HashSet<int> _persistentReceivedItem = new HashSet<int>();

	private static HashSet<int> _receivedFirstAid = new HashSet<int>();

	private static HashSet<int> _persistentReceivedFirstAid = new HashSet<int>();

	private static HashSet<int> _pendingRemoteWeaponGrantActors = new HashSet<int>();

	private static HashSet<int> _pendingRemoteFirstAidGrantActors = new HashSet<int>();

	private static Dictionary<int, float> _lastWeaponGrantTimeByActor = new Dictionary<int, float>();

	private static Dictionary<int, float> _lastFirstAidGrantTimeByActor = new Dictionary<int, float>();

	private static Dictionary<int, float> _weaponMissingSinceByActor = new Dictionary<int, float>();

	private static Dictionary<int, float> _firstAidMissingSinceByActor = new Dictionary<int, float>();

	private static Dictionary<int, float> _recentWeaponDropTimeByActor = new Dictionary<int, float>();

	private static Character _localCharacter;

	private static float _lastFireTime;

	private static bool _hasWeapon;

	private static Coroutine _scanCoroutine;

	private Coroutine _gameplayLoadoutBootstrapCoroutine;

	private static string _activeSceneBucket = string.Empty;

	private static Transform _localWeaponVisualRoot;

	private static Transform _localWeaponVisualModel;

	private static Transform _localWeaponMuzzle;

	private static Transform _localHeldDebugSphereRoot;

	private static Material _localHeldDebugSphereMaterial;

	private static bool _localFirstPersonHandsSuppressed;

	private static Character _weaponVisualOwner;

	private static int _localWeaponSourceItemId;

	private static float _lastHeldWeaponSeenTime;

	private static Item _cachedHeldBlowgunItem;

	private static float _lastHeldBlowgunSearchTime = -10f;

	private static readonly Vector3 _localWeaponOffset = new Vector3(-0.08f, -0.18f, 0.62f);

	private static readonly Vector3 _localHeldDebugSphereOffset = new Vector3(0.24f, -0.12f, 0.72f);

	private static readonly Vector3 _localHeldDebugSphereScale = Vector3.one * 0.18f;

	private static readonly Vector3 _localWeaponEuler = Vector3.zero;

	private static readonly Vector3 _localWeaponRootScale = Vector3.one;

	private static readonly Vector3 _localWeaponModelOffset = Vector3.zero;

	private static readonly Vector3 _localWeaponModelEuler = Vector3.zero;

	private static readonly Vector3 _localWeaponViewEulerOffset = new Vector3(0f, 180f, 0f);

	private static readonly Vector3 _localWeaponModelScale = Vector3.one;

	private static readonly Vector3 _weaponBaseEuler = new Vector3(0f, -90f, 0f);

	private const float LocalWeaponModelBaseScaleMultiplier = 1.275f;

	private static readonly string[] LocalWeaponExcludedRendererKeywords = new string[10] { "hand", "arm", "finger", "collider", "trigger", "vfx", "effect", "spawn", "holiday", "smoke" };

	private static readonly string[] LocalFirstPersonHandSuppressionKeywords = new string[7] { "hand", "arm", "finger", "glove", "wrist", "forearm", "sleeve" };

	private static Vector3 _localWeaponModelBaseScale = Vector3.one;

	private static Vector3 _localWeaponModelBasePosition = Vector3.zero;

	private static Quaternion _localWeaponModelBaseRotation = Quaternion.identity;

	private static bool _localWeaponModelUsesPreparedPose;

	private static bool _pendingLocalWeaponVisualModelRefresh;

	private const float LocalWeaponMissingTolerance = 4f;

	private static readonly bool EnableLocalWeaponVisualFollower = false;

	private const bool EnableLocalHeldDebugSphere = false;

	private const bool PreferStableLocalFirstPersonPose = true;

	private const float LocalWeaponFirstPersonFinalTargetSize = 0.9f;

	private const float LocalWeaponFirstPersonMinBaseSize = 0.24f;

	private const float LocalWeaponFirstPersonMaxBaseSize = 0.8f;

	private const float LocalWeaponModelScaleClampMin = 0.01f;

	private const float LocalWeaponModelScaleClampMax = 96f;

	private const string DefaultAkSoundOption = "ak_sound1";

	private static readonly string[] AkSoundSelectionValues = new string[3] { "ak_sound1", "ak_sound2", "ak_sound3" };

	private const string DefaultConfigPanelThemeOption = "dark";

	private static readonly string[] ConfigPanelThemeValues = new string[3] { "dark", "light", "transparent" };

	private const string DefaultWeaponSelection = "AK47";

	private const string PlayerWeaponSelectionPropertyKey = "sz.weapon";

	private const string PlayerAkSoundSelectionPropertyKey = "sz.sound";

	private static readonly string[] WeaponSelectionValues = new string[3] { "AK47", "MPX", "HK416" };

	private const string DefaultZombieBehaviorDifficulty = "Easy";

	private const int DefaultMaxZombieCount = 2;

	private const int DefaultZombieSpawnCount = 1;

	private static readonly string[] ZombieBehaviorDifficultyValues = new string[5] { "Easy", "Standard", "Hard", "Insane", "Nightmare" };

	private static readonly ZombieBehaviorDifficultyPreset[] ZombieBehaviorDifficultyPresets = new ZombieBehaviorDifficultyPreset[5]
	{
		// Easy keeps the original zombie behavior parameters so players can recover after a bite.
		new ZombieBehaviorDifficultyPreset(1f, 650f, 10f, 3f, 5f, 20f, 3f, 8f, 1.5f, 5f, 30f, 30f),
		new ZombieBehaviorDifficultyPreset(1.05f, 500f, 6f, 2.5f, 4f, 22f, 2.25f, 10f, 1.65f, 4f, 40f, 34f),
		new ZombieBehaviorDifficultyPreset(1.15f, 425f, 3f, 1.5f, 2.75f, 26f, 1.25f, 12f, 1.85f, 2.5f, 55f, 44f),
		new ZombieBehaviorDifficultyPreset(1.25f, 350f, 1.5f, 0.9f, 1.75f, 30f, 0.85f, 13f, 2f, 1.5f, 72f, 58f),
		new ZombieBehaviorDifficultyPreset(1.35f, 275f, 0.75f, 0.55f, 1.1f, 34f, 0.65f, 14f, 2.15f, 0.85f, 90f, 72f)
	};

	private static readonly WeaponVariantDefinition[] WeaponVariantDefinitions = new WeaponVariantDefinition[3]
	{
		new WeaponVariantDefinition
		{
			SelectionKey = "AK47",
			DisplayName = "AK47",
			PrefabAliases = new string[2] { "ak47", "ak" },
			IconAliases = new string[2] { "ak47icon", "akicon" },
			ScaleMultiplier = 1f,
			PositionOffset = Vector3.zero
		},
		new WeaponVariantDefinition
		{
			SelectionKey = "MPX",
			DisplayName = "MPX",
			PrefabAliases = new string[1] { "mpx" },
			IconAliases = new string[1] { "mpxicon" },
			ScaleMultiplier = 0.729f,
			PositionOffset = new Vector3(0f, -0.015f, 0f)
		},
		new WeaponVariantDefinition
		{
			SelectionKey = "HK416",
			DisplayName = "HK416",
			PrefabAliases = new string[2] { "hk416", "hk417" },
			IconAliases = new string[2] { "hk416icon", "hk417icon" },
			ScaleMultiplier = 1f,
			PositionOffset = Vector3.zero
		}
	};

	public static AudioClip _gunshotSound;

	private static readonly Dictionary<string, AudioClip> _externalGunshotSounds = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

	private static Coroutine _gunshotSoundLoadCoroutine;

	private static string _currentGunshotSoundSelection = "ak_sound1";

	private static bool _resourcesLoaded;

	private const float GameplaySceneWarmupSeconds = 10f;

	private const float LobbySceneWarmupSeconds = 1.5f;

	private const float OtherSceneWarmupSeconds = 0.5f;

	private static float _sceneWarmupUntilUnscaledTime;

	private static string _sceneWarmupSceneName = string.Empty;

	public static GameObject _ak47Prefab;

	private static AssetBundle _ak47Bundle;

	private static GameObject _ak47VFX;

	private static object _ak47ItemContent;

	private static Material _localVisualFallbackMaterial;

	public static Texture2D _ak47IconTexture;

	private static readonly Dictionary<string, GameObject> _resolvedWeaponPrefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<string, Texture2D> _resolvedWeaponIcons = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<int, string> _playerWeaponSelectionsByActor = new Dictionary<int, string>();

	private static readonly Dictionary<int, string> _playerAkSoundSelectionsByActor = new Dictionary<int, string>();

	private static readonly List<GameObject> _runtimeWeaponPrefabClones = new List<GameObject>();

	private static GameObject _genericResolvedWeaponPrefab;

	private static Texture2D _genericResolvedWeaponIcon;

	private static bool _useExplicitSelectedWeaponIcon;

	private static readonly HashSet<string> _diagnosticLogKeys = new HashSet<string>(StringComparer.Ordinal);

	private static Type _actionRaycastDartType;

	private static Type _actionConsumeType;

	private static Type _actionConsumeAndSpawnType;

	private static MethodInfo _rpcDartImpactMethod;

	private static MethodInfo _characterAddForceMethod;

	private static MethodInfo _characterCanDoInputMethod;

	private static FieldInfo _dartVfxField;

	private static MethodInfo _actionRunActionMethod;

	private static FieldInfo _actionRaycastDartSpawnTransformField;

	private static Material _muzzleFlashMaterial;

	private static readonly List<MuzzleFlashInstance> _muzzleFlashPool = new List<MuzzleFlashInstance>();

	private static int _nextMuzzleFlashPoolIndex;

	private static readonly List<RemoteGunshotAudioInstance> _remoteGunshotAudioPool = new List<RemoteGunshotAudioInstance>();

	private static int _nextRemoteGunshotAudioPoolIndex;

	private static readonly List<DartImpactVfxInstance> _dartImpactVfxPool = new List<DartImpactVfxInstance>();

	private static int _nextDartImpactVfxPoolIndex;

	private static FieldInfo _castProgressField;

	private static float _lastChargeSyncTime;

	private static int _lastChargeSyncItemId = int.MinValue;

	private static MethodInfo _itemGetDataGenericMethod;

	private static Type _optionableIntItemDataType;

	private static object _usesDataKey;

	private static FieldInfo _optionableIntValueField;

	private static bool _characterCanDoInputMethodInitialized;

	private static float _lastUseSyncTime;

	private static AudioSource _sharedAudioSource;

	private static AudioSource _sharedFallbackGunshotAudioSource;

	private static float _lastSoundTime;

	public static OrbFogHandler _orbFogHandler;

	private static GameObject _fogUIObject;

	private static GameObject _fogTimerUIObject;

	private static RectTransform _fogUIContainerRect;

	private static GameObject _fogModeLobbyNoticeObject;

	private static TextMeshProUGUI _fogModeLobbyNoticeText;

	private static RectTransform _fogModeLobbyNoticeRect;

	private static GameObject _sharedFogBottomLeftRootObject;

	private static RectTransform _sharedFogBottomLeftRootRect;

	private static Canvas _sharedFogBottomLeftRootCanvas;

	private static string _lastFogModeLobbyNoticeText = string.Empty;

	private static GameObject _weaponLobbyNoticeObject;

	private static TextMeshProUGUI _weaponLobbyNoticeText;

	private static RectTransform _weaponLobbyNoticeRect;

	private static string _lastWeaponLobbyNoticeText = string.Empty;

	private static float _lastWeaponLobbyNoticeLayoutTime = -10f;

	private static RectTransform _cachedVersionLabelRect;

	private static float _lastVersionLabelSearchTime;

	private const float LobbyNoticeRightOffset = 28f;

	private const float LobbyNoticeDownOffset = 96f;

	private const float LobbyNoticeWidth = 980f;

	private const float LobbyNoticeSingleLineHeight = 108f;

	private const float LobbyNoticeMultiLineHeight = 166f;

	private const float LobbyNoticeFontSizeMultiplier = 1.6f;

	private const float LobbyNoticeLineSpacing = 1.5f;

	private const float FogUiVersionHorizontalGap = 28f;

	private const float FogUiAnchorLeftNudge = -8f;

	private const float FogUiAnchorTopNudge = 4f;

	private const float FogUiMainWidth = 410f;

	private const float FogUiTimerWidth = 220f;

	private const float FogUiComponentGap = 3f;

	private const float FogUiRowHeight = 38f;

	private const float FogUiContainerHeight = 40f;

	private const float FogUiCanvasOverflowXRatio = 0.25f;

	private const float FogUiCanvasOverflowYRatio = 0.25f;

	private const float FogUiCanvasOverflowXMax = 320f;

	private const float FogUiCanvasOverflowYMax = 220f;

	private const float FogUiSharedTopReferenceHeight = 108f;

	private const float FogModeLobbyNoticeWidth = 700f;

	private const float FogModeLobbyNoticeFontSizeMultiplier = 1.05f;

	private const float FogModeForcedSpeed = 2f;

	private const float FogModeForcedWaitTime = 90f;

	private const float WeaponLobbyNoticeRightOffset = 36f;

	private const float WeaponLobbyNoticeBottomOffset = 76f;

	private const float WeaponLobbyNoticeWidth = 420f;

	private const float WeaponLobbyNoticeMinWidth = 120f;

	private const float WeaponLobbyNoticeSingleLineHeight = 56f;

	private const float WeaponLobbyNoticeMultiLineHeight = 96f;

	private const float WeaponLobbyNoticeFontSizeMultiplier = 1.05f;

	private const float WeaponLobbyNoticeCompassVerticalGap = 2f;

	private const float WeaponLobbyNoticeScaleMultiplier = 0.75f;

	private const string LobbyNoticeKeyColor = "#FF3B30";

	private const byte ZombieHitEventCode = 178;

	private const byte RemoteLoadoutGrantEventCode = 180;

	private const byte RemoteShotEffectsEventCode = 181;

	private const byte RemoteLoadoutGrantAckEventCode = 182;

	private const byte PlayerShotStatusEventCode = 183;

	private const float PlayerShotSporeReductionFraction = 0.05f;

	private const float PlayerShotColdFraction = 0.025f;

	private const int RemoteLoadoutGrantWeapon = 1;

	private const int RemoteLoadoutGrantFirstAid = 2;

	private const string RoomConfigPropertyKey = "ShootZombies.HostConfig";

	private const float RoomConfigPublishInterval = 0.35f;

	private const float RoomConfigPollInterval = 1f;

	private const float ZombieSpeedRefreshInterval = 1.5f;

	private const float DefaultZombieTimeReductionSeconds = 25f;

	private const float DefaultHitScanDistance = 100f;

	private const float DefaultHitScanRadius = 0.1f;

	private const float HeldBlowgunSearchInterval = 0.15f;

	private const float PlayerScanInitialDelay = 2f;

	private const float PlayerScanInterval = 5f;

	private const float LanguagePollInterval = 0.5f;

	private const float ChargeStateSyncInterval = 0.2f;

	private const float PendingRemoteGrantRetryInterval = 0.5f;

	private const float ScheduledRoomConfigSyncInterval = 0.1f;

	private const float ScheduledConfigCheckInterval = 0.1f;

	private const float ScheduledLobbyNoticeInterval = 0.25f;

	private const float ScheduledZombieTimerInterval = 0.25f;

	private const float ScheduledZombieSpeedInterval = 0.5f;

	private const float ScheduledLocalCharacterRefreshInterval = 0.1f;

	private const float ScheduledPendingRemoteGrantInterval = 0.25f;

	private const float GameplayLoadoutBootstrapRetryInterval = 0.2f;

	private const float GameplayLoadoutBootstrapDuration = 8f;

	private const float MuzzleFlashDuration = 0.08f;

	private const int MaxMuzzleFlashPoolSize = 8;

	private const int MaxRemoteGunshotAudioPoolSize = 12;

	private const float RemoteGunshotAudioReleasePadding = 0.1f;

	private const int MaxDartImpactVfxPoolSizePerPrefab = 8;

	private const float DefaultDartImpactVfxLifetime = 2f;

	private const float RemoteLoadoutGrantRetryTimeout = 2.5f;

	private const float LoadoutRepairGracePeriod = 4f;

	private const float LoadoutMissingConfirmDuration = 1.5f;

	private const float WeaponDropGrantSuppressDuration = 15f;

	private const float LobbyNoticeLayoutRefreshInterval = 1.0f;

	private const float NightTestHoldDuration = 5f;

	private const float NightTestTargetTimeOfDay = 23.75f;

	private const KeyCode NightTestKey = KeyCode.Backslash;

	private static HashSet<Material> _processedMaterials = new HashSet<Material>();

	private static float _lastMaterialUpdateTime = 0f;

	private static bool _pendingAkVisualRefresh = true;

	private static bool _pendingAkVisualForceRefresh;

	private static bool _pendingAkUiRefresh = true;

	private static HashSet<GameObject> _replacedBlowguns = new HashSet<GameObject>();

	private bool _lastLanguageSetting;

	private bool _cachedIsChineseLanguage;

	private float _lastLanguagePollTime = -10f;

	private float _lastZombieMoveSpeed;

	private float _lastZombieKnockbackForce;

	private bool _lastZombieSpawnEnabled;

	private float _lastZombieSpawnInterval;

	private float _lastZombieSpawnIntervalRandom;

	private int _lastZombieSpawnCount;

	private int _lastZombieSpawnCountRandom;

	private float _lastZombieSpawnRadius;

	private int _lastMaxZombies;

	private float _lastZombieMaxLifetime;

	private float _lastDistanceBeforeWakeup;

	private float _lastZombieSprintDistance;

	private float _lastChaseTimeBeforeSprint;

	private float _lastZombieLungeDistance;

	private float _lastZombieBiteRecoveryTime;

	private float _lastZombieLungeTime;

	private float _lastZombieLungeRecoveryTime;

	private float _lastZombieLookAngleBeforeWakeup;

	private string _lastZombieBehaviorDifficulty = DefaultZombieBehaviorDifficulty;

	private string _loadedZombieBehaviorDifficultyMetadata = string.Empty;

	private bool _applyingZombieBehaviorDifficultySelection;

	private bool _lastModEnabled;

	private bool _lastWeaponEnabled;

	private string _lastWeaponSelection = "AK47";

	private string _lastPublishedLocalWeaponSelection = string.Empty;

	private string _lastPublishedLocalAkSoundSelection = string.Empty;

	private float _lastWeaponModelPitch;

	private float _lastWeaponModelYaw;

	private float _lastWeaponModelRoll;

	private float _lastWeaponModelScale;

	private float _lastWeaponModelOffsetX;

	private float _lastWeaponModelOffsetY;

	private float _lastWeaponModelOffsetZ;

	private float _lastFireInterval;

	private float _lastFireVolume;

	private float _lastZombieTimeReduction;

	private float _nightTestHoldStartTime = -1f;

	private bool _nightTestTriggeredThisHold;

	private bool _isRefreshingLanguage;

	private bool _applyingRoomConfigPayload;

	private string _activeRoomConfigRoomName = string.Empty;

	private string _lastPublishedRoomConfigPayload = string.Empty;

	private string _lastAppliedRoomConfigPayload = string.Empty;

	private string _localRoomConfigBackupPayload = string.Empty;

	private bool _hasLocalRoomConfigBackup;

	private bool _wasRoomMasterClient;

	private float _lastRoomConfigPublishTime = -10f;

	private float _lastRoomConfigPollTime = -10f;

	private bool _roomConfigDirty = true;

	private bool _roomConfigCallbacksRegistered;

	private bool _pendingRemoteWeaponGrant;

	private bool _pendingRemoteFirstAidGrant;

	private float _lastPendingRemoteGrantAttemptTime = -10f;

	private ConfigFile _pluginConfig;

	private string _pluginConfigPath = string.Empty;

	private readonly Dictionary<ConfigEntryBase, Delegate> _observedConfigEntries = new Dictionary<ConfigEntryBase, Delegate>();

	private RoomConfigCallbackProxy _roomConfigCallbackProxy;

	private ConfigFile PluginConfig
	{
		get
		{
			return _pluginConfig ?? ((BaseUnityPlugin)this).Config;
		}
	}

	private int _lastLocalizedModConfigUiFrame = -1;

	private static readonly HashSet<ConfigEntryBase> _ownedConfigEntries = new HashSet<ConfigEntryBase>();

	private static readonly bool DisableModConfigRuntimePatches = true;

	private bool _repairingModConfigUi;

	private string _activeModConfigName = string.Empty;

	private Coroutine _modConfigStabilizeCoroutine;

	private string _modConfigStabilizeOwnerName = string.Empty;

	private static float _lastZombieSpeedUpdateTime = 0f;

	private static Dictionary<Character, float> _zombieBaseSpeeds = new Dictionary<Character, float>();

	private static bool _pendingZombieSpeedRefresh = true;

	private static readonly Dictionary<MemberAccessorCacheKey, ReflectionMemberCacheEntry> _reflectionMemberCache = new Dictionary<MemberAccessorCacheKey, ReflectionMemberCacheEntry>();

	private static readonly Dictionary<Type, ZombieSpeedReflectionCache> _zombieSpeedReflectionCache = new Dictionary<Type, ZombieSpeedReflectionCache>();

	private float _nextRoomConfigSyncTime = -10f;

	private float _nextConfigCheckTime = -10f;

	private float _nextLobbyNoticeUpdateTime = -10f;

	private float _nextZombieTimerUpdateTime = -10f;

	private float _nextZombieSpeedUpdateCheckTime = -10f;

	private float _nextLocalCharacterRefreshTime = -10f;

	private float _nextPendingRemoteGrantProcessTime = -10f;

	private LobbyConfigPanel _lobbyConfigPanel;

	public static Plugin Instance { get; private set; }

	internal static ManualLogSource Log { get; private set; }

	public static ConfigEntry<float> FireInterval { get; private set; }

	public static ConfigEntry<float> ZombieTimeReduction { get; private set; }

	public static ConfigEntry<float> FireVolume { get; private set; }

	public static ConfigEntry<float> WeaponModelPitch { get; private set; }

	public static ConfigEntry<float> WeaponModelYaw { get; private set; }

	public static ConfigEntry<float> WeaponModelRoll { get; private set; }

	public static ConfigEntry<float> WeaponModelScale { get; private set; }

	public static ConfigEntry<float> WeaponModelOffsetX { get; private set; }

	public static ConfigEntry<float> WeaponModelOffsetY { get; private set; }

	public static ConfigEntry<float> WeaponModelOffsetZ { get; private set; }

	public static ConfigEntry<string> AkSoundSelection { get; private set; }

	public static ConfigEntry<string> WeaponSelection { get; private set; }

	public static ConfigEntry<KeyCode> SpawnWeaponKey { get; private set; }

	public static ConfigEntry<KeyCode> OpenConfigPanelKey { get; private set; }

	public static ConfigEntry<string> ConfigPanelTheme { get; private set; }

	public static ConfigEntry<bool> ModEnabled { get; private set; }

	public static ConfigEntry<bool> WeaponEnabled { get; private set; }

	public static ConfigEntry<float> ZombieMoveSpeed { get; private set; }

	public static ConfigEntry<float> ZombieAggressiveness { get; private set; }

	public static ConfigEntry<float> ZombieKnockbackForce { get; private set; }

	public static ConfigEntry<bool> ZombieSpawnEnabled { get; private set; }

	public static ConfigEntry<float> ZombieSpawnInterval { get; private set; }

	public static ConfigEntry<float> ZombieSpawnIntervalRandom { get; private set; }

	public static ConfigEntry<float> ZombieSpawnRadius { get; private set; }

	public static ConfigEntry<int> ZombieSpawnCount { get; private set; }

	public static ConfigEntry<int> ZombieSpawnCountRandom { get; private set; }

	public static ConfigEntry<int> MaxZombies { get; private set; }

	public static ConfigEntry<float> ZombieMaxLifetime { get; private set; }

	public static ConfigEntry<float> ZombieDestroyDistance { get; private set; }

	public static ConfigEntry<float> DistanceBeforeWakeup { get; private set; }

	public static ConfigEntry<float> DistanceBeforeChase { get; private set; }

	public static ConfigEntry<float> ZombieSprintDistance { get; private set; }

	public static ConfigEntry<float> ChaseTimeBeforeSprint { get; private set; }

	public static ConfigEntry<float> ZombieLungeDistance { get; private set; }

	public static ConfigEntry<string> ZombieBehaviorDifficulty { get; private set; }

	public static ConfigEntry<float> ZombieTargetSearchInterval { get; private set; }

	public static ConfigEntry<float> ZombieBiteRecoveryTime { get; private set; }

	public static ConfigEntry<float> ZombieSamePlayerBiteCooldown { get; private set; }

	public static ConfigEntry<float> ZombieLungeTime { get; private set; }

	public static ConfigEntry<float> ZombieLungeRecoveryTime { get; private set; }

	public static ConfigEntry<float> ZombieLookAngleBeforeWakeup { get; private set; }

	public static void PlayGunshotSound(Vector3 position)
	{
		if ((Object)_gunshotSound == (Object)null)
		{
			return;
		}
		try
		{
			if (!(Time.time - _lastSoundTime < 0.05f))
			{
				_lastSoundTime = Time.time;
				if ((Object)_sharedAudioSource == (Object)null)
				{
					GameObject val = new GameObject("AK47_GunshotSound");
					_sharedAudioSource = val.AddComponent<AudioSource>();
					_sharedAudioSource.spatialBlend = 1f;
					_sharedAudioSource.maxDistance = 100f;
					Object.DontDestroyOnLoad((Object)val);
				}
				((Component)_sharedAudioSource).transform.position = position;
				_sharedAudioSource.clip = _gunshotSound;
				_sharedAudioSource.volume = FireVolume.Value;
				float length = _gunshotSound.length;
				float value = FireInterval.Value;
				float num = 1f;
				if (value <= length)
				{
					float num2 = length / value;
					float num3 = Random.Range(-0.1f, 0.1f);
					num = num2 + num3;
				}
				else
				{
					num = 1f + Random.Range(-0.05f, 0.05f);
				}
				_sharedAudioSource.pitch = num;
				_sharedAudioSource.Play();
			}
		}
		catch (Exception)
		{
		}
	}

	private static void PlayFallbackGunshotSound(Vector3 position)
	{
		try
		{
			AudioSource fallbackGunshotAudioSource = GetOrCreateFallbackGunshotAudioSource();
			if ((Object)fallbackGunshotAudioSource == (Object)null)
			{
				return;
			}
			((Component)fallbackGunshotAudioSource).transform.position = position;
			fallbackGunshotAudioSource.volume = 0.8f;
			fallbackGunshotAudioSource.pitch = 1.4f;
			if ((Object)fallbackGunshotAudioSource.clip != (Object)null)
			{
				fallbackGunshotAudioSource.Play();
			}
		}
		catch
		{
		}
	}

	private static AudioSource GetOrCreateFallbackGunshotAudioSource()
	{
		if ((Object)_sharedFallbackGunshotAudioSource != (Object)null)
		{
			return _sharedFallbackGunshotAudioSource;
		}
		GameObject val = new GameObject("DefaultGunshotSound");
		_sharedFallbackGunshotAudioSource = val.AddComponent<AudioSource>();
		_sharedFallbackGunshotAudioSource.spatialBlend = 1f;
		_sharedFallbackGunshotAudioSource.rolloffMode = AudioRolloffMode.Linear;
		_sharedFallbackGunshotAudioSource.minDistance = 1.5f;
		_sharedFallbackGunshotAudioSource.maxDistance = 100f;
		_sharedFallbackGunshotAudioSource.dopplerLevel = 0f;
		_sharedFallbackGunshotAudioSource.playOnAwake = false;
		Object.DontDestroyOnLoad((Object)val);
		return _sharedFallbackGunshotAudioSource;
	}

	private static AudioClip ResolveGunshotSoundClip(string selection = null)
	{
		string text = NormalizeAkSoundSelection(selection);
		if (!string.IsNullOrWhiteSpace(text) && _externalGunshotSounds.TryGetValue(text, out var value) && (Object)value != (Object)null)
		{
			return value;
		}
		if (string.IsNullOrWhiteSpace(text) || string.Equals(_currentGunshotSoundSelection, text, StringComparison.OrdinalIgnoreCase))
		{
			return _gunshotSound;
		}
		return _gunshotSound;
	}

	private static void PlayRemoteGunshotSound(Vector3 position, string selection = null)
	{
		AudioClip clip = ResolveGunshotSoundClip(selection);
		if ((Object)clip == (Object)null)
		{
			return;
		}
		try
		{
			RemoteGunshotAudioInstance remoteGunshotAudioInstance = AcquireRemoteGunshotAudioInstance();
			if (remoteGunshotAudioInstance == null || (Object)remoteGunshotAudioInstance.Root == (Object)null || (Object)remoteGunshotAudioInstance.AudioSource == (Object)null)
			{
				return;
			}
			remoteGunshotAudioInstance.Root.transform.position = position;
			AudioSource audioSource = remoteGunshotAudioInstance.AudioSource;
			audioSource.volume = FireVolume.Value;
			audioSource.clip = clip;
			float length = clip.length;
			float value = FireInterval.Value;
			float pitch = (value <= length) ? (length / value) : 1f;
			audioSource.pitch = Mathf.Clamp(pitch, 0.5f, 3f);
			audioSource.time = 0f;
			if (!remoteGunshotAudioInstance.Root.activeSelf)
			{
				remoteGunshotAudioInstance.Root.SetActive(true);
			}
			audioSource.Play();
			remoteGunshotAudioInstance.ReleaseAtTime = Time.time + Mathf.Max(clip.length / Mathf.Max(audioSource.pitch, 0.01f), 0.3f) + RemoteGunshotAudioReleasePadding;
		}
		catch (Exception)
		{
		}
	}

	private void Awake()
	{
		Instance = this;
		Log = Logger;
		try
		{
			bool flag = IsChineseLanguage();
			PreparePrimaryConfigFile();
			InitializePluginConfig();
			_loadedZombieBehaviorDifficultyMetadata = ReadStoredZombieBehaviorDifficultySelection();
			InitConfig();
			MigrateFeatureConfigDefinitions();
			MigrateLegacyLocalizedConfigEntries();
			ApplyLocalizedConfigMetadata(flag);
			RefreshLocalizedConfigFiles(flag);
			_lastLanguageSetting = flag;
			_cachedIsChineseLanguage = flag;
			_lastLanguagePollTime = Time.unscaledTime;
			LoadAk47BundleInAwake();
			Harmony val = new Harmony("com.github.Thanks.ShootZombies");
			val.PatchAll(Assembly.GetExecutingAssembly());
			PatchUpdatedRunMethods(val);
			if (!DisableModConfigRuntimePatches)
			{
				PatchModConfigUiMethods(val);
			}
			InitializeRoomConfigCallbacks();
			SceneManager.sceneLoaded += OnSceneLoaded;
			((MonoBehaviour)this).StartCoroutine(DeferredInitialLocalizationRefresh());
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("[ShootZombies] Awake failed: " + ex));
		}
	}

	private void InitializePluginConfig()
	{
		try
		{
			string canonicalConfigPath = GetCanonicalConfigPath();
			if (string.IsNullOrWhiteSpace(canonicalConfigPath))
			{
				_pluginConfig = ((BaseUnityPlugin)this).Config;
				_pluginConfigPath = GetConfigFilePath(_pluginConfig);
				return;
			}
			string directoryName = Path.GetDirectoryName(canonicalConfigPath);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			_pluginConfig = new ConfigFile(canonicalConfigPath, saveOnInit: true);
			FieldInfo fieldInfo = typeof(BaseUnityPlugin).GetField("<Config>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
			if (fieldInfo == null)
			{
				throw new MissingFieldException("BepInEx.BaseUnityPlugin", "<Config>k__BackingField");
			}
			fieldInfo.SetValue(this, _pluginConfig);
			_pluginConfigPath = GetConfigFilePath(_pluginConfig);
			if (string.IsNullOrWhiteSpace(_pluginConfigPath))
			{
				_pluginConfigPath = canonicalConfigPath;
			}
		}
		catch (Exception ex)
		{
			_pluginConfig = ((BaseUnityPlugin)this).Config;
			_pluginConfigPath = GetConfigFilePath(_pluginConfig);
			Log.LogWarning((object)("[ShootZombies] InitializePluginConfig failed, falling back to default config routing: " + DescribeReflectionException(ex)));
		}
	}

	private IEnumerator DeferredInitialLocalizationRefresh()
	{
		for (int i = 0; i < 20; i++)
		{
			yield return null;
		}
		for (int i = 0; i < 180; i++)
		{
			if (TryResolveGameLanguage(out var _, out var _, out var _))
			{
				break;
			}
			yield return null;
		}
		try
		{
			ReinitializeConfig();
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] DeferredInitialLocalizationRefresh failed: " + ex.Message));
		}
	}

	private static void CharacterUpdatePostfix(Character __instance)
	{
	}

	private void PatchUpdatedRunMethods(Harmony harmony)
	{
		if (harmony == null)
		{
			return;
		}
		try
		{
			HarmonyMethod val = new HarmonyMethod(AccessTools.Method(typeof(Plugin), "UpdatedRunLifecyclePostfix", (Type[])null, (Type[])null));
			MethodInfo[] array = new MethodInfo[3]
			{
				AccessTools.Method(typeof(RunManager), "StartRun", Type.EmptyTypes, (Type[])null),
				AccessTools.Method(typeof(RunManager), "JumpToMiniRunBiomeWhenReady", Type.EmptyTypes, (Type[])null),
				AccessTools.Method(typeof(RunManager), "SetUpFromQuicksave", new Type[2]
				{
					typeof(Guid),
					typeof(float)
				}, (Type[])null)
			};
			MethodInfo[] array2 = array;
			foreach (MethodInfo methodInfo in array2)
			{
				if (methodInfo != null)
				{
					harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, val, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] PatchUpdatedRunMethods failed: " + DescribeReflectionException(ex)));
		}
	}

	private static void UpdatedRunLifecyclePostfix()
	{
		try
		{
			_cachedHeldBlowgunItem = null;
			_lastHeldBlowgunSearchTime = -10f;
			RequestAkVisualRefresh(includeUiRefresh: true);
			TryScheduleDelayedBlowgunReplacement();
			ClearLocalAmbientColdStatus();
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] UpdatedRunLifecyclePostfix failed: " + ex.Message));
		}
	}

	private void LoadAk47BundleInAwake()
	{
		try
		{
			LoadBundleInternal();
			if (HasAkVisualPrefab())
			{
				return;
			}
			TryLoadLightweightAkResources();
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[ShootZombies] LoadAk47BundleInAwake error: " + ex));
		}
	}

	private bool TryLoadLightweightAkResources()
	{
		try
		{
			if (!AkLightweightAssetLoader.TryLoad(out var prefab, out var diagnostic))
			{
				LogDiagnosticOnce("ak-lightweight-unavailable", "Lightweight AK resources unavailable: " + diagnostic);
				return false;
			}
			ResetAkBundleSelectionState();
			_ak47Prefab = prefab;
			_ak47Bundle = null;
			_ak47VFX = null;
			_ak47ItemContent = null;
			_ak47IconTexture = null;
			if (AkLightweightAssetLoader.TryLoadIconTexture(out var texture, out var diagnostic2))
			{
				_ak47IconTexture = texture;
				LogDiagnosticOnce("ak-lightweight-icon-loaded", "Loaded lightweight AK icon: " + diagnostic2);
			}
			CaptureGenericResolvedWeaponAssets();
			ApplySelectedWeaponAssets();
			if (!HasAkVisualPrefab())
			{
				LogDiagnosticOnce("ak-lightweight-invalid", "Lightweight AK load produced no renderable prefab: " + DescribeAkVisualPrefabForDiagnostics());
				ResetAkBundleSelectionState();
				return false;
			}
			LogDiagnosticOnce("ak-lightweight-loaded", "Loaded lightweight AK resources: " + diagnostic + ", prefab=" + DescribeAkVisualPrefabForDiagnostics());
			ReplaceBlowgunSound();
			return true;
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] Lightweight AK load failed, falling back to bundle: " + ex.Message));
			ResetAkBundleSelectionState();
			return false;
		}
	}

	private void LoadBundleInternal()
	{
		try
		{
			List<string> list = FindBundleCandidatePaths().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (list.Count == 0)
			{
				Log.LogWarning((object)"[ShootZombies] No bundle files found");
				return;
			}
			foreach (string item in list)
			{
				ResetAkBundleSelectionState();
				AssetBundle val = AssetBundle.LoadFromFile(item);
				if ((Object)val == (Object)null)
				{
					Log.LogWarning((object)("[ShootZombies] Failed to load bundle from path: " + item));
					continue;
				}
				OnBundleLoaded(val);
				if (HasAkVisualPrefab())
				{
					LogDiagnosticOnce("ak-bundle-accepted:" + item, "Accepted AK bundle path: " + item);
					return;
				}
				LogDiagnosticOnce("ak-bundle-rejected:" + item, "Rejected AK bundle path because no renderable AK prefab was resolved: " + item + ", prefab=" + DescribeAkVisualPrefabForDiagnostics());
				Log.LogWarning((object)("[ShootZombies] Bundle loaded but no model found, trying next candidate: " + item));
				val.Unload(false);
				if ((Object)_ak47Bundle == (Object)val)
				{
					_ak47Bundle = null;
				}
				ResetAkBundleSelectionState();
			}
			ResetAkBundleSelectionState();
			Log.LogWarning((object)"[ShootZombies] No usable AK bundle found after scanning all candidate paths");
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[ShootZombies] LoadBundleInternal error: " + ex));
		}
	}

	private static IEnumerable<string> FindBundleCandidatePaths()
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string[] array = new string[4] { "Weapons_shootzombies.bundle", "ak47_shootzombies.peakbundle", "shootzombies_weapons.peakbundle", "ak.peakbundle" };
		foreach (string item in GetBundleSearchRoots())
		{
			if (string.IsNullOrWhiteSpace(item) || !Directory.Exists(item))
			{
				continue;
			}
			string[] array2 = array;
			foreach (string path in array2)
			{
				string text = Path.Combine(item, path);
				if (File.Exists(text) && seen.Add(text))
				{
					yield return text;
				}
			}
		}
		foreach (string item in GetBundleSearchRoots())
		{
			if (string.IsNullOrWhiteSpace(item) || !Directory.Exists(item))
			{
				continue;
			}
			string[] array2 = array;
			foreach (string searchPattern in array2)
			{
				string[] array3 = Array.Empty<string>();
				try
				{
					array3 = Directory.GetFiles(item, searchPattern, SearchOption.AllDirectories);
				}
				catch
				{
				}
				string[] array4 = array3;
				foreach (string text2 in array4)
				{
					if (seen.Add(text2))
					{
						yield return text2;
					}
				}
			}
		}
	}

	private static IEnumerable<string> GetBundleSearchRoots()
	{
		string text = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
		string text2 = Paths.PluginPath ?? string.Empty;
		string text3 = ((string.IsNullOrWhiteSpace(text2) || !Directory.Exists(text2)) ? string.Empty : (Directory.GetParent(text2)?.FullName ?? string.Empty));
		if (!string.IsNullOrWhiteSpace(text))
		{
			yield return text;
		}
		if (!string.IsNullOrWhiteSpace(text2))
		{
			yield return text2;
		}
		if (!string.IsNullOrWhiteSpace(text3))
		{
			yield return text3;
		}
	}

	private void OnBundleLoaded(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			Log.LogError((object)"[ShootZombies] AssetBundle is null");
			return;
		}
		try
		{
			_ak47Bundle = bundle;
			_ak47VFX = null;
			TryResolvePreferredAkAssets(bundle);
			TryResolveKnownAkItemContent(bundle);
			Object[] array = bundle.LoadAllAssets();
			foreach (Object val in array)
			{
				Type type = ((object)val).GetType();
				if (type.Name.Contains("ItemContent") || type.Name.Contains("IC_AK"))
				{
					_ak47ItemContent = val;
					LogItemContentReflectionDiagnosis(_ak47ItemContent);
					FieldInfo field = type.GetField("ItemPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					PropertyInfo property = type.GetProperty("ItemPrefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (property != null)
					{
						object value = property.GetValue(val);
						_ak47Prefab = (GameObject)((value is GameObject) ? value : null);
					}
					else if (field != null)
					{
						object value2 = field.GetValue(val);
						_ak47Prefab = (GameObject)((value2 is GameObject) ? value2 : null);
					}
					if ((Object)_ak47IconTexture == (Object)null)
					{
						Texture2D val2 = TryExtractIconFromItemContent(_ak47ItemContent);
						if ((Object)val2 != (Object)null)
						{
							_ak47IconTexture = val2;
						}
					}
				}
				GameObject val3 = (GameObject)((val is GameObject) ? val : null);
				if ((Object)(object)val3 != (Object)null && IsLikelyAkVisualPrefab(val3))
				{
					_ak47VFX = val3;
				}
				if ((Object)(object)val3 != (Object)null && (Object)_ak47Prefab == (Object)null)
				{
					MeshRenderer[] componentsInChildren = val3.GetComponentsInChildren<MeshRenderer>(true);
					SkinnedMeshRenderer[] componentsInChildren2 = val3.GetComponentsInChildren<SkinnedMeshRenderer>(true);
					MeshFilter[] componentsInChildren3 = val3.GetComponentsInChildren<MeshFilter>(true);
					if (componentsInChildren.Length != 0 || componentsInChildren2.Length != 0 || componentsInChildren3.Length != 0)
					{
						_ak47Prefab = val3;
					}
				}
				Texture2D val5 = (Texture2D)((val is Texture2D) ? val : null);
				if ((Object)(object)val5 != (Object)null && (Object)_ak47IconTexture == (Object)null && IsLikelyIconTexture(val5))
				{
					_ak47IconTexture = val5;
				}
			}
			TryResolveItemContentFromNamedAssets(bundle);
			if ((Object)_ak47Prefab == (Object)null)
			{
				GameObject[] array2 = bundle.LoadAllAssets<GameObject>();
				GameObject[] array3 = array2;
				foreach (GameObject val6 in array3)
				{
					MeshRenderer[] componentsInChildren4 = val6.GetComponentsInChildren<MeshRenderer>(true);
					SkinnedMeshRenderer[] componentsInChildren5 = val6.GetComponentsInChildren<SkinnedMeshRenderer>(true);
					MeshFilter[] componentsInChildren6 = val6.GetComponentsInChildren<MeshFilter>(true);
					if (componentsInChildren4.Length != 0 || componentsInChildren5.Length != 0 || componentsInChildren6.Length != 0)
					{
						_ak47Prefab = val6;
					}
				}
				if ((Object)_ak47Prefab == (Object)null && array2.Length != 0)
				{
					_ak47Prefab = array2[0];
				}
			}
			if (!HasRenderableGeometry(_ak47Prefab))
			{
				GameObject val10 = TryLoadRenderablePrefabFromNamedAssets(bundle);
				if ((Object)val10 != (Object)null)
				{
					_ak47Prefab = val10;
				}
			}
			if (!HasRenderableGeometry(_ak47Prefab))
			{
				Mesh[] array7 = bundle.LoadAllAssets<Mesh>();
				Material[] array8 = bundle.LoadAllAssets<Material>();
				Texture2D[] array9 = bundle.LoadAllAssets<Texture2D>();
				GameObject val11 = TryCreateSyntheticAkPrefabFromBundle(array7, array8, array9);
				if ((Object)val11 != (Object)null)
				{
					_ak47Prefab = val11;
				}
			}
			if (!HasRenderableGeometry(_ak47Prefab))
			{
				GameObject val12 = TryCreateSyntheticAkPrefabFromNamedAssets(bundle);
				if ((Object)val12 != (Object)null)
				{
					_ak47Prefab = val12;
				}
			}
			if ((Object)_ak47Prefab == (Object)null)
			{
				Log.LogWarning((object)"[ShootZombies] No model found in bundle, creating fallback cube model");
				_ak47Prefab = CreateFallbackModel();
			}
			if ((Object)_ak47VFX == (Object)null)
			{
				GameObject val7 = bundle.LoadAsset<GameObject>("VFX_AK");
				if ((Object)val7 != (Object)null)
				{
					_ak47VFX = val7;
				}
			}
			Texture2D[] array5 = bundle.LoadAllAssets<Texture2D>();
			if (array5 != null && array5.Length != 0 && (Object)_ak47IconTexture == (Object)null)
			{
				Texture2D[] array6 = array5;
				foreach (Texture2D val8 in array6)
				{
					if (IsLikelyIconTexture(val8))
					{
						_ak47IconTexture = val8;
						break;
					}
				}
			}
			if ((Object)_ak47IconTexture == (Object)null && _ak47ItemContent != null)
			{
				Texture2D val9 = TryExtractIconFromItemContent(_ak47ItemContent);
				if ((Object)val9 != (Object)null)
				{
					_ak47IconTexture = val9;
				}
			}
			CaptureGenericResolvedWeaponAssets();
			TryResolveSelectableWeaponAssets(bundle);
			ApplySelectedWeaponAssets();
			LogBundleSelectionDiagnosis(bundle);
			ReplaceBlowgunSound();
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[ShootZombies] OnBundleLoaded error: " + ex));
		}
	}

	private static void ResetAkBundleSelectionState()
	{
		for (int num = _runtimeWeaponPrefabClones.Count - 1; num >= 0; num--)
		{
			GameObject val = _runtimeWeaponPrefabClones[num];
			if (!((Object)val == (Object)null))
			{
				try
				{
					Object.DestroyImmediate((Object)val);
				}
				catch
				{
				}
			}
		}
		_runtimeWeaponPrefabClones.Clear();
		_resolvedWeaponPrefabs.Clear();
		_resolvedWeaponIcons.Clear();
		_ak47Bundle = null;
		_ak47Prefab = null;
		_ak47VFX = null;
		_ak47ItemContent = null;
		_ak47IconTexture = null;
		_genericResolvedWeaponPrefab = null;
		_genericResolvedWeaponIcon = null;
		_useExplicitSelectedWeaponIcon = false;
	}

	private void TryResolveItemContentFromNamedAssets(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null || _ak47ItemContent != null)
		{
			return;
		}
		TryResolveKnownAkItemContent(bundle);
		if (_ak47ItemContent != null)
		{
			return;
		}
		string[] allAssetNames = bundle.GetAllAssetNames() ?? Array.Empty<string>();
		foreach (string text in allAssetNames)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			Object val = null;
			try
			{
				val = bundle.LoadAsset(text);
			}
			catch (Exception ex2)
			{
				LogDiagnosticOnce("ak-itemcontent-load-error:" + text, "Named asset load failed: asset=" + text + ", error=" + ex2.GetType().Name);
			}
			if ((Object)val == (Object)null)
			{
				if (text.IndexOf("ic_ak", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					LogDiagnosticOnce("ak-itemcontent-null:" + text, "Named asset load returned null: asset=" + text);
					Object[] array = null;
					try
					{
						array = bundle.LoadAssetWithSubAssets(text);
					}
					catch (Exception ex3)
					{
						LogDiagnosticOnce("ak-itemcontent-subasset-error:" + text, "LoadAssetWithSubAssets failed: asset=" + text + ", error=" + ex3.GetType().Name);
					}
					if (array == null || array.Length == 0)
					{
						LogDiagnosticOnce("ak-itemcontent-subasset-empty:" + text, "LoadAssetWithSubAssets returned no sub-assets: asset=" + text);
					}
					else
					{
						string value = string.Join(" || ", array.Where((Object item) => (Object)item != (Object)null).Select((Object item) => item.GetType().FullName + ":" + item.name).Take(24));
						LogDiagnosticOnce("ak-itemcontent-subasset-list:" + text, "LoadAssetWithSubAssets returned: asset=" + text + ", items=" + value);
						Object[] array2 = array;
						foreach (Object val2 in array2)
						{
							if ((Object)val2 == (Object)null)
							{
								continue;
							}
							Type type2 = ((object)val2).GetType();
							if (!type2.Name.Contains("ItemContent") && !type2.Name.Contains("IC_AK"))
							{
								continue;
							}
							_ak47ItemContent = val2;
							LogItemContentReflectionDiagnosis(_ak47ItemContent);
							TryAssignAkPrefabFromItemContent(_ak47ItemContent);
							if ((Object)_ak47IconTexture == (Object)null)
							{
								_ak47IconTexture = TryExtractIconFromItemContent(_ak47ItemContent);
							}
							LogDiagnosticOnce("ak-itemcontent-resolved-subasset", "Resolved IC_AK from sub-asset: " + text + ", type=" + type2.FullName + ", name=" + ((Object)val2).name);
							break;
						}
						if (_ak47ItemContent != null)
						{
							break;
						}
					}
				}
				continue;
			}
			Type type = ((object)val).GetType();
			LogDiagnosticOnce("ak-itemcontent-load:" + text, "Named asset load: asset=" + text + ", type=" + type.FullName + ", name=" + ((Object)val).name);
			if (!type.Name.Contains("ItemContent") && !type.Name.Contains("IC_AK"))
			{
				continue;
			}
			_ak47ItemContent = val;
			LogItemContentReflectionDiagnosis(_ak47ItemContent);
			TryAssignAkPrefabFromItemContent(_ak47ItemContent);
			if ((Object)_ak47IconTexture == (Object)null)
			{
				_ak47IconTexture = TryExtractIconFromItemContent(_ak47ItemContent);
			}
			LogDiagnosticOnce("ak-itemcontent-resolved", "Resolved IC_AK from named asset: " + text);
			break;
		}
	}

	private void TryAssignAkPrefabFromItemContent(object itemContent)
	{
		if (itemContent == null || (Object)_ak47Prefab != (Object)null)
		{
			return;
		}
		try
		{
			Type type = itemContent.GetType();
			foreach (PropertyInfo item in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (!item.CanRead || item.GetIndexParameters().Length != 0)
				{
					continue;
				}
				object value = null;
				try
				{
					value = item.GetValue(itemContent, null);
				}
				catch
				{
				}
				if (TryAssignAkPrefabFromValue(value, "prop " + item.Name))
				{
					return;
				}
			}
			foreach (FieldInfo item2 in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				object value2 = null;
				try
				{
					value2 = item2.GetValue(itemContent);
				}
				catch
				{
				}
				if (TryAssignAkPrefabFromValue(value2, "field " + item2.Name))
				{
					return;
				}
			}
		}
		catch (Exception ex)
		{
			LogDiagnosticOnce("ak-itemcontent-prefab-error", "TryAssignAkPrefabFromItemContent failed: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	private bool TryAssignAkPrefabFromValue(object value, string source)
	{
		GameObject val = (GameObject)((value is GameObject) ? value : null);
		if ((Object)val != (Object)null && HasRenderableGeometry(val))
		{
			_ak47Prefab = val;
			LogDiagnosticOnce("ak-itemcontent-prefab:" + source, "Resolved AK prefab from IC_AK " + source + ": " + DescribeGameObjectForDiagnostics(val));
			return true;
		}
		Item val2 = (Item)((value is Item) ? value : null);
		if ((Object)val2 != (Object)null && HasRenderableGeometry(((Component)val2).gameObject))
		{
			_ak47Prefab = ((Component)val2).gameObject;
			LogDiagnosticOnce("ak-itemcontent-item:" + source, "Resolved AK item/prefab from IC_AK " + source + ": " + DescribeGameObjectForDiagnostics(_ak47Prefab));
			return true;
		}
		Component val3 = (Component)((value is Component) ? value : null);
		if ((Object)val3 != (Object)null && HasRenderableGeometry(((Component)val3).gameObject))
		{
			_ak47Prefab = ((Component)val3).gameObject;
			LogDiagnosticOnce("ak-itemcontent-component:" + source, "Resolved AK component/prefab from IC_AK " + source + ": " + DescribeGameObjectForDiagnostics(_ak47Prefab));
			return true;
		}
		return false;
	}

	private void LogBundleSelectionDiagnosis(AssetBundle bundle)
	{
		string fullName = _ak47ItemContent?.GetType().FullName ?? "null";
		string text = (((Object)_ak47VFX != (Object)null) ? ((Object)_ak47VFX).name : "null");
		string text2 = DescribeBundleAssetSummary(bundle);
		LogDiagnosticOnce("ak-bundle-selection", $"Bundle selection complete: bundle={(((Object)bundle != (Object)null) ? ((Object)bundle).name : "null")}, itemContentType={fullName}, prefab={DescribeAkVisualPrefabForDiagnostics()}, vfx={text}, icon={DescribeTextureForDiagnostics(_ak47IconTexture)}, assets={text2}");
	}

	private static string DescribeBundleAssetSummary(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			return "bundle=null";
		}
		try
		{
			GameObject[] array = bundle.LoadAllAssets<GameObject>();
			Mesh[] array2 = bundle.LoadAllAssets<Mesh>();
			Material[] array3 = bundle.LoadAllAssets<Material>();
			Texture2D[] array4 = bundle.LoadAllAssets<Texture2D>();
			string[] allAssetNames = bundle.GetAllAssetNames() ?? Array.Empty<string>();
			string[] allScenePaths = bundle.GetAllScenePaths() ?? Array.Empty<string>();
			string text = string.Join(", ", array.Take(6).Select((GameObject go) => ((Object)go).name));
			string text2 = string.Join(", ", array2.OrderByDescending((Mesh mesh) => (mesh != null) ? mesh.vertexCount : 0).Take(6).Select((Mesh mesh) => $"{((Object)mesh).name}:{mesh.vertexCount}"));
			string text3 = string.Join(", ", array3.Take(6).Select((Material material) => ((Object)material).name));
			string text4 = string.Join(", ", array4.Take(6).Select((Texture2D texture) => $"{((Object)texture).name}:{((Texture)texture).width}x{((Texture)texture).height}"));
			string text5 = string.Join(", ", allAssetNames.Take(8));
			string text6 = string.Join(", ", allScenePaths.Take(4));
			return $"go[{array.Length}]={text}; mesh[{array2.Length}]={text2}; mat[{array3.Length}]={text3}; tex[{array4.Length}]={text4}; names[{allAssetNames.Length}]={text5}; scenes[{allScenePaths.Length}]={text6}";
		}
		catch (Exception ex)
		{
			return "asset-summary-error:" + ex.GetType().Name;
		}
	}

	private void LogItemContentReflectionDiagnosis(object itemContent)
	{
		if (itemContent == null)
		{
			return;
		}
		try
		{
			Type type = itemContent.GetType();
			List<string> list = new List<string>();
			BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
			foreach (FieldInfo item in type.GetFields(bindingAttr).OrderBy((FieldInfo f) => f.Name))
			{
				object value = null;
				string text = "unreadable";
				try
				{
					value = item.GetValue(itemContent);
					text = DescribeReflectionValue(value);
				}
				catch (Exception ex2)
				{
					text = "error:" + ex2.GetType().Name;
				}
				list.Add("field " + item.Name + "=" + text);
			}
			foreach (PropertyInfo item2 in type.GetProperties(bindingAttr).OrderBy((PropertyInfo p) => p.Name))
			{
				if (!item2.CanRead || item2.GetIndexParameters().Length != 0)
				{
					continue;
				}
				object value2 = null;
				string text2 = "unreadable";
				try
				{
					value2 = item2.GetValue(itemContent, null);
					text2 = DescribeReflectionValue(value2);
				}
				catch (Exception ex3)
				{
					text2 = "error:" + ex3.GetType().Name;
				}
				list.Add("prop " + item2.Name + "=" + text2);
			}
			LogDiagnosticOnce("ak-ic-members:" + type.FullName, "IC_AK members: type=" + type.FullName + ", summary=" + string.Join(" || ", list.Take(24)));
			List<DiagnosticGraphHit> list2 = new List<DiagnosticGraphHit>();
			HashSet<object> visited = new HashSet<object>(ReferenceIdentityComparer.Instance);
			InspectItemContentGraph(itemContent, "IC_AK", 0, 3, visited, list2);
			if (list2.Count == 0)
			{
				LogDiagnosticOnce("ak-ic-candidates:" + type.FullName, "IC_AK graph candidates: none");
				return;
			}
			string value3 = string.Join(" || ", list2.OrderByDescending((DiagnosticGraphHit hit) => hit.Score).ThenBy((DiagnosticGraphHit hit) => hit.Path).Take(16).Select((DiagnosticGraphHit hit) => $"{hit.Path} => {hit.Summary}"));
			LogDiagnosticOnce("ak-ic-candidates:" + type.FullName, "IC_AK graph candidates: " + value3);
		}
		catch (Exception ex)
		{
			LogDiagnosticOnce("ak-ic-reflection-error", "IC_AK reflection diagnosis failed: " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	private static void InspectItemContentGraph(object value, string path, int depth, int maxDepth, HashSet<object> visited, List<DiagnosticGraphHit> hits)
	{
		if (value == null || depth > maxDepth)
		{
			return;
		}
		Type type = value.GetType();
		if (!(value is string) && !type.IsValueType && !visited.Add(value))
		{
			return;
		}
		if (TryCreateDiagnosticGraphHit(value, path, out var hit))
		{
			hits.Add(hit);
		}
		if (depth >= maxDepth || IsTerminalDiagnosticType(type))
		{
			return;
		}
		if (value is IEnumerable enumerable && !(value is string))
		{
			int num = 0;
			foreach (object item in enumerable)
			{
				InspectItemContentGraph(item, path + "[" + num + "]", depth + 1, maxDepth, visited, hits);
				num++;
				if (num >= 8)
				{
					break;
				}
			}
			return;
		}
		BindingFlags bindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		foreach (FieldInfo item2 in type.GetFields(bindingAttr))
		{
			if (ShouldSkipDiagnosticMember(item2.FieldType))
			{
				continue;
			}
			object value2 = null;
			try
			{
				value2 = item2.GetValue(value);
			}
			catch
			{
			}
			if (value2 != null)
			{
				InspectItemContentGraph(value2, path + "." + item2.Name, depth + 1, maxDepth, visited, hits);
			}
		}
		foreach (PropertyInfo item3 in type.GetProperties(bindingAttr))
		{
			if (!item3.CanRead || item3.GetIndexParameters().Length != 0 || ShouldSkipDiagnosticMember(item3.PropertyType))
			{
				continue;
			}
			object value3 = null;
			try
			{
				value3 = item3.GetValue(value, null);
			}
			catch
			{
			}
			if (value3 != null)
			{
				InspectItemContentGraph(value3, path + "." + item3.Name, depth + 1, maxDepth, visited, hits);
			}
		}
	}

	private static bool TryCreateDiagnosticGraphHit(object value, string path, out DiagnosticGraphHit hit)
	{
		hit = null;
		Item val = (Item)((value is Item) ? value : null);
		if ((Object)val != (Object)null)
		{
			GameObject gameObject = ((Component)val).gameObject;
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"Item(type={value.GetType().FullName}, go={((Object)gameObject).name}, itemID={val.itemID}, uiName={val.UIData?.itemName ?? "null"}, renderable={HasRenderableGeometry(gameObject)}, detail={DescribeGameObjectForDiagnostics(gameObject, 4)})",
				Score = ScoreDiagnosticGameObject(gameObject)
			};
			return true;
		}
		GameObject val2 = (GameObject)((value is GameObject) ? value : null);
		if ((Object)val2 != (Object)null)
		{
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"GameObject(type={value.GetType().FullName}, renderable={HasRenderableGeometry(val2)}, detail={DescribeGameObjectForDiagnostics(val2, 4)})",
				Score = ScoreDiagnosticGameObject(val2)
			};
			return true;
		}
		Component val3 = (Component)((value is Component) ? value : null);
		if ((Object)val3 != (Object)null)
		{
			GameObject gameObject2 = ((Component)val3).gameObject;
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"Component(type={value.GetType().FullName}, go={((Object)gameObject2).name}, renderable={HasRenderableGeometry(gameObject2)}, detail={DescribeGameObjectForDiagnostics(gameObject2, 4)})",
				Score = ScoreDiagnosticGameObject(gameObject2)
			};
			return true;
		}
		Texture2D val4 = (Texture2D)((value is Texture2D) ? value : null);
		if ((Object)val4 != (Object)null)
		{
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = "Texture2D(" + DescribeTextureForDiagnostics(val4) + ")",
				Score = 200
			};
			return true;
		}
		Material val5 = (Material)((value is Material) ? value : null);
		if ((Object)val5 != (Object)null)
		{
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"Material(name={((Object)val5).name}, shader={val5.shader?.name ?? "null"}, mainTex={(((Object)val5.mainTexture != (Object)null) ? ((Object)val5.mainTexture).name : "null")})",
				Score = 150
			};
			return true;
		}
		Mesh val6 = (Mesh)((value is Mesh) ? value : null);
		if ((Object)val6 != (Object)null)
		{
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"Mesh(name={((Object)val6).name}, verts={val6.vertexCount}, subMeshes={val6.subMeshCount})",
				Score = 100 + val6.vertexCount
			};
			return true;
		}
		UnityEngine.Object val7 = (UnityEngine.Object)((value is UnityEngine.Object) ? value : null);
		if ((Object)val7 != (Object)null)
		{
			hit = new DiagnosticGraphHit
			{
				Path = path,
				Summary = $"UnityObject(type={value.GetType().FullName}, name={((Object)val7).name})",
				Score = 50
			};
			return true;
		}
		return false;
	}

	private static int ScoreDiagnosticGameObject(GameObject candidate)
	{
		if ((Object)candidate == (Object)null)
		{
			return int.MinValue;
		}
		int num = 0;
		string text = (((Object)candidate).name ?? string.Empty).ToLowerInvariant();
		if (text.Contains("ak") || text.Contains("rifle") || text.Contains("gun") || text.Contains("weapon"))
		{
			num += 12000;
		}
		if (text.Contains("vfx") || text.Contains("effect") || text.Contains("flash") || text.Contains("particle") || text.Contains("smoke"))
		{
			num -= 20000;
		}
		MeshFilter[] componentsInChildren = candidate.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val != (Object)null && (Object)val.sharedMesh != (Object)null)
			{
				num += val.sharedMesh.vertexCount;
			}
		}
		SkinnedMeshRenderer[] componentsInChildren2 = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
		{
			if ((Object)val2 != (Object)null && (Object)val2.sharedMesh != (Object)null)
			{
				num += val2.sharedMesh.vertexCount;
			}
		}
		return num;
	}

	private static bool IsTerminalDiagnosticType(Type type)
	{
		if (type == null)
		{
			return true;
		}
		if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
		{
			return true;
		}
		if (typeof(UnityEngine.Object).IsAssignableFrom(type))
		{
			return true;
		}
		string fullName = type.FullName ?? string.Empty;
		if (!fullName.StartsWith("System.", StringComparison.Ordinal) && !fullName.StartsWith("UnityEngine.", StringComparison.Ordinal))
		{
			return fullName.StartsWith("TMPro.", StringComparison.Ordinal);
		}
		return true;
	}

	private static bool ShouldSkipDiagnosticMember(Type memberType)
	{
		if (memberType == null)
		{
			return true;
		}
		if (memberType.IsPointer)
		{
			return true;
		}
		if (!typeof(Delegate).IsAssignableFrom(memberType))
		{
			return memberType == typeof(IntPtr) || memberType == typeof(UIntPtr);
		}
		return true;
	}

	private static string DescribeReflectionValue(object value)
	{
		if (value == null)
		{
			return "null";
		}
		Type type = value.GetType();
		GameObject val = (GameObject)((value is GameObject) ? value : null);
		if ((Object)val != (Object)null)
		{
			return $"GameObject(name={((Object)val).name}, renderable={HasRenderableGeometry(val)})";
		}
		Item val2 = (Item)((value is Item) ? value : null);
		if ((Object)val2 != (Object)null)
		{
			return $"Item(go={((Object)((Component)val2).gameObject).name}, itemID={val2.itemID}, uiName={val2.UIData?.itemName ?? "null"})";
		}
		Component val3 = (Component)((value is Component) ? value : null);
		if ((Object)val3 != (Object)null)
		{
			return $"Component(type={type.FullName}, go={((Object)((Component)val3).gameObject).name})";
		}
		Texture2D val4 = (Texture2D)((value is Texture2D) ? value : null);
		if ((Object)val4 != (Object)null)
		{
			return "Texture2D(" + DescribeTextureForDiagnostics(val4) + ")";
		}
		Material val5 = (Material)((value is Material) ? value : null);
		if ((Object)val5 != (Object)null)
		{
			return $"Material(name={((Object)val5).name}, shader={val5.shader?.name ?? "null"})";
		}
		Mesh val6 = (Mesh)((value is Mesh) ? value : null);
		if ((Object)val6 != (Object)null)
		{
			return $"Mesh(name={((Object)val6).name}, verts={val6.vertexCount})";
		}
		UnityEngine.Object val7 = (UnityEngine.Object)((value is UnityEngine.Object) ? value : null);
		if ((Object)val7 != (Object)null)
		{
			return $"UnityObject(type={type.FullName}, name={((Object)val7).name})";
		}
		if (value is IEnumerable enumerable && !(value is string))
		{
			int num = 0;
			foreach (object item in enumerable)
			{
				num++;
				if (num >= 8)
				{
					break;
				}
			}
			return $"Enumerable(type={type.FullName}, count>={num})";
		}
		return $"Object(type={type.FullName})";
	}

	private static bool IsLikelyIconTexture(Texture2D texture)
	{
		if ((Object)texture == (Object)null)
		{
			return false;
		}
		int width = ((Texture)texture).width;
		int height = ((Texture)texture).height;
		if (width <= 0 || height <= 0)
		{
			return false;
		}
		string text = (((Object)texture).name ?? string.Empty).ToLowerInvariant();
		bool flag = text.Contains("icon") || text.Contains("ui") || text.Contains("thumb") || text.Contains("inventory");
		if (flag)
		{
			if (width <= 4096 && height <= 4096)
			{
				return Mathf.Abs(width - height) <= Mathf.Max(4, Mathf.RoundToInt((float)Mathf.Max(width, height) * 0.02f));
			}
			return false;
		}
		if (width >= 32 && height >= 32 && width <= 512 && height <= 512)
		{
			return Mathf.Abs(width - height) <= 2;
		}
		return false;
	}

	private static Texture2D TryExtractIconFromItemContent(object itemContent)
	{
		if (itemContent == null)
		{
			return null;
		}
		try
		{
			Type type = itemContent.GetType();
			object obj = type.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(itemContent) ?? type.GetField("Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(itemContent);
			Texture2D val = TryExtractIconFromItem((Item)((obj is Item) ? obj : null));
			if ((Object)val != (Object)null)
			{
				return val;
			}
			if (obj != null && !ReferenceEquals(obj, itemContent))
			{
				Texture2D val2 = TryExtractIconFromItemContent(obj);
				if ((Object)val2 != (Object)null)
				{
					return val2;
				}
			}
			object obj2 = type.GetProperty("icon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(itemContent);
			Texture2D val3 = (Texture2D)((obj2 is Texture2D) ? obj2 : null);
			if ((Object)val3 != (Object)null)
			{
				return val3;
			}
			object obj3 = type.GetField("icon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(itemContent);
			Texture2D val4 = (Texture2D)((obj3 is Texture2D) ? obj3 : null);
			if ((Object)val4 != (Object)null)
			{
				return val4;
			}
		}
		catch
		{
		}
		return null;
	}

	private static Texture2D TryExtractIconFromItem(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return null;
		}
		try
		{
			Texture2D val = item.UIData?.icon;
			if ((Object)val != (Object)null)
			{
				return val;
			}
			Texture2D val2 = item.UIData?.altIcon;
			if ((Object)val2 != (Object)null)
			{
				return val2;
			}
			ItemUIData uIData = item.UIData;
			Texture2D val3 = ((uIData != null) ? uIData.GetIcon() : null);
			if ((Object)val3 != (Object)null)
			{
				return val3;
			}
		}
		catch
		{
		}
		return null;
	}

	private void TryResolveKnownAkItemContent(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null || _ak47ItemContent != null)
		{
			return;
		}
		Type type = ResolveLoadedType("PEAKLib.Items.UnityEditor.UnityItemContent", "com.github.PEAKModding.PEAKLib.Items") ?? ResolveLoadedType("PEAKLib.Items.ItemContent", "com.github.PEAKModding.PEAKLib.Items");
		if (type == null)
		{
			LogDiagnosticOnce("ak-itemcontent-type-missing", "PEAKLib.Items is not loaded, so IC_AK cannot be deserialized from the AK bundle.");
			return;
		}
		IEnumerable<string> source = bundle.GetAllAssetNames() ?? Array.Empty<string>();
		foreach (string text in PreferredAkItemContentAssetNames.Concat(source.Where((string name) => !string.IsNullOrWhiteSpace(name) && name.IndexOf("ic_ak", StringComparison.OrdinalIgnoreCase) >= 0)).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			Object val = null;
			try
			{
				val = bundle.LoadAsset(text, type);
			}
			catch (Exception ex)
			{
				LogDiagnosticOnce("ak-itemcontent-typed-load-error:" + text, "Typed IC_AK load failed: asset=" + text + ", type=" + type.FullName + ", error=" + ex.GetType().Name);
			}
			if ((Object)val == (Object)null)
			{
				continue;
			}
			_ak47ItemContent = val;
			LogItemContentReflectionDiagnosis(_ak47ItemContent);
			TryAssignAkPrefabFromItemContent(_ak47ItemContent);
			if ((Object)_ak47IconTexture == (Object)null)
			{
				_ak47IconTexture = TryExtractIconFromItemContent(_ak47ItemContent);
			}
			LogDiagnosticOnce("ak-itemcontent-typed-load:" + text, "Resolved IC_AK using exact type load: asset=" + text + ", type=" + type.FullName + ", name=" + ((Object)val).name);
			break;
		}
	}

	private GameObject CreateFallbackModel()
	{
		GameObject obj = GameObject.CreatePrimitive((PrimitiveType)3);
		((Object)obj).name = "AK47_Fallback";
		obj.transform.localScale = new Vector3(0.1f, 0.1f, 0.5f);
		MeshRenderer component = obj.GetComponent<MeshRenderer>();
		if ((Object)component != (Object)null)
		{
			Material val = new Material(ResolveLocalVisualCompatibleShader());
			if (val.HasProperty("_BaseColor"))
			{
				val.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.2f, 1f));
			}
			if (val.HasProperty("_Color"))
			{
				val.SetColor("_Color", new Color(0.2f, 0.2f, 0.2f, 1f));
			}
			((Renderer)component).material = val;
		}
		return obj;
	}

	private static GameObject TryCreateSyntheticAkPrefabFromBundle(Mesh[] meshes, Material[] materials, Texture2D[] textures)
	{
		Mesh val = SelectBestBundleMesh(meshes);
		if ((Object)val == (Object)null)
		{
			return null;
		}
		int num = Mathf.Max(val.subMeshCount, 1);
		Material[] array = ChooseBundleMaterials(materials, textures, num);
		if (array == null || array.Length == 0)
		{
			array = (Material[])(object)new Material[num];
			for (int i = 0; i < num; i++)
			{
				array[i] = CreateSyntheticFallbackMaterial(null);
			}
		}
		GameObject val2 = new GameObject("AK");
		val2.hideFlags = (HideFlags)61;
		val2.SetActive(false);
		GameObject val3 = new GameObject("Mesh");
		val3.hideFlags = (HideFlags)61;
		val3.transform.SetParent(val2.transform, false);
		MeshFilter val4 = val3.AddComponent<MeshFilter>();
		MeshRenderer obj = val3.AddComponent<MeshRenderer>();
		val4.sharedMesh = val;
		((Renderer)obj).sharedMaterials = NormalizeBundleMaterialArray(array, num);
		LogDiagnosticOnce("ak-synthetic-prefab:" + ((Object)val).name, $"Created synthetic AK prefab from raw bundle mesh: mesh={((Object)val).name}, verts={val.vertexCount}, subMeshes={num}, mats={((Renderer)obj).sharedMaterials.Length}");
		return val2;
	}

	private static GameObject TryLoadRenderablePrefabFromNamedAssets(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			return null;
		}
		string[] allAssetNames = bundle.GetAllAssetNames() ?? Array.Empty<string>();
		GameObject result = null;
		int num = int.MinValue;
		foreach (string text in allAssetNames)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			GameObject val = null;
			try
			{
				val = bundle.LoadAsset<GameObject>(text);
			}
			catch
			{
			}
			if ((Object)val == (Object)null || !HasRenderableGeometry(val))
			{
				continue;
			}
			int num2 = ScoreNamedRenderablePrefab(text, val);
			if (num2 > num)
			{
				num = num2;
				result = val;
			}
		}
		if ((Object)result != (Object)null)
		{
			LogDiagnosticOnce("ak-named-prefab", $"Loaded renderable prefab from named asset: asset={FindAssetNameForObject(bundle, result)}, prefab={DescribeGameObjectForDiagnostics(result)}");
		}
		return result;
	}

	private static int ScoreNamedRenderablePrefab(string assetName, GameObject candidate)
	{
		int num = 0;
		string text = ((assetName ?? string.Empty) + "/" + ((((Object)candidate).name ?? string.Empty))).ToLowerInvariant();
		if (text.Contains("ak") || text.Contains("rifle") || text.Contains("gun") || text.Contains("weapon"))
		{
			num += 12000;
		}
		if (text.Contains("prefab") || text.Contains("model") || text.Contains("mesh"))
		{
			num += 4000;
		}
		if (text.Contains("vfx") || text.Contains("effect") || text.Contains("flash") || text.Contains("smoke") || text.Contains("particle") || text.Contains("muzzle"))
		{
			num -= 20000;
		}
		MeshFilter[] componentsInChildren = candidate.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val != (Object)null && (Object)val.sharedMesh != (Object)null)
			{
				num += val.sharedMesh.vertexCount;
			}
		}
		SkinnedMeshRenderer[] componentsInChildren2 = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
		{
			if ((Object)val2 != (Object)null && (Object)val2.sharedMesh != (Object)null)
			{
				num += val2.sharedMesh.vertexCount;
			}
		}
		return num;
	}

	private static GameObject TryCreateSyntheticAkPrefabFromNamedAssets(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			return null;
		}
		string[] allAssetNames = bundle.GetAllAssetNames() ?? Array.Empty<string>();
		List<Mesh> list = new List<Mesh>();
		List<Material> list2 = new List<Material>();
		List<Texture2D> list3 = new List<Texture2D>();
		foreach (string text in allAssetNames)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			TryAddNamedAsset(bundle, text, list, list2, list3);
		}
		if (list.Count == 0 && list2.Count == 0 && list3.Count == 0)
		{
			return null;
		}
		LogDiagnosticOnce("ak-named-assets-scan", $"Named asset scan found mesh={list.Count}, mat={list2.Count}, tex={list3.Count}");
		return TryCreateSyntheticAkPrefabFromBundle(list.Distinct().ToArray(), list2.Distinct().ToArray(), list3.Distinct().ToArray());
	}

	private static void TryAddNamedAsset(AssetBundle bundle, string assetName, List<Mesh> meshes, List<Material> materials, List<Texture2D> textures)
	{
		try
		{
			Mesh val = bundle.LoadAsset<Mesh>(assetName);
			if ((Object)val != (Object)null)
			{
				meshes.Add(val);
			}
		}
		catch
		{
		}
		try
		{
			Material val2 = bundle.LoadAsset<Material>(assetName);
			if ((Object)val2 != (Object)null)
			{
				materials.Add(val2);
			}
		}
		catch
		{
		}
		try
		{
			Texture2D val3 = bundle.LoadAsset<Texture2D>(assetName);
			if ((Object)val3 != (Object)null)
			{
				textures.Add(val3);
			}
		}
		catch
		{
		}
		try
		{
			Object[] array = bundle.LoadAssetWithSubAssets(assetName);
			if (array == null)
			{
				return;
			}
			foreach (Object val4 in array)
			{
				Mesh val5 = (Mesh)(object)((val4 is Mesh) ? val4 : null);
				if ((Object)val5 != (Object)null)
				{
					meshes.Add(val5);
					continue;
				}
				Material val6 = (Material)(object)((val4 is Material) ? val4 : null);
				if ((Object)val6 != (Object)null)
				{
					materials.Add(val6);
					continue;
				}
				Texture2D val7 = (Texture2D)(object)((val4 is Texture2D) ? val4 : null);
				if ((Object)val7 != (Object)null)
				{
					textures.Add(val7);
				}
			}
		}
		catch
		{
		}
	}

	private static string FindAssetNameForObject(AssetBundle bundle, Object target)
	{
		if ((Object)bundle == (Object)null || (Object)target == (Object)null)
		{
			return "unknown";
		}
		string[] allAssetNames = bundle.GetAllAssetNames() ?? Array.Empty<string>();
		foreach (string text in allAssetNames)
		{
			try
			{
				Object val = bundle.LoadAsset(text);
				if ((Object)val == (Object)target)
				{
					return text;
				}
			}
			catch
			{
			}
		}
		return "unknown";
	}

	private static void TryResolvePreferredAkAssets(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			return;
		}
		if (!HasRenderableGeometry(_ak47Prefab))
		{
			GameObject val = TryLoadNamedGameObject(bundle, PreferredAkPrefabAssetNames);
			if ((Object)val != (Object)null && HasRenderableGeometry(val))
			{
				_ak47Prefab = val;
				LogDiagnosticOnce("ak-preferred-prefab", "Resolved AK prefab from top-level bundle asset: " + DescribeGameObjectForDiagnostics(val));
			}
		}
		if ((Object)_ak47IconTexture == (Object)null)
		{
			Texture2D val2 = TryLoadNamedTexture(bundle, PreferredAkIconAssetNames);
			if ((Object)val2 != (Object)null)
			{
				_ak47IconTexture = val2;
				LogDiagnosticOnce("ak-preferred-icon", "Resolved AK icon from top-level bundle asset: " + DescribeTextureForDiagnostics(val2));
			}
		}
		if ((Object)_ak47VFX == (Object)null)
		{
			GameObject val3 = TryLoadNamedGameObject(bundle, PreferredAkVfxAssetNames);
			if ((Object)val3 != (Object)null)
			{
				_ak47VFX = val3;
			}
		}
	}

	private static GameObject TryLoadNamedGameObject(AssetBundle bundle, IEnumerable<string> assetNames)
	{
		if ((Object)bundle == (Object)null || assetNames == null)
		{
			return null;
		}
		foreach (string assetName in assetNames)
		{
			if (string.IsNullOrWhiteSpace(assetName))
			{
				continue;
			}
			try
			{
				GameObject val = bundle.LoadAsset<GameObject>(assetName);
				if ((Object)val != (Object)null)
				{
					return val;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static Texture2D TryLoadNamedTexture(AssetBundle bundle, IEnumerable<string> assetNames)
	{
		if ((Object)bundle == (Object)null || assetNames == null)
		{
			return null;
		}
		foreach (string assetName in assetNames)
		{
			if (string.IsNullOrWhiteSpace(assetName))
			{
				continue;
			}
			try
			{
				Texture2D val = bundle.LoadAsset<Texture2D>(assetName);
				if ((Object)val != (Object)null)
				{
					return val;
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static Mesh SelectBestBundleMesh(Mesh[] meshes)
	{
		if (meshes == null || meshes.Length == 0)
		{
			return null;
		}
		Mesh result = null;
		int num = int.MinValue;
		foreach (Mesh val in meshes)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			string text = (((Object)val).name ?? string.Empty).ToLowerInvariant();
			int vertexCount = val.vertexCount;
			if (vertexCount <= 24)
			{
				vertexCount -= 20000;
			}
			if (text.Contains("ak") || text.Contains("rifle") || text.Contains("gun") || text.Contains("weapon"))
			{
				vertexCount += 12000;
			}
			if (text.Contains("mesh") || text.Contains("body"))
			{
				vertexCount += 2000;
			}
			if (text.Contains("vfx") || text.Contains("effect") || text.Contains("spawn") || text.Contains("muzzle") || text.Contains("flash") || text.Contains("smoke") || text.Contains("hand") || text.Contains("arm") || text.Contains("finger") || text.Contains("trigger") || text.Contains("collider") || text.Contains("cube") || text.Contains("plane") || text.Contains("quad") || text.Contains("sphere") || text.Contains("capsule"))
			{
				vertexCount -= 30000;
			}
			if (vertexCount > num)
			{
				num = vertexCount;
				result = val;
			}
		}
		if ((Object)result != (Object)null)
		{
			LogDiagnosticOnce("ak-synthetic-mesh-candidate", $"Selected raw bundle mesh candidate: name={((Object)result).name}, verts={result.vertexCount}, score={num}");
		}
		return result;
	}

	private static Material[] ChooseBundleMaterials(Material[] materials, Texture2D[] textures, int subMeshCount)
	{
		subMeshCount = Mathf.Max(subMeshCount, 1);
		List<Material> list = new List<Material>();
		if (materials != null)
		{
			foreach (Material val in materials.OrderByDescending(ScoreBundleMaterial))
			{
				if ((Object)val == (Object)null)
				{
					continue;
				}
				if (ScoreBundleMaterial(val) < -1000)
				{
					continue;
				}
				list.Add(val);
				if (list.Count >= subMeshCount)
				{
					break;
				}
			}
		}
		if (list.Count == 0)
		{
			Texture2D val2 = SelectBestBundleTexture(textures);
			for (int i = 0; i < subMeshCount; i++)
			{
				list.Add(CreateSyntheticFallbackMaterial(val2));
			}
		}
		return NormalizeBundleMaterialArray(list.ToArray(), subMeshCount);
	}

	private static int ScoreBundleMaterial(Material material)
	{
		if ((Object)material == (Object)null)
		{
			return int.MinValue;
		}
		string text = (((Object)material).name ?? string.Empty).ToLowerInvariant();
		int num = 0;
		if (text.Contains("ak") || text.Contains("gun") || text.Contains("weapon") || text.Contains("body") || text.Contains("mat"))
		{
			num += 4000;
		}
		if (text.Contains("vfx") || text.Contains("effect") || text.Contains("particle") || text.Contains("smoke") || text.Contains("flash"))
		{
			num -= 12000;
		}
		Texture mainTexture = material.mainTexture;
		if ((Object)mainTexture != (Object)null)
		{
			num += 2500;
		}
		string text2 = material.shader?.name?.ToLowerInvariant() ?? string.Empty;
		if (text2.Contains("particle") || text2.Contains("vfx"))
		{
			num -= 10000;
		}
		return num;
	}

	private static Texture2D SelectBestBundleTexture(Texture2D[] textures)
	{
		if (textures == null || textures.Length == 0)
		{
			return null;
		}
		Texture2D result = null;
		int num = int.MinValue;
		foreach (Texture2D val in textures)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			string text = (((Object)val).name ?? string.Empty).ToLowerInvariant();
			int num2 = ((Texture)val).width * ((Texture)val).height;
			if (IsLikelyIconTexture(val))
			{
				num2 -= 20000;
			}
			if (text.Contains("ak") || text.Contains("gun") || text.Contains("weapon") || text.Contains("body") || text.Contains("albedo") || text.Contains("base"))
			{
				num2 += 12000;
			}
			if (text.Contains("icon") || text.Contains("ui") || text.Contains("thumb") || text.Contains("inventory") || text.Contains("vfx") || text.Contains("effect") || text.Contains("particle"))
			{
				num2 -= 15000;
			}
			if (num2 > num)
			{
				num = num2;
				result = val;
			}
		}
		return result;
	}

	private static Material CreateSyntheticFallbackMaterial(Texture2D mainTexture)
	{
		Material val = new Material(ResolveLocalVisualCompatibleShader());
		if (val.HasProperty("_BaseColor"))
		{
			val.SetColor("_BaseColor", Color.white);
		}
		if (val.HasProperty("_Color"))
		{
			val.SetColor("_Color", Color.white);
		}
		if ((Object)mainTexture != (Object)null)
		{
			if (val.HasProperty("_BaseMap"))
			{
				val.SetTexture("_BaseMap", mainTexture);
			}
			if (val.HasProperty("_MainTex"))
			{
				val.SetTexture("_MainTex", mainTexture);
			}
		}
		return val;
	}

	private static Material[] NormalizeBundleMaterialArray(Material[] source, int subMeshCount)
	{
		subMeshCount = Mathf.Max(subMeshCount, 1);
		Material[] array = (Material[])(object)new Material[subMeshCount];
		if (source == null || source.Length == 0)
		{
			for (int i = 0; i < subMeshCount; i++)
			{
				array[i] = CreateSyntheticFallbackMaterial(null);
			}
			return array;
		}
		for (int j = 0; j < subMeshCount; j++)
		{
			Material val = source[Mathf.Clamp(j, 0, source.Length - 1)];
			array[j] = (((Object)val != (Object)null) ? val : CreateSyntheticFallbackMaterial(null));
		}
		return array;
	}

	private bool IsChineseLanguage()
	{
		bool isChinese;
		string languageName;
		string source;
		return TryResolveGameLanguage(out isChinese, out languageName, out source) && isChinese;
	}

	private bool GetCachedChineseLanguageSetting(bool forceRefresh = false)
	{
		if (forceRefresh || Time.unscaledTime - _lastLanguagePollTime >= LanguagePollInterval)
		{
			_cachedIsChineseLanguage = IsChineseLanguage();
			_lastLanguagePollTime = Time.unscaledTime;
		}
		return _cachedIsChineseLanguage;
	}

	internal bool GetCachedChineseLanguageSettingRuntime(bool forceRefresh = false)
	{
		return GetCachedChineseLanguageSetting(forceRefresh);
	}

	private bool TryResolveGameLanguage(out bool isChinese, out string languageName, out string source)
	{
		if (TryGetConfiguredGameLanguage(out isChinese, out languageName))
		{
			source = "PlayerPrefs.LanguageSetting";
			return true;
		}
		if (TryGetLocalizedTextCurrentLanguage(out isChinese, out languageName))
		{
			source = "LocalizedText.CURRENT_LANGUAGE";
			return true;
		}
		isChinese = false;
		languageName = string.Empty;
		source = string.Empty;
		return false;
	}

	private static bool TryGetConfiguredGameLanguage(out bool isChinese, out string languageName)
	{
		isChinese = false;
		languageName = string.Empty;
		try
		{
			if (!PlayerPrefs.HasKey("LanguageSetting"))
			{
				return false;
			}
			if (TryGetPlayerPrefsInt("LanguageSetting", out var value))
			{
				languageName = GetConfiguredLanguageName(value);
				isChinese = value == 9;
				return true;
			}
			string text = PlayerPrefs.GetString("LanguageSetting", string.Empty);
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
			{
				languageName = GetConfiguredLanguageName(value);
				isChinese = value == 9;
				return true;
			}
			languageName = text;
			isChinese = IsChineseLanguageName(text);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsLikelyAkVisualPrefab(GameObject candidate)
	{
		if ((Object)candidate == (Object)null)
		{
			return false;
		}
		string text = (((Object)candidate).name ?? string.Empty).ToLowerInvariant();
		if (!(text == "vfx_ak") && !text.Contains("vfx_ak"))
		{
			if (text.Contains("vfx"))
			{
				return text.Contains("ak");
			}
			return false;
		}
		return true;
	}

	private static string DescribeRenderableCounts(GameObject root)
	{
		if ((Object)root == (Object)null)
		{
			return "no-renderers";
		}
		MeshRenderer[] componentsInChildren = root.GetComponentsInChildren<MeshRenderer>(true);
		SkinnedMeshRenderer[] componentsInChildren2 = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		MeshFilter[] componentsInChildren3 = root.GetComponentsInChildren<MeshFilter>(true);
		return $"{componentsInChildren.Length} MeshRenderers, {componentsInChildren2.Length} SkinnedMeshRenderers, {componentsInChildren3.Length} MeshFilters";
	}

	private static bool TryGetLocalizedTextCurrentLanguage(out bool isChinese, out string languageName)
	{
		isChinese = false;
		languageName = string.Empty;
		try
		{
			object obj = ((typeof(Item).Assembly.GetType("LocalizedText") ?? ResolveLoadedType("LocalizedText"))?.GetField("CURRENT_LANGUAGE", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))?.GetValue(null);
			if (obj == null)
			{
				return false;
			}
			languageName = obj.ToString() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(languageName))
			{
				languageName = Convert.ToInt32(obj, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
			}
			isChinese = IsChineseLanguageName(languageName);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetPlayerPrefsInt(string key, out int value)
	{
		value = int.MinValue;
		try
		{
			value = PlayerPrefs.GetInt(key, int.MinValue);
			return value != int.MinValue;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsChineseLanguageName(string languageName)
	{
		if (string.IsNullOrWhiteSpace(languageName))
		{
			return false;
		}
		if (languageName.IndexOf("SimplifiedChinese", StringComparison.OrdinalIgnoreCase) < 0 && languageName.IndexOf("TraditionalChinese", StringComparison.OrdinalIgnoreCase) < 0 && !languageName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && languageName.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return languageName.IndexOf("中文", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return true;
	}

	private static string GetConfiguredLanguageName(int languageValue)
	{
		return languageValue switch
		{
			0 => "English", 
			1 => "French", 
			2 => "Italian", 
			3 => "German", 
			4 => "SpanishSpain", 
			5 => "SpanishLatam", 
			6 => "BRPortuguese", 
			7 => "Russian", 
			8 => "Ukrainian", 
			9 => "SimplifiedChinese", 
			10 => "Japanese", 
			11 => "Korean", 
			12 => "Polish", 
			13 => "Turkish", 
			_ => languageValue.ToString(CultureInfo.InvariantCulture), 
		};
	}

	private ConfigEntry<float> BindRangedFloat(string section, string key, float defaultValue, float minValue, float maxValue)
	{
		return ((BaseUnityPlugin)this).Config.Bind<float>(section, key, defaultValue, new ConfigDescription(GetLocalizedDescription(key, isChinese: false), (AcceptableValueBase)(object)new AcceptableValueRange<float>(minValue, maxValue), Array.Empty<object>()));
	}

	private ConfigEntry<int> BindRangedInt(string section, string key, int defaultValue, int minValue, int maxValue)
	{
		return ((BaseUnityPlugin)this).Config.Bind<int>(section, key, defaultValue, new ConfigDescription(GetLocalizedDescription(key, isChinese: false), (AcceptableValueBase)(object)new AcceptableValueRange<int>(minValue, maxValue), Array.Empty<object>()));
	}

	private static string GetConfigBindingSectionName(string canonicalSection, bool isChinese)
	{
		string localizedSectionName = GetLocalizedSectionName(canonicalSection, isChinese);
		return string.IsNullOrWhiteSpace(localizedSectionName) ? canonicalSection : localizedSectionName;
	}

	private static string GetConfigBindingKeyName(string canonicalKey, bool isChinese)
	{
		if (!isChinese)
		{
			return canonicalKey;
		}
		string localizedKeyName = GetLocalizedKeyName(canonicalKey, isChinese);
		return string.IsNullOrWhiteSpace(localizedKeyName) ? canonicalKey : localizedKeyName;
	}

	private static void NormalizeConfigRanges()
	{
		if (ZombieBehaviorDifficulty != null)
		{
			ZombieBehaviorDifficulty.Value = NormalizeZombieBehaviorDifficultySelection(ZombieBehaviorDifficulty.Value);
		}
		FireInterval.Value = Mathf.Clamp(FireInterval.Value, 0.1f, 3f);
		FireVolume.Value = Mathf.Clamp(FireVolume.Value, 0f, 1.5f);
		WeaponModelPitch.Value = Mathf.Clamp(WeaponModelPitch.Value, -180f, 180f);
		WeaponModelYaw.Value = Mathf.Clamp(WeaponModelYaw.Value, -180f, 180f);
		WeaponModelRoll.Value = Mathf.Clamp(WeaponModelRoll.Value, -180f, 180f);
		WeaponModelScale.Value = Mathf.Clamp(WeaponModelScale.Value, 0.3f, 2f);
		WeaponModelOffsetX.Value = Mathf.Clamp(WeaponModelOffsetX.Value, -0.5f, 0.5f);
		WeaponModelOffsetY.Value = Mathf.Clamp(WeaponModelOffsetY.Value, -0.5f, 0.5f);
		WeaponModelOffsetZ.Value = Mathf.Clamp(WeaponModelOffsetZ.Value, -0.5f, 0.5f);
		ZombieTimeReduction.Value = Mathf.Clamp(ZombieTimeReduction.Value, 0f, 60f);
		MaxZombies.Value = Mathf.Clamp(MaxZombies.Value, 0, 30);
		ZombieSpawnCount.Value = Mathf.Clamp(ZombieSpawnCount.Value, 0, 30);
		ZombieSpawnInterval.Value = Mathf.Clamp(ZombieSpawnInterval.Value, 1f, 120f);
		ZombieMaxLifetime.Value = Mathf.Clamp(ZombieMaxLifetime.Value, 10f, 600f);
	}

	private void InitConfig()
	{
		// é…ç½®é”®åä¿æŒç¨³å®šå¹¶é›†ä¸­åœ¨è¿™é‡Œå®šä¹‰ï¼Œä¾¿äºŽè¿ç§»æ—§é…ç½®æ–‡ä»¶ã€‚
		// Keep config keys centralized here so legacy config migration stays predictable.
		bool isChinese = IsChineseLanguage();
		string configBindingSectionName = GetConfigBindingSectionName(FeaturesConfigSectionName, isChinese);
		string configBindingSectionName2 = GetConfigBindingSectionName(WeaponConfigSectionName, isChinese);
		string configBindingSectionName3 = GetConfigBindingSectionName(ZombieConfigSectionName, isChinese);
		ModEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>(configBindingSectionName, GetConfigBindingKeyName("Mod", isChinese), true, GetLocalizedDescription("Mod", isChinese: false));
		OpenConfigPanelKey = ((BaseUnityPlugin)this).Config.Bind<KeyCode>(configBindingSectionName, GetConfigBindingKeyName("Open Config Panel", isChinese), KeyCode.Backslash, GetLocalizedDescription("Open Config Panel", isChinese: false));
		ConfigPanelTheme = ((BaseUnityPlugin)this).Config.Bind<string>(configBindingSectionName, GetConfigBindingKeyName("Config Panel Theme", isChinese), DefaultConfigPanelThemeOption, GetLocalizedDescription("Config Panel Theme", isChinese: false));
		string text0 = LobbyConfigPanel.NormalizeThemeSelectionValue(ConfigPanelTheme.Value);
		if (!string.Equals(ConfigPanelTheme.Value, text0, StringComparison.Ordinal))
		{
			ConfigPanelTheme.Value = text0;
			SavePluginConfigQuietly();
		}
		WeaponEnabled = ((BaseUnityPlugin)this).Config.Bind<bool>(configBindingSectionName2, GetConfigBindingKeyName("Weapon", isChinese), true, GetLocalizedDescription("Weapon", isChinese: false));
		WeaponSelection = ((BaseUnityPlugin)this).Config.Bind<string>(configBindingSectionName2, GetConfigBindingKeyName("Weapon Selection", isChinese), DefaultWeaponSelection, GetLocalizedDescription("Weapon Selection", isChinese: false));
		string text = NormalizeWeaponSelection(WeaponSelection.Value);
		if (!string.Equals(WeaponSelection.Value, text, StringComparison.Ordinal))
		{
			WeaponSelection.Value = text;
			SavePluginConfigQuietly();
		}
		SpawnWeaponKey = ((BaseUnityPlugin)this).Config.Bind<KeyCode>(configBindingSectionName2, GetConfigBindingKeyName("Spawn Weapon", isChinese), (KeyCode)116, GetLocalizedDescription("Spawn Weapon", isChinese: false));
		FireInterval = BindRangedFloat(configBindingSectionName2, GetConfigBindingKeyName("Fire Interval", isChinese), 0.4f, 0.1f, 3f);
		FireVolume = BindRangedFloat(configBindingSectionName2, GetConfigBindingKeyName("Fire Volume", isChinese), 0.8f, 0f, 1.5f);
		AkSoundSelection = ((BaseUnityPlugin)this).Config.Bind<string>(configBindingSectionName2, GetConfigBindingKeyName("AK Sound", isChinese), DefaultAkSoundOption, GetLocalizedDescription("AK Sound", isChinese: false));
		string text2 = NormalizeAkSoundSelection(AkSoundSelection.Value);
		if (!string.Equals(AkSoundSelection.Value, text2, StringComparison.Ordinal))
		{
			AkSoundSelection.Value = text2;
			SavePluginConfigQuietly();
		}
		AkSoundSelection.SettingChanged += delegate
		{
			OnAkSoundSelectionChanged();
		};
		ZombieTimeReduction = BindRangedFloat(configBindingSectionName2, GetConfigBindingKeyName("Zombie Time Reduction", isChinese), DefaultZombieTimeReductionSeconds, 0f, 60f);
		WeaponModelScale = BindRangedFloat(configBindingSectionName2, GetConfigBindingKeyName("Weapon Model Scale", isChinese), _localWeaponModelScale.x, 0.3f, 2f);
		WeaponModelPitch = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model X Rotation", isChinese), _localWeaponModelEuler.x, -180f, 180f);
		WeaponModelYaw = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model Y Rotation", isChinese), _localWeaponModelEuler.y, -180f, 180f);
		WeaponModelRoll = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model Z Rotation", isChinese), _localWeaponModelEuler.z, -180f, 180f);
		WeaponModelOffsetX = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model X Position", isChinese), _localWeaponModelOffset.x, -0.5f, 0.5f);
		WeaponModelOffsetY = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model Y Position", isChinese), _localWeaponModelOffset.y, -0.5f, 0.5f);
		WeaponModelOffsetZ = BindRangedFloat(configBindingSectionName, GetConfigBindingKeyName("Weapon Model Z Position", isChinese), _localWeaponModelOffset.z, -0.5f, 0.5f);
		if (Mathf.Abs(WeaponModelYaw.Value - 10f) < 0.001f || Mathf.Abs(WeaponModelYaw.Value - 11.5f) < 0.001f || Mathf.Abs(WeaponModelYaw.Value - -4f) < 0.001f)
		{
			WeaponModelYaw.Value = _localWeaponModelEuler.y;
			SavePluginConfigQuietly();
		}
		if (Mathf.Abs(WeaponModelScale.Value - 2.8f) < 0.001f || Mathf.Abs(WeaponModelScale.Value - 2.55f) < 0.001f || Mathf.Abs(WeaponModelScale.Value - 0.8f) < 0.001f)
		{
			WeaponModelScale.Value = _localWeaponModelScale.x;
			SavePluginConfigQuietly();
		}
		if (Mathf.Abs(WeaponModelOffsetX.Value - -0.01f) < 0.001f && Mathf.Abs(WeaponModelOffsetY.Value - 0.01f) < 0.001f && Mathf.Abs(WeaponModelOffsetZ.Value) < 0.001f)
		{
			WeaponModelOffsetX.Value = _localWeaponModelOffset.x;
			WeaponModelOffsetY.Value = _localWeaponModelOffset.y;
			WeaponModelOffsetZ.Value = _localWeaponModelOffset.z;
			SavePluginConfigQuietly();
		}
		MaxZombies = BindRangedInt(configBindingSectionName3, GetConfigBindingKeyName("Max Count", isChinese), DefaultMaxZombieCount, 0, 30);
		ZombieSpawnCount = BindRangedInt(configBindingSectionName3, GetConfigBindingKeyName("Spawn Count", isChinese), DefaultZombieSpawnCount, 0, 30);
		ZombieSpawnInterval = BindRangedFloat(configBindingSectionName3, GetConfigBindingKeyName("Spawn Interval", isChinese), 15f, 1f, 120f);
		ZombieMaxLifetime = BindRangedFloat(configBindingSectionName3, GetConfigBindingKeyName("Max Lifetime", isChinese), 120f, 10f, 600f);
		ZombieBehaviorDifficulty = ((BaseUnityPlugin)this).Config.Bind<string>(configBindingSectionName3, GetConfigBindingKeyName("Behavior Difficulty", isChinese), DefaultZombieBehaviorDifficulty, GetLocalizedDescription("Behavior Difficulty", isChinese: false));
		string text3 = NormalizeZombieBehaviorDifficultySelection(string.IsNullOrWhiteSpace(_loadedZombieBehaviorDifficultyMetadata) ? ZombieBehaviorDifficulty.Value : _loadedZombieBehaviorDifficultyMetadata);
		if (!string.Equals(ZombieBehaviorDifficulty.Value, text3, StringComparison.Ordinal))
		{
			ZombieBehaviorDifficulty.Value = text3;
		}
		ApplyZombieBehaviorDifficultyPreset(text3);
		ApplySimplifiedZombieDerivedValues();
		_lastZombieBehaviorDifficulty = text3;
		NormalizeConfigRanges();
		RemoveLegacyInventorySlotConfig();
		RemoveLegacyFogModeConfig();
		RemoveLegacyPlayerShotConfig();
		RemoveLegacyRecoilConfig();
		RefreshOwnedConfigEntryCache();
		SubscribeOwnedConfigEntryChanges();
		SavePluginConfigQuietly();
	}

	private void OnDestroy()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
		ReleaseRoomConfigCallbacks();
		UnsubscribeOwnedConfigEntryChanges();
		StopGameplayLoadoutBootstrap();
		StopOwnedModConfigStabilizer();
		CleanupLocalHeldDebugSphere();
		CleanupMuzzleFlashPool();
		CleanupRemoteGunshotAudioPool();
		CleanupDartImpactVfxPool();
		_lobbyConfigPanel?.Dispose();
		_lobbyConfigPanel = null;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		string sceneBucket = GetSceneBucket(scene);
		if (string.Equals(sceneBucket, "other", StringComparison.Ordinal))
		{
			return;
		}
		if (string.Equals(sceneBucket, _activeSceneBucket, StringComparison.Ordinal))
		{
			return;
		}
		_activeSceneBucket = sceneBucket;
		BeginSceneRuntimeWarmup(scene);
		if (_scanCoroutine != null)
		{
			((MonoBehaviour)this).StopCoroutine(_scanCoroutine);
			_scanCoroutine = null;
		}
		StopGameplayLoadoutBootstrap();
		ResetLoadoutGrantTracking(clearPersistentRecords: true);
		CleanupWeaponLobbyNotice();
		CleanupLocalWeaponVisual();
		CleanupLocalHeldDebugSphere();
		RequestAkVisualRefresh(includeUiRefresh: true);
		_lastLocalizedModConfigUiFrame = -1;
		_localCharacter = null;
		_hasWeapon = false;
		_cachedHeldBlowgunItem = null;
		_lastHeldBlowgunSearchTime = -10f;
		_pendingRemoteWeaponGrant = false;
		_pendingRemoteFirstAidGrant = false;
		_lastPendingRemoteGrantAttemptTime = -10f;
		ZombieSpawner.StopZombieSpawning();
		ZombieSpawner.ClearZombies();
		bool flag2 = scene.name != null && scene.name.Contains("Airport");
		if (scene.name != null && !scene.name.Contains("Pretitle") && !scene.name.Contains("Title") && !flag2)
		{
			_resourcesLoaded = false;
			_scanCoroutine = ((MonoBehaviour)this).StartCoroutine(PeriodicPlayerScan());
			if (IsZombieSpawnFeatureEnabled())
			{
				ZombieSpawner.StartZombieSpawning();
			}
		}
		if (IsModConfigUiRuntimeSafeScene(scene))
		{
			((MonoBehaviour)this).StartCoroutine(RefreshLocalizedUiAfterStartup());
		}
	}

	private static void ResetLoadoutGrantTracking(bool clearPersistentRecords)
	{
		_receivedItem.Clear();
		_receivedFirstAid.Clear();
		if (clearPersistentRecords)
		{
			_persistentReceivedItem.Clear();
			_persistentReceivedFirstAid.Clear();
		}
		_pendingRemoteWeaponGrantActors.Clear();
		_pendingRemoteFirstAidGrantActors.Clear();
		_lastWeaponGrantTimeByActor.Clear();
		_lastFirstAidGrantTimeByActor.Clear();
		_weaponMissingSinceByActor.Clear();
		_firstAidMissingSinceByActor.Clear();
		_recentWeaponDropTimeByActor.Clear();
	}

	private void StopGameplayLoadoutBootstrap()
	{
		if (_gameplayLoadoutBootstrapCoroutine != null)
		{
			((MonoBehaviour)this).StopCoroutine(_gameplayLoadoutBootstrapCoroutine);
			_gameplayLoadoutBootstrapCoroutine = null;
		}
	}

	private void RestartGameplayLoadoutBootstrapIfNeeded()
	{
		StopGameplayLoadoutBootstrap();
		if (!IsGameplayScene(SceneManager.GetActiveScene()) || !IsWeaponFeatureEnabled())
		{
			return;
		}
		_gameplayLoadoutBootstrapCoroutine = ((MonoBehaviour)this).StartCoroutine(GameplayLoadoutBootstrapCoroutine());
	}

	private IEnumerator GameplayLoadoutBootstrapCoroutine()
	{
		float deadline = Time.unscaledTime + GameplayLoadoutBootstrapDuration;
		while (Time.unscaledTime < deadline && IsGameplayScene(SceneManager.GetActiveScene()))
		{
			ProcessPendingRemoteGrantRequests(force: true);
			Character localCharacter = Character.localCharacter ?? _localCharacter;
			if ((Object)localCharacter != (Object)null && !localCharacter.isBot && !localCharacter.isZombie && IsAlive(localCharacter))
			{
				int characterGrantTrackingId = GetCharacterGrantTrackingId(localCharacter);
				if (characterGrantTrackingId != int.MinValue)
				{
					bool flag = CharacterAlreadyHasShootZombiesWeapon(localCharacter);
					if (flag)
					{
						MarkWeaponGrantedForCharacter(localCharacter);
						_gameplayLoadoutBootstrapCoroutine = null;
						yield break;
					}
					if (!HasWeaponGrantRecord(characterGrantTrackingId) && !IsPendingRemoteWeaponGrant(characterGrantTrackingId) && !HasRecentWeaponDrop(characterGrantTrackingId))
					{
						TryGiveItemTo(localCharacter);
					}
					if (CharacterAlreadyHasShootZombiesWeapon(localCharacter))
					{
						MarkWeaponGrantedForCharacter(localCharacter);
						_gameplayLoadoutBootstrapCoroutine = null;
						yield break;
					}
				}
			}
			yield return (object)new WaitForSecondsRealtime(GameplayLoadoutBootstrapRetryInterval);
		}
		_gameplayLoadoutBootstrapCoroutine = null;
	}

	private void LoadResources()
	{
		if (_resourcesLoaded)
		{
			return;
		}
		try
		{
			ReplaceBlowgunSound();
			_resourcesLoaded = true;
		}
		catch (Exception)
		{
		}
	}

	private void CreateIconFromModel()
	{
		if ((Object)_ak47Prefab == (Object)null)
		{
			return;
		}
		try
		{
			GameObject val = new GameObject("AK47IconCamera");
			Camera obj = val.AddComponent<Camera>();
			obj.orthographic = false;
			obj.fieldOfView = 18f;
			obj.clearFlags = CameraClearFlags.SolidColor;
			obj.backgroundColor = new Color(0f, 0f, 0f, 0f);
			obj.cullingMask = -1;
			obj.nearClipPlane = 0.01f;
			obj.farClipPlane = 100f;
			obj.allowHDR = false;
			obj.allowMSAA = true;
			obj.useOcclusionCulling = false;
			GameObject val2 = new GameObject("IconLight");
			val2.transform.SetParent(val.transform);
			val2.transform.localPosition = new Vector3(2.2f, 2.8f, -2.8f);
			Light obj2 = val2.AddComponent<Light>();
			obj2.type = (LightType)1;
			obj2.intensity = 2.1f;
			obj2.color = Color.white;
			GameObject val3 = new GameObject("FillLight");
			val3.transform.SetParent(val.transform);
			val3.transform.localPosition = new Vector3(-2.1f, 1.4f, -2.2f);
			Light obj3 = val3.AddComponent<Light>();
			obj3.type = (LightType)1;
			obj3.intensity = 0.95f;
			obj3.color = Color.white;
			int num = 256;
			RenderTexture val4 = new RenderTexture(num, num, 24, RenderTextureFormat.ARGB32);
			val4.antiAliasing = 2;
			val4.useMipMap = false;
			val4.autoGenerateMips = false;
			val4.filterMode = FilterMode.Bilinear;
			val4.Create();
			obj.targetTexture = val4;
			GameObject val5 = Object.Instantiate<GameObject>(_ak47Prefab);
			val5.transform.position = Vector3.zero;
			val5.transform.rotation = Quaternion.Euler(-5f, -90f, 0f);
			val5.transform.localScale = Vector3.one * 0.54f;
			ItemPatch.PrepareAkVisualRenderers(val5, null);
			if (TryGetAkIconBounds(val5, out var bounds))
			{
				Vector3 center = bounds.center + new Vector3(0.04f, -0.02f, 0f);
				float num2 = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
				float num3 = Mathf.Max(1.8f, num2 / Mathf.Tan(obj.fieldOfView * 0.5f * (Mathf.PI / 180f)) * 2.15f);
				val.transform.position = center - Vector3.forward * num3;
				val.transform.rotation = Quaternion.identity;
			}
			else
			{
				val.transform.position = new Vector3(0.03f, -0.02f, -2.4f);
				val.transform.rotation = Quaternion.identity;
			}
			RenderTexture.active = val4;
			GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
			RenderTexture.active = null;
			obj.Render();
			_ak47IconTexture = new Texture2D(num, num, TextureFormat.RGBA32, false);
			((Object)_ak47IconTexture).name = "AK47_GeneratedIcon";
			_ak47IconTexture.wrapMode = TextureWrapMode.Clamp;
			_ak47IconTexture.filterMode = FilterMode.Bilinear;
			RenderTexture.active = val4;
			_ak47IconTexture.ReadPixels(new Rect(0f, 0f, (float)num, (float)num), 0, 0);
			ForceTransparentBackground(_ak47IconTexture);
			_ak47IconTexture.Apply();
			RenderTexture.active = null;
			obj.targetTexture = null;
			val4.Release();
			Object.DestroyImmediate((Object)val4);
			Object.DestroyImmediate((Object)val5);
			Object.DestroyImmediate((Object)val);
		}
		catch (Exception)
		{
		}
	}

	public static Texture2D GetAkIconTexture()
	{
		if ((Object)_ak47IconTexture != (Object)null && !IsLikelyIconTexture(_ak47IconTexture))
		{
			_ak47IconTexture = null;
		}
		if ((Object)_ak47IconTexture == (Object)null && _ak47ItemContent != null)
		{
			_ak47IconTexture = TryExtractIconFromItemContent(_ak47ItemContent);
		}
		if ((Object)_ak47IconTexture != (Object)null && !_useExplicitSelectedWeaponIcon && !IsGeneratedModelIcon(_ak47IconTexture) && IsIconLikelyTooDark(_ak47IconTexture))
		{
			LogDiagnosticOnce("ak-icon-too-dark:" + DescribeTextureForDiagnostics(_ak47IconTexture), "AK icon rejected as too dark: " + DescribeTextureForDiagnostics(_ak47IconTexture));
			_ak47IconTexture = null;
		}
		if ((Object)_ak47IconTexture == (Object)null)
		{
			Instance?.CreateIconFromModel();
			if ((Object)_ak47IconTexture != (Object)null)
			{
				LogDiagnosticOnce("ak-icon-generated:" + DescribeTextureForDiagnostics(_ak47IconTexture), "CreateIconFromModel result: " + DescribeTextureForDiagnostics(_ak47IconTexture));
			}
		}
		if ((Object)_ak47IconTexture != (Object)null && !_useExplicitSelectedWeaponIcon && !IsGeneratedModelIcon(_ak47IconTexture) && IsIconLikelyTooDark(_ak47IconTexture))
		{
			LogDiagnosticOnce("ak-icon-post-generate-too-dark:" + DescribeTextureForDiagnostics(_ak47IconTexture), "AK icon rejected after generation check: " + DescribeTextureForDiagnostics(_ak47IconTexture));
			_ak47IconTexture = null;
		}
		if ((Object)_ak47IconTexture != (Object)null && !IsLikelyIconTexture(_ak47IconTexture))
		{
			LogDiagnosticOnce("ak-icon-invalid:" + DescribeTextureForDiagnostics(_ak47IconTexture), "AK icon rejected as invalid UI texture: " + DescribeTextureForDiagnostics(_ak47IconTexture));
			_ak47IconTexture = null;
		}
		if ((Object)_ak47IconTexture == (Object)null)
		{
			LogDiagnosticOnce("ak-icon-fallback", "AK icon fell back to built-in placeholder icon");
			_ak47IconTexture = CreateFallbackIconTexture();
		}
		return _ak47IconTexture;
	}

	public static Texture2D GetAkIconTexture(string selection)
	{
		string text = NormalizeWeaponSelection(selection);
		if (TryGetResolvedWeaponIcon(text, out var icon) && (Object)icon != (Object)null)
		{
			return icon;
		}
		if (string.Equals(text, GetCurrentWeaponSelection(), StringComparison.OrdinalIgnoreCase))
		{
			return GetAkIconTexture();
		}
		if ((Object)_genericResolvedWeaponIcon != (Object)null)
		{
			return _genericResolvedWeaponIcon;
		}
		return GetAkIconTexture();
	}

	internal static void LogDiagnosticOnce(string key, string message)
	{
		try
		{
			if (!EnableDiagnosticLogs)
			{
				return;
			}
			if (string.IsNullOrWhiteSpace(key))
			{
				key = "diagnostic:" + message;
			}
			if (!_diagnosticLogKeys.Add(key))
			{
				return;
			}
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)("[ShootZombies][Diag] " + message));
			}
		}
		catch
		{
		}
	}

	internal static void LogDiagnostic(string message)
	{
		try
		{
			if (!EnableDiagnosticLogs)
			{
				return;
			}
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogInfo((object)("[ShootZombies][Diag] " + message));
			}
		}
		catch
		{
		}
	}

	private static bool TryGetAkIconBounds(GameObject root, out Bounds bounds)
	{
		bounds = default(Bounds);
		if ((Object)root == (Object)null)
		{
			return false;
		}
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(true);
		bool flag = false;
		Renderer[] array = componentsInChildren;
		foreach (Renderer val in array)
		{
			if (!((Object)val == (Object)null) && val.enabled && !val.forceRenderingOff)
			{
				if (!flag)
				{
					bounds = val.bounds;
					flag = true;
				}
				else
				{
					bounds.Encapsulate(val.bounds);
				}
			}
		}
		return flag;
	}

	private static void ForceTransparentBackground(Texture2D texture)
	{
		if ((Object)texture == (Object)null)
		{
			return;
		}
		try
		{
			Color32[] pixels = texture.GetPixels32();
			if (pixels == null || pixels.Length == 0)
			{
				return;
			}
			for (int i = 0; i < pixels.Length; i++)
			{
				Color32 val = pixels[i];
				if ((int)val.a <= 10)
				{
					pixels[i] = new Color32(0, 0, 0, 0);
				}
			}
			texture.SetPixels32(pixels);
		}
		catch
		{
		}
	}

	internal static string DescribeAkVisualPrefabForDiagnostics()
	{
		return DescribeGameObjectForDiagnostics(_ak47Prefab);
	}

	internal static string DescribeAkVisualPrefabForDiagnostics(string selection)
	{
		return DescribeGameObjectForDiagnostics(GetAkVisualPrefab(selection));
	}

	private static bool IsGeneratedModelIcon(Texture2D texture)
	{
		if ((Object)texture == (Object)null)
		{
			return false;
		}
		string text = ((Object)texture).name ?? string.Empty;
		if (!text.Equals("AK47_GeneratedIcon", StringComparison.Ordinal))
		{
			return text.Equals("AK47_FallbackIcon", StringComparison.Ordinal);
		}
		return true;
	}

	public static GameObject GetAkVisualPrefab()
	{
		return _ak47Prefab;
	}

	public static GameObject GetAkVisualPrefab(string selection)
	{
		if (TryGetResolvedWeaponPrefab(selection, out var prefab) && HasRenderableGeometry(prefab))
		{
			return prefab;
		}
		if (HasRenderableGeometry(_genericResolvedWeaponPrefab))
		{
			return _genericResolvedWeaponPrefab;
		}
		return _ak47Prefab;
	}

	public static bool HasAkVisualPrefab()
	{
		return HasRenderableGeometry(_ak47Prefab);
	}

	public static bool HasAkVisualPrefab(string selection)
	{
		return HasRenderableGeometry(GetAkVisualPrefab(selection));
	}

	internal static bool IsLocalWeaponVisualFollowerEnabled()
	{
		return EnableLocalWeaponVisualFollower;
	}

	internal static float GetWeaponModelYaw()
	{
		return Mathf.Clamp(WeaponModelYaw?.Value ?? _localWeaponModelEuler.y, -180f, 180f);
	}

	internal static float GetWeaponModelPitch()
	{
		return Mathf.Clamp(WeaponModelPitch?.Value ?? _localWeaponModelEuler.x, -180f, 180f);
	}

	internal static float GetWeaponModelRoll()
	{
		return Mathf.Clamp(WeaponModelRoll?.Value ?? _localWeaponModelEuler.z, -180f, 180f);
	}

	internal static float GetWeaponModelScale()
	{
		return Mathf.Clamp(WeaponModelScale?.Value ?? _localWeaponModelScale.x, 0.3f, 2f);
	}

	internal static float GetWeaponVariantScaleMultiplier(string selection)
	{
		return Mathf.Clamp(GetWeaponVariantDefinition(selection)?.ScaleMultiplier ?? 1f, 0.3f, 3f);
	}

	internal static Vector3 GetWeaponVariantPositionOffset(string selection)
	{
		WeaponVariantDefinition weaponVariantDefinition = GetWeaponVariantDefinition(selection);
		return (weaponVariantDefinition != null) ? weaponVariantDefinition.PositionOffset : Vector3.zero;
	}

	internal static float GetEffectiveWeaponModelScale(string selection)
	{
		return Mathf.Clamp(GetWeaponModelScale() * GetWeaponVariantScaleMultiplier(selection), 0.3f, 6f);
	}

	internal static float GetWeaponModelOffsetX()
	{
		return Mathf.Clamp(WeaponModelOffsetX?.Value ?? _localWeaponModelOffset.x, -0.5f, 0.5f);
	}

	internal static float GetWeaponModelOffsetY()
	{
		return Mathf.Clamp(WeaponModelOffsetY?.Value ?? _localWeaponModelOffset.y, -0.5f, 0.5f);
	}

	internal static float GetWeaponModelOffsetZ()
	{
		return Mathf.Clamp(WeaponModelOffsetZ?.Value ?? _localWeaponModelOffset.z, -0.5f, 0.5f);
	}

	internal static Quaternion GetDirectAkRotationOverride()
	{
		return Quaternion.Euler(_weaponBaseEuler.x + GetWeaponModelPitch(), _weaponBaseEuler.y + GetWeaponModelYaw(), _weaponBaseEuler.z + GetWeaponModelRoll());
	}

	internal static Vector3 GetDirectAkPositionOverride(string selection)
	{
		return new Vector3(GetWeaponModelOffsetX(), GetWeaponModelOffsetY(), GetWeaponModelOffsetZ()) + GetWeaponVariantPositionOffset(selection);
	}

	internal static Vector3 GetDirectAkPositionOverride()
	{
		return GetDirectAkPositionOverride(GetCurrentWeaponSelection());
	}

	private static Vector3 GetConfiguredLocalWeaponModelEuler()
	{
		return new Vector3(_weaponBaseEuler.x + GetWeaponModelPitch(), _weaponBaseEuler.y + GetWeaponModelYaw(), _weaponBaseEuler.z + GetWeaponModelRoll());
	}

	private static Vector3 GetConfiguredLocalWeaponModelOffset()
	{
		return GetDirectAkPositionOverride();
	}

	private static bool HasRenderableGeometry(GameObject prefab)
	{
		if ((Object)prefab == (Object)null)
		{
			return false;
		}
		MeshFilter[] componentsInChildren = prefab.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val != (Object)null && (Object)val.sharedMesh != (Object)null)
			{
				return true;
			}
		}
		SkinnedMeshRenderer[] componentsInChildren2 = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
		{
			if ((Object)val2 != (Object)null && (Object)val2.sharedMesh != (Object)null)
			{
				return true;
			}
		}
		return false;
	}

	private static string FormatTransformPath(Transform transform)
	{
		if ((Object)transform == (Object)null)
		{
			return "null";
		}
		List<string> list = new List<string>();
		Transform val = transform;
		while ((Object)val != (Object)null)
		{
			list.Add(((Object)val).name);
			val = val.parent;
		}
		list.Reverse();
		return string.Join("/", list);
	}

	private static string DescribeGameObjectForDiagnostics(GameObject root, int maxEntries = 6)
	{
		if ((Object)root == (Object)null)
		{
			return "prefab=null";
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("name=").Append(((Object)root).name);
		stringBuilder.Append(", counts=").Append(DescribeRenderableCounts(root));
		List<string> list = new List<string>();
		MeshFilter[] componentsInChildren = root.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val == (Object)null || (Object)val.sharedMesh == (Object)null)
			{
				continue;
			}
			MeshRenderer component = ((Component)val).GetComponent<MeshRenderer>();
			list.Add($"{FormatTransformPath(((Component)val).transform)}|mesh={((Object)val.sharedMesh).name}|verts={val.sharedMesh.vertexCount}|renderer={((Object)component != (Object)null ? ((Object)component).name : "none")}");
			if (list.Count >= maxEntries)
			{
				break;
			}
		}
		if (list.Count == 0)
		{
			SkinnedMeshRenderer[] componentsInChildren2 = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
			{
				if ((Object)val2 == (Object)null || (Object)val2.sharedMesh == (Object)null)
				{
					continue;
				}
				list.Add($"{FormatTransformPath(((Component)val2).transform)}|skinnedMesh={((Object)val2.sharedMesh).name}|verts={val2.sharedMesh.vertexCount}");
				if (list.Count >= maxEntries)
				{
					break;
				}
			}
		}
		if (list.Count != 0)
		{
			stringBuilder.Append(", candidates=").Append(string.Join(" || ", list));
		}
		return stringBuilder.ToString();
	}

	private static string DescribeTextureForDiagnostics(Texture2D texture)
	{
		if ((Object)texture == (Object)null)
		{
			return "null";
		}
		return $"name={((Object)texture).name}, size={((Texture)texture).width}x{((Texture)texture).height}, format={texture.format}, instance={((Object)texture).GetInstanceID()}";
	}

	private static bool IsIconLikelyTooDark(Texture2D texture)
	{
		if ((Object)texture == (Object)null)
		{
			return false;
		}
		try
		{
			Color32[] pixels = texture.GetPixels32();
			if (pixels == null || pixels.Length == 0)
			{
				return false;
			}
			float num = 0f;
			int num2 = 0;
			int num3 = Mathf.Max(1, pixels.Length / 1024);
			for (int i = 0; i < pixels.Length; i += num3)
			{
				Color32 val = pixels[i];
				if (!((float)(int)val.a / 255f < 0.08f))
				{
					float num4 = (0.2126f * (float)(int)val.r + 0.7152f * (float)(int)val.g + 0.0722f * (float)(int)val.b) / 255f;
					num += num4;
					num2++;
				}
			}
			if (num2 < 8)
			{
				return false;
			}
			return num / (float)num2 < 0.08f;
		}
		catch
		{
			return false;
		}
	}

	private static Texture2D CreateFallbackIconTexture()
	{
		try
		{
			Texture2D val = new Texture2D(128, 128, (TextureFormat)5, false);
			((Object)val).name = "AK47_FallbackIcon";
			Color val2 = new Color(0f, 0f, 0f, 0f);
			Color val3 = new Color(0.94f, 0.94f, 0.94f, 1f);
			Color val4 = new Color(0.62f, 0.62f, 0.62f, 1f);
			Color[] array = (Color[])(object)new Color[16384];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = val2;
			}
			val.SetPixels(array);
			for (int j = 18; j < 104; j++)
			{
				for (int k = 58; k < 68; k++)
				{
					val.SetPixel(j, k, val3);
				}
			}
			for (int l = 76; l < 112; l++)
			{
				for (int m = 68; m < 80; m++)
				{
					val.SetPixel(l, m, val4);
				}
			}
			for (int n = 22; n < 40; n++)
			{
				for (int num = 44; num < 58; num++)
				{
					val.SetPixel(n, num, val3);
				}
			}
			for (int num2 = 52; num2 < 72; num2++)
			{
				for (int num3 = 68; num3 < 92; num3++)
				{
					if (num3 - 68 >= (num2 - 52) / 2)
					{
						val.SetPixel(num2, num3, val3);
					}
				}
			}
			for (int num4 = 92; num4 < 112; num4++)
			{
				for (int num5 = 54; num5 < 62; num5++)
				{
					val.SetPixel(num4, num5, val4);
				}
			}
			val.Apply();
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogWarning((object)"[ShootZombies] Using fallback icon texture");
			}
			return val;
		}
		catch
		{
			return null;
		}
	}

	private void ReplaceBlowgunModel()
	{
		if (IsRuntimeVisualRefreshBlocked())
		{
			return;
		}
		if ((Object)_ak47Prefab == (Object)null)
		{
			return;
		}
		try
		{
			if (TryResolveBaseWeaponItem(out var item))
			{
				ItemPatch.ApplyAkDisplay(item);
				ItemPatch.EnsureAkVisual(item, forceRefreshMarker: true);
				ItemPatch.EnsureAkVisualOnAllItems(forceRefresh: true);
			}
		}
		catch (Exception)
		{
		}
	}

	private void RefreshWeaponModelVisuals()
	{
		try
		{
			ApplyLocalWeaponModelPose();
			ReplaceBlowgunModel();
		}
		catch (Exception ex)
		{
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogWarning((object)("[ShootZombies] RefreshWeaponModelVisuals failed: " + ex.Message));
			}
		}
	}

	private void ReplaceBlowgunLocalization()
	{
		try
		{
			Type type = typeof(Item).Assembly.GetType("LocalizedText");
			if (type != null)
			{
				FieldInfo field = type.GetField("mainTable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (field != null)
				{
					object value = field.GetValue(null);
					if (value != null)
					{
						PropertyInfo property = value.GetType().GetProperty("Item");
						if (property != null)
						{
							List<string> list = new List<string>(15);
							for (int i = 0; i < 15; i++)
							{
								list.Add("AK-47");
							}
							property.SetValue(value, list, new object[1] { "NAME_AK" });
						}
					}
				}
			}
			if (!TryResolveBaseWeaponItem(out var item))
			{
				return;
			}
			ItemPatch.ApplyAkDisplay(item);
			FieldInfo field2 = typeof(Item).GetField("localizationKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field2 != null)
			{
				field2.SetValue(item, "NAME_AK");
			}
			FieldInfo field3 = typeof(Item).GetField("titleKey", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field3 != null)
			{
				field3.SetValue(item, "NAME_AK");
			}
			if (_ak47ItemContent == null || !((Object)_ak47Prefab != (Object)null))
			{
				return;
			}
			GameObject ak47Prefab = _ak47Prefab;
			FieldInfo field4 = typeof(Item).GetField("icon", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field4 != null)
			{
				object value2 = field4.GetValue(ak47Prefab);
				if (value2 != null)
				{
					field4.SetValue(item, value2);
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private void ReplaceBlowgunInstances()
	{
		if ((Object)_ak47Prefab == (Object)null)
		{
			Log.LogWarning((object)"[ShootZombies] AK47 prefab is null, cannot replace blowgun");
			return;
		}
		try
		{
			if (TryResolveBaseWeaponItem(out var item))
			{
				ItemPatch.ApplyAkDisplay(item);
			}
			ItemPatch.EnsureAkVisualOnAllItems();
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[ShootZombies] ReplaceBlowgunInstances error: " + ex));
		}
	}

	private void ReplaceBlowgunInstances_Old()
	{
		if ((Object)_ak47Prefab == (Object)null)
		{
			return;
		}
		try
		{
			Item[] array = Object.FindObjectsByType<Item>((FindObjectsSortMode)0);
			int num = 0;
			Item[] array2 = array;
			foreach (Item val in array2)
			{
				if (_replacedBlowguns.Contains(((Component)val).gameObject))
				{
					continue;
				}
				string name = val.GetName();
				if (!ItemPatch.IsBlowgunLike(val, name))
				{
					continue;
				}
				MeshRenderer[] componentsInChildren = ((Component)val).GetComponentsInChildren<MeshRenderer>();
				MeshRenderer[] array3 = componentsInChildren;
				foreach (MeshRenderer val2 in array3)
				{
					if (!((Object)val2).name.Contains("Hand"))
					{
						((Renderer)val2).enabled = false;
					}
				}
				SkinnedMeshRenderer[] componentsInChildren2 = ((Component)val).GetComponentsInChildren<SkinnedMeshRenderer>();
				SkinnedMeshRenderer[] array4 = componentsInChildren2;
				foreach (SkinnedMeshRenderer val3 in array4)
				{
					if (!((Object)val3).name.Contains("Hand"))
					{
						((Renderer)val3).enabled = false;
					}
				}
				GameObject val4 = Object.Instantiate<GameObject>(_ak47Prefab, ((Component)val).transform);
				val4.transform.localPosition = Vector3.zero;
				val4.transform.localRotation = Quaternion.identity;
				val4.transform.localScale = Vector3.one;
				componentsInChildren = val4.GetComponentsInChildren<MeshRenderer>();
				for (int k = 0; k < componentsInChildren.Length; k++)
				{
					((Renderer)componentsInChildren[k]).enabled = true;
				}
				componentsInChildren2 = val4.GetComponentsInChildren<SkinnedMeshRenderer>();
				for (int l = 0; l < componentsInChildren2.Length; l++)
				{
					((Renderer)componentsInChildren2[l]).enabled = true;
				}
				_replacedBlowguns.Add(((Component)val).gameObject);
				num++;
			}
		}
		catch (Exception)
		{
		}
	}

	private static IEnumerator DelayedReplaceBlowgunModel()
	{
		yield return (object)new WaitForSeconds(0.5f);
		if (!((Object)(object)Instance == (Object)null))
		{
			Instance.ReplaceBlowgunInstances();
		}
		yield return (object)new WaitForSeconds(0.75f);
		if (!((Object)(object)Instance == (Object)null))
		{
			Instance.ReplaceBlowgunInstances();
		}
	}

	private static string NormalizeAkSoundSelection(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return DefaultAkSoundOption;
		}
		string a = value.Trim();
		string[] akSoundSelectionValues = AkSoundSelectionValues;
		foreach (string text in akSoundSelectionValues)
		{
			if (string.Equals(a, text, StringComparison.OrdinalIgnoreCase))
			{
				return text;
			}
		}
		if (string.Equals(a, "默认", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Default", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "default", StringComparison.OrdinalIgnoreCase))
		{
			return DefaultAkSoundOption;
		}
		return DefaultAkSoundOption;
	}

	private static string NormalizeZombieBehaviorDifficultySelection(string value)
	{
		string text = NormalizeLookupToken(value);
		if (string.IsNullOrEmpty(text))
		{
			return DefaultZombieBehaviorDifficulty;
		}
		switch (text)
		{
		case "1":
		case "easy":
		case "simple":
		case "casual":
		case "休闲":
		case "简单":
			return "Easy";
		case "2":
		case "standard":
		case "normal":
		case "默认":
		case "标准":
			return "Standard";
		case "3":
		case "hard":
		case "困难":
			return "Hard";
		case "4":
		case "insane":
		case "brutal":
		case "疯狂":
		case "残酷":
			return "Insane";
		case "5":
		case "nightmare":
		case "hell":
		case "噩梦":
		case "地狱":
			return "Nightmare";
		default:
		{
			string[] zombieBehaviorDifficultyValues = ZombieBehaviorDifficultyValues;
			foreach (string text2 in zombieBehaviorDifficultyValues)
			{
				if (string.Equals(text, NormalizeLookupToken(text2), StringComparison.Ordinal))
				{
					return text2;
				}
			}
			return DefaultZombieBehaviorDifficulty;
		}
		}
	}

	private static int GetZombieBehaviorDifficultyPresetIndex(string difficulty)
	{
		string text = NormalizeZombieBehaviorDifficultySelection(difficulty);
		int num = Array.IndexOf(ZombieBehaviorDifficultyValues, text);
		if (num >= 0)
		{
			return num;
		}
		return 1;
	}

	private static ZombieBehaviorDifficultyPreset GetZombieBehaviorDifficultyPreset(string difficulty)
	{
		int zombieBehaviorDifficultyPresetIndex = GetZombieBehaviorDifficultyPresetIndex(difficulty);
		return ZombieBehaviorDifficultyPresets[Mathf.Clamp(zombieBehaviorDifficultyPresetIndex, 0, ZombieBehaviorDifficultyPresets.Length - 1)];
	}

	private void ApplyZombieBehaviorDifficultyPreset(string difficulty = null)
	{
		if (ZombieBehaviorDifficulty == null)
		{
			return;
		}
		string text = NormalizeZombieBehaviorDifficultySelection(difficulty ?? ZombieBehaviorDifficulty.Value);
		if (!string.Equals(ZombieBehaviorDifficulty.Value, text, StringComparison.Ordinal))
		{
			ZombieBehaviorDifficulty.Value = text;
		}
		DisableZombieSleepPatch.ResetRuntimeState();
		_pendingZombieSpeedRefresh = true;
		UpdateZombieSpeed(forceRefresh: true);
		ZombieSpawner.RefreshLiveZombieProperties();
	}

	private void ApplySimplifiedZombieDerivedValues()
	{
		if (!Application.isPlaying)
		{
			return;
		}
		if (IsZombieSpawnFeatureEnabled())
		{
			ZombieSpawner.StartZombieSpawning();
		}
		else
		{
			ZombieSpawner.StopZombieSpawning();
		}
	}

	private static float GetDerivedZombieSpawnIntervalRandomRange()
	{
		float num = Mathf.Max((ZombieSpawnInterval != null) ? ZombieSpawnInterval.Value : 15f, 1f);
		return Mathf.Clamp(num * 0.2f, 2f, 6f);
	}

	internal static float GetDerivedZombieSpawnIntervalRandomRangeRuntime()
	{
		return GetDerivedZombieSpawnIntervalRandomRange();
	}

	private static int GetDerivedZombieWaveMaxCount()
	{
		int num = Mathf.Max((MaxZombies != null) ? MaxZombies.Value : DefaultMaxZombieCount, 0);
		if (num <= 0)
		{
			return 0;
		}
		int num2 = Mathf.Max((ZombieSpawnCount != null) ? ZombieSpawnCount.Value : DefaultZombieSpawnCount, 0);
		return Mathf.Clamp(num2, 0, num);
	}

	private static int GetDerivedZombieWaveMinCount()
	{
		return 0;
	}

	internal static int GetDerivedZombieWaveSpawnCountRuntime()
	{
		int derivedZombieWaveMinCount = GetDerivedZombieWaveMinCount();
		int derivedZombieWaveMaxCount = GetDerivedZombieWaveMaxCount();
		if (derivedZombieWaveMaxCount <= 0)
		{
			return 0;
		}
		if (derivedZombieWaveMinCount >= derivedZombieWaveMaxCount)
		{
			return derivedZombieWaveMaxCount;
		}
		return Random.Range(derivedZombieWaveMinCount, derivedZombieWaveMaxCount + 1);
	}

	private static float GetDerivedZombieSpawnRadius()
	{
		int num = Mathf.Max((MaxZombies != null) ? MaxZombies.Value : DefaultMaxZombieCount, 0);
		float num2 = NormalizeZombieBehaviorDifficultySelection((ZombieBehaviorDifficulty != null) ? ZombieBehaviorDifficulty.Value : DefaultZombieBehaviorDifficulty) switch
		{
			"Easy" => 2.5f,
			"Hard" => 0f,
			"Insane" => -1.5f,
			"Nightmare" => -3f,
			_ => 1.5f,
		};
		return Mathf.Clamp(12f + (float)Mathf.Max(num - 5, 0) * 0.45f + num2, 8f, 20f);
	}

	internal static float GetDerivedZombieSpawnRadiusRuntime()
	{
		return GetDerivedZombieSpawnRadius();
	}

	private static float GetDerivedZombieDestroyDistance()
	{
		int num = Mathf.Max((MaxZombies != null) ? MaxZombies.Value : DefaultMaxZombieCount, 0);
		float num2 = NormalizeZombieBehaviorDifficultySelection((ZombieBehaviorDifficulty != null) ? ZombieBehaviorDifficulty.Value : DefaultZombieBehaviorDifficulty) switch
		{
			"Easy" => 0f,
			"Hard" => 8f,
			"Insane" => 12f,
			"Nightmare" => 16f,
			_ => 4f,
		};
		return Mathf.Clamp(72f + (float)Mathf.Max(num - 5, 0) * 1.5f + num2, 72f, 130f);
	}

	internal static float GetDerivedZombieDestroyDistanceRuntime()
	{
		return GetDerivedZombieDestroyDistance();
	}

	private static ZombieBehaviorDifficultyPreset GetCurrentZombieBehaviorDifficultyPresetRuntime()
	{
		return GetZombieBehaviorDifficultyPreset((ZombieBehaviorDifficulty != null) ? ZombieBehaviorDifficulty.Value : DefaultZombieBehaviorDifficulty);
	}

	internal static float GetZombieMoveSpeedMultiplierRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().MoveSpeedMultiplier, 0.1f, 5f);
	}

	internal static bool IsVanillaZombieBehaviorDifficultyRuntime()
	{
		return string.Equals(NormalizeZombieBehaviorDifficultySelection((ZombieBehaviorDifficulty != null) ? ZombieBehaviorDifficulty.Value : DefaultZombieBehaviorDifficulty), "Easy", StringComparison.Ordinal);
	}

	internal static float GetZombieKnockbackForceRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().HitKnockbackForce, 0f, 2000f);
	}

	internal static float GetZombieTargetSearchIntervalRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().TargetSearchInterval, 0.1f, 10f);
	}

	internal static float GetZombieBiteRecoveryTimeRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().BiteRecoveryTime, 0f, 8f);
	}

	internal static float GetZombieSamePlayerBiteCooldownRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().SamePlayerBiteCooldown, 0.1f, 10f);
	}

	internal static float GetZombieSprintDistanceRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().SprintDistance, 1f, 50f);
	}

	internal static float GetZombieChaseTimeBeforeSprintRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().ChaseTimeBeforeSprint, 0f, 20f);
	}

	internal static float GetZombieLungeDistanceRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().LungeDistance, 4f, 20f);
	}

	internal static float GetZombieLungeTimeRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().LungeTime, 0.1f, 5f);
	}

	internal static float GetZombieLungeRecoveryTimeRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().LungeRecoveryTime, 0f, 10f);
	}

	internal static float GetZombieLookAngleBeforeWakeupRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().LookAngleBeforeWakeup, 0f, 180f);
	}

	internal static float GetZombieDistanceBeforeWakeupRuntime()
	{
		return Mathf.Clamp(GetCurrentZombieBehaviorDifficultyPresetRuntime().DistanceBeforeWakeup, 5f, 100f);
	}

	internal static string GetZombieBehaviorDifficultyDisplayNameRuntime(string difficulty)
	{
		bool flag = Instance?.GetCachedChineseLanguageSetting() ?? false;
		switch (NormalizeZombieBehaviorDifficultySelection(difficulty))
		{
		case "Easy":
			return flag ? "简单" : "Easy";
		case "Hard":
			return flag ? "困难" : "Hard";
		case "Insane":
			return flag ? "疯狂" : "Insane";
		case "Nightmare":
			return flag ? "噩梦" : "Nightmare";
		default:
			return flag ? "标准" : "Standard";
		}
	}

	internal static string GetZombieBehaviorDifficultyDetailsRuntime()
	{
		bool flag = Instance?.GetCachedChineseLanguageSetting() ?? false;
		return flag ? "难度细节：\n简单：保留原版扑击、咬后恢复和咬人节奏，但对刷出的僵尸保持及时唤醒与追击，避免长时间站立。\n标准：移速 1.05x；索敌 6s；咬后 2.5s；冲刺 22m/2.25s；猛扑 10m/1.65s/4s。\n困难：移速 1.15x；索敌 3s；咬后 1.5s；冲刺 26m/1.25s；猛扑 12m/1.85s/2.5s。\n疯狂：移速 1.25x；索敌 1.5s；咬后 0.9s；冲刺 30m/0.85s；猛扑 13m/2s/1.5s。\n噩梦：移速 1.35x；索敌 0.75s；咬后 0.55s；冲刺 34m/0.65s；猛扑 14m/2.15s/0.85s。" : "Difficulty details:\nEasy: keeps vanilla lunge, bite recovery, and bite cadence, but spawned zombies still wake and pursue promptly so they do not stand idle for long periods.\nStandard: Move 1.05x; Search 6s; Post-bite 2.5s; Sprint 22m/2.25s; Lunge 10m/1.65s/4s.\nHard: Move 1.15x; Search 3s; Post-bite 1.5s; Sprint 26m/1.25s; Lunge 12m/1.85s/2.5s.\nInsane: Move 1.25x; Search 1.5s; Post-bite 0.9s; Sprint 30m/0.85s; Lunge 13m/2s/1.5s.\nNightmare: Move 1.35x; Search 0.75s; Post-bite 0.55s; Sprint 34m/0.65s; Lunge 14m/2.15s/0.85s.";
	}

	private static WeaponVariantDefinition GetWeaponVariantDefinition(string selection)
	{
		string text = NormalizeWeaponSelection(selection);
		WeaponVariantDefinition[] weaponVariantDefinitions = WeaponVariantDefinitions;
		foreach (WeaponVariantDefinition weaponVariantDefinition in weaponVariantDefinitions)
		{
			if (weaponVariantDefinition != null && string.Equals(weaponVariantDefinition.SelectionKey, text, StringComparison.OrdinalIgnoreCase))
			{
				return weaponVariantDefinition;
			}
		}
		return WeaponVariantDefinitions[0];
	}

	public static string GetWeaponDisplayName(string selection)
	{
		return GetWeaponVariantDefinition(selection)?.DisplayName ?? DefaultWeaponSelection;
	}

	private static string NormalizeLookupToken(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		foreach (char c in value)
		{
			if (char.IsLetterOrDigit(c))
			{
				stringBuilder.Append(char.ToLowerInvariant(c));
			}
		}
		return stringBuilder.ToString();
	}

	private static bool MatchesWeaponAliasToken(string value, IEnumerable<string> aliases)
	{
		string text = NormalizeLookupToken(value);
		if (string.IsNullOrEmpty(text) || aliases == null)
		{
			return false;
		}
		foreach (string alias in aliases)
		{
			string text2 = NormalizeLookupToken(alias);
			if (!string.IsNullOrEmpty(text2) && (string.Equals(text, text2, StringComparison.Ordinal) || text.StartsWith(text2, StringComparison.Ordinal) || (text2.Length >= 4 && text.IndexOf(text2, StringComparison.Ordinal) >= 0)))
			{
				return true;
			}
		}
		return false;
	}

	internal static string NormalizeWeaponSelection(string value)
	{
		string text = NormalizeLookupToken(value);
		if (string.IsNullOrEmpty(text))
		{
			return DefaultWeaponSelection;
		}
		switch (text)
		{
		case "ak":
		case "ak47":
			return "AK47";
		case "mpx":
			return "MPX";
		case "hk416":
		case "hk417":
			return "HK416";
		}
		WeaponVariantDefinition[] weaponVariantDefinitions = WeaponVariantDefinitions;
		foreach (WeaponVariantDefinition weaponVariantDefinition in weaponVariantDefinitions)
		{
			if (weaponVariantDefinition != null && (MatchesWeaponAliasToken(text, weaponVariantDefinition.PrefabAliases) || MatchesWeaponAliasToken(text, weaponVariantDefinition.IconAliases) || string.Equals(text, NormalizeLookupToken(weaponVariantDefinition.SelectionKey), StringComparison.Ordinal)))
			{
				return weaponVariantDefinition.SelectionKey;
			}
		}
		return DefaultWeaponSelection;
	}

	public static string GetCurrentWeaponSelection()
	{
		return NormalizeWeaponSelection(WeaponSelection?.Value);
	}

	public static string GetCurrentAkSoundSelection()
	{
		return NormalizeAkSoundSelection(AkSoundSelection?.Value);
	}

	public static string GetCurrentWeaponDisplayName()
	{
		return GetWeaponDisplayName(GetCurrentWeaponSelection());
	}

	private static bool TryGetLocalPlayerWeaponSelectionOverride(int actorNr, out string selection)
	{
		selection = null;
		RoomPlayer localPlayer = PhotonNetwork.LocalPlayer;
		if (actorNr <= 0 || localPlayer == null || localPlayer.ActorNumber != actorNr)
		{
			return false;
		}
		selection = GetCurrentWeaponSelection();
		_playerWeaponSelectionsByActor[actorNr] = selection;
		return true;
	}

	private static bool TryGetLocalPlayerAkSoundSelectionOverride(int actorNr, out string selection)
	{
		selection = null;
		RoomPlayer localPlayer = PhotonNetwork.LocalPlayer;
		if (actorNr <= 0 || localPlayer == null || localPlayer.ActorNumber != actorNr)
		{
			return false;
		}
		selection = GetCurrentAkSoundSelection();
		_playerAkSoundSelectionsByActor[actorNr] = selection;
		return true;
	}

	internal static string GetWeaponSelectionForActor(int actorNr)
	{
		if (TryGetLocalPlayerWeaponSelectionOverride(actorNr, out var selection))
		{
			return selection;
		}
		if (actorNr > 0 && _playerWeaponSelectionsByActor.TryGetValue(actorNr, out var value))
		{
			return NormalizeWeaponSelection(value);
		}
		if (PhotonNetwork.InRoom)
		{
			RoomPlayer player = PhotonNetwork.PlayerList?.FirstOrDefault((RoomPlayer candidate) => candidate != null && candidate.ActorNumber == actorNr);
			if (player != null)
			{
				return GetWeaponSelectionForPlayer(player);
			}
		}
		return GetCurrentWeaponSelection();
	}

	internal static string GetAkSoundSelectionForActor(int actorNr)
	{
		if (TryGetLocalPlayerAkSoundSelectionOverride(actorNr, out var selection))
		{
			return selection;
		}
		if (actorNr > 0 && _playerAkSoundSelectionsByActor.TryGetValue(actorNr, out var value))
		{
			return NormalizeAkSoundSelection(value);
		}
		if (PhotonNetwork.InRoom)
		{
			RoomPlayer player = PhotonNetwork.PlayerList?.FirstOrDefault((RoomPlayer candidate) => candidate != null && candidate.ActorNumber == actorNr);
			if (player != null)
			{
				return GetAkSoundSelectionForPlayer(player);
			}
		}
		return GetCurrentAkSoundSelection();
	}

	internal static string GetWeaponSelectionForPlayer(RoomPlayer player)
	{
		if (player == null)
		{
			return GetCurrentWeaponSelection();
		}
		if (TryGetLocalPlayerWeaponSelectionOverride(player.ActorNumber, out var selection))
		{
			return selection;
		}
		object obj = (player.CustomProperties != null) ? player.CustomProperties[(object)PlayerWeaponSelectionPropertyKey] : null;
		string text = NormalizeWeaponSelection(obj as string);
		if (player.ActorNumber > 0)
		{
			_playerWeaponSelectionsByActor[player.ActorNumber] = text;
		}
		return text;
	}

	internal static string GetAkSoundSelectionForPlayer(RoomPlayer player)
	{
		if (player == null)
		{
			return GetCurrentAkSoundSelection();
		}
		if (TryGetLocalPlayerAkSoundSelectionOverride(player.ActorNumber, out var selection))
		{
			return selection;
		}
		object obj = (player.CustomProperties != null) ? player.CustomProperties[(object)PlayerAkSoundSelectionPropertyKey] : null;
		string text = NormalizeAkSoundSelection(obj as string);
		if (player.ActorNumber > 0)
		{
			_playerAkSoundSelectionsByActor[player.ActorNumber] = text;
		}
		return text;
	}

	internal static string GetWeaponSelectionForCharacter(Character character)
	{
		if ((Object)character == (Object)null)
		{
			return GetCurrentWeaponSelection();
		}
		PhotonView val = character.refs?.view;
		if ((Object)val == (Object)null)
		{
			val = ((Component)character).GetComponent<PhotonView>() ?? ((Component)character).GetComponentInParent<PhotonView>();
		}
		if ((Object)val != (Object)null && val.OwnerActorNr > 0)
		{
			return GetWeaponSelectionForActor(val.OwnerActorNr);
		}
		if ((Object)character == (Object)Character.localCharacter)
		{
			return GetCurrentWeaponSelection();
		}
		return GetCurrentWeaponSelection();
	}

	internal static string GetWeaponSelectionForItem(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return GetCurrentWeaponSelection();
		}
		Character val = (((Object)item.trueHolderCharacter != (Object)null) ? item.trueHolderCharacter : item.holderCharacter);
		if ((Object)val != (Object)null)
		{
			return GetWeaponSelectionForCharacter(val);
		}
		PhotonView val2 = ((Component)item).GetComponent<PhotonView>() ?? ((Component)item).GetComponentInParent<PhotonView>();
		if ((Object)val2 != (Object)null)
		{
			int num = (val2.OwnerActorNr > 0) ? val2.OwnerActorNr : val2.CreatorActorNr;
			if (num > 0)
			{
				return GetWeaponSelectionForActor(num);
			}
		}
		return GetCurrentWeaponSelection();
	}

	internal static string GetItemWeaponDisplayName(Item item)
	{
		return GetWeaponDisplayName(GetWeaponSelectionForItem(item));
	}

	private static void ApplySelectedWeaponAssets()
	{
		string currentWeaponSelection = GetCurrentWeaponSelection();
		GameObject val = null;
		Texture2D val2 = null;
		bool flag = false;
		if (!TryGetResolvedWeaponPrefab(currentWeaponSelection, out val))
		{
			TryGetResolvedWeaponPrefab(DefaultWeaponSelection, out val);
		}
		if (!HasRenderableGeometry(val))
		{
			val = (HasRenderableGeometry(_genericResolvedWeaponPrefab) ? _genericResolvedWeaponPrefab : null);
		}
		if (TryGetResolvedWeaponIcon(currentWeaponSelection, out val2) || TryGetResolvedWeaponIcon(DefaultWeaponSelection, out val2))
		{
			flag = (Object)val2 != (Object)null;
		}
		if ((Object)val2 == (Object)null)
		{
			val2 = _genericResolvedWeaponIcon;
		}
		_ak47Prefab = val;
		_ak47IconTexture = val2;
		_useExplicitSelectedWeaponIcon = flag;
	}

	private static bool TryGetResolvedWeaponPrefab(string selection, out GameObject prefab)
	{
		prefab = null;
		string key = NormalizeWeaponSelection(selection);
		if (_resolvedWeaponPrefabs.TryGetValue(key, out prefab) && HasRenderableGeometry(prefab))
		{
			return true;
		}
		prefab = null;
		return false;
	}

	private static bool TryGetResolvedWeaponIcon(string selection, out Texture2D icon)
	{
		icon = null;
		string key = NormalizeWeaponSelection(selection);
		if (_resolvedWeaponIcons.TryGetValue(key, out icon) && (Object)icon != (Object)null)
		{
			return true;
		}
		icon = null;
		return false;
	}

	private static void CaptureGenericResolvedWeaponAssets()
	{
		_genericResolvedWeaponPrefab = (HasRenderableGeometry(_ak47Prefab) ? _ak47Prefab : null);
		_genericResolvedWeaponIcon = _ak47IconTexture;
	}

	private static void TryResolveSelectableWeaponAssets(AssetBundle bundle)
	{
		if ((Object)bundle == (Object)null)
		{
			return;
		}
		GameObject[] source = bundle.LoadAllAssets<GameObject>() ?? Array.Empty<GameObject>();
		Texture2D[] textures = bundle.LoadAllAssets<Texture2D>() ?? Array.Empty<Texture2D>();
		WeaponVariantDefinition[] weaponVariantDefinitions = WeaponVariantDefinitions;
		foreach (WeaponVariantDefinition weaponVariantDefinition in weaponVariantDefinitions)
		{
			if (weaponVariantDefinition == null || string.IsNullOrWhiteSpace(weaponVariantDefinition.SelectionKey))
			{
				continue;
			}
			GameObject matchingWeaponPrefabCandidate = FindMatchingWeaponPrefabCandidate(source, weaponVariantDefinition);
			if ((Object)matchingWeaponPrefabCandidate != (Object)null)
			{
				GameObject runtimeWeaponPrefabClone = CreateRuntimeWeaponPrefabClone(matchingWeaponPrefabCandidate, weaponVariantDefinition.SelectionKey);
				if ((Object)runtimeWeaponPrefabClone != (Object)null)
				{
					_resolvedWeaponPrefabs[weaponVariantDefinition.SelectionKey] = runtimeWeaponPrefabClone;
				}
			}
			Texture2D bestMatchingWeaponIcon = FindBestMatchingWeaponIcon(bundle, textures, weaponVariantDefinition);
			if ((Object)bestMatchingWeaponIcon != (Object)null)
			{
				_resolvedWeaponIcons[weaponVariantDefinition.SelectionKey] = bestMatchingWeaponIcon;
			}
		}
	}

	private static GameObject FindMatchingWeaponPrefabCandidate(IEnumerable<GameObject> prefabs, WeaponVariantDefinition definition)
	{
		if (prefabs == null || definition == null)
		{
			return null;
		}
		foreach (GameObject prefab in prefabs)
		{
			GameObject matchingWeaponChildPrefab = FindMatchingWeaponChildPrefab(prefab, definition.PrefabAliases, directChildrenOnly: true);
			if ((Object)matchingWeaponChildPrefab != (Object)null)
			{
				return matchingWeaponChildPrefab;
			}
		}
		foreach (GameObject prefab2 in prefabs)
		{
			GameObject matchingWeaponChildPrefab2 = FindMatchingWeaponChildPrefab(prefab2, definition.PrefabAliases, directChildrenOnly: false);
			if ((Object)matchingWeaponChildPrefab2 != (Object)null)
			{
				return matchingWeaponChildPrefab2;
			}
		}
		foreach (GameObject prefab3 in prefabs)
		{
			if ((Object)prefab3 != (Object)null && HasRenderableGeometry(prefab3) && MatchesWeaponAliasToken(((Object)prefab3).name, definition.PrefabAliases))
			{
				return prefab3;
			}
		}
		return null;
	}

	private static GameObject FindMatchingWeaponChildPrefab(GameObject root, IEnumerable<string> aliases, bool directChildrenOnly)
	{
		if ((Object)root == (Object)null)
		{
			return null;
		}
		GameObject result = null;
		int bestScore = int.MinValue;
		if (directChildrenOnly)
		{
			for (int i = 0; i < root.transform.childCount; i++)
			{
				Transform child = root.transform.GetChild(i);
				if ((Object)child != (Object)null)
				{
					EvaluateWeaponPrefabCandidate(child.gameObject, aliases, ref result, ref bestScore);
				}
			}
			return result;
		}
		Transform[] componentsInChildren = root.GetComponentsInChildren<Transform>(true);
		foreach (Transform val in componentsInChildren)
		{
			if (!((Object)val == (Object)null) && !((Object)val == (Object)root.transform))
			{
				EvaluateWeaponPrefabCandidate(val.gameObject, aliases, ref result, ref bestScore);
			}
		}
		return result;
	}

	private static void EvaluateWeaponPrefabCandidate(GameObject candidate, IEnumerable<string> aliases, ref GameObject result, ref int bestScore)
	{
		if ((Object)candidate == (Object)null || !HasRenderableGeometry(candidate) || !MatchesWeaponAliasToken(((Object)candidate).name, aliases))
		{
			return;
		}
		int weaponPrefabCandidateScore = GetWeaponPrefabCandidateScore(candidate);
		if (weaponPrefabCandidateScore > bestScore)
		{
			bestScore = weaponPrefabCandidateScore;
			result = candidate;
		}
	}

	private static int GetWeaponPrefabCandidateScore(GameObject candidate)
	{
		if ((Object)candidate == (Object)null)
		{
			return int.MinValue;
		}
		int num = Mathf.Clamp(candidate.transform.childCount * 1200, 0, 12000);
		MeshFilter[] componentsInChildren = candidate.GetComponentsInChildren<MeshFilter>(true);
		foreach (MeshFilter val in componentsInChildren)
		{
			if ((Object)val == (Object)null || (Object)val.sharedMesh == (Object)null)
			{
				continue;
			}
			num += 1200;
			num += Mathf.Clamp(val.sharedMesh.vertexCount / 2, 0, 24000);
		}
		SkinnedMeshRenderer[] componentsInChildren2 = candidate.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		foreach (SkinnedMeshRenderer val2 in componentsInChildren2)
		{
			if (!((Object)val2 == (Object)null) && (Object)val2.sharedMesh != (Object)null)
			{
				num += 1400;
				num += Mathf.Clamp(val2.sharedMesh.vertexCount / 2, 0, 24000);
			}
		}
		return num;
	}

	private static GameObject CreateRuntimeWeaponPrefabClone(GameObject source, string selectionKey)
	{
		if ((Object)source == (Object)null || !HasRenderableGeometry(source))
		{
			return null;
		}
		GameObject val = null;
		Transform transform = source.transform;
		if ((Object)transform.parent != (Object)null)
		{
			Transform root = transform.root;
			if ((Object)root == (Object)null)
			{
				return null;
			}
			if (!TryBuildRelativeChildIndexPath(root, transform, out var siblingIndexPath))
			{
				return null;
			}
			GameObject val2 = Object.Instantiate<GameObject>(((Component)root).gameObject);
			if ((Object)val2 == (Object)null)
			{
				return null;
			}
			Transform val3 = ResolveChildByIndexPath(val2.transform, siblingIndexPath);
			if ((Object)val3 == (Object)null || !HasRenderableGeometry(((Component)val3).gameObject))
			{
				Object.Destroy((Object)(object)val2);
				return null;
			}
			GameObject val4 = new GameObject(selectionKey + "_RuntimeRoot");
			val3.SetParent(val4.transform, worldPositionStays: true);
			Object.Destroy((Object)(object)val2);
			val = val4;
		}
		else
		{
			val = Object.Instantiate<GameObject>(source);
		}
		if ((Object)val == (Object)null)
		{
			return null;
		}
		val.transform.SetParent(null, worldPositionStays: false);
		val.transform.localPosition = Vector3.zero;
		val.transform.localRotation = Quaternion.identity;
		val.transform.localScale = Vector3.one;
		NormalizeRuntimeWeaponPrefabClone(val);
		val.SetActive(false);
		val.hideFlags = (HideFlags)61;
		((Object)val).name = selectionKey + "_RuntimePrefab";
		Object.DontDestroyOnLoad((Object)val);
		_runtimeWeaponPrefabClones.Add(val);
		return val;
	}

	private static bool TryBuildRelativeChildIndexPath(Transform root, Transform descendant, out List<int> siblingIndexPath)
	{
		siblingIndexPath = null;
		if ((Object)root == (Object)null || (Object)descendant == (Object)null)
		{
			return false;
		}
		List<int> list = new List<int>();
		Transform val = descendant;
		while ((Object)val != (Object)null && (Object)val != (Object)root)
		{
			list.Add(val.GetSiblingIndex());
			val = val.parent;
		}
		if ((Object)val != (Object)root)
		{
			return false;
		}
		list.Reverse();
		siblingIndexPath = list;
		return true;
	}

	private static Transform ResolveChildByIndexPath(Transform root, IReadOnlyList<int> siblingIndexPath)
	{
		if ((Object)root == (Object)null || siblingIndexPath == null)
		{
			return null;
		}
		Transform val = root;
		for (int i = 0; i < siblingIndexPath.Count; i++)
		{
			int num = siblingIndexPath[i];
			if (num < 0 || num >= val.childCount)
			{
				return null;
			}
			val = val.GetChild(num);
			if ((Object)val == (Object)null)
			{
				return null;
			}
		}
		return val;
	}

	private static void NormalizeRuntimeWeaponPrefabClone(GameObject root)
	{
		if ((Object)root == (Object)null)
		{
			return;
		}
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if (!((Object)val == (Object)null))
			{
				ItemPatch.NormalizeAkRenderer(val);
			}
		}
	}

	private static Texture2D FindBestMatchingWeaponIcon(AssetBundle bundle, IEnumerable<Texture2D> textures, WeaponVariantDefinition definition)
	{
		if (textures == null || definition == null)
		{
			return null;
		}
		Texture2D result = null;
		int num = int.MinValue;
		foreach (Texture2D texture in textures)
		{
			if ((Object)texture == (Object)null)
			{
				continue;
			}
			string name = ((Object)texture).name;
			string assetNameForObject = FindAssetNameForObject(bundle, (Object)(object)texture);
			int num2 = int.MinValue;
			if (MatchesWeaponAliasToken(name, definition.IconAliases))
			{
				num2 = 100000;
			}
			else if (MatchesWeaponAliasToken(assetNameForObject, definition.IconAliases))
			{
				num2 = 96000;
			}
			else if (NormalizeLookupToken(name).IndexOf("icon", StringComparison.Ordinal) >= 0 && MatchesWeaponAliasToken(name, definition.PrefabAliases))
			{
				num2 = 92000;
			}
			else if (NormalizeLookupToken(assetNameForObject).IndexOf("icon", StringComparison.Ordinal) >= 0 && MatchesWeaponAliasToken(assetNameForObject, definition.PrefabAliases))
			{
				num2 = 90000;
			}
			else if (IsLikelyIconTexture(texture) && MatchesWeaponAliasToken(name, definition.PrefabAliases))
			{
				num2 = 86000;
			}
			else if (IsLikelyIconTexture(texture) && MatchesWeaponAliasToken(assetNameForObject, definition.PrefabAliases))
			{
				num2 = 84000;
			}
			if (num2 == int.MinValue)
			{
				continue;
			}
			if (IsLikelyIconTexture(texture))
			{
				num2 += 2000;
			}
			num2 += Mathf.Clamp(((Texture)texture).width * ((Texture)texture).height / 256, 0, 12000);
			if (num2 > num)
			{
				num = num2;
				result = texture;
			}
		}
		return result;
	}

	private void OnAkSoundSelectionChanged()
	{
		if (AkSoundSelection == null)
		{
			return;
		}
		string text = NormalizeAkSoundSelection(AkSoundSelection.Value);
		if (!string.Equals(AkSoundSelection.Value, text, StringComparison.Ordinal))
		{
			AkSoundSelection.Value = text;
			SavePluginConfigQuietly();
			return;
		}
		SavePluginConfigQuietly();
		ApplySelectedGunshotSound();
		PublishLocalAkSoundSelectionToPlayerProperties(force: true);
	}

	private void SavePluginConfigQuietly()
	{
		try
		{
			((BaseUnityPlugin)this).Config.Save();
			RewriteCanonicalConfigForUserPresentation();
		}
		catch
		{
		}
	}

	internal void SaveOwnedConfigRuntime()
	{
		SavePluginConfigQuietly();
	}

	private void ApplySelectedGunshotSound()
	{
		try
		{
			string text = (_currentGunshotSoundSelection = NormalizeAkSoundSelection(AkSoundSelection?.Value));
			if (_gunshotSoundLoadCoroutine != null)
			{
				((MonoBehaviour)this).StopCoroutine(_gunshotSoundLoadCoroutine);
				_gunshotSoundLoadCoroutine = null;
			}
			if (_externalGunshotSounds.TryGetValue(text, out var value) && (Object)value != (Object)null)
			{
				SetGunshotSoundClip(value, text, isExternal: true);
				return;
			}
			string externalAkSoundPath = GetExternalAkSoundPath(text);
			if (string.IsNullOrWhiteSpace(externalAkSoundPath) || !File.Exists(externalAkSoundPath))
			{
				ManualLogSource log = Log;
				if (log != null)
				{
					log.LogWarning((object)("[ShootZombies] Missing external AK sound for selection '" + text + "'"));
				}
				string text2 = FindFallbackAkSoundSelection(text);
				if (!string.IsNullOrWhiteSpace(text2) && !string.Equals(text2, text, StringComparison.Ordinal))
				{
					if (AkSoundSelection != null && !string.Equals(AkSoundSelection.Value, text2, StringComparison.Ordinal))
					{
						AkSoundSelection.Value = text2;
					}
					else
					{
						_currentGunshotSoundSelection = text2;
						ApplySelectedGunshotSound();
					}
				}
				else
				{
					_gunshotSound = null;
					if ((Object)_sharedAudioSource != (Object)null)
					{
						_sharedAudioSource.clip = null;
					}
				}
			}
			else
			{
				_gunshotSoundLoadCoroutine = ((MonoBehaviour)this).StartCoroutine(LoadAudioClipFromFile(text, externalAkSoundPath));
			}
		}
		catch (Exception ex)
		{
			ManualLogSource log2 = Log;
			if (log2 != null)
			{
				log2.LogWarning((object)("[ShootZombies] ApplySelectedGunshotSound failed: " + ex.Message));
			}
		}
	}

	private void SetGunshotSoundClip(AudioClip clip, string selection, bool isExternal)
	{
		if (!((Object)clip == (Object)null))
		{
			_currentGunshotSoundSelection = NormalizeAkSoundSelection(selection);
			_gunshotSound = clip;
			if ((Object)_sharedAudioSource != (Object)null)
			{
				_sharedAudioSource.clip = clip;
			}
			ReplaceBlowgunSoundInternal();
		}
	}

	private static AudioType GetAudioTypeForPath(string filePath)
	{
		return (AudioType)((Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant() switch
		{
			".wav" => 20, 
			".ogg" => 14, 
			".mp3" => 13, 
			_ => 0, 
		});
	}

	private static IEnumerable<string> EnumerateAkSoundDirectories()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		if (!string.IsNullOrWhiteSpace(directoryName) && hashSet.Add(directoryName))
		{
			yield return Path.Combine(directoryName, "AK_Sounds");
		}
		if (!string.IsNullOrWhiteSpace(Paths.PluginPath) && hashSet.Add(Paths.PluginPath))
		{
			yield return Path.Combine(Paths.PluginPath, "AK_Sounds");
		}
		string currentDirectory = Directory.GetCurrentDirectory();
		if (!string.IsNullOrWhiteSpace(currentDirectory) && hashSet.Add(currentDirectory))
		{
			yield return Path.Combine(currentDirectory, "AK_Sounds");
		}
	}

	private static string GetExternalAkSoundPath(string selection)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(selection);
		string[] array = new string[3] { ".wav", ".mp3", ".ogg" };
		foreach (string item in EnumerateAkSoundDirectories())
		{
			if (string.IsNullOrWhiteSpace(item))
			{
				continue;
			}
			string[] array2 = array;
			foreach (string text in array2)
			{
				string text2 = Path.Combine(item, fileNameWithoutExtension + text);
				if (File.Exists(text2))
				{
					return text2;
				}
			}
		}
		return null;
	}

	private IEnumerator LoadAudioClipFromFile(string selection, string filePath)
	{
		string text = "file://" + filePath;
		UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(text, GetAudioTypeForPath(filePath));
		try
		{
			yield return www.SendWebRequest();
			if ((int)www.result == 1)
			{
				AudioClip content = DownloadHandlerAudioClip.GetContent(www);
				if ((Object)content != (Object)null)
				{
					((Object)content).name = Path.GetFileNameWithoutExtension(filePath);
					_externalGunshotSounds[selection] = content;
					if (string.Equals(NormalizeAkSoundSelection(AkSoundSelection?.Value), NormalizeAkSoundSelection(selection), StringComparison.Ordinal))
					{
						SetGunshotSoundClip(content, selection, isExternal: true);
					}
				}
				yield break;
			}
			ManualLogSource log = Log;
			if (log != null)
			{
				log.LogWarning((object)("[ShootZombies] Failed to load AK sound '" + selection + "' from '" + filePath + "': " + www.error));
			}
		}
		finally
		{
			_gunshotSoundLoadCoroutine = null;
			((IDisposable)www)?.Dispose();
		}
	}

	private void ReplaceBlowgunSound()
	{
		try
		{
			ApplySelectedGunshotSound();
		}
		catch (Exception ex)
		{
			Log.LogError((object)("[ShootZombies] ReplaceBlowgunSound error: " + ex));
		}
	}

	private void ReplaceBlowgunSoundInternal()
	{
		try
		{
			if ((Object)_gunshotSound == (Object)null)
			{
				return;
			}
			if (TryResolveBaseWeaponItem(out var item))
			{
				Type type = ((object)item).GetType();
				FieldInfo field = type.GetField("prefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field == null)
				{
					field = type.GetField("_prefab", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}
				if (field != null && (Object)_gunshotSound != (Object)null)
				{
					object value = field.GetValue(item);
					GameObject val = (GameObject)((value is GameObject) ? value : null);
					if ((Object)val != (Object)null)
					{
						AudioSource[] componentsInChildren = val.GetComponentsInChildren<AudioSource>(true);
						foreach (AudioSource val2 in componentsInChildren)
						{
							if ((Object)val2.clip != (Object)null)
							{
								val2.clip = _gunshotSound;
							}
						}
					}
				}
			}
			Item[] array = Object.FindObjectsByType<Item>((FindObjectsSortMode)0);
			foreach (Item val3 in array)
			{
				string name = val3.GetName();
				if (!ItemPatch.IsBlowgunLike(val3, name))
				{
					continue;
				}
				AudioSource[] componentsInChildren = ((Component)val3).GetComponentsInChildren<AudioSource>(true);
				foreach (AudioSource val4 in componentsInChildren)
				{
					if ((Object)val4.clip != (Object)null && (Object)_gunshotSound != (Object)null)
					{
						val4.clip = _gunshotSound;
					}
				}
				FieldInfo[] fields = ((object)val3).GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo in fields)
				{
					if (!(fieldInfo.FieldType == typeof(AudioClip)) && !fieldInfo.Name.ToLower().Contains("sound") && !fieldInfo.Name.ToLower().Contains("audio") && !fieldInfo.Name.ToLower().Contains("sfx"))
					{
						continue;
					}
					try
					{
						if (fieldInfo.GetValue(val3) is AudioClip && (Object)_gunshotSound != (Object)null)
						{
							fieldInfo.SetValue(val3, _gunshotSound);
						}
					}
					catch (Exception)
					{
					}
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private static bool TryResolveBaseWeaponItem(out Item item)
	{
		item = null;
		if (TryGetItemById(70, out item))
		{
			ItemPatch.ApplyAkDisplay(item);
			return true;
		}
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
					int num2 = ScoreBaseWeaponItem(val2);
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
			Log.LogWarning((object)("[ShootZombies] TryResolveBaseWeaponItem database scan failed: " + ex.Message));
		}
		if ((Object)item == (Object)null)
		{
			try
			{
				Item[] array2 = Resources.FindObjectsOfTypeAll<Item>();
				foreach (Item val3 in array2)
				{
					int num3 = ScoreBaseWeaponItem(val3);
					if (num3 > num)
					{
						num = num3;
						item = val3;
					}
				}
			}
			catch (Exception ex2)
			{
				Log.LogWarning((object)("[ShootZombies] TryResolveBaseWeaponItem resource scan failed: " + ex2.Message));
			}
		}
		if ((Object)item != (Object)null)
		{
			ItemPatch.ApplyAkDisplay(item);
		}
		return (Object)item != (Object)null;
	}

	private static bool TryGetItemById(ushort id, out Item item)
	{
		item = null;
		try
		{
			MethodInfo method = typeof(ItemDatabase).GetMethod("TryGetItem", BindingFlags.Static | BindingFlags.Public);
			if (method == null)
			{
				return false;
			}
			object[] array = new object[2] { id, null };
			object obj = method.Invoke(null, array);
			if (!(obj is bool) || !(bool)obj)
			{
				return false;
			}
			object obj2 = array[1];
			item = (Item)((obj2 is Item) ? obj2 : null);
			return (Object)item != (Object)null;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsLobbyScene(Scene scene)
	{
		if (scene.name != null)
		{
			return scene.name.Contains("Airport");
		}
		return false;
	}

	private static bool IsTitleScene(Scene scene)
	{
		if (scene.name != null)
		{
			if (!scene.name.Contains("Pretitle"))
			{
				return scene.name.Contains("Title");
			}
			return true;
		}
		return false;
	}

	private static bool IsGameplayScene(Scene scene)
	{
		if (scene.name != null && !IsTitleScene(scene))
		{
			return !IsLobbyScene(scene);
		}
		return false;
	}

	internal static bool IsGameplaySceneRuntime(Scene scene)
	{
		return IsGameplayScene(scene);
	}

	internal static bool IsLobbySceneRuntime(Scene scene)
	{
		return IsLobbyScene(scene);
	}

	internal static bool IsTitleSceneRuntime(Scene scene)
	{
		return IsTitleScene(scene);
	}

	private static bool IsModConfigUiRuntimeSafeScene(Scene scene)
	{
		if (!scene.IsValid() || !scene.isLoaded)
		{
			return false;
		}
		return !IsGameplayScene(scene);
	}

	private static bool IsModConfigUiRuntimeSafe()
	{
		return IsModConfigUiRuntimeSafeScene(SceneManager.GetActiveScene());
	}

	private static void BeginSceneRuntimeWarmup(Scene scene)
	{
		float num = OtherSceneWarmupSeconds;
		if (IsGameplayScene(scene))
		{
			num = GameplaySceneWarmupSeconds;
		}
		else if (IsLobbyScene(scene))
		{
			num = LobbySceneWarmupSeconds;
		}
		_sceneWarmupSceneName = scene.name ?? string.Empty;
		_sceneWarmupUntilUnscaledTime = Time.unscaledTime + num;
		ItemUIDataPatch.ResetVisibleUiTrackingFallback();
	}

	private static void RequestAkVisualRefresh(bool includeUiRefresh = true, bool forceRefresh = false)
	{
		_pendingAkVisualRefresh = true;
		if (forceRefresh)
		{
			_pendingAkVisualForceRefresh = true;
		}
		if (includeUiRefresh)
		{
			_pendingAkUiRefresh = true;
		}
	}

	internal static bool IsRuntimeVisualRefreshBlocked()
	{
		return IsRuntimeVisualRefreshBlocked(SceneManager.GetActiveScene());
	}

	internal static bool IsRuntimeVisualRefreshBlocked(Scene scene)
	{
		if (!scene.IsValid() || !scene.isLoaded)
		{
			return true;
		}
		if (Time.unscaledTime < _sceneWarmupUntilUnscaledTime)
		{
			string text = scene.name ?? string.Empty;
			if (string.IsNullOrEmpty(_sceneWarmupSceneName) || string.Equals(text, _sceneWarmupSceneName, StringComparison.Ordinal))
			{
				return true;
			}
		}
		if (IsGameplayScene(scene) && (Object)(object)Character.localCharacter == (Object)null)
		{
			return true;
		}
		return false;
	}

	private static bool IsFogRuntimeReady()
	{
		return IsFogRuntimeReady(SceneManager.GetActiveScene());
	}

	private static bool IsFogRuntimeReady(Scene scene)
	{
		if (!scene.IsValid() || !scene.isLoaded || !IsGameplayScene(scene))
		{
			return false;
		}
		if (IsRuntimeVisualRefreshBlocked(scene))
		{
			return false;
		}
		Character character = _localCharacter ?? Character.localCharacter;
		if ((Object)(object)character == (Object)null || character.isZombie || character.isBot)
		{
			return false;
		}
		return true;
	}

	private static string GetSceneBucket(Scene scene)
	{
		if (IsTitleScene(scene))
		{
			return "title";
		}
		if (IsLobbyScene(scene))
		{
			return "lobby";
		}
		if (IsGameplayScene(scene))
		{
			return "gameplay";
		}
		return "other";
	}

	private static int ScoreBaseWeaponItem(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return 0;
		}
		if (IsGuaranteedBlowgunItem(item))
		{
			return 10000;
		}
		string text = (((Object)((Component)item).gameObject).name ?? string.Empty).ToLowerInvariant();
		string text2 = (item.UIData?.itemName ?? string.Empty).ToLowerInvariant();
		if (text.Contains("ak"))
		{
			return 0;
		}
		int num = 0;
		num += ScoreActionProfile(item);
		int num2;
		if (!text.Contains("blowgun"))
		{
			num2 = (text2.Contains("blowgun") ? 1 : 0);
			if (num2 == 0)
			{
				goto IL_0099;
			}
		}
		else
		{
			num2 = 1;
		}
		num += 1500;
		goto IL_0099;
		IL_0099:
		if (text.Contains("healingdart") || text2.Contains("healingdart"))
		{
			num += 120;
		}
		if (text.Contains("dart") || text2.Contains("dart"))
		{
			num += 40;
		}
		if (num2 == 0 && text.Contains("variant"))
		{
			num -= 120;
		}
		if (num2 == 0 && (text.Contains("projectile") || text.Contains("ammo")))
		{
			num -= 220;
		}
		return Mathf.Max(num, 0);
	}

	private static int ScoreActionProfile(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return 0;
		}
		int num = 0;
		try
		{
			bool flag = false;
			bool flag2 = false;
			ItemActionBase[] componentsInChildren = ((Component)item).GetComponentsInChildren<ItemActionBase>(true);
			foreach (ItemActionBase val in componentsInChildren)
			{
				if (!((Object)val == (Object)null))
				{
					switch (((object)val).GetType().Name)
					{
					case "Action_RaycastDart":
						flag = true;
						break;
					case "Action_Consume":
					case "Action_ConsumeAndSpawn":
						flag2 = true;
						break;
					}
				}
			}
			if (flag)
			{
				num += 2500;
			}
			if (flag2)
			{
				num -= 2200;
			}
		}
		catch
		{
		}
		return num;
	}

	private static bool IsGuaranteedBlowgunItem(Item item)
	{
		if ((Object)item == (Object)null)
		{
			return false;
		}
		try
		{
			if (item.itemID == 70)
			{
				return true;
			}
			if ((Object)item.isSecretlyOtherItemPrefab != (Object)null && item.isSecretlyOtherItemPrefab.itemID == 70)
			{
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

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
						if (ownerActorNr == int.MinValue)
						{
							continue;
						}
						bool flag2 = IsAlive(allCharacter);
						bool flag4 = HasWeaponGrantRecord(ownerActorNr);
						bool flag5 = HasFirstAidGrantRecord(ownerActorNr);
						if (!flag2 || (flag4 && flag5))
						{
							continue;
						}
						if (!flag5)
						{
							TryGrantFirstAidWithAuthority(allCharacter, ownerActorNr);
						}
						if (!flag4)
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
			Player player = c.player;
			if ((Object)player == (Object)null || player.itemSlots == null)
			{
				return false;
			}
			if (CharacterAlreadyHasShootZombiesWeapon(c))
			{
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
			ItemPatch.ApplyAkDisplay(item);
			if (!TrySetItemIntoSlot(player, preferredWeaponSlot, item, 999))
			{
				return false;
			}
			TrySyncInventoryRpc(c, player, "weapon-slot");
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
		_recentWeaponDropTimeByActor[characterGrantTrackingId] = Time.unscaledTime;
		_weaponMissingSinceByActor.Remove(characterGrantTrackingId);
		_pendingRemoteWeaponGrantActors.Remove(characterGrantTrackingId);
	}

	private static void MarkFirstAidGrantedForCharacter(Character c)
	{
		int characterGrantTrackingId = GetCharacterGrantTrackingId(c);
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
		_firstAidMissingSinceByActor.Remove(actorNr);
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

	private static void ClearFirstAidGrantRecordForActor(int actorNr)
	{
		_receivedFirstAid.Remove(actorNr);
		_persistentReceivedFirstAid.Remove(actorNr);
		_pendingRemoteFirstAidGrantActors.Remove(actorNr);
		_lastFirstAidGrantTimeByActor.Remove(actorNr);
		_firstAidMissingSinceByActor.Remove(actorNr);
	}

	private static void RepairMissingLoadoutGrantRecords(Character c, int actorNr)
	{
		if (actorNr == int.MinValue || (Object)c == (Object)null || c.isBot || c.isZombie)
		{
			return;
		}
		RepairMissingWeaponGrantRecord(c, actorNr);
		RepairMissingFirstAidGrantRecord(c, actorNr);
	}

	private static void RepairMissingWeaponGrantRecord(Character c, int actorNr)
	{
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

	private static void RepairMissingFirstAidGrantRecord(Character c, int actorNr)
	{
		if (!HasFirstAidGrantRecord(actorNr) || IsPendingRemoteFirstAidGrant(actorNr))
		{
			_firstAidMissingSinceByActor.Remove(actorNr);
			return;
		}
		if (CharacterAlreadyHasFirstAid(c))
		{
			_firstAidMissingSinceByActor.Remove(actorNr);
			return;
		}
		if (_lastFirstAidGrantTimeByActor.TryGetValue(actorNr, out var value) && Time.unscaledTime - value < LoadoutRepairGracePeriod)
		{
			return;
		}
		if (!_firstAidMissingSinceByActor.TryGetValue(actorNr, out var value2))
		{
			_firstAidMissingSinceByActor[actorNr] = Time.unscaledTime;
			return;
		}
		if (Time.unscaledTime - value2 >= LoadoutMissingConfirmDuration)
		{
			ClearFirstAidGrantRecordForActor(actorNr);
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
		if ((Object)(c.refs?.view) != (Object)null && c.refs.view.OwnerActorNr != 0)
		{
			return c.refs.view.OwnerActorNr;
		}
		Player player = c.player;
		if (!HasOnlineRoomSession())
		{
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
		if ((Object)c == (Object)Character.localCharacter || (Object)c == (Object)_localCharacter)
		{
			if ((Object)player != (Object)null)
			{
				return -Mathf.Abs(((Object)player).GetInstanceID());
			}
			return -Mathf.Abs(((Object)c).GetInstanceID());
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
		_lobbyConfigPanel?.Tick();
		if (!IsModFeatureEnabled())
		{
			CleanupLocalWeaponVisual();
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
		else
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
		_nextLocalCharacterRefreshTime = unscaledTime;
		_nextPendingRemoteGrantProcessTime = unscaledTime;
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
		if (ShouldRunScheduledTask(ref _nextLocalCharacterRefreshTime, unscaledTime, ScheduledLocalCharacterRefreshInterval))
		{
			RefreshLocalCharacterReference();
		}
		if (ShouldRunScheduledTask(ref _nextPendingRemoteGrantProcessTime, unscaledTime, ScheduledPendingRemoteGrantInterval))
		{
			ProcessPendingRemoteGrantRequests();
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
		if (((currentRoom != null) ? ((RoomInfo)currentRoom).CustomProperties : null) == null)
		{
			return false;
		}
		payload = ((RoomInfo)PhotonNetwork.CurrentRoom).CustomProperties[(object)RoomConfigPropertyKey] as string;
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
		PruneGrantTrackingDictionary(_firstAidMissingSinceByActor, hashSet);
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
			ApplyZombieBehaviorDifficultyPreset();
			ApplySimplifiedZombieDerivedValues();
			if (ZombieBehaviorDifficulty != null)
			{
				_lastZombieBehaviorDifficulty = NormalizeZombieBehaviorDifficultySelection(ZombieBehaviorDifficulty.Value);
			}
			NormalizeConfigRanges();
			RewriteCanonicalConfigForUserPresentation();
		}
		finally
		{
			_applyingRoomConfigPayload = false;
		}
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

	private void OnOwnedConfigEntryChanged(object sender, EventArgs e)
	{
		if (!_applyingRoomConfigPayload)
		{
			ConfigEntryBase val = (ConfigEntryBase)((sender is ConfigEntryBase) ? sender : null);
			if (val == null || !IsRoomConfigSyncedEntry(val))
			{
				return;
			}
			if (_applyingZombieBehaviorDifficultySelection)
			{
				return;
			}
			if ((object)val == ZombieBehaviorDifficulty)
			{
				try
				{
					_applyingZombieBehaviorDifficultySelection = true;
					string text = NormalizeZombieBehaviorDifficultySelection(ZombieBehaviorDifficulty?.Value);
					if (ZombieBehaviorDifficulty != null && !string.Equals(ZombieBehaviorDifficulty.Value, text, StringComparison.Ordinal))
					{
						ZombieBehaviorDifficulty.Value = text;
					}
					ApplyZombieBehaviorDifficultyPreset(text);
					ApplySimplifiedZombieDerivedValues();
					_lastZombieBehaviorDifficulty = text;
				}
				finally
				{
					_applyingZombieBehaviorDifficultySelection = false;
				}
			}
			else if ((object)val == MaxZombies || (object)val == ZombieSpawnCount || (object)val == ZombieSpawnInterval || (object)val == ZombieMaxLifetime)
			{
				ApplySimplifiedZombieDerivedValues();
			}
			MarkRoomConfigDirty(forceImmediate: true);
			SavePluginConfigQuietly();
		}
	}

	private static bool IsRoomConfigSyncedEntry(ConfigEntryBase entry)
	{
		return (object)entry == ModEnabled || (object)entry == WeaponEnabled || (object)entry == FireInterval || (object)entry == ZombieTimeReduction || (object)entry == ZombieSpawnInterval || (object)entry == MaxZombies || (object)entry == ZombieSpawnCount || (object)entry == ZombieMaxLifetime || (object)entry == ZombieBehaviorDifficulty;
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

	private void RefreshLocalCharacterReference()
	{
		try
		{
			Character localCharacter = Character.localCharacter;
			if ((Object)localCharacter != (Object)null && !localCharacter.isBot && !localCharacter.isZombie)
			{
				if ((Object)_localCharacter != (Object)localCharacter)
				{
					_localCharacter = localCharacter;
					_weaponVisualOwner = null;
					_localWeaponSourceItemId = 0;
				}
				TryEnsureLocalWeaponGrant(localCharacter);
			}
			else
			{
				_localCharacter = null;
				_hasWeapon = false;
				CleanupLocalWeaponVisual();
			}
		}
		catch (Exception)
		{
		}
	}

	private void TryEnsureLocalWeaponGrant(Character localCharacter)
	{
		if ((Object)localCharacter == (Object)null || localCharacter.isBot || localCharacter.isZombie || !IsGameplayScene(SceneManager.GetActiveScene()) || !IsWeaponFeatureEnabled() || !IsAlive(localCharacter))
		{
			return;
		}
		if ((Object)(localCharacter.refs?.view) != (Object)null && !localCharacter.refs.view.IsMine)
		{
			return;
		}
		int characterGrantTrackingId = GetCharacterGrantTrackingId(localCharacter);
		if (characterGrantTrackingId == int.MinValue)
		{
			return;
		}
		if (CharacterAlreadyHasShootZombiesWeapon(localCharacter))
		{
			MarkWeaponGrantedForCharacter(localCharacter);
			return;
		}
	}

	private void EnsureLocalWeaponVisual(Item heldBlowgunItem)
	{
		if (!EnableLocalWeaponVisualFollower || (Object)_localCharacter == (Object)null || !HasAkVisualPrefab())
		{
			CleanupLocalWeaponVisual();
			return;
		}
		if ((Object)heldBlowgunItem == (Object)null)
		{
			if (Time.time - _lastHeldWeaponSeenTime > 4f)
			{
				CleanupLocalWeaponVisual();
			}
			return;
		}
		int instanceID = ((Object)heldBlowgunItem).GetInstanceID();
		if ((Object)_localWeaponVisualRoot == (Object)null || (Object)_localWeaponVisualModel == (Object)null)
		{
			if (!CreateLocalWeaponVisualInstance(heldBlowgunItem))
			{
				LogDiagnosticOnce("local-weapon-create-fail:" + instanceID, "CreateLocalWeaponVisualInstance failed: prefab=" + DescribeAkVisualPrefabForDiagnostics());
				return;
			}
		}
		else if (_pendingLocalWeaponVisualModelRefresh)
		{
			Transform val4 = ItemPatch.ResolveTargetVisualRootForItem(heldBlowgunItem);
			if ((Object)val4 == (Object)null)
			{
				val4 = ((Component)heldBlowgunItem).transform;
			}
			if (!RebuildLocalWeaponVisualModel(heldBlowgunItem, val4))
			{
				LogDiagnosticOnce("local-weapon-model-refresh-fail:" + instanceID, "RebuildLocalWeaponVisualModel failed, falling back to full recreate: prefab=" + DescribeAkVisualPrefabForDiagnostics());
				CleanupLocalWeaponVisual();
				if (!CreateLocalWeaponVisualInstance(heldBlowgunItem))
				{
					LogDiagnosticOnce("local-weapon-create-fail:" + instanceID, "CreateLocalWeaponVisualInstance failed after model refresh fallback: prefab=" + DescribeAkVisualPrefabForDiagnostics());
					return;
				}
			}
		}
		_localWeaponSourceItemId = instanceID;
		((Component)_localWeaponVisualRoot).gameObject.SetActive(true);
		Transform val = ResolveViewAnchor();
		if ((Object)val == (Object)null)
		{
			val = (((Object)(_localCharacter?.refs?.view) != (Object)null) ? ((Component)_localCharacter.refs.view).transform : ((Component)_localCharacter).transform);
		}
		if ((Object)val == (Object)null)
		{
			CleanupLocalWeaponVisual();
			return;
		}
		if ((Object)(object)_localWeaponVisualRoot.parent != (Object)(object)val || (Object)_weaponVisualOwner != (Object)_localCharacter)
		{
			_localWeaponVisualRoot.SetParent(val, false);
			_weaponVisualOwner = _localCharacter;
		}
		Transform val2 = ItemPatch.ResolveTargetVisualRootForItem(heldBlowgunItem);
		Transform val3 = ResolveLocalHeldWeaponAnchor(heldBlowgunItem, val2);
		if ((Object)val3 != (Object)null)
		{
			AttachLocalWeaponVisualToView(val3, val);
		}
		else
		{
			ApplyStableLocalWeaponViewPose();
		}
		ApplyLocalWeaponModelPose();
		int num = ResolveLocalWeaponVisibleLayer(val, heldBlowgunItem, val3, val2);
		EnsureLocalVisualRenderers(num);
		ForceEnableAllLocalRenderers();
		SuppressLocalFirstPersonHandRenderers();
		PreventLocalWeaponCameraClipping(val);
		LogLocalWeaponVisualState("ensure", heldBlowgunItem, val, val2, num);
		UpdateLocalWeaponMuzzle();
	}

	private Transform ResolveLocalHeldWeaponAnchor(Item heldBlowgunItem, Transform targetVisualRootForItem)
	{
		if ((Object)heldBlowgunItem != (Object)null)
		{
			Transform transform = ((Component)heldBlowgunItem).transform.Find("Blowgun");
			if ((Object)transform != (Object)null)
			{
				return transform;
			}
		}
		if ((Object)targetVisualRootForItem != (Object)null)
		{
			if (!((Object)heldBlowgunItem != (Object)null) || !((Object)(object)targetVisualRootForItem == (Object)(object)((Component)heldBlowgunItem).transform))
			{
				return targetVisualRootForItem;
			}
		}
		if ((Object)heldBlowgunItem != (Object)null && (Object)heldBlowgunItem.mainRenderer != (Object)null)
		{
			return ((Component)heldBlowgunItem.mainRenderer).transform;
		}
		if (!((Object)heldBlowgunItem != (Object)null))
		{
			return null;
		}
		return ((Component)heldBlowgunItem).transform;
	}

	private Transform ResolveViewAnchor()
	{
		if ((Object)_localCharacter != (Object)null)
		{
			Transform val = FindBestCameraAnchor(((Component)_localCharacter).transform);
			if ((Object)val != (Object)null)
			{
				return val;
			}
		}
		if (TryFindBestGlobalCameraAnchor(out var cameraTransform))
		{
			return cameraTransform;
		}
		if ((Object)(_localCharacter?.refs?.view) != (Object)null)
		{
			return ((Component)_localCharacter.refs.view).transform;
		}
		if (!((Object)_localCharacter != (Object)null))
		{
			return null;
		}
		return ((Component)_localCharacter).transform;
	}

	private static void SuppressLocalFirstPersonHandRenderers()
	{
		Transform localFirstPersonRenderRoot = GetLocalFirstPersonRenderRoot();
		if ((Object)localFirstPersonRenderRoot == (Object)null)
		{
			return;
		}
		Renderer[] componentsInChildren = ((Component)localFirstPersonRenderRoot).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if (!IsSuppressibleLocalFirstPersonHandRenderer(val))
			{
				continue;
			}
			val.enabled = false;
			val.forceRenderingOff = true;
			val.allowOcclusionWhenDynamic = false;
		}
		_localFirstPersonHandsSuppressed = true;
	}

	private static void RestoreLocalFirstPersonHandRenderers()
	{
		if (!_localFirstPersonHandsSuppressed)
		{
			return;
		}
		Transform localFirstPersonRenderRoot = GetLocalFirstPersonRenderRoot();
		if ((Object)localFirstPersonRenderRoot != (Object)null)
		{
			Renderer[] componentsInChildren = ((Component)localFirstPersonRenderRoot).GetComponentsInChildren<Renderer>(true);
			foreach (Renderer val in componentsInChildren)
			{
				if (!IsSuppressibleLocalFirstPersonHandRenderer(val))
				{
					continue;
				}
				val.enabled = true;
				val.forceRenderingOff = false;
				val.allowOcclusionWhenDynamic = false;
			}
		}
		_localFirstPersonHandsSuppressed = false;
	}

	private static Transform GetLocalFirstPersonRenderRoot()
	{
		if ((Object)(_localCharacter?.refs?.view) != (Object)null)
		{
			return ((Component)_localCharacter.refs.view).transform;
		}
		Character localCharacter = Character.localCharacter;
		if ((Object)(localCharacter?.refs?.view) != (Object)null)
		{
			return ((Component)localCharacter.refs.view).transform;
		}
		return null;
	}

	private static bool IsSuppressibleLocalFirstPersonHandRenderer(Renderer renderer)
	{
		if ((Object)renderer == (Object)null)
		{
			return false;
		}
		if ((Object)_localWeaponVisualRoot != (Object)null && ((Component)renderer).transform.IsChildOf(_localWeaponVisualRoot))
		{
			return false;
		}
		string text = ((((Object)renderer).name ?? string.Empty) + "/" + (FormatTransformPath(((Component)renderer).transform) ?? string.Empty)).ToLowerInvariant();
		string[] localFirstPersonHandSuppressionKeywords = LocalFirstPersonHandSuppressionKeywords;
		foreach (string value in localFirstPersonHandSuppressionKeywords)
		{
			if (!string.IsNullOrWhiteSpace(value) && text.Contains(value))
			{
				return true;
			}
		}
		return false;
	}

	private static Vector3 GetInverseLossyScale(Transform target)
	{
		if ((Object)target == (Object)null)
		{
			return Vector3.one;
		}
		Vector3 lossyScale = target.lossyScale;
		float num = 1f / Mathf.Max(Mathf.Abs(lossyScale.x), 0.0001f);
		float num2 = 1f / Mathf.Max(Mathf.Abs(lossyScale.y), 0.0001f);
		float num3 = 1f / Mathf.Max(Mathf.Abs(lossyScale.z), 0.0001f);
		return new Vector3(num, num2, num3);
	}

	private static Vector3 GetRelativeLossyScale(Transform source, Transform targetParent)
	{
		if ((Object)source == (Object)null || (Object)targetParent == (Object)null)
		{
			return Vector3.one;
		}
		Vector3 lossyScale = source.lossyScale;
		Vector3 lossyScale2 = targetParent.lossyScale;
		return new Vector3(SafeAxisScaleRatio(lossyScale.x, lossyScale2.x), SafeAxisScaleRatio(lossyScale.y, lossyScale2.y), SafeAxisScaleRatio(lossyScale.z, lossyScale2.z));
	}

	private static float SafeAxisScaleRatio(float sourceAxis, float parentAxis)
	{
		if (Mathf.Abs(parentAxis) < 0.0001f)
		{
			return 1f;
		}
		return sourceAxis / parentAxis;
	}

	private static Vector3 GetEffectiveLocalWeaponModelScale()
	{
		float weaponModelScale = GetWeaponModelScale() * LocalWeaponModelBaseScaleMultiplier;
		return ClampLocalWeaponScale(new Vector3(_localWeaponModelBaseScale.x * weaponModelScale, _localWeaponModelBaseScale.y * weaponModelScale, _localWeaponModelBaseScale.z * weaponModelScale));
	}

	private static void ApplyStableLocalWeaponViewPose()
	{
		if (!((Object)_localWeaponVisualRoot == (Object)null))
		{
			_localWeaponVisualRoot.localPosition = _localWeaponOffset;
			_localWeaponVisualRoot.localRotation = Quaternion.Euler(_localWeaponEuler);
			_localWeaponVisualRoot.localScale = _localWeaponRootScale;
		}
	}

	private static void ApplyLocalWeaponModelPose()
	{
		if (!((Object)_localWeaponVisualModel == (Object)null))
		{
			_localWeaponVisualModel.localPosition = _localWeaponModelBasePosition + _localWeaponModelBaseRotation * GetConfiguredLocalWeaponModelOffset();
			_localWeaponVisualModel.localRotation = _localWeaponModelBaseRotation * Quaternion.Euler(GetConfiguredLocalWeaponModelEuler() + _localWeaponViewEulerOffset);
			_localWeaponVisualModel.localScale = GetEffectiveLocalWeaponModelScale();
		}
	}

	private static void NormalizeLocalWeaponScaleForFirstPersonView(Transform visualModel)
	{
		if ((Object)visualModel == (Object)null || !TryGetCombinedRendererBounds(visualModel, out var bounds))
		{
			return;
		}
		float num = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
		if (!IsFiniteFloat(num) || num < 0.0005f)
		{
			return;
		}
		float num2 = Mathf.Clamp(LocalWeaponFirstPersonFinalTargetSize / Mathf.Max(LocalWeaponModelBaseScaleMultiplier, 0.01f), LocalWeaponFirstPersonMinBaseSize, LocalWeaponFirstPersonMaxBaseSize);
		float num3 = Mathf.Clamp(num2 / num, 0.05f, LocalWeaponModelScaleClampMax);
		if (!IsFiniteFloat(num3) || Mathf.Abs(num3 - 1f) <= 0.01f)
		{
			return;
		}
		Vector3 localScale = visualModel.localScale;
		visualModel.localScale = ClampLocalWeaponScale(new Vector3(Mathf.Abs(localScale.x * num3), Mathf.Abs(localScale.y * num3), Mathf.Abs(localScale.z * num3)));
	}

	private static Vector3 ClampLocalWeaponScale(Vector3 scale)
	{
		return new Vector3(ClampLocalWeaponScaleAxis(scale.x), ClampLocalWeaponScaleAxis(scale.y), ClampLocalWeaponScaleAxis(scale.z));
	}

	private static float ClampLocalWeaponScaleAxis(float value)
	{
		if (!IsFiniteFloat(value))
		{
			return 1f;
		}
		return Mathf.Clamp(Mathf.Abs(value), LocalWeaponModelScaleClampMin, LocalWeaponModelScaleClampMax);
	}

	private static void PreventLocalWeaponCameraClipping(Transform viewAnchor)
	{
		if ((Object)_localWeaponVisualRoot == (Object)null || (Object)_localWeaponVisualModel == (Object)null || (Object)viewAnchor == (Object)null)
		{
			return;
		}
		Camera val = ResolveCameraForAnchor(viewAnchor);
		if ((Object)val == (Object)null || !TryGetCombinedRendererBounds(_localWeaponVisualModel, out var bounds))
		{
			return;
		}
		float boundsClosestDepth = GetBoundsClosestDepth(bounds, viewAnchor);
		if (!IsFiniteFloat(boundsClosestDepth))
		{
			return;
		}
		float num = Mathf.Max(val.nearClipPlane + 0.06f, 0.18f);
		if (boundsClosestDepth >= num)
		{
			return;
		}
		float num2 = Mathf.Clamp(num - boundsClosestDepth + 0.02f, 0.01f, 1.6f);
		Vector3 localPosition = _localWeaponVisualRoot.localPosition;
		localPosition.z += num2;
		_localWeaponVisualRoot.localPosition = localPosition;
		float boundsClosestDepth2 = boundsClosestDepth;
		if (TryGetCombinedRendererBounds(_localWeaponVisualModel, out var bounds2))
		{
			float boundsClosestDepth3 = GetBoundsClosestDepth(bounds2, viewAnchor);
			if (IsFiniteFloat(boundsClosestDepth3))
			{
				boundsClosestDepth2 = boundsClosestDepth3;
			}
		}
		LogDiagnosticOnce("local-weapon-clip-fix:" + GetCurrentWeaponSelection() + ":" + ((Object)_localWeaponVisualRoot).GetInstanceID(), $"Adjusted local weapon depth to avoid clipping: selection={GetCurrentWeaponSelection()}, nearClip={val.nearClipPlane:0.###}, before={boundsClosestDepth:0.###}, after={boundsClosestDepth2:0.###}, delta={num2:0.###}, rootLocalPos={_localWeaponVisualRoot.localPosition}");
	}
 
	private static Camera ResolveCameraForAnchor(Transform viewAnchor)
	{
		Camera val = null;
		if ((Object)viewAnchor != (Object)null)
		{
			val = ((Component)viewAnchor).GetComponent<Camera>() ?? ((Component)viewAnchor).GetComponentInParent<Camera>();
		}
		if ((Object)val == (Object)null)
		{
			val = Camera.main;
		}
		return val;
	}

	private static bool TryGetCombinedRendererBounds(Transform root, out Bounds bounds)
	{
		bounds = default(Bounds);
		if ((Object)root == (Object)null)
		{
			return false;
		}
		Renderer[] componentsInChildren = ((Component)root).GetComponentsInChildren<Renderer>(true);
		bool flag = false;
		Renderer[] array = componentsInChildren;
		foreach (Renderer val in array)
		{
			if ((Object)val == (Object)null || !val.enabled)
			{
				continue;
			}
			if (!flag)
			{
				bounds = val.bounds;
				flag = true;
			}
			else
			{
				bounds.Encapsulate(val.bounds);
			}
		}
		return flag;
	}

	private static string DescribeLocalMaterialStateForDiagnostics(Material material)
	{
		if ((Object)material == (Object)null)
		{
			return "null";
		}
		Texture val = null;
		if (material.HasProperty("_BaseMap"))
		{
			val = material.GetTexture("_BaseMap");
		}
		if ((Object)val == (Object)null && material.HasProperty("_MainTex"))
		{
			val = material.GetTexture("_MainTex");
		}
		string text = (material.HasProperty("_Cull") ? material.GetFloat("_Cull").ToString("0.###", CultureInfo.InvariantCulture) : "-");
		return $"{((Object)material).name}[shader={material.shader?.name ?? "null"},rq={material.renderQueue},cull={text},tex={(((Object)val != (Object)null) ? ((Object)val).name : "null")}]";
	}

	private static string DescribeLocalRendererStateForDiagnostics(Renderer renderer)
	{
		if ((Object)renderer == (Object)null)
		{
			return "null";
		}
		MeshFilter component = ((Component)renderer).GetComponent<MeshFilter>();
		Mesh val = ((component != null) ? component.sharedMesh : null);
		SkinnedMeshRenderer val2 = (SkinnedMeshRenderer)(object)((renderer is SkinnedMeshRenderer) ? renderer : null);
		if ((Object)val == (Object)null && val2 != null)
		{
			val = val2.sharedMesh;
		}
		Material[] array = renderer.sharedMaterials ?? Array.Empty<Material>();
		string text = string.Join(" | ", array.Take(4).Select(DescribeLocalMaterialStateForDiagnostics));
		if (array.Length > 4)
		{
			text += $" | ...+{array.Length - 4}";
		}
		string text2 = (((Object)val != (Object)null) ? ((Object)val).name : "null");
		int num = (((Object)val != (Object)null) ? val.subMeshCount : 0);
		int num2 = (((Object)val != (Object)null) ? val.vertexCount : 0);
		return $"{FormatTransformPath(((Component)renderer).transform)}[enabled={renderer.enabled},forceOff={renderer.forceRenderingOff},active={((Component)renderer).gameObject.activeInHierarchy},layer={((Component)renderer).gameObject.layer},mesh={text2},subMeshes={num},verts={num2},mats={text}]";
	}

	private static string DescribeLocalRendererCollectionForDiagnostics(Transform root, int maxCount = 10)
	{
		if ((Object)root == (Object)null)
		{
			return "none";
		}
		Renderer[] componentsInChildren = ((Component)root).GetComponentsInChildren<Renderer>(true);
		List<string> list = componentsInChildren.Where((Renderer r) => (Object)r != (Object)null).Take(maxCount).Select(DescribeLocalRendererStateForDiagnostics).ToList();
		if (list.Count == 0)
		{
			return "none";
		}
		if (componentsInChildren.Length > list.Count)
		{
			list.Add("...+" + (componentsInChildren.Length - list.Count));
		}
		return string.Join(" || ", list);
	}

	private static string DescribeLocalBoundsForDiagnostics(Transform root)
	{
		if (!TryGetCombinedRendererBounds(root, out var bounds))
		{
			return "none";
		}
		return $"center={bounds.center},size={bounds.size}";
	}

	private void LogLocalWeaponVisualState(string phase, Item sourceItem, Transform viewAnchor, Transform targetVisualRoot, int resolvedLayer)
	{
		if ((Object)_localWeaponVisualRoot == (Object)null || (Object)_localWeaponVisualModel == (Object)null)
		{
			return;
		}
		Camera val = ResolveCameraForAnchor(viewAnchor);
		int instanceID = (((Object)sourceItem != (Object)null) ? ((Object)sourceItem).GetInstanceID() : 0);
		string text = "null";
		if ((Object)sourceItem != (Object)null)
		{
			string text2 = ((((Object)sourceItem.holderCharacter != (Object)null) ? ((Object)sourceItem.holderCharacter).name : null) ?? ((((Object)sourceItem.trueHolderCharacter != (Object)null) ? ((Object)sourceItem.trueHolderCharacter).name : null) ?? "null"));
			text = $"name={((Object)sourceItem).name},id={sourceItem.itemID},state={(int)sourceItem.itemState},holder={text2}";
		}
		LogDiagnosticOnce("local-weapon-state:" + phase + ":" + GetCurrentWeaponSelection() + ":" + instanceID + ":" + ((Object)_localWeaponVisualRoot).GetInstanceID(), $"Local weapon visual state: phase={phase}, selection={GetCurrentWeaponSelection()}, item={text}, view={FormatTransformPath(viewAnchor)}, targetRoot={FormatTransformPath(targetVisualRoot)}, camera={(((Object)val != (Object)null) ? ((Object)val).name : "null")}, cameraLayer={(((Object)val != (Object)null) ? ((Component)val).gameObject.layer : (-1))}, cullingMask={(((Object)val != (Object)null) ? val.cullingMask : 0)}, resolvedLayer={resolvedLayer}, rootPath={FormatTransformPath(_localWeaponVisualRoot)}, rootLocalPos={_localWeaponVisualRoot.localPosition}, rootLocalRot={_localWeaponVisualRoot.localRotation.eulerAngles}, rootLocalScale={_localWeaponVisualRoot.localScale}, modelPath={FormatTransformPath(_localWeaponVisualModel)}, modelLocalPos={_localWeaponVisualModel.localPosition}, modelLocalRot={_localWeaponVisualModel.localRotation.eulerAngles}, modelLocalScale={_localWeaponVisualModel.localScale}, rootBounds={DescribeLocalBoundsForDiagnostics(_localWeaponVisualRoot)}, modelBounds={DescribeLocalBoundsForDiagnostics(_localWeaponVisualModel)}, renderers={DescribeLocalRendererCollectionForDiagnostics(_localWeaponVisualRoot)}");
	}

	private static float GetBoundsClosestDepth(Bounds bounds, Transform anchor)
	{
		if ((Object)anchor == (Object)null)
		{
			return float.PositiveInfinity;
		}
		Vector3 center = bounds.center;
		Vector3 extents = bounds.extents;
		Vector3[] array = new Vector3[8]
		{
			center + new Vector3(0f - extents.x, 0f - extents.y, 0f - extents.z),
			center + new Vector3(0f - extents.x, 0f - extents.y, extents.z),
			center + new Vector3(0f - extents.x, extents.y, 0f - extents.z),
			center + new Vector3(0f - extents.x, extents.y, extents.z),
			center + new Vector3(extents.x, 0f - extents.y, 0f - extents.z),
			center + new Vector3(extents.x, 0f - extents.y, extents.z),
			center + new Vector3(extents.x, extents.y, 0f - extents.z),
			center + new Vector3(extents.x, extents.y, extents.z)
		};
		float num = float.PositiveInfinity;
		for (int i = 0; i < array.Length; i++)
		{
			Vector3 val = anchor.InverseTransformPoint(array[i]);
			if (IsFiniteVector(val))
			{
				num = Mathf.Min(num, val.z);
			}
		}
		return num;
	}

	private void AttachLocalWeaponVisualToView(Transform sourceAnchor, Transform viewAnchor)
	{
		if (!((Object)_localWeaponVisualRoot == (Object)null) && !((Object)viewAnchor == (Object)null))
		{
			if ((Object)(object)_localWeaponVisualRoot.parent != (Object)(object)viewAnchor || (Object)_weaponVisualOwner != (Object)_localCharacter)
			{
				_localWeaponVisualRoot.SetParent(viewAnchor, false);
				_weaponVisualOwner = _localCharacter;
			}
			if (!TryApplySourceAnchorRelativePose(sourceAnchor, viewAnchor))
			{
				ApplyStableLocalWeaponViewPose();
			}
		}
	}

	private bool TryApplySourceAnchorRelativePose(Transform sourceAnchor, Transform viewAnchor)
	{
		if ((Object)_localWeaponVisualRoot == (Object)null || (Object)sourceAnchor == (Object)null || (Object)viewAnchor == (Object)null)
		{
			return false;
		}
		Vector3 val = viewAnchor.InverseTransformPoint(sourceAnchor.position);
		if (!IsFiniteVector(val))
		{
			return false;
		}
		bool flag = IsReasonableHeldVisualPose(val);
		if (PreferStableLocalFirstPersonPose || !flag)
		{
			LogDiagnosticOnce("local-weapon-source-anchor-stable-fallback:" + ((Object)_localWeaponVisualRoot).GetInstanceID(), $"Skipped source anchor local pose and used stable first-person pose: source={FormatTransformPath(sourceAnchor)}, view={FormatTransformPath(viewAnchor)}, sourceLocalPos={val}, preferStable={PreferStableLocalFirstPersonPose}, poseReasonable={flag}");
			return false;
		}
		Vector3 val2 = val;
		Quaternion quaternion = Quaternion.Inverse(viewAnchor.rotation) * sourceAnchor.rotation;
		if (!IsFiniteQuaternion(quaternion))
		{
			quaternion = Quaternion.identity;
		}
		Vector3 relativeLossyScale = GetRelativeLossyScale(sourceAnchor, viewAnchor);
		Vector3 val3 = Vector3.one;
		_localWeaponVisualRoot.localPosition = val2;
		_localWeaponVisualRoot.localRotation = quaternion;
		_localWeaponVisualRoot.localScale = val3;
		LogDiagnosticOnce("local-weapon-source-anchor-pose:" + ((Object)_localWeaponVisualRoot).GetInstanceID(), $"Applied local weapon source anchor pose: source={FormatTransformPath(sourceAnchor)}, view={FormatTransformPath(viewAnchor)}, sourceLocalPos={val}, appliedLocalPos={val2}, localRot={quaternion.eulerAngles}, sourceScale={relativeLossyScale}, appliedScale={val3}, poseMode={(flag ? "direct" : "clamped")}");
		return true;
	}

	private static bool IsReasonableHeldVisualPose(Vector3 localPosition)
	{
		if (!IsFiniteVector(localPosition))
		{
			return false;
		}
		if (localPosition.z < 0.05f || localPosition.z > 1.45f)
		{
			return false;
		}
		if (Mathf.Abs(localPosition.x) > 0.8f || Mathf.Abs(localPosition.y) > 0.8f)
		{
			return false;
		}
		return true;
	}

	private static Vector3 SanitizeHeldWeaponLocalPosition(Vector3 localPosition)
	{
		if (!IsFiniteVector(localPosition))
		{
			return new Vector3(0f, 0f, 0.22f);
		}
		return new Vector3(Mathf.Clamp(localPosition.x, -0.85f, 0.85f), Mathf.Clamp(localPosition.y, -0.85f, 0.85f), Mathf.Clamp(localPosition.z, 0.18f, 1.45f));
	}

	private static bool IsFiniteVector(Vector3 value)
	{
		if (IsFiniteFloat(value.x) && IsFiniteFloat(value.y))
		{
			return IsFiniteFloat(value.z);
		}
		return false;
	}

	private static bool IsFiniteQuaternion(Quaternion value)
	{
		if (IsFiniteFloat(value.x) && IsFiniteFloat(value.y) && IsFiniteFloat(value.z))
		{
			return IsFiniteFloat(value.w);
		}
		return false;
	}

	private static bool IsFiniteFloat(float value)
	{
		if (!float.IsNaN(value))
		{
			return !float.IsInfinity(value);
		}
		return false;
	}

	private bool CreateLocalWeaponVisualInstance(Item sourceItem)
	{
		CleanupLocalWeaponVisual();
		if (!HasAkVisualPrefab() || (Object)sourceItem == (Object)null)
		{
			return false;
		}
		GameObject val = new GameObject("AK47_LocalVisualRoot");
		_localWeaponVisualRoot = val.transform;
		val.SetActive(false);
		Transform val2 = ItemPatch.ResolveTargetVisualRootForItem(sourceItem);
		if ((Object)val2 == (Object)null)
		{
			val2 = ((Component)sourceItem).transform;
		}
		if (!RebuildLocalWeaponVisualModel(sourceItem, val2))
		{
			CleanupLocalWeaponVisual();
			return false;
		}
		LogDiagnosticOnce("local-weapon-create-success:" + ((Object)sourceItem).GetInstanceID(), $"Created local weapon visual: model={FormatTransformPath(_localWeaponVisualModel)}, targetRoot={FormatTransformPath(val2)}, prefab={DescribeAkVisualPrefabForDiagnostics()}");
		Transform val4 = ResolveViewAnchor();
		if ((Object)val4 == (Object)null)
		{
			val4 = ((Component)sourceItem).transform;
		}
		if ((Object)val4 != (Object)null)
		{
			_localWeaponVisualRoot.SetParent(val4, false);
			_weaponVisualOwner = _localCharacter;
			if ((Object)((Component)val4).GetComponent<Camera>() != (Object)null || (Object)((Component)val4).GetComponentInParent<Camera>() != (Object)null)
			{
				Transform val5 = ResolveLocalHeldWeaponAnchor(sourceItem, val2);
				if (!TryApplySourceAnchorRelativePose(val5, val4))
				{
					ApplyStableLocalWeaponViewPose();
				}
			}
		}
		int layer = ResolveLocalWeaponVisibleLayer(val4, sourceItem, val2, ItemPatch.TryGetMuzzleMarker(sourceItem));
		EnsureLocalVisualRenderers(layer);
		ForceEnableAllLocalRenderers();
		PreventLocalWeaponCameraClipping(val4);
		LogLocalWeaponVisualState("create", sourceItem, val4, val2, layer);
		_localWeaponSourceItemId = ((Object)sourceItem).GetInstanceID();
		val.SetActive(true);
		UpdateLocalWeaponMuzzle();
		return true;
	}

	private bool RebuildLocalWeaponVisualModel(Item sourceItem, Transform targetVisualRoot)
	{
		if ((Object)_localWeaponVisualRoot == (Object)null || (Object)sourceItem == (Object)null)
		{
			return false;
		}
		if ((Object)targetVisualRoot == (Object)null)
		{
			targetVisualRoot = ((Component)sourceItem).transform;
		}
		if ((Object)_localWeaponVisualModel != (Object)null)
		{
			Object.Destroy((Object)((Component)_localWeaponVisualModel).gameObject);
			_localWeaponVisualModel = null;
		}
		_localWeaponMuzzle = null;
		_localWeaponModelUsesPreparedPose = false;
		_localWeaponVisualModel = CreateFirstPersonLocalWeaponModel(_localWeaponVisualRoot, targetVisualRoot);
		if ((Object)_localWeaponVisualModel == (Object)null)
		{
			GameObject val = new GameObject("AK47_LocalVisualModel");
			val.transform.SetParent(_localWeaponVisualRoot, false);
			_localWeaponVisualModel = val.transform;
		}
		if ((Object)_localWeaponVisualModel == (Object)null)
		{
			return false;
		}
		if (_localWeaponModelUsesPreparedPose)
		{
			BakeLocalWeaponSkinnedRenderers(((Component)_localWeaponVisualModel).gameObject);
		}
		SetActiveRecursively(_localWeaponVisualModel, active: true);
		StripLocalVisualBehaviours(((Component)_localWeaponVisualModel).gameObject);
		int num = CountMeshBackedRenderers(_localWeaponVisualModel);
		if (num == 0)
		{
			EnsureLocalVisualFallbackGeometry(_localWeaponVisualModel);
			num = CountMeshBackedRenderers(_localWeaponVisualModel);
		}
		else
		{
			CleanupLocalVisualFallbackGeometry(_localWeaponVisualModel);
		}
		if (num == 0)
		{
			if ((Object)_localWeaponVisualModel != (Object)null)
			{
				Object.Destroy((Object)((Component)_localWeaponVisualModel).gameObject);
				_localWeaponVisualModel = null;
			}
			_localWeaponMuzzle = null;
			return false;
		}
		NormalizeLocalWeaponScaleForFirstPersonView(_localWeaponVisualModel);
		_localWeaponModelBasePosition = _localWeaponVisualModel.localPosition;
		_localWeaponModelBaseRotation = _localWeaponVisualModel.localRotation;
		_localWeaponModelBaseScale = ClampLocalWeaponScale(_localWeaponVisualModel.localScale);
		ApplyLocalWeaponModelPose();
		_pendingLocalWeaponVisualModelRefresh = false;
		LogDiagnosticOnce("local-weapon-model-ready:" + ((Object)sourceItem).GetInstanceID() + ":" + GetCurrentWeaponSelection(), $"Prepared local weapon visual model: model={FormatTransformPath(_localWeaponVisualModel)}, renderers={num}, targetRoot={FormatTransformPath(targetVisualRoot)}, prefab={DescribeAkVisualPrefabForDiagnostics()}");
		return true;
	}

	private static Transform CreateFirstPersonLocalWeaponModel(Transform parent, Transform targetVisualRoot)
	{
		Transform val = CreateBakedLocalWeaponModel(parent);
		if ((Object)val != (Object)null)
		{
			return val;
		}
		val = CreateStandaloneLocalWeaponModel(parent);
		if ((Object)val != (Object)null)
		{
			return val;
		}
		return CreatePreparedLocalWeaponModel(parent, targetVisualRoot);
	}

	private static Transform CreateBakedLocalWeaponModel(Transform parent)
	{
		if ((Object)parent == (Object)null || !HasAkVisualPrefab())
		{
			return null;
		}
		GameObject akVisualPrefab = GetAkVisualPrefab();
		if ((Object)akVisualPrefab == (Object)null)
		{
			return null;
		}
		GameObject val = new GameObject("AK47_LocalVisualModel");
		val.transform.SetParent(parent, false);
		val.transform.localPosition = Vector3.zero;
		val.transform.localRotation = Quaternion.identity;
		val.transform.localScale = Vector3.one;
		int num = 0;
		Renderer[] componentsInChildren = akVisualPrefab.GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val2 in componentsInChildren)
		{
			if ((Object)val2 == (Object)null)
			{
				continue;
			}
			Mesh mesh = null;
			Material[] sharedMaterials = null;
			if (val2 is MeshRenderer)
			{
				MeshFilter component = ((Component)val2).GetComponent<MeshFilter>();
				mesh = ((component != null) ? component.sharedMesh : null);
				sharedMaterials = val2.sharedMaterials;
			}
			else
			{
				SkinnedMeshRenderer val3 = (SkinnedMeshRenderer)(object)((val2 is SkinnedMeshRenderer) ? val2 : null);
				if (val3 != null)
				{
					mesh = val3.sharedMesh;
					sharedMaterials = ((Renderer)val3).sharedMaterials;
				}
			}
			if ((Object)mesh == (Object)null || mesh.vertexCount <= 0)
			{
				continue;
			}
			string text = ((((Object)val2).name ?? string.Empty) + "/" + (FormatTransformPath(((Component)val2).transform) ?? string.Empty)).ToLowerInvariant();
			if (LocalWeaponExcludedRendererKeywords.Any((string keyword) => !string.IsNullOrWhiteSpace(keyword) && text.Contains(keyword)))
			{
				continue;
			}
			GameObject val4 = new GameObject(string.IsNullOrWhiteSpace(((Object)val2).name) ? ("AK47_LocalMesh_" + num) : ((Object)val2).name);
			val4.transform.SetParent(val.transform, false);
			if (!ItemPatch.TryGetTransformPoseRelativeToAkRoot(((Component)val2).transform, out var localPosition, out var localRotation, out var localScale))
			{
				localPosition = ((Component)val2).transform.localPosition;
				localRotation = ((Component)val2).transform.localRotation;
				localScale = ((Component)val2).transform.localScale;
			}
			val4.transform.localPosition = localPosition;
			val4.transform.localRotation = localRotation;
			val4.transform.localScale = localScale;
			MeshFilter val5 = val4.AddComponent<MeshFilter>();
			MeshRenderer obj = val4.AddComponent<MeshRenderer>();
			val5.sharedMesh = mesh;
			((Renderer)obj).sharedMaterials = BuildLocalViewMaterials(sharedMaterials, Mathf.Max(mesh.subMeshCount, 1));
			ItemPatch.NormalizeAkRenderer((Renderer)(object)obj);
			num++;
		}
		if (num == 0)
		{
			Object.Destroy((Object)val);
			return null;
		}
		_localWeaponModelUsesPreparedPose = false;
		return val.transform;
	}

	private static Transform CreatePreparedLocalWeaponModel(Transform parent, Transform targetVisualRoot)
	{
		if ((Object)parent == (Object)null || !HasAkVisualPrefab())
		{
			return null;
		}
		GameObject akVisualPrefab = GetAkVisualPrefab();
		if ((Object)akVisualPrefab == (Object)null)
		{
			return null;
		}
		GameObject val = Object.Instantiate<GameObject>(akVisualPrefab, parent, false);
		if ((Object)val == (Object)null)
		{
			return null;
		}
		_localWeaponModelUsesPreparedPose = true;
		((Object)val).name = "AK47_LocalVisualModel";
		val.transform.localPosition = Vector3.zero;
		val.transform.localRotation = Quaternion.identity;
		val.transform.localScale = Vector3.one;
		PrepareLocalWeaponVisualModel(val, targetVisualRoot);
		StripLocalVisualBehaviours(val);
		return val.transform;
	}

	private static Transform CreateStandaloneLocalWeaponModel(Transform parent)
	{
		if ((Object)parent == (Object)null || !HasAkVisualPrefab())
		{
			return null;
		}
		if (!TryResolveLocalStandaloneAkVisual(out var mesh, out var materials, out var localScale, out var localRotation, out var localPosition) || (Object)mesh == (Object)null)
		{
			return null;
		}
		_localWeaponModelUsesPreparedPose = false;
		GameObject val = new GameObject("AK47_LocalVisualModel");
		val.transform.SetParent(parent, false);
		val.transform.localPosition = Vector3.zero;
		val.transform.localRotation = Quaternion.identity;
		val.transform.localScale = Vector3.one;
		GameObject val2 = new GameObject("AK47_LocalVisualMesh");
		val2.transform.SetParent(val.transform, false);
		val2.transform.localPosition = localPosition;
		val2.transform.localRotation = localRotation;
		val2.transform.localScale = localScale;
		MeshFilter val3 = val2.AddComponent<MeshFilter>();
		MeshRenderer obj = val2.AddComponent<MeshRenderer>();
		val3.sharedMesh = mesh;
		((Renderer)obj).sharedMaterials = BuildLocalViewMaterials(materials, Mathf.Max(mesh.subMeshCount, 1));
		ItemPatch.NormalizeAkRenderer((Renderer)(object)obj);
		return val.transform;
	}

	private static Material[] BuildLocalViewMaterials(Material[] sourceMaterials, int subMeshCount)
	{
		subMeshCount = Mathf.Max(subMeshCount, 1);
		Material[] array = (Material[])(object)new Material[subMeshCount];
		for (int i = 0; i < subMeshCount; i++)
		{
			Material source = ((sourceMaterials != null && sourceMaterials.Length > 0) ? sourceMaterials[Mathf.Clamp(i, 0, sourceMaterials.Length - 1)] : null);
			array[i] = CreateVisibleLocalViewMaterial(source);
		}
		return array;
	}

	private static Material CreateVisibleLocalViewMaterial(Material source)
	{
		Shader val2 = ResolveLocalVisualCompatibleShader();
		Material val = new Material(((Object)val2 != (Object)null) ? val2 : GetLocalVisualFallbackMaterial().shader);
		((Object)val).name = "AK47_LocalViewMat";
		Texture val3 = null;
		string text = null;
		Color val4 = Color.white;
		if ((Object)source != (Object)null)
		{
			if (source.HasProperty("_BaseMap"))
			{
				val3 = source.GetTexture("_BaseMap");
				if ((Object)val3 != (Object)null)
				{
					text = "_BaseMap";
				}
			}
			if ((Object)val3 == (Object)null && source.HasProperty("_MainTex"))
			{
				val3 = source.GetTexture("_MainTex");
				if ((Object)val3 != (Object)null)
				{
					text = "_MainTex";
				}
			}
			if (source.HasProperty("_BaseColor"))
			{
				val4 = source.GetColor("_BaseColor");
			}
			else if (source.HasProperty("_Color"))
			{
				val4 = source.GetColor("_Color");
			}
		}
		val4.a = 1f;
		if (val.HasProperty("_BaseColor"))
		{
			val.SetColor("_BaseColor", val4);
		}
		if (val.HasProperty("_Color"))
		{
			val.SetColor("_Color", val4);
		}
		if ((Object)val3 != (Object)null)
		{
			if (val.HasProperty("_BaseMap"))
			{
				val.SetTexture("_BaseMap", val3);
				if (!string.IsNullOrWhiteSpace(text))
				{
					val.SetTextureScale("_BaseMap", source.GetTextureScale(text));
					val.SetTextureOffset("_BaseMap", source.GetTextureOffset(text));
				}
			}
			if (val.HasProperty("_MainTex"))
			{
				val.SetTexture("_MainTex", val3);
				if (!string.IsNullOrWhiteSpace(text))
				{
					val.SetTextureScale("_MainTex", source.GetTextureScale(text));
					val.SetTextureOffset("_MainTex", source.GetTextureOffset(text));
				}
			}
		}
		if (val.HasProperty("_Surface"))
		{
			val.SetFloat("_Surface", 0f);
		}
		if (val.HasProperty("_AlphaClip"))
		{
			val.SetFloat("_AlphaClip", 0f);
		}
		if (val.HasProperty("_Cutoff"))
		{
			val.SetFloat("_Cutoff", 0f);
		}
		if (val.HasProperty("_Cull"))
		{
			val.SetFloat("_Cull", 0f);
		}
		if (val.HasProperty("_Metallic"))
		{
			val.SetFloat("_Metallic", 0f);
		}
		if (val.HasProperty("_Glossiness"))
		{
			val.SetFloat("_Glossiness", 0.15f);
		}
		val.renderQueue = -1;
		return val;
	}

	private static bool TryResolveLocalStandaloneAkVisual(out Mesh mesh, out Material[] materials, out Vector3 localScale, out Quaternion localRotation, out Vector3 localPosition)
	{
		mesh = null;
		materials = null;
		localScale = Vector3.one;
		localRotation = Quaternion.identity;
		localPosition = Vector3.zero;
		if ((Object)_ak47Prefab == (Object)null)
		{
			return false;
		}
		Transform val = FindPreferredStandaloneAkMeshTransform(_ak47Prefab.transform);
		if ((Object)val != (Object)null)
		{
			MeshFilter component = ((Component)val).GetComponent<MeshFilter>();
			MeshRenderer component2 = ((Component)val).GetComponent<MeshRenderer>();
			mesh = ((component != null) ? component.sharedMesh : null);
			materials = ((component2 != null) ? ((Renderer)component2).sharedMaterials : null);
			if ((Object)mesh != (Object)null)
			{
				ItemPatch.TryGetTransformPoseRelativeToAkRoot(val, out localPosition, out localRotation, out localScale);
				return true;
			}
		}
		if (!ItemPatch.TryResolveBaseAkVisual(out mesh, out materials, out localScale, out localRotation, out localPosition, out var _))
		{
			return false;
		}
		return (Object)mesh != (Object)null;
	}

	private static Transform FindPreferredStandaloneAkMeshTransform(Transform root)
	{
		if ((Object)root == (Object)null)
		{
			return null;
		}
		Transform val = root.Find("Mesh") ?? root.Find("AK/Mesh");
		if (IsValidStandaloneAkMeshTransform(val))
		{
			return val;
		}
		MeshFilter[] componentsInChildren = ((Component)root).GetComponentsInChildren<MeshFilter>(true);
		Transform result = null;
		int num = int.MinValue;
		MeshFilter[] array = componentsInChildren;
		foreach (MeshFilter val2 in array)
		{
			if ((Object)val2 == (Object)null || (Object)val2.sharedMesh == (Object)null)
			{
				continue;
			}
			MeshRenderer component = ((Component)val2).GetComponent<MeshRenderer>();
			if (!((Object)component == (Object)null))
			{
				int standaloneAkMeshScore = GetStandaloneAkMeshScore(((Component)val2).transform, val2.sharedMesh, component);
				if (standaloneAkMeshScore > num)
				{
					num = standaloneAkMeshScore;
					result = ((Component)val2).transform;
				}
			}
		}
		return result;
	}

	private static bool IsValidStandaloneAkMeshTransform(Transform candidate)
	{
		if ((Object)candidate == (Object)null)
		{
			return false;
		}
		MeshFilter component = ((Component)candidate).GetComponent<MeshFilter>();
		MeshRenderer component2 = ((Component)candidate).GetComponent<MeshRenderer>();
		if ((Object)((component != null) ? component.sharedMesh : null) != (Object)null)
		{
			return (Object)component2 != (Object)null;
		}
		return false;
	}

	private static int GetStandaloneAkMeshScore(Transform candidate, Mesh mesh, MeshRenderer renderer)
	{
		if ((Object)candidate == (Object)null || (Object)mesh == (Object)null || (Object)renderer == (Object)null)
		{
			return int.MinValue;
		}
		string text = (((Object)candidate).name ?? string.Empty).ToLowerInvariant();
		string text2 = FormatTransformPath(candidate).ToLowerInvariant();
		WeaponVariantDefinition weaponVariantDefinition = GetWeaponVariantDefinition(GetCurrentWeaponSelection());
		int num2;
		int num = (num2 = mesh.vertexCount);
		if (text == "mesh" || text2.EndsWith("/mesh"))
		{
			num2 += 3000;
		}
		if (weaponVariantDefinition != null && weaponVariantDefinition.PrefabAliases != null && weaponVariantDefinition.PrefabAliases.Any((string alias) => !string.IsNullOrWhiteSpace(alias) && (MatchesWeaponAliasToken(text, new string[1] { alias }) || MatchesWeaponAliasToken(text2, new string[1] { alias }))))
		{
			num2 += 5200;
		}
		if (text2.Contains("/weapon/") || text2.Contains("/gun/") || text2.Contains("/rifle/") || text2.Contains("/smg/") || text2.Contains("/carbine/"))
		{
			num2 += 2000;
		}
		if (text.Contains("ak") || text.Contains("mpx") || text.Contains("hk") || text.Contains("weapon") || text.Contains("rifle") || text.Contains("gun") || text.Contains("smg") || text.Contains("carbine") || text.Contains("body") || text.Contains("main"))
		{
			num2 += 2400;
		}
		if (text.Contains("hand") || text2.Contains("/hand") || text.Contains("arm") || text.Contains("finger"))
		{
			num2 -= 20000;
		}
		if (text.Contains("bone") || text.Contains("helper") || text.Contains("socket") || text.Contains("locator"))
		{
			num2 -= 6000;
		}
		if (text.Contains("holiday") || text2.Contains("holiday"))
		{
			num2 -= 12000;
		}
		if (text.Contains("vfx") || text.Contains("effect") || text.Contains("spawn") || text.Contains("muzzle") || text.Contains("flash") || text.Contains("shell"))
		{
			num2 -= 8000;
		}
		if (num <= 64)
		{
			num2 -= 12000;
		}
		else if (num >= 400)
		{
			num2 += Mathf.Clamp(num / 3, 0, 6000);
		}
		if (((Renderer)renderer).sharedMaterials == null || ((Renderer)renderer).sharedMaterials.Length == 0)
		{
			num2 -= 3000;
		}
		else
		{
			num2 += Mathf.Clamp(((Renderer)renderer).sharedMaterials.Length * 120, 0, 600);
		}
		if (((Renderer)renderer).enabled)
		{
			num2 += 120;
		}
		return num2;
	}

	private static void PrepareLocalWeaponVisualModel(GameObject modelObject, Transform targetVisualRoot)
	{
		if (!((Object)modelObject == (Object)null))
		{
			BakeLocalWeaponSkinnedRenderers(modelObject);
			PrepareLocalWeaponRenderers(modelObject, targetVisualRoot);
		}
	}

	private static void PrepareLocalWeaponRenderers(GameObject root, Transform targetVisualRoot)
	{
		if ((Object)root == (Object)null)
		{
			return;
		}
		_ = targetVisualRoot;
		Renderer[] componentsInChildren = root.GetComponentsInChildren<Renderer>(true);
		bool flag = componentsInChildren.Any(IsRenderableLocalWeaponRenderer);
		Renderer[] array = componentsInChildren;
		foreach (Renderer val in array)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			Mesh localRendererMesh = GetLocalRendererMesh(val);
			bool flag2 = (Object)(object)localRendererMesh != (Object)null;
			bool flag3 = flag2 && (!flag || IsRenderableLocalWeaponRenderer(val));
			if (!flag3)
			{
				val.enabled = false;
				val.forceRenderingOff = true;
				continue;
			}
			val.enabled = true;
			val.forceRenderingOff = false;
			val.allowOcclusionWhenDynamic = false;
			val.shadowCastingMode = ShadowCastingMode.Off;
			val.receiveShadows = false;
			val.lightProbeUsage = LightProbeUsage.Off;
			val.reflectionProbeUsage = ReflectionProbeUsage.Off;
			SkinnedMeshRenderer val2 = (SkinnedMeshRenderer)(object)((val is SkinnedMeshRenderer) ? val : null);
			if (val2 != null)
			{
				val2.updateWhenOffscreen = true;
			}
			int subMeshCount = Mathf.Max(localRendererMesh.subMeshCount, 1);
			val.sharedMaterials = BuildLocalViewMaterials(val.sharedMaterials, subMeshCount);
		}
	}

	private static bool IsRenderableLocalWeaponRenderer(Renderer renderer)
	{
		Mesh localRendererMesh = GetLocalRendererMesh(renderer);
		if ((Object)(object)localRendererMesh == (Object)null || localRendererMesh.vertexCount <= 0)
		{
			return false;
		}
		string text = ((((Object)renderer).name ?? string.Empty) + "/" + (FormatTransformPath(((Component)renderer).transform) ?? string.Empty)).ToLowerInvariant();
		string[] localWeaponExcludedRendererKeywords = LocalWeaponExcludedRendererKeywords;
		foreach (string value in localWeaponExcludedRendererKeywords)
		{
			if (text.Contains(value))
			{
				return false;
			}
		}
		return true;
	}

	private static Mesh GetLocalRendererMesh(Renderer renderer)
	{
		if ((Object)renderer == (Object)null)
		{
			return null;
		}
		MeshFilter component = ((Component)renderer).GetComponent<MeshFilter>();
		Mesh val = ((component != null) ? component.sharedMesh : null);
		if ((Object)(object)val != (Object)null)
		{
			return val;
		}
		SkinnedMeshRenderer val2 = (SkinnedMeshRenderer)(object)((renderer is SkinnedMeshRenderer) ? renderer : null);
		if (val2 != null)
		{
			return val2.sharedMesh;
		}
		return null;
	}

	private static void BakeLocalWeaponSkinnedRenderers(GameObject root)
	{
		if ((Object)root == (Object)null)
		{
			return;
		}
		SkinnedMeshRenderer[] componentsInChildren = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		int num = 0;
		SkinnedMeshRenderer[] array = componentsInChildren;
		foreach (SkinnedMeshRenderer val in array)
		{
			if ((Object)val == (Object)null || (Object)val.sharedMesh == (Object)null)
			{
				continue;
			}
			try
			{
				Mesh val2 = new Mesh();
				((Object)val2).name = (((Object)val.sharedMesh).name ?? ((Object)val).name) + "_Baked";
				val.BakeMesh(val2);
				MeshFilter val3 = ((Component)val).GetComponent<MeshFilter>();
				if ((Object)val3 == (Object)null)
				{
					val3 = ((Component)val).gameObject.AddComponent<MeshFilter>();
				}
				MeshRenderer val4 = ((Component)val).GetComponent<MeshRenderer>();
				if ((Object)val4 == (Object)null)
				{
					val4 = ((Component)val).gameObject.AddComponent<MeshRenderer>();
				}
				val3.sharedMesh = val2;
				int num2 = Mathf.Max(val2.subMeshCount, 1);
				Material[] array2 = ((Renderer)val).sharedMaterials?.ToArray() ?? Array.Empty<Material>();
				if (array2.Length == 0)
				{
					array2 = (Material[])(object)new Material[1] { GetLocalVisualFallbackMaterial() };
				}
				if (array2.Length != num2)
				{
					Material[] array3 = (Material[])(object)new Material[num2];
					for (int j = 0; j < num2; j++)
					{
						Material val5 = ((j < array2.Length) ? array2[j] : null);
						array3[j] = (((Object)val5 != (Object)null) ? val5 : array2[Mathf.Clamp(array2.Length - 1, 0, array2.Length - 1)]);
						if ((Object)array3[j] == (Object)null)
						{
							array3[j] = GetLocalVisualFallbackMaterial();
						}
					}
					array2 = array3;
				}
				((Renderer)val4).sharedMaterials = array2;
				((Renderer)val4).enabled = ((Renderer)val).enabled;
				((Renderer)val4).forceRenderingOff = ((Renderer)val).forceRenderingOff;
				((Renderer)val4).allowOcclusionWhenDynamic = ((Renderer)val).allowOcclusionWhenDynamic;
				((Renderer)val4).shadowCastingMode = ((Renderer)val).shadowCastingMode;
				((Renderer)val4).receiveShadows = ((Renderer)val).receiveShadows;
				Object.DestroyImmediate((Object)val);
				num++;
			}
			catch (Exception ex)
			{
				Log.LogWarning((object)("[ShootZombies] BakeLocalWeaponSkinnedRenderers failed on " + ((Object)val).name + ": " + ex.Message));
			}
		}
	}

	private static void EnsureLocalVisualRenderers(int layer)
	{
		if ((Object)_localWeaponVisualRoot == (Object)null)
		{
			return;
		}
		SetLayerRecursively(_localWeaponVisualRoot, layer);
		Renderer[] componentsInChildren = ((Component)_localWeaponVisualRoot).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			if (val is MeshRenderer)
			{
				MeshFilter component = ((Component)val).GetComponent<MeshFilter>();
				if ((Object)((component != null) ? component.sharedMesh : null) != (Object)null)
				{
					val.enabled = true;
				}
			}
			else
			{
				SkinnedMeshRenderer val2 = (SkinnedMeshRenderer)(object)((val is SkinnedMeshRenderer) ? val : null);
				if (val2 != null)
				{
					if ((Object)val2.sharedMesh != (Object)null)
					{
						val.enabled = true;
					}
					val2.updateWhenOffscreen = true;
				}
			}
			val.forceRenderingOff = false;
			val.allowOcclusionWhenDynamic = false;
			val.shadowCastingMode = (ShadowCastingMode)0;
			val.receiveShadows = false;
		}
	}

	private static void ForceEnableAllLocalRenderers()
	{
		if ((Object)_localWeaponVisualRoot == (Object)null)
		{
			return;
		}
		SetActiveRecursively(_localWeaponVisualRoot, active: true);
		Material localVisualFallbackMaterial = GetLocalVisualFallbackMaterial();
		Renderer[] componentsInChildren = ((Component)_localWeaponVisualRoot).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			val.enabled = true;
			val.forceRenderingOff = false;
			val.allowOcclusionWhenDynamic = false;
			MeshRenderer val2 = (MeshRenderer)(object)((val is MeshRenderer) ? val : null);
			if (val2 != null)
			{
				MeshFilter component = ((Component)val2).GetComponent<MeshFilter>();
				Mesh val3 = ((component != null) ? component.sharedMesh : null);
				if ((Object)val3 != (Object)null)
				{
					int num = Mathf.Max(val3.subMeshCount, 1);
					Material[] sharedMaterials = val.sharedMaterials;
					if (sharedMaterials != null && sharedMaterials.Length > num)
					{
						Material[] array = (Material[])(object)new Material[num];
						for (int j = 0; j < num; j++)
						{
							array[j] = sharedMaterials[j];
						}
						val.sharedMaterials = array;
					}
				}
			}
			else
			{
				SkinnedMeshRenderer val4 = (SkinnedMeshRenderer)(object)((val is SkinnedMeshRenderer) ? val : null);
				if (val4 != null)
				{
					val4.updateWhenOffscreen = true;
					Mesh sharedMesh = val4.sharedMesh;
					if ((Object)sharedMesh != (Object)null)
					{
						int num2 = Mathf.Max(sharedMesh.subMeshCount, 1);
						Material[] sharedMaterials2 = val.sharedMaterials;
						if (sharedMaterials2 != null && sharedMaterials2.Length > num2)
						{
							Material[] array2 = (Material[])(object)new Material[num2];
							for (int k = 0; k < num2; k++)
							{
								array2[k] = sharedMaterials2[k];
							}
							val.sharedMaterials = array2;
						}
					}
				}
			}
			Material[] sharedMaterials3 = val.sharedMaterials;
			if (sharedMaterials3 == null || sharedMaterials3.Length == 0)
			{
				val.sharedMaterial = localVisualFallbackMaterial;
				continue;
			}
			bool flag = false;
			for (int l = 0; l < sharedMaterials3.Length; l++)
			{
				Material val5 = sharedMaterials3[l];
				bool flag2 = false;
				if ((Object)val5 == (Object)null || (Object)val5.shader == (Object)null || !val5.shader.isSupported)
				{
					flag2 = true;
				}
				if (!flag2 && val5.HasProperty("_Surface") && val5.GetFloat("_Surface") > 0.5f)
				{
					flag2 = true;
				}
				if (!flag2 && val5.renderQueue >= 3000)
				{
					flag2 = true;
				}
				if (flag2)
				{
					sharedMaterials3[l] = localVisualFallbackMaterial;
					flag = true;
					continue;
				}
				if (val5.HasProperty("_BaseColor"))
				{
					Color color = val5.GetColor("_BaseColor");
					color.a = 1f;
					val5.SetColor("_BaseColor", color);
				}
				if (val5.HasProperty("_Color"))
				{
					Color color2 = val5.GetColor("_Color");
					color2.a = 1f;
					val5.SetColor("_Color", color2);
				}
				if (val5.HasProperty("_Cull"))
				{
					val5.SetFloat("_Cull", 0f);
				}
			}
			if (flag)
			{
				val.sharedMaterials = sharedMaterials3;
			}
		}
	}

	private static void StripLocalVisualBehaviours(GameObject visualModel)
	{
		if ((Object)visualModel == (Object)null)
		{
			return;
		}
		foreach (Component item in visualModel.GetComponentsInChildren<Component>(true).Reverse())
		{
			if ((Object)(object)item == (Object)null || item is Transform || item is MeshFilter || item is MeshRenderer || item is SkinnedMeshRenderer)
			{
				continue;
			}
			Behaviour val = (Behaviour)(object)((item is Behaviour) ? item : null);
			if (val != null)
			{
				val.enabled = false;
			}
			Collider val2 = (Collider)(object)((item is Collider) ? item : null);
			if (val2 != null)
			{
				val2.enabled = false;
			}
			Renderer val3 = (Renderer)(object)((item is Renderer) ? item : null);
			if (val3 != null)
			{
				val3.enabled = false;
				val3.forceRenderingOff = true;
			}
			Object.Destroy((Object)(object)item);
		}
	}

	private static void NormalizeLocalVisualModel(Transform modelRoot)
	{
		if ((Object)modelRoot == (Object)null)
		{
			return;
		}
		Renderer[] componentsInChildren = ((Component)modelRoot).GetComponentsInChildren<Renderer>(true);
		bool flag = false;
		Bounds val = default(Bounds);
		Renderer[] array = componentsInChildren;
		foreach (Renderer val2 in array)
		{
			if (!((Object)val2 == (Object)null))
			{
				if (!flag)
				{
					val = val2.bounds;
					flag = true;
				}
				else
				{
					val.Encapsulate(val2.bounds);
				}
			}
		}
		if (flag)
		{
			Vector3 val3 = modelRoot.InverseTransformPoint(val.center);
			if (IsFiniteVector(val3) && val3.sqrMagnitude > 1E-06f)
			{
				modelRoot.localPosition -= val3;
			}
			float num = Mathf.Max(new float[3]
			{
				val.size.x,
				val.size.y,
				val.size.z
			});
			if (num > 0.0001f)
			{
				float num2 = Mathf.Clamp(0.65f / num, 0.08f, 3f);
				modelRoot.localScale *= num2;
			}
		}
	}

	private static void SetActiveRecursively(Transform root, bool active)
	{
		if (!((Object)root == (Object)null))
		{
			((Component)root).gameObject.SetActive(active);
			for (int i = 0; i < root.childCount; i++)
			{
				SetActiveRecursively(root.GetChild(i), active);
			}
		}
	}

	private static int CountMeshBackedRenderers(Transform root)
	{
		if ((Object)root == (Object)null)
		{
			return 0;
		}
		int num = 0;
		Renderer[] componentsInChildren = ((Component)root).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val in componentsInChildren)
		{
			if ((Object)val == (Object)null)
			{
				continue;
			}
			if (val is MeshRenderer)
			{
				MeshFilter component = ((Component)val).GetComponent<MeshFilter>();
				if ((Object)((component != null) ? component.sharedMesh : null) != (Object)null)
				{
					num++;
				}
			}
			else
			{
				SkinnedMeshRenderer val2 = (SkinnedMeshRenderer)(object)((val is SkinnedMeshRenderer) ? val : null);
				if (val2 != null && (Object)val2.sharedMesh != (Object)null)
				{
					num++;
				}
			}
		}
		return num;
	}

	private static void EnsureLocalVisualFallbackGeometry(Transform modelRoot)
	{
		if ((Object)modelRoot == (Object)null || (Object)modelRoot.Find("AK47_LocalFallback") != (Object)null)
		{
			return;
		}
		GameObject val = new GameObject("AK47_LocalFallback");
		val.transform.SetParent(modelRoot, false);
		val.transform.localPosition = new Vector3(0f, -0.01f, 0.12f);
		val.transform.localRotation = Quaternion.identity;
		val.transform.localScale = Vector3.one;
		GameObject obj = GameObject.CreatePrimitive((PrimitiveType)3);
		obj.transform.SetParent(val.transform, false);
		obj.transform.localScale = new Vector3(0.06f, 0.06f, 0.42f);
		obj.transform.localPosition = new Vector3(0f, 0f, 0.15f);
		GameObject obj2 = GameObject.CreatePrimitive((PrimitiveType)2);
		obj2.transform.SetParent(val.transform, false);
		obj2.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		obj2.transform.localScale = new Vector3(0.011f, 0.18f, 0.011f);
		obj2.transform.localPosition = new Vector3(0f, -0.005f, 0.39f);
		Renderer[] componentsInChildren = val.GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val2 in componentsInChildren)
		{
			if (!((Object)val2 == (Object)null))
			{
				val2.sharedMaterial = GetLocalVisualFallbackMaterial();
			}
		}
		Collider[] componentsInChildren2 = val.GetComponentsInChildren<Collider>(true);
		foreach (Collider val3 in componentsInChildren2)
		{
			if (!((Object)val3 == (Object)null))
			{
				val3.enabled = false;
			}
		}
	}

	private static void CleanupLocalVisualFallbackGeometry(Transform modelRoot)
	{
		if ((Object)modelRoot == (Object)null)
		{
			return;
		}
		Transform val = modelRoot.Find("AK47_LocalFallback");
		if ((Object)val != (Object)null)
		{
			Object.Destroy((Object)(object)((Component)val).gameObject);
		}
	}

	private static Material GetLocalVisualFallbackMaterial()
	{
		if ((Object)_localVisualFallbackMaterial != (Object)null)
		{
			return _localVisualFallbackMaterial;
		}
		Shader val = ResolveLocalVisualCompatibleShader() ?? Shader.Find("Sprites/Default");
		_localVisualFallbackMaterial = (((Object)val != (Object)null) ? new Material(val) : new Material(Shader.Find("W/Peak_Standard") ?? Shader.Find("Standard")));
		((Object)_localVisualFallbackMaterial).name = "AK47_LocalVisualFallbackMat";
		if (_localVisualFallbackMaterial.HasProperty("_BaseColor"))
		{
			_localVisualFallbackMaterial.SetColor("_BaseColor", new Color(0.24f, 0.25f, 0.27f, 1f));
		}
		if (_localVisualFallbackMaterial.HasProperty("_Color"))
		{
			_localVisualFallbackMaterial.SetColor("_Color", new Color(0.24f, 0.25f, 0.27f, 1f));
		}
		if (_localVisualFallbackMaterial.HasProperty("_Metallic"))
		{
			_localVisualFallbackMaterial.SetFloat("_Metallic", 0.55f);
		}
		if (_localVisualFallbackMaterial.HasProperty("_Smoothness"))
		{
			_localVisualFallbackMaterial.SetFloat("_Smoothness", 0.35f);
		}
		if (_localVisualFallbackMaterial.HasProperty("_Glossiness"))
		{
			_localVisualFallbackMaterial.SetFloat("_Glossiness", 0.35f);
		}
		if (_localVisualFallbackMaterial.HasProperty("_Cull"))
		{
			_localVisualFallbackMaterial.SetFloat("_Cull", 0f);
		}
		_localVisualFallbackMaterial.renderQueue = -1;
		return _localVisualFallbackMaterial;
	}

	private static Shader ResolveLocalVisualCompatibleShader()
	{
		return Shader.Find("Unlit/Texture") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("W/Peak_Standard") ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Hidden/InternalErrorShader");
	}

	private static int ResolveVisibleLayerForCamera(Transform fallbackAnchor)
	{
		Camera val = null;
		if ((Object)fallbackAnchor != (Object)null)
		{
			val = ((Component)fallbackAnchor).GetComponentInParent<Camera>();
		}
		if ((Object)val == (Object)null)
		{
			val = Camera.main;
		}
		if ((Object)val == (Object)null)
		{
			if (!((Object)fallbackAnchor != (Object)null))
			{
				return 0;
			}
			return ((Component)fallbackAnchor).gameObject.layer;
		}
		int cullingMask = val.cullingMask;
		if ((cullingMask & 1) != 0)
		{
			return 0;
		}
		int layer = ((Component)val).gameObject.layer;
		if ((cullingMask & (1 << layer)) != 0)
		{
			return layer;
		}
		for (int i = 0; i < 32; i++)
		{
			if ((cullingMask & (1 << i)) != 0)
			{
				return i;
			}
		}
		if (!((Object)fallbackAnchor != (Object)null))
		{
			return 0;
		}
		return ((Component)fallbackAnchor).gameObject.layer;
	}

	private static int ResolveLocalWeaponVisibleLayer(Transform viewAnchor, Item item, params Transform[] preferredAnchors)
	{
		Camera val = null;
		if ((Object)viewAnchor != (Object)null)
		{
			val = ((Component)viewAnchor).GetComponent<Camera>() ?? ((Component)viewAnchor).GetComponentInParent<Camera>();
		}
		if ((Object)val == (Object)null)
		{
			val = Camera.main;
		}
		int num = (((Object)val != (Object)null) ? val.cullingMask : (-1));
		if ((num & 1) != 0)
		{
			return 0;
		}
		List<int> list = new List<int>();
		int i;
		if (preferredAnchors != null)
		{
			for (i = 0; i < preferredAnchors.Length; i++)
			{
				Transform val2 = preferredAnchors[i];
				if ((Object)val2 != (Object)null)
				{
					list.Add(((Component)val2).gameObject.layer);
				}
			}
		}
		if ((Object)item != (Object)null)
		{
			if ((Object)item.mainRenderer != (Object)null)
			{
				list.Add(((Component)item.mainRenderer).gameObject.layer);
			}
			list.Add(((Component)item).gameObject.layer);
		}
		if ((Object)val != (Object)null)
		{
			list.Add(((Component)val).gameObject.layer);
		}
		foreach (int item2 in list.Distinct())
		{
			if (item2 <= 0 || item2 >= 32 || (num & (1 << item2)) == 0)
			{
				continue;
			}
			i = item2;
			goto IL_0195;
		}
		foreach (int item3 in list.Distinct())
		{
			if (item3 < 0 || item3 >= 32 || (num & (1 << item3)) == 0)
			{
				continue;
			}
			i = item3;
			goto IL_0195;
		}
		return ResolveVisibleLayerForCamera(viewAnchor);
		IL_0195:
		return i;
	}

	private static void UpdateLocalWeaponMuzzle()
	{
		if ((Object)_localWeaponVisualModel == (Object)null)
		{
			_localWeaponMuzzle = null;
			return;
		}
		if ((Object)_localWeaponMuzzle == (Object)null)
		{
			GameObject val = new GameObject("AK47_LocalMuzzle");
			val.transform.SetParent(_localWeaponVisualModel, false);
			_localWeaponMuzzle = val.transform;
		}
		Transform val2 = FindPreferredLocalMuzzleTransform(_localWeaponVisualModel);
		if ((Object)val2 != (Object)null)
		{
			_localWeaponMuzzle.position = val2.position;
			_localWeaponMuzzle.rotation = val2.rotation;
			return;
		}
		Renderer val3 = FindBestLocalMuzzleRenderer();
		if ((Object)val3 != (Object)null)
		{
			Bounds bounds = val3.bounds;
			_localWeaponMuzzle.position = bounds.center + ((Component)val3).transform.forward * Mathf.Max(bounds.extents.z, 0.06f);
			_localWeaponMuzzle.rotation = ((Component)val3).transform.rotation;
		}
		else
		{
			_localWeaponMuzzle.position = _localWeaponVisualModel.position + _localWeaponVisualModel.forward * 0.55f;
			_localWeaponMuzzle.rotation = _localWeaponVisualModel.rotation;
		}
	}

	private static Transform FindPreferredLocalMuzzleTransform(Transform root)
	{
		if ((Object)root == (Object)null)
		{
			return null;
		}
		Transform val = null;
		int num = 0;
		float num2 = float.MinValue;
		Transform[] componentsInChildren = ((Component)root).GetComponentsInChildren<Transform>(true);
		foreach (Transform val2 in componentsInChildren)
		{
			if ((Object)val2 == (Object)null)
			{
				continue;
			}
			string text = (((Object)val2).name ?? string.Empty).ToLowerInvariant();
			int num3 = 0;
			if (text.Contains("muzzle"))
			{
				num3 += 100;
			}
			if (text.Contains("barrel"))
			{
				num3 += 85;
			}
			if (text.Contains("firepoint") || text.Contains("fire_point"))
			{
				num3 += 80;
			}
			if (text.Contains("tip") || text.Contains("end"))
			{
				num3 += 60;
			}
			if (text.Contains("spawn"))
			{
				num3 += 20;
			}
			if (text.Contains("shell") || text.Contains("eject") || text.Contains("mag") || text.Contains("clip"))
			{
				num3 -= 80;
			}
			if (num3 > 0)
			{
				float num4 = Vector3.Dot(val2.position - root.position, root.forward);
				if ((Object)(object)val == (Object)null || num3 > num || (num3 == num && num4 > num2))
				{
					val = val2;
					num = num3;
					num2 = num4;
				}
			}
		}
		return val;
	}

	private static Renderer FindBestLocalMuzzleRenderer()
	{
		if ((Object)_localWeaponVisualModel == (Object)null)
		{
			return null;
		}
		Renderer[] componentsInChildren = ((Component)_localWeaponVisualModel).GetComponentsInChildren<Renderer>(true);
		Renderer result = null;
		float num = float.MinValue;
		Renderer[] array = componentsInChildren;
		foreach (Renderer val in array)
		{
			if (!((Object)val == (Object)null) && val.enabled)
			{
				Bounds bounds = val.bounds;
				Vector3 size = bounds.size;
				float num2 = size.magnitude + bounds.center.z;
				if (num2 > num)
				{
					num = num2;
					result = val;
				}
			}
		}
		return result;
	}

	private static void CleanupLocalWeaponVisual()
	{
		RestoreLocalFirstPersonHandRenderers();
		if ((Object)_localWeaponVisualRoot != (Object)null)
		{
			Object.Destroy((Object)((Component)_localWeaponVisualRoot).gameObject);
		}
		_localWeaponVisualRoot = null;
		_localWeaponVisualModel = null;
		_localWeaponMuzzle = null;
		_weaponVisualOwner = null;
		_localWeaponSourceItemId = 0;
		_localWeaponModelBasePosition = Vector3.zero;
		_localWeaponModelBaseRotation = Quaternion.identity;
		_localWeaponModelBaseScale = Vector3.one;
		_localWeaponModelUsesPreparedPose = false;
		_pendingLocalWeaponVisualModelRefresh = false;
	}

	private void EnsureLocalHeldDebugSphere(Item heldBlowgunItem)
	{
		if (!EnableLocalHeldDebugSphere || (Object)_localCharacter == (Object)null || (Object)heldBlowgunItem == (Object)null)
		{
			CleanupLocalHeldDebugSphere();
			return;
		}
		Transform val = ResolveViewAnchor();
		if ((Object)val == (Object)null)
		{
			val = (((Object)(_localCharacter?.refs?.view) != (Object)null) ? ((Component)_localCharacter.refs.view).transform : null);
		}
		if ((Object)val == (Object)null)
		{
			CleanupLocalHeldDebugSphere();
			return;
		}
		if ((Object)_localHeldDebugSphereRoot == (Object)null)
		{
			GameObject val2 = GameObject.CreatePrimitive((PrimitiveType)0);
			((Object)val2).name = "AK_LocalHeldDebugSphere";
			Collider component = val2.GetComponent<Collider>();
			if ((Object)component != (Object)null)
			{
				Object.Destroy((Object)(object)component);
			}
			_localHeldDebugSphereRoot = val2.transform;
			Renderer component2 = val2.GetComponent<Renderer>();
			if ((Object)component2 != (Object)null)
			{
				component2.sharedMaterial = GetLocalHeldDebugSphereMaterial();
				component2.shadowCastingMode = ShadowCastingMode.Off;
				component2.receiveShadows = false;
				component2.allowOcclusionWhenDynamic = false;
				component2.enabled = true;
				component2.forceRenderingOff = false;
			}
		}
		if ((Object)(object)_localHeldDebugSphereRoot.parent != (Object)(object)val)
		{
			_localHeldDebugSphereRoot.SetParent(val, false);
		}
		_localHeldDebugSphereRoot.localPosition = _localHeldDebugSphereOffset;
		_localHeldDebugSphereRoot.localRotation = Quaternion.identity;
		_localHeldDebugSphereRoot.localScale = _localHeldDebugSphereScale;
		int layer = ResolveVisibleLayerForCamera(val);
		SetLayerRecursively(_localHeldDebugSphereRoot, layer);
		((Component)_localHeldDebugSphereRoot).gameObject.SetActive(true);
		Renderer[] componentsInChildren = ((Component)_localHeldDebugSphereRoot).GetComponentsInChildren<Renderer>(true);
		foreach (Renderer val3 in componentsInChildren)
		{
			if (!((Object)val3 == (Object)null))
			{
				val3.enabled = true;
				val3.forceRenderingOff = false;
				val3.allowOcclusionWhenDynamic = false;
			}
		}
		LogLocalHeldDebugSphereState("ensure", heldBlowgunItem, val, layer);
	}

	private static void CleanupLocalHeldDebugSphere()
	{
		if ((Object)_localHeldDebugSphereRoot != (Object)null)
		{
			Object.Destroy((Object)((Component)_localHeldDebugSphereRoot).gameObject);
		}
		_localHeldDebugSphereRoot = null;
	}

	private static Material GetLocalHeldDebugSphereMaterial()
	{
		if ((Object)_localHeldDebugSphereMaterial != (Object)null)
		{
			return _localHeldDebugSphereMaterial;
		}
		_localHeldDebugSphereMaterial = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Standard"));
		((Object)_localHeldDebugSphereMaterial).name = "ShootZombies_LocalHeldDebugSphere";
		if (_localHeldDebugSphereMaterial.HasProperty("_Color"))
		{
			_localHeldDebugSphereMaterial.SetColor("_Color", new Color(1f, 0.05f, 0.05f, 1f));
		}
		if (_localHeldDebugSphereMaterial.HasProperty("_BaseColor"))
		{
			_localHeldDebugSphereMaterial.SetColor("_BaseColor", new Color(1f, 0.05f, 0.05f, 1f));
		}
		if (_localHeldDebugSphereMaterial.HasProperty("_EmissionColor"))
		{
			_localHeldDebugSphereMaterial.EnableKeyword("_EMISSION");
			_localHeldDebugSphereMaterial.SetColor("_EmissionColor", new Color(1.5f, 0.08f, 0.08f, 1f));
		}
		if (_localHeldDebugSphereMaterial.HasProperty("_Cull"))
		{
			_localHeldDebugSphereMaterial.SetInt("_Cull", 0);
		}
		return _localHeldDebugSphereMaterial;
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

	private static string FormatHeldItemForDiagnostics(Item sourceItem)
	{
		if ((Object)sourceItem == (Object)null)
		{
			return "null";
		}
		string text = ((((Object)sourceItem.holderCharacter != (Object)null) ? ((Object)sourceItem.holderCharacter).name : null) ?? ((((Object)sourceItem.trueHolderCharacter != (Object)null) ? ((Object)sourceItem.trueHolderCharacter).name : null) ?? "null"));
		return $"name={((Object)sourceItem).name},id={sourceItem.itemID},state={(int)sourceItem.itemState},holder={text}";
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
				CleanupLocalWeaponVisual();
				_hasWeapon = false;
			}
			UpdateWeaponLobbyNotice();
		}
		if (WeaponEnabled != null && WeaponEnabled.Value != _lastWeaponEnabled)
		{
			_lastWeaponEnabled = WeaponEnabled.Value;
			if (!_lastWeaponEnabled)
			{
				CleanupLocalWeaponVisual();
				_hasWeapon = false;
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
			flag |= RemoveLegacyConfigEntryValue("Zombie", "Knockback Force", dictionary);
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
			flag |= RemoveLegacyConfigEntryValue("Zombie AI", "Knockback Force", dictionary);
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
		return isChinese ? "每一波僵尸会在 0 到该值之间随机生成，并始终受僵尸最大数量限制。" : "Each wave spawns a random amount between 0 and this value, and always respects the zombie max count.";
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
		return dictionary;
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
		if (string.Equals(value, "default", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Default", StringComparison.OrdinalIgnoreCase))
		{
			return NormalizeLocalizedText(GetLocalizedKeyName(DefaultAkSoundOption, isChinese));
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
		return (object)entry == ZombieBehaviorDifficulty || (object)entry == MaxZombies || (object)entry == ZombieSpawnCount || (object)entry == ZombieSpawnInterval || (object)entry == ZombieMaxLifetime;
	}

	private static bool IsDerivedZombieConfigEntry(ConfigEntryBase entry)
	{
		return (object)entry == ZombieSpawnEnabled || (object)entry == ZombieSpawnIntervalRandom || (object)entry == ZombieSpawnRadius || (object)entry == ZombieSpawnCountRandom || (object)entry == ZombieDestroyDistance || (object)entry == ZombieMoveSpeed || (object)entry == ZombieAggressiveness || (object)entry == ZombieKnockbackForce || (object)entry == DistanceBeforeWakeup || (object)entry == DistanceBeforeChase || (object)entry == ZombieSprintDistance || (object)entry == ChaseTimeBeforeSprint || (object)entry == ZombieLungeDistance || (object)entry == ZombieTargetSearchInterval || (object)entry == ZombieBiteRecoveryTime || (object)entry == ZombieSamePlayerBiteCooldown || (object)entry == ZombieLungeTime || (object)entry == ZombieLungeRecoveryTime || (object)entry == ZombieLookAngleBeforeWakeup;
	}

	private static bool IsZombieConfigEntry(ConfigEntryBase entry)
	{
		return IsPrimaryZombieConfigEntry(entry) || IsDerivedZombieConfigEntry(entry);
	}

	private static bool ShouldPersistZombieConfigEntryInCanonicalFile(ConfigEntryBase entry)
	{
		return (object)entry == MaxZombies || (object)entry == ZombieSpawnCount || (object)entry == ZombieSpawnInterval || (object)entry == ZombieMaxLifetime;
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

	private void RefreshLocalizedConfigFiles(bool isChinese)
	{
		try
		{
			ConfigFile config = ((BaseUnityPlugin)this).Config;
			config?.Save();
			CleanupAuxiliaryConfigFiles();
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] RefreshLocalizedConfigFiles failed: " + DescribeReflectionException(ex)));
		}
	}

	private void PreparePrimaryConfigFile()
	{
		try
		{
			string canonicalConfigPath = GetCanonicalConfigPath();
			if (!string.IsNullOrWhiteSpace(canonicalConfigPath))
			{
				string directoryName = Path.GetDirectoryName(canonicalConfigPath);
				if (!string.IsNullOrWhiteSpace(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
			}
			if (!string.IsNullOrWhiteSpace(canonicalConfigPath) && !File.Exists(canonicalConfigPath))
			{
				string text = GetLatestExistingConfigPath(new string[7]
				{
					GetCanonicalConfigPath(),
					GetPreviousCanonicalConfigPath(),
					GetLegacyCanonicalConfigPath(),
					Path.Combine(Paths.ConfigPath, "Thanks.ShootZombies.zh-CN.cfg"),
					Path.Combine(Paths.ConfigPath, "Thanks.ShootZombies.en.cfg"),
					GetLocalizedConfigMirrorPath(),
					GetPreviousLocalizedConfigMirrorPath()
				});
				if (!string.IsNullOrWhiteSpace(text) && File.Exists(text))
				{
					File.Copy(text, canonicalConfigPath, overwrite: true);
				}
			}
			CleanupAuxiliaryConfigFiles();
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] PreparePrimaryConfigFile failed: " + ex.Message));
		}
	}

	private static string GetLatestExistingConfigPath(IEnumerable<string> candidatePaths)
	{
		string text = string.Empty;
		DateTime dateTime = DateTime.MinValue;
		if (candidatePaths == null)
		{
			return text;
		}
		foreach (string candidatePath in candidatePaths)
		{
			if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
			{
				continue;
			}
			DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(candidatePath);
			if (string.IsNullOrWhiteSpace(text) || lastWriteTimeUtc > dateTime)
			{
				text = candidatePath;
				dateTime = lastWriteTimeUtc;
			}
		}
		return text;
	}

	private void ResetVersionMismatchedConfigFiles()
	{
		// Intentionally left blank.
		// BepInEx rewrites cfg files through Config.Save() and drops our custom metadata comments.
		// Using plugin version metadata as a hard-reset condition therefore causes valid user configs
		// to be deleted on the next launch. We preserve existing values and let the later rewrite path
		// refresh metadata/comments without wiping the user's settings.
	}

	private void ClearLoadedConfigState()
	{
		try
		{
			ConfigFile config = ((BaseUnityPlugin)this).Config;
			ClearConfigDictionary(config, "Entries");
			ClearConfigDictionary(config, "OrphanedEntries");
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] ClearLoadedConfigState failed: " + ex.Message));
		}
	}

	private static void ClearConfigDictionary(ConfigFile config, string propertyName)
	{
		if (config != null && !string.IsNullOrWhiteSpace(propertyName) && ((object)config).GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(config) is IDictionary dictionary)
		{
			dictionary.Clear();
		}
	}

	private static bool IsConfigFileVersionMismatch(string configPath)
	{
		if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
		{
			return false;
		}
		return !string.Equals(ReadConfigMetadataValue(configPath, ConfigMetadataVersionPrefix), Version, StringComparison.Ordinal);
	}

	private static string ReadConfigMetadataValue(string configPath, string metadataPrefix)
	{
		if (string.IsNullOrWhiteSpace(configPath) || string.IsNullOrWhiteSpace(metadataPrefix) || !File.Exists(configPath))
		{
			return string.Empty;
		}
		try
		{
			foreach (string item in File.ReadAllLines(configPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
			{
				string text = (item ?? string.Empty).Trim();
				if (text.StartsWith(metadataPrefix, StringComparison.Ordinal))
				{
					return text.Substring(metadataPrefix.Length).Trim();
				}
			}
		}
		catch
		{
		}
		return string.Empty;
	}

	private string ReadStoredZombieBehaviorDifficultySelection()
	{
		string text = ReadConfigMetadataValue(GetCanonicalConfigPath(), ConfigMetadataZombieDifficultyPrefix);
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		return NormalizeZombieBehaviorDifficultySelection(text);
	}

	private void CleanupAuxiliaryConfigFiles()
	{
		try
		{
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string canonicalConfigPath = GetCanonicalConfigPath();
			if (!string.IsNullOrWhiteSpace(canonicalConfigPath))
			{
				hashSet.Add(canonicalConfigPath);
			}
			HashSet<string> hashSet2 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string[] array2 = new string[7]
			{
				GetPreviousCanonicalConfigPath(),
				GetLegacyCanonicalConfigPath(),
				Path.Combine(Paths.ConfigPath, "Thanks.ShootZombies.zh-CN.cfg"),
				Path.Combine(Paths.ConfigPath, "Thanks.ShootZombies.en.cfg"),
				GetLocalizedConfigMirrorPath(),
				GetPreviousLocalizedConfigMirrorPath(),
				GetLegacyLocalizedConfigMirrorPath()
			};
			foreach (string item2 in array2)
			{
				if (!string.IsNullOrWhiteSpace(item2) && !hashSet.Contains(item2))
				{
					hashSet2.Add(item2);
				}
			}
			foreach (string item3 in hashSet2)
			{
				if (File.Exists(item3))
				{
					File.Delete(item3);
				}
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] CleanupAuxiliaryConfigFiles failed: " + ex.Message));
		}
	}

	private void NormalizeCanonicalConfigEncoding()
	{
		try
		{
			string canonicalConfigPath = GetCanonicalConfigPath();
			if (!string.IsNullOrWhiteSpace(canonicalConfigPath) && File.Exists(canonicalConfigPath))
			{
				string contents = File.ReadAllText(canonicalConfigPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				File.WriteAllText(canonicalConfigPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] NormalizeCanonicalConfigEncoding failed: " + ex.Message));
		}
	}

	private string GetCurrentConfigSchemaSignature()
	{
		try
		{
			ConfigEntryBase[] configEntriesSnapshot = GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config);
			StringBuilder stringBuilder = new StringBuilder();
			foreach (ConfigEntryBase item in configEntriesSnapshot.Where((ConfigEntryBase entry) => entry != null && entry.Definition != (ConfigDefinition)null).OrderBy((ConfigEntryBase entry) => entry.Definition.Section, StringComparer.Ordinal).ThenBy((ConfigEntryBase entry) => entry.Definition.Key, StringComparer.Ordinal))
			{
				stringBuilder.Append(item.Definition.Section).Append('|').Append(item.Definition.Key).Append('|').Append(item.SettingType?.FullName ?? string.Empty).Append('|');
				if (item.Description != null && item.Description.AcceptableValues != null)
				{
					stringBuilder.Append(item.Description.AcceptableValues.GetType().FullName ?? string.Empty);
				}
				stringBuilder.AppendLine();
			}
			using SHA256 sHA = SHA256.Create();
			byte[] bytes = Encoding.UTF8.GetBytes(stringBuilder.ToString());
			byte[] value = sHA.ComputeHash(bytes);
			return BitConverter.ToString(value).Replace("-", string.Empty).ToLowerInvariant();
		}
		catch
		{
			return Version;
		}
	}

	private static string GetCurrentConfigLanguageToken(bool isChinese)
	{
		if (!isChinese)
		{
			return "en";
		}
		return "zh-CN";
	}

	private bool ShouldRewriteCanonicalConfigFile(string schemaSignature, string languageToken)
	{
		string canonicalConfigPath = GetCanonicalConfigPath();
		if (string.IsNullOrWhiteSpace(canonicalConfigPath) || !File.Exists(canonicalConfigPath))
		{
			return true;
		}
		try
		{
			string[] array = File.ReadAllLines(canonicalConfigPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			string text = string.Empty;
			string text2 = string.Empty;
			string text3 = string.Empty;
			string text4 = string.Empty;
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			string text5 = string.Empty;
			foreach (string item in array)
			{
				string text6 = (item ?? string.Empty).Trim();
				if (text6.StartsWith(ConfigMetadataVersionPrefix, StringComparison.Ordinal))
				{
					text = text6.Substring(ConfigMetadataVersionPrefix.Length).Trim();
					continue;
				}
				if (text6.StartsWith(ConfigMetadataSchemaPrefix, StringComparison.Ordinal))
				{
					text2 = text6.Substring(ConfigMetadataSchemaPrefix.Length).Trim();
					continue;
				}
				if (text6.StartsWith(ConfigMetadataLanguagePrefix, StringComparison.Ordinal))
				{
					text3 = text6.Substring(ConfigMetadataLanguagePrefix.Length).Trim();
					continue;
				}
				if (text6.StartsWith(ConfigMetadataZombieDifficultyPrefix, StringComparison.Ordinal))
				{
					text4 = NormalizeZombieBehaviorDifficultySelection(text6.Substring(ConfigMetadataZombieDifficultyPrefix.Length).Trim());
					continue;
				}
				if (text6.Length == 0 || text6.StartsWith("#", StringComparison.Ordinal))
				{
					continue;
				}
				if (text6.StartsWith("[", StringComparison.Ordinal) && text6.EndsWith("]", StringComparison.Ordinal) && text6.Length > 2)
				{
					text5 = text6.Substring(1, text6.Length - 2).Trim();
					continue;
				}
				int num = text6.IndexOf('=');
				if (num <= 0 || string.IsNullOrWhiteSpace(text5))
				{
					continue;
				}
				string text7 = text6.Substring(0, num).Trim();
				if (!string.IsNullOrWhiteSpace(text7))
				{
					hashSet.Add(text5 + "\u0001" + text7);
				}
			}
			if (!string.Equals(text, Version, StringComparison.Ordinal) || !string.Equals(text2, schemaSignature, StringComparison.Ordinal) || !string.Equals(text3, languageToken, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			if (!string.Equals(text4, NormalizeZombieBehaviorDifficultySelection(ZombieBehaviorDifficulty?.Value), StringComparison.Ordinal))
			{
				return true;
			}
			if (HasUnexpectedLocalizedDescriptions(array, string.Equals(languageToken, "zh-CN", StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
			return GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config).Any((ConfigEntryBase entry) => entry != null && entry.Definition != (ConfigDefinition)null && ShouldPersistConfigEntryInCanonicalFile(entry) && !hashSet.Contains(entry.Definition.Section + "\u0001" + entry.Definition.Key));
		}
		catch
		{
			return true;
		}
	}

	private void ForceRewriteCanonicalConfigFile(ConfigFile config)
	{
		if (config == null)
		{
			return;
		}
		string canonicalConfigPath = GetCanonicalConfigPath();
		if (string.IsNullOrWhiteSpace(canonicalConfigPath))
		{
			config.Save();
			return;
		}
		try
		{
			string directoryName = Path.GetDirectoryName(canonicalConfigPath);
			if (!string.IsNullOrWhiteSpace(directoryName))
			{
				Directory.CreateDirectory(directoryName);
			}
			if (File.Exists(canonicalConfigPath))
			{
				File.Delete(canonicalConfigPath);
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] ForceRewriteCanonicalConfigFile delete failed: " + ex.Message));
		}
		config.Save();
	}

	private bool HasUnexpectedLocalizedDescriptions(IEnumerable<string> lines, bool isChinese)
	{
		string text = string.Join("\n", lines ?? Array.Empty<string>());
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}
		bool flag = false;
		foreach (ConfigEntryBase item in GetConfigEntriesSnapshot(((BaseUnityPlugin)this).Config))
		{
			if (item == null || item.Definition == (ConfigDefinition)null)
			{
				continue;
			}
			string localizedDescription = GetLocalizedDescription(item.Definition.Key, isChinese);
			if (!string.IsNullOrWhiteSpace(localizedDescription) && text.IndexOf(localizedDescription, StringComparison.Ordinal) >= 0)
			{
				flag = true;
			}
			string localizedDescription2 = GetLocalizedDescription(item.Definition.Key, !isChinese);
			if (!string.IsNullOrWhiteSpace(localizedDescription2) && !string.Equals(localizedDescription, localizedDescription2, StringComparison.Ordinal) && text.IndexOf(localizedDescription2, StringComparison.Ordinal) >= 0)
			{
				return true;
			}
		}
		return !flag;
	}

	private void UpdateCanonicalConfigMetadata(string schemaSignature, string languageToken)
	{
		string canonicalConfigPath = GetCanonicalConfigPath();
		if (string.IsNullOrWhiteSpace(canonicalConfigPath) || !File.Exists(canonicalConfigPath))
		{
			return;
		}
		try
		{
			List<string> list = SanitizeCanonicalConfigLines(File.ReadAllLines(canonicalConfigPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));
			List<string> list2 = new List<string>
			{
				ConfigMetadataVersionPrefix + Version,
				ConfigMetadataSchemaPrefix + schemaSignature,
				ConfigMetadataLanguagePrefix + languageToken,
				ConfigMetadataZombieDifficultyPrefix + NormalizeZombieBehaviorDifficultySelection(ZombieBehaviorDifficulty?.Value),
				string.Empty
			};
			list2.AddRange(list);
			File.WriteAllLines(canonicalConfigPath, list2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] UpdateCanonicalConfigMetadata failed: " + ex.Message));
		}
	}

	private static bool ShouldPersistConfigEntryInCanonicalFile(ConfigEntryBase entry)
	{
		if (entry == null || entry.Definition == (ConfigDefinition)null)
		{
			return false;
		}
		if (IsZombieConfigEntry(entry))
		{
			return ShouldPersistZombieConfigEntryInCanonicalFile(entry);
		}
		return true;
	}

	private static bool ShouldPersistCanonicalConfigLine(string section, string key)
	{
		if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key))
		{
			return true;
		}
		ConfigEntryBase[] configEntriesSnapshotRuntime = GetConfigEntriesSnapshotRuntime(Instance?.PluginConfig);
		ConfigEntryBase val = configEntriesSnapshotRuntime.FirstOrDefault((ConfigEntryBase entry) => entry != null && entry.Definition != (ConfigDefinition)null && string.Equals(entry.Definition.Section, section, StringComparison.Ordinal) && string.Equals(entry.Definition.Key, key, StringComparison.Ordinal));
		return ShouldPersistConfigEntryInCanonicalFile(val);
	}

	private static bool IsZombieConfigSectionName(string section)
	{
		return string.Equals(section, "Zombie", StringComparison.Ordinal) || string.Equals(section, "Zombie Spawn", StringComparison.Ordinal);
	}

	private static List<string> SanitizeCanonicalConfigLines(IEnumerable<string> lines)
	{
		List<string> list = new List<string>();
		if (lines == null)
		{
			return list;
		}
		List<string> list2 = new List<string>();
		string text = string.Empty;
		bool flag = false;
		void FlushPendingComments(bool keep)
		{
			if (keep && list2.Count > 0)
			{
				list.AddRange(list2);
			}
			list2.Clear();
		}
		foreach (string item in lines)
		{
			string text2 = item ?? string.Empty;
			string text3 = text2.TrimStart();
			if (text3.StartsWith(ConfigMetadataVersionPrefix, StringComparison.Ordinal) || text3.StartsWith(ConfigMetadataSchemaPrefix, StringComparison.Ordinal) || text3.StartsWith(ConfigMetadataLanguagePrefix, StringComparison.Ordinal) || text3.StartsWith(ConfigMetadataZombieDifficultyPrefix, StringComparison.Ordinal))
			{
				continue;
			}
			string text4 = text2.Trim();
			if (text4.StartsWith("[", StringComparison.Ordinal) && text4.EndsWith("]", StringComparison.Ordinal) && text4.Length > 2)
			{
				FlushPendingComments(keep: false);
				text = text4.Substring(1, text4.Length - 2).Trim();
				flag = false;
				if (!IsZombieConfigSectionName(text))
				{
					list.Add(text2);
					flag = true;
				}
				continue;
			}
			if (!IsZombieConfigSectionName(text))
			{
				FlushPendingComments(keep: true);
				list.Add(text2);
				continue;
			}
			if (text4.Length == 0)
			{
				FlushPendingComments(keep: false);
				if (flag && (list.Count == 0 || !string.IsNullOrWhiteSpace(list[list.Count - 1])))
				{
					list.Add(text2);
				}
				continue;
			}
			if (text4.StartsWith("#", StringComparison.Ordinal))
			{
				list2.Add(text2);
				continue;
			}
			int num = text4.IndexOf('=');
			if (num > 0)
			{
				string text5 = text4.Substring(0, num).Trim();
				if (ShouldPersistCanonicalConfigLine(text, text5))
				{
					if (!flag)
					{
						list.Add("[" + text + "]");
						flag = true;
					}
					FlushPendingComments(keep: true);
					list.Add(text2);
				}
				else
				{
					FlushPendingComments(keep: false);
				}
				continue;
			}
			FlushPendingComments(keep: flag);
			if (flag)
			{
				list.Add(text2);
			}
		}
		return list;
	}

	private void RewriteCanonicalConfigForUserPresentation()
	{
		// Intentionally left blank.
		// ModConfig now reads the single canonical cfg file directly, so we no longer
		// post-process the saved file or trim entries after BepInEx writes it.
	}

	private static string GetCanonicalConfigPath()
	{
		return Path.Combine(Paths.ConfigPath, CanonicalConfigFileName);
	}

	private string GetActivePluginConfigPath()
	{
		if (!string.IsNullOrWhiteSpace(_pluginConfigPath))
		{
			return _pluginConfigPath;
		}
		return GetCanonicalConfigPath();
	}

	private static string GetConfigFilePath(ConfigFile config)
	{
		return (((object)config)?.GetType().GetProperty("ConfigFilePath", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(config) as string) ?? string.Empty;
	}

	private static string GetPreviousCanonicalConfigPath()
	{
		return Path.Combine(Paths.ConfigPath, PreviousCanonicalConfigFileName);
	}

	private static string GetLegacyCanonicalConfigPath()
	{
		return Path.Combine(Paths.ConfigPath, LegacyCanonicalConfigFileName);
	}

	private static string GetLocalizedConfigMirrorPath()
	{
		return Path.Combine(Paths.ConfigPath, LocalizedConfigMirrorFileName);
	}

	private static string GetPreviousLocalizedConfigMirrorPath()
	{
		return Path.Combine(Paths.ConfigPath, PreviousLocalizedConfigMirrorFileName);
	}

	private static string GetLegacyLocalizedConfigMirrorPath()
	{
		return Path.Combine(Paths.ConfigPath, LegacyLocalizedConfigMirrorFileName);
	}

	private void UpdateFogControl()
	{
		CleanupOldFogUI();
		CleanupFogModeLobbyNotice();
	}

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

	private bool IsGameplayScene()
	{
		return IsGameplayScene(SceneManager.GetActiveScene());
	}

	private static bool IsFogModeEnabled()
	{
		return false;
	}

	private static bool IsNightColdFeatureEnabled()
	{
		return true;
	}

	private static bool IsOfficialNightColdDisabled()
	{
		return false;
	}

	internal static bool ShouldSuppressAmbientColdDamage()
	{
		return false;
	}

	private static float GetEffectiveFogSpeed()
	{
		return 0f;
	}

	private static float GetEffectiveFogStartDelay()
	{
		return 0f;
	}

	private static bool ShouldFogBeMoving()
	{
		return false;
	}

	private static bool ShouldBroadcastFogState(float currentSize, bool moving)
	{
		return false;
	}

	public static bool ShouldSuppressFogColdDamage()
	{
		return false;
	}

	internal static void BeginLocalFogStatusSuppression()
	{
	}

	internal static void EndLocalFogStatusSuppression()
	{
	}

	internal static bool ShouldSuppressLocalFogSourceStatus(CharacterAfflictions afflictions, CharacterAfflictions.STATUSTYPE statusType)
	{
		return false;
	}

	internal static void EnforceAmbientColdSuppression(CharacterAfflictions afflictions)
	{
	}

	internal static bool ShouldSuppressAmbientColdDamageFor(CharacterAfflictions afflictions)
	{
		return false;
	}

	internal static bool ShouldSuppressAmbientColdDamageFor(Character character)
	{
		return false;
	}

	public static void ClearLocalAmbientColdStatus()
	{
	}

	public static void ClearLocalFogColdStatus()
	{
	}

	private bool ShouldShowFogModeLobbyNotice()
	{
		return false;
	}

	private bool ShouldShowFogUI()
	{
		return false;
	}

	private static bool IsFogUiHintsEnabled()
	{
		return false;
	}

	private static bool IsOfficialCustomRunActive()
	{
		try
		{
			return RunSettings.initialized && RunSettings.IsCustomRun;
		}
		catch
		{
			return false;
		}
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

	private static bool IsOfficialMiniRunActive()
	{
		try
		{
			return RunSettings.initialized && RunSettings.isMiniRun;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryGetOfficialRunSetting(RunSettings.SETTINGTYPE settingType, out int value)
	{
		value = 0;
		try
		{
			if (!RunSettings.initialized)
			{
				return false;
			}
			bool forceGetCustomValue = IsOfficialCustomRunActive() || IsOfficialMiniRunActive();
			value = RunSettings.GetValue(settingType, forceGetCustomValue);
			return true;
		}
		catch
		{
			value = 0;
			return false;
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

	private Canvas ResolveHudCanvas()
	{
		GUIManager instance = GUIManager.instance;
		if ((Object)instance == (Object)null)
		{
			return null;
		}
		TextMeshProUGUI val = (((Object)instance.itemPromptMain != (Object)null) ? instance.itemPromptMain : instance.interactNameText);
		Canvas val2 = (((Object)val != (Object)null) ? ((Component)val).GetComponentInParent<Canvas>() : null);
		Canvas[] componentsInChildren = ((Component)instance).GetComponentsInChildren<Canvas>(true);
		Canvas val3 = null;
		float num = -1f;
		Canvas[] array = componentsInChildren;
		foreach (Canvas val4 in array)
		{
			if ((Object)val4 == (Object)null)
			{
				continue;
			}
			RectTransform component = ((Component)val4).GetComponent<RectTransform>();
			if ((Object)component == (Object)null)
			{
				continue;
			}
			Rect rect = component.rect;
			float num2 = Mathf.Abs(rect.width * rect.height);
			if (num2 < 1f)
			{
				continue;
			}
			if (val4.isRootCanvas)
			{
				num2 += 1000000f;
			}
			if (((Component)val4).gameObject.activeInHierarchy)
			{
				num2 += 500000f;
			}
			if ((Object)val2 != (Object)null && (Object)val4 == (Object)val2)
			{
				num2 += 250000f;
			}
			if (num2 > num)
			{
				num = num2;
				val3 = val4;
			}
		}
		if ((Object)val3 != (Object)null)
		{
			return val3;
		}
		return val2;
	}

	private TextMeshProUGUI ResolveHudTextStyleSource()
	{
		GUIManager instance = GUIManager.instance;
		if ((Object)instance == (Object)null)
		{
			return null;
		}
		if ((Object)instance.itemPromptMain != (Object)null)
		{
			return instance.itemPromptMain;
		}
		if ((Object)instance.interactNameText != (Object)null)
		{
			return instance.interactNameText;
		}
		return instance.interactPromptText;
	}

	private void ApplyGameTextStyle(TextMeshProUGUI target, Color color, float sizeMultiplier = 1f, float? customLineSpacing = null)
	{
		if (!((Object)target == (Object)null))
		{
			TextMeshProUGUI val = ResolveHudTextStyleSource();
			if ((Object)val != (Object)null)
			{
				((TMP_Text)target).font = ((TMP_Text)val).font;
				((TMP_Text)target).fontSharedMaterial = ((TMP_Text)val).fontSharedMaterial;
				((TMP_Text)target).fontStyle = ((TMP_Text)val).fontStyle;
				((TMP_Text)target).characterSpacing = ((TMP_Text)val).characterSpacing;
				((TMP_Text)target).wordSpacing = ((TMP_Text)val).wordSpacing;
				((TMP_Text)target).lineSpacing = customLineSpacing ?? ((TMP_Text)val).lineSpacing;
				((TMP_Text)target).textWrappingMode = (TextWrappingModes)0;
			}
			((TMP_Text)target).fontSize = 18f * sizeMultiplier;
			((Graphic)target).color = color;
			((TMP_Text)target).alignment = (TextAlignmentOptions)257;
		}
	}

	private static void ApplyBottomLeftAnchoredRect(RectTransform target, RectTransform canvasRect, float width, float height, float scale, float x, float y)
	{
		if ((Object)target == (Object)null || (Object)canvasRect == (Object)null)
		{
			return;
		}
		((Transform)target).localScale = Vector3.one * Mathf.Max(scale, 0.3f);
		target.anchorMin = new Vector2(0f, 0f);
		target.anchorMax = new Vector2(0f, 0f);
		target.pivot = new Vector2(0f, 0f);
		target.sizeDelta = new Vector2(width, height);
		Rect rect = canvasRect.rect;
		float num5 = height * Mathf.Max(scale, 0.3f);
		float num6 = y + FogUiSharedTopReferenceHeight;
		float num7 = Mathf.Min(rect.width * FogUiCanvasOverflowXRatio, FogUiCanvasOverflowXMax);
		float num8 = Mathf.Min(rect.height * FogUiCanvasOverflowYRatio, FogUiCanvasOverflowYMax);
		float num9 = Mathf.Clamp(x, 0f - num7, rect.width + num7);
		float num10 = Mathf.Clamp(num6, 0f - num8, rect.height + num8);
		float num11 = num10 - num5;
		target.anchoredPosition = new Vector2(num9, num11);
	}

	private static void ApplyRightMiddleRect(RectTransform target, float width, float height, float rightOffset, float downOffset)
	{
		if ((Object)target == (Object)null)
		{
			return;
		}
		((Transform)target).localScale = Vector3.one;
		target.anchorMin = new Vector2(1f, 0.5f);
		target.anchorMax = new Vector2(1f, 0.5f);
		target.pivot = new Vector2(1f, 0.5f);
		target.sizeDelta = new Vector2(width, height);
		target.anchoredPosition = new Vector2(0f - rightOffset, 0f - downOffset);
	}

	private static void ApplyRightBottomRect(RectTransform target, float width, float height, float rightOffset, float bottomOffset)
	{
		if ((Object)target == (Object)null)
		{
			return;
		}
		((Transform)target).localScale = Vector3.one;
		target.anchorMin = new Vector2(1f, 0f);
		target.anchorMax = new Vector2(1f, 0f);
		target.pivot = new Vector2(1f, 0f);
		target.sizeDelta = new Vector2(width, height);
		target.anchoredPosition = new Vector2(0f - rightOffset, bottomOffset);
	}

	private static string GetFogModeLobbyNoticeTextCore(bool isChinese)
	{
		return string.Empty;
	}

	private void ClampFogModeLobbyNoticeToCanvas()
	{
		if ((Object)_fogModeLobbyNoticeRect == (Object)null)
		{
			return;
		}
		string text = NormalizeLocalizedText((Object)_fogModeLobbyNoticeText != (Object)null ? ((TMP_Text)_fogModeLobbyNoticeText).text : _lastFogModeLobbyNoticeText);
		ApplySharedFogBottomLeftLayout(_fogModeLobbyNoticeRect, FogModeLobbyNoticeWidth, GetLobbyNoticePreferredHeight(text));
	}

	private void UpdateFogModeLobbyNotice()
	{
		CleanupFogModeLobbyNotice();
	}

	private void ClampWeaponLobbyNoticeToCanvas()
	{
		if ((Object)_weaponLobbyNoticeRect == (Object)null)
		{
			return;
		}
		string text = NormalizeLocalizedText((Object)_weaponLobbyNoticeText != (Object)null ? ((TMP_Text)_weaponLobbyNoticeText).text : _lastWeaponLobbyNoticeText);
		ApplyDefaultWeaponLobbyNoticeStyle();
		Canvas val = ResolveHudCanvas();
		if ((Object)val != (Object)null && TryAnchorWeaponLobbyNoticeToCompassPrompt(val, text))
		{
			return;
		}
		ApplyRightMiddleRect(_weaponLobbyNoticeRect, LobbyNoticeWidth * WeaponLobbyNoticeScaleMultiplier, GetScaledWeaponLobbyNoticePreferredHeight(text), LobbyNoticeRightOffset, LobbyNoticeDownOffset);
	}

	private void UpdateWeaponLobbyNotice()
	{
		if (!ShouldShowWeaponLobbyNotice())
		{
			CleanupWeaponLobbyNotice();
			return;
		}
		if ((Object)_weaponLobbyNoticeObject == (Object)null || (Object)_weaponLobbyNoticeText == (Object)null || !((Object)_weaponLobbyNoticeObject.transform.parent != (Object)null))
		{
			CreateWeaponLobbyNotice();
		}
		if ((Object)_weaponLobbyNoticeText == (Object)null)
		{
			return;
		}
		string lobbyWeaponNoticeTextCore = NormalizeLocalizedText(GetLobbyWeaponNoticeTextCore(GetCachedChineseLanguageSetting()));
		bool flag = false;
		if (!string.Equals(_lastWeaponLobbyNoticeText, lobbyWeaponNoticeTextCore, StringComparison.Ordinal))
		{
			_lastWeaponLobbyNoticeText = lobbyWeaponNoticeTextCore;
			((TMP_Text)_weaponLobbyNoticeText).text = lobbyWeaponNoticeTextCore;
			flag = true;
		}
		if (flag || Time.unscaledTime - _lastWeaponLobbyNoticeLayoutTime >= LobbyNoticeLayoutRefreshInterval)
		{
			ClampWeaponLobbyNoticeToCanvas();
			_lastWeaponLobbyNoticeLayoutTime = Time.unscaledTime;
		}
		_weaponLobbyNoticeObject.SetActive(true);
	}

	private void CreateFogModeLobbyNotice()
	{
		CleanupFogModeLobbyNotice();
	}

	private void CreateWeaponLobbyNotice()
	{
		CleanupWeaponLobbyNotice();
		Canvas val = ResolveHudCanvas();
		if ((Object)val == (Object)null)
		{
			return;
		}
		GameObject val2 = new GameObject("WeaponLobbyNotice");
		val2.transform.SetParent(((Component)val).transform, false);
		RectTransform obj = (_weaponLobbyNoticeRect = val2.AddComponent<RectTransform>());
		obj.anchoredPosition = Vector2.zero;
		string lobbyWeaponNoticeTextCore = NormalizeLocalizedText(GetLobbyWeaponNoticeTextCore(GetCachedChineseLanguageSetting()));
		obj.sizeDelta = new Vector2(LobbyNoticeWidth * WeaponLobbyNoticeScaleMultiplier, GetScaledWeaponLobbyNoticePreferredHeight(lobbyWeaponNoticeTextCore));
		_weaponLobbyNoticeObject = val2;
		_weaponLobbyNoticeText = val2.AddComponent<TextMeshProUGUI>();
		ApplyDefaultWeaponLobbyNoticeStyle();
		_lastWeaponLobbyNoticeText = lobbyWeaponNoticeTextCore;
		((TMP_Text)_weaponLobbyNoticeText).text = _lastWeaponLobbyNoticeText;
		ClampWeaponLobbyNoticeToCanvas();
		_lastWeaponLobbyNoticeLayoutTime = Time.unscaledTime;
	}

	private static void CleanupFogModeLobbyNotice()
	{
		if ((Object)_fogModeLobbyNoticeObject != (Object)null)
		{
			Object.Destroy((Object)_fogModeLobbyNoticeObject);
		}
		_fogModeLobbyNoticeObject = null;
		_fogModeLobbyNoticeText = null;
		_fogModeLobbyNoticeRect = null;
		_lastFogModeLobbyNoticeText = string.Empty;
		CleanupSharedFogBottomLeftRootIfUnused();
	}

	private static void CleanupWeaponLobbyNotice()
	{
		if ((Object)_weaponLobbyNoticeObject != (Object)null)
		{
			Object.Destroy((Object)_weaponLobbyNoticeObject);
		}
		_weaponLobbyNoticeObject = null;
		_weaponLobbyNoticeText = null;
		_weaponLobbyNoticeRect = null;
		_lastWeaponLobbyNoticeText = string.Empty;
		_lastWeaponLobbyNoticeLayoutTime = -10f;
	}

	private void ApplyDefaultWeaponLobbyNoticeStyle()
	{
		if (!((Object)_weaponLobbyNoticeText == (Object)null))
		{
			ApplyGameTextStyle(_weaponLobbyNoticeText, new Color(1f, 0.94f, 0.72f), LobbyNoticeFontSizeMultiplier * WeaponLobbyNoticeScaleMultiplier, LobbyNoticeLineSpacing * WeaponLobbyNoticeScaleMultiplier);
			((TMP_Text)_weaponLobbyNoticeText).fontStyle = (FontStyles)0;
			((TMP_Text)_weaponLobbyNoticeText).alignment = TextAlignmentOptions.MidlineRight;
			((TMP_Text)_weaponLobbyNoticeText).textWrappingMode = (TextWrappingModes)0;
			((TMP_Text)_weaponLobbyNoticeText).overflowMode = (TextOverflowModes)0;
		}
	}

	private TextMeshProUGUI ResolveLobbyCompassPromptText(Canvas hudCanvas)
	{
		if ((Object)hudCanvas == (Object)null)
		{
			return null;
		}
		TextMeshProUGUI[] componentsInChildren = ((Component)hudCanvas).GetComponentsInChildren<TextMeshProUGUI>(true);
		TextMeshProUGUI val = null;
		int num = int.MinValue;
		TextMeshProUGUI[] array = componentsInChildren;
		foreach (TextMeshProUGUI val2 in array)
		{
			if ((Object)val2 == (Object)null || (Object)val2 == (Object)_weaponLobbyNoticeText)
			{
				continue;
			}
			string text2 = NormalizeLocalizedText(StripRichText(((TMP_Text)val2).text)).ToLowerInvariant();
			if (string.IsNullOrWhiteSpace(text2))
			{
				continue;
			}
			int num2 = 0;
			if (text2.Contains("指南针") || text2.Contains("compass") || text2.Contains("罗盘"))
			{
				num2 += 240;
			}
			else
			{
				continue;
			}
			if (text2.Contains("按") || text2.Contains("press"))
			{
				num2 += 80;
			}
			if (text2.Contains("生成") || text2.Contains("spawn") || text2.Contains("get"))
			{
				num2 += 50;
			}
			string text3 = (((Object)val2).name ?? string.Empty).ToLowerInvariant();
			if (text3.Contains("prompt") || text3.Contains("interact") || text3.Contains("hint"))
			{
				num2 += 20;
			}
			RectTransform rectTransform = ((TMP_Text)val2).rectTransform;
			if ((Object)rectTransform != (Object)null)
			{
				Vector3 position = ((Transform)rectTransform).position;
				if (position.y > (float)Screen.height * 0.35f)
				{
					num2 += 25;
				}
				if (position.x > (float)Screen.width * 0.35f)
				{
					num2 += 25;
				}
				Rect rect = rectTransform.rect;
				if (rect.width > 60f && rect.width < 1200f)
				{
					num2 += 10;
				}
			}
			if (num2 > num)
			{
				num = num2;
				val = val2;
			}
		}
		if ((Object)val != (Object)null && num >= 300)
		{
			return val;
		}
		return null;
	}

	private bool TryAnchorWeaponLobbyNoticeToCompassPrompt(Canvas hudCanvas, string text)
	{
		if ((Object)hudCanvas == (Object)null || (Object)_weaponLobbyNoticeRect == (Object)null || (Object)_weaponLobbyNoticeText == (Object)null)
		{
			return false;
		}
		TextMeshProUGUI val = ResolveLobbyCompassPromptText(hudCanvas);
		if ((Object)val == (Object)null)
		{
			return false;
		}
		RectTransform rectTransform = ((TMP_Text)val).rectTransform;
		if ((Object)rectTransform == (Object)null || !(((Object)rectTransform.parent) is RectTransform))
		{
			return false;
		}
		RectTransform val2 = (RectTransform)((Transform)rectTransform).parent;
		if ((Object)(object)((Transform)_weaponLobbyNoticeRect).parent != (Object)(object)val2)
		{
			((Transform)_weaponLobbyNoticeRect).SetParent((Transform)val2, false);
		}
		((TMP_Text)_weaponLobbyNoticeText).fontSize = ((TMP_Text)val).fontSize;
		((TMP_Text)_weaponLobbyNoticeText).fontStyle = ((TMP_Text)val).fontStyle;
		((TMP_Text)_weaponLobbyNoticeText).alignment = ((TMP_Text)val).alignment;
		((TMP_Text)_weaponLobbyNoticeText).lineSpacing = ((TMP_Text)val).lineSpacing;
		((TMP_Text)_weaponLobbyNoticeText).textWrappingMode = (TextWrappingModes)0;
		((TMP_Text)_weaponLobbyNoticeText).overflowMode = (TextOverflowModes)0;
		Canvas.ForceUpdateCanvases();
		float num = Mathf.Clamp(((TMP_Text)_weaponLobbyNoticeText).preferredWidth + 24f, WeaponLobbyNoticeMinWidth, WeaponLobbyNoticeWidth);
		float num2 = Mathf.Max(((TMP_Text)_weaponLobbyNoticeText).preferredHeight + 8f, GetWeaponLobbyNoticePreferredHeight(text));
		_weaponLobbyNoticeRect.anchorMin = rectTransform.anchorMin;
		_weaponLobbyNoticeRect.anchorMax = rectTransform.anchorMax;
		_weaponLobbyNoticeRect.pivot = rectTransform.pivot;
		_weaponLobbyNoticeRect.sizeDelta = new Vector2(num, num2);
		Vector2 anchoredPosition = rectTransform.anchoredPosition;
		float num3 = rectTransform.rect.height * 0.5f + num2 * 0.5f + WeaponLobbyNoticeCompassVerticalGap;
		_weaponLobbyNoticeRect.anchoredPosition = new Vector2(anchoredPosition.x, anchoredPosition.y - num3);
		((Transform)_weaponLobbyNoticeRect).SetAsLastSibling();
		return true;
	}

	private static RectTransform ResolveSharedFogBottomLeftRoot(Canvas hudCanvas)
	{
		if ((Object)hudCanvas == (Object)null)
		{
			return null;
		}
		if ((Object)_sharedFogBottomLeftRootRect == (Object)null || (Object)_sharedFogBottomLeftRootObject == (Object)null)
		{
			GameObject val = new GameObject("ShootZombiesFogBottomLeftRoot");
			_sharedFogBottomLeftRootObject = val;
			_sharedFogBottomLeftRootRect = val.AddComponent<RectTransform>();
		}
		Transform transform = ((Component)hudCanvas).transform;
		if ((Object)_sharedFogBottomLeftRootCanvas != (Object)hudCanvas || (Object)_sharedFogBottomLeftRootObject.transform.parent != (Object)transform)
		{
			_sharedFogBottomLeftRootObject.transform.SetParent(transform, false);
		}
		_sharedFogBottomLeftRootCanvas = hudCanvas;
		((Transform)_sharedFogBottomLeftRootRect).localScale = Vector3.one;
		_sharedFogBottomLeftRootRect.anchorMin = Vector2.zero;
		_sharedFogBottomLeftRootRect.anchorMax = Vector2.one;
		_sharedFogBottomLeftRootRect.pivot = new Vector2(0.5f, 0.5f);
		_sharedFogBottomLeftRootRect.offsetMin = Vector2.zero;
		_sharedFogBottomLeftRootRect.offsetMax = Vector2.zero;
		_sharedFogBottomLeftRootRect.anchoredPosition = Vector2.zero;
		_sharedFogBottomLeftRootObject.transform.SetAsLastSibling();
		return _sharedFogBottomLeftRootRect;
	}

	private static void CleanupSharedFogBottomLeftRootIfUnused()
	{
		if ((Object)_fogUIContainerRect != (Object)null || (Object)_fogModeLobbyNoticeRect != (Object)null)
		{
			return;
		}
		CleanupSharedFogBottomLeftRoot();
	}

	private static void CleanupSharedFogBottomLeftRoot()
	{
		if ((Object)_sharedFogBottomLeftRootObject != (Object)null)
		{
			Object.Destroy((Object)_sharedFogBottomLeftRootObject);
		}
		_sharedFogBottomLeftRootObject = null;
		_sharedFogBottomLeftRootRect = null;
		_sharedFogBottomLeftRootCanvas = null;
	}

	private static float GetLobbyNoticePreferredHeight(string text)
	{
		return (text != null && text.Contains("\n")) ? LobbyNoticeMultiLineHeight : LobbyNoticeSingleLineHeight;
	}

	private static float GetWeaponLobbyNoticePreferredHeight(string text)
	{
		return (text != null && text.Contains("\n")) ? WeaponLobbyNoticeMultiLineHeight : WeaponLobbyNoticeSingleLineHeight;
	}

	private static float GetScaledWeaponLobbyNoticePreferredHeight(string text)
	{
		return GetLobbyNoticePreferredHeight(text) * WeaponLobbyNoticeScaleMultiplier;
	}

	private RectTransform ResolveVersionLabelRect(Canvas hudCanvas, bool forceRefresh = false)
	{
		if ((Object)hudCanvas == (Object)null)
		{
			return null;
		}
		if (!forceRefresh && (Object)_cachedVersionLabelRect != (Object)null && (Object)((Component)_cachedVersionLabelRect).GetComponentInParent<Canvas>() == (Object)hudCanvas && ((Component)_cachedVersionLabelRect).gameObject.activeInHierarchy)
		{
			return _cachedVersionLabelRect;
		}
		if (!forceRefresh && Time.unscaledTime - _lastVersionLabelSearchTime < 1f)
		{
			return null;
		}
		_lastVersionLabelSearchTime = Time.unscaledTime;
		TextMeshProUGUI[] componentsInChildren = ((Component)hudCanvas).GetComponentsInChildren<TextMeshProUGUI>(true);
		TextMeshProUGUI val = null;
		int num = int.MinValue;
		TextMeshProUGUI[] array = componentsInChildren;
		foreach (TextMeshProUGUI val2 in array)
		{
			if ((Object)val2 == (Object)null)
			{
				continue;
			}
			string text = StripRichText(((TMP_Text)val2).text);
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			string text2 = text.ToLowerInvariant();
			string text3 = (((Object)val2).name ?? string.Empty).ToLowerInvariant() ?? string.Empty;
			int num2 = 0;
			if (text2.Contains("1.54"))
			{
				num2 += 320;
			}
			if (text2.Contains("version") || text2.Contains("ver"))
			{
				num2 += 150;
			}
			if (LooksLikeVersionToken(text2))
			{
				num2 += 120;
			}
			if (text3.Contains("version") || text3.Contains("ver"))
			{
				num2 += 80;
			}
			if (text2.Contains("fog") || text2.Contains("speed") || text2.Contains("actual"))
			{
				num2 -= 320;
			}
			if (text3.Contains("fog") || text3.Contains("timer") || text3.Contains("shootzombies"))
			{
				num2 -= 320;
			}
			if (text.Length <= 16)
			{
				num2 += 20;
			}
			RectTransform rectTransform = ((TMP_Text)val2).rectTransform;
			if ((Object)rectTransform != (Object)null)
			{
				if (((Component)rectTransform).transform.position.y > (float)Screen.height * 0.45f)
				{
					num2 += 15;
				}
				Rect rect = rectTransform.rect;
				if (rect.width > 8f)
				{
					rect = rectTransform.rect;
					if (rect.width < 320f)
					{
						num2 += 10;
					}
				}
			}
			if (num2 > num)
			{
				num = num2;
				val = val2;
			}
		}
		if ((Object)val != (Object)null && num >= 120)
		{
			_cachedVersionLabelRect = ((TMP_Text)val).rectTransform;
			return _cachedVersionLabelRect;
		}
		_cachedVersionLabelRect = null;
		return null;
	}

	private bool TryAnchorFogUiToVersion(RectTransform containerRect, Canvas hudCanvas, bool forceRefresh = false)
	{
		if ((Object)containerRect == (Object)null || (Object)hudCanvas == (Object)null)
		{
			return false;
		}
		RectTransform val = ResolveVersionLabelRect(hudCanvas, forceRefresh);
		if ((Object)val == (Object)null || (Object)((Transform)val).parent == (Object)null)
		{
			return false;
		}
		RectTransform val2 = (RectTransform)((Transform)val).parent;
		if ((Object)val2 == (Object)null)
		{
			return false;
		}
		if ((Object)(object)((Transform)containerRect).parent != (Object)(Transform)val2)
		{
			((Transform)containerRect).SetParent((Transform)val2, false);
		}
		containerRect.anchorMin = new Vector2(0f, 1f);
		containerRect.anchorMax = new Vector2(0f, 1f);
		containerRect.pivot = new Vector2(0f, 1f);
		Canvas.ForceUpdateCanvases();
		Vector3[] array = (Vector3[])(object)new Vector3[4];
		val.GetWorldCorners(array);
		Vector3 val3 = array[2];
		TextMeshProUGUI component = ((Component)val).GetComponent<TextMeshProUGUI>();
		Rect rect = val.rect;
		float num = rect.width;
		if ((Object)component != (Object)null)
		{
			num = Mathf.Max(num, ((TMP_Text)component).preferredWidth);
		}
		float num2 = Mathf.Max(FogUiVersionHorizontalGap, num * 0.08f);
		Vector3 val4 = val3 + ((Component)val).transform.right * num2;
		Vector3 val5 = ((Transform)val2).InverseTransformPoint(val4);
		float x = val5.x;
		rect = val2.rect;
		float num3 = x + rect.width * val2.pivot.x;
		float y = val5.y;
		rect = val2.rect;
		float num4 = y - rect.height * (1f - val2.pivot.y);
		containerRect.anchoredPosition = new Vector2(num3 + FogUiAnchorLeftNudge, num4 + FogUiAnchorTopNudge);
		((Transform)containerRect).SetAsLastSibling();
		return true;
	}

	private static string StripRichText(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(value.Length);
		bool flag = false;
		foreach (char c in value)
		{
			switch (c)
			{
			case '<':
				flag = true;
				continue;
			case '>':
				flag = false;
				continue;
			}
			if (!flag)
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString().Trim();
	}

	private static bool LooksLikeVersionToken(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		char[] separator = new char[17]
		{
			' ', '\n', '\r', '\t', '|', ':', '(', ')', '[', ']',
			'-', '_', '/', '\\', ',', ';', '!'
		};
		string[] array = value.Split(separator, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim().ToLowerInvariant();
			if (text.Length < 3 || text.Length > 10 || !text.Contains("."))
			{
				continue;
			}
			bool flag = false;
			bool flag2 = true;
			foreach (char c in text)
			{
				if (char.IsDigit(c))
				{
					flag = true;
					continue;
				}
				switch (c)
				{
				case '.':
				case 'a':
				case 'b':
				case 'c':
				case 'd':
				case 'e':
				case 'f':
				case 'g':
				case 'h':
				case 'i':
				case 'j':
				case 'k':
				case 'l':
				case 'm':
				case 'n':
				case 'o':
				case 'p':
				case 'q':
				case 'r':
				case 's':
				case 't':
				case 'u':
				case 'v':
				case 'w':
				case 'x':
				case 'y':
				case 'z':
					continue;
				}
				flag2 = false;
				break;
			}
			if (flag2 && flag)
			{
				return true;
			}
		}
		return false;
	}

	private void ClampFogUIToCanvas()
	{
		RectTransform val = _fogUIContainerRect;
		if ((Object)val == (Object)null && (Object)_fogUIObject != (Object)null && (Object)_fogUIObject.transform.parent != (Object)null)
		{
			val = (_fogUIContainerRect = ((Component)_fogUIObject.transform.parent).GetComponent<RectTransform>());
		}
		if ((Object)val == (Object)null)
		{
			return;
		}
		ApplySharedFogBottomLeftLayout(val, val.sizeDelta.x, val.sizeDelta.y);
	}

	private void ApplySharedFogBottomLeftLayout(RectTransform target, float width, float height)
	{
		if ((Object)target == (Object)null)
		{
			return;
		}
		Canvas val = ResolveHudCanvas();
		if ((Object)val == (Object)null)
		{
			val = ((Component)target).GetComponentInParent<Canvas>();
		}
		if ((Object)val == (Object)null)
		{
			return;
		}
		Canvas.ForceUpdateCanvases();
		RectTransform component = ((Component)val).GetComponent<RectTransform>();
		if ((Object)component == (Object)null)
		{
			return;
		}
		if ((Object)(object)((Transform)target).parent != (Object)(object)((Component)val).transform)
		{
			((Transform)target).SetParent(((Component)val).transform, false);
		}
		ApplyBottomLeftAnchoredRect(target, component, width, height, 1f, 0f, 0f);
	}

	private void SetFogUIVisible(bool visible)
	{
		if ((Object)_fogUIObject != (Object)null)
		{
			_fogUIObject.SetActive(visible);
		}
		if ((Object)_fogTimerUIObject != (Object)null)
		{
			_fogTimerUIObject.SetActive(false);
		}
		if ((Object)_fogUIContainerRect != (Object)null)
		{
			((Component)_fogUIContainerRect).gameObject.SetActive(visible);
		}
	}

	private void SendFogSpeed()
	{
	}

	private void DisableFrostDamage()
	{
	}

	private void StartFogMovement()
	{
	}

	private void CreateFogUI()
	{
		CleanupOldFogUI();
	}

	private void UpdateFogUI()
	{
		CleanupOldFogUI();
	}

	private void CleanupOldFogUI()
	{
		try
		{
			if ((Object)_fogUIObject != (Object)null && (Object)_fogUIObject.transform.parent != (Object)null)
			{
				Object.Destroy((Object)((Component)_fogUIObject.transform.parent).gameObject);
			}
			else if ((Object)_fogUIObject != (Object)null)
			{
				Object.Destroy((Object)_fogUIObject);
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			_fogUIObject = null;
			_fogTimerUIObject = null;
			_fogUIContainerRect = null;
			_cachedVersionLabelRect = null;
			CleanupSharedFogBottomLeftRootIfUnused();
		}
	}

	private static ItemInstanceData CreateItemInstanceData(int uses)
	{
		ItemInstanceData itemInstanceData = new ItemInstanceData(Guid.NewGuid());
		if (itemInstanceData.data == null)
		{
			itemInstanceData.data = new Dictionary<DataEntryKey, DataEntryValue>();
		}
		OptionableIntItemData optionableIntItemData = new OptionableIntItemData
		{
			HasData = true,
			Value = Mathf.Max(uses, 0)
		};
		itemInstanceData.data[DataEntryKey.ItemUses] = optionableIntItemData;
		ItemInstanceDataHandler.AddInstanceData(itemInstanceData);
		return itemInstanceData;
	}

	private static bool TrySetItemIntoSlot(Player player, int slotIndex, Item item, int uses)
	{
		if ((Object)player == (Object)null || (Object)item == (Object)null || player.itemSlots == null || slotIndex < 0 || slotIndex >= player.itemSlots.Length)
		{
			return false;
		}
		player.itemSlots[slotIndex].SetItem(item, CreateItemInstanceData(uses));
		return true;
	}

	private static bool TryResolveFirstAidItem(out Item item)
	{
		item = null;
		try
		{
			Type type = typeof(Item).Assembly.GetType("ItemDatabase");
			MethodInfo methodInfo = type?.GetMethod("TryGetItem", BindingFlags.Static | BindingFlags.Public, null, new Type[2]
			{
				typeof(ushort),
				typeof(Item).MakeByRefType()
			}, null);
			if (methodInfo == null)
			{
				return false;
			}
			object[] array = new object[2]
			{
				(ushort)29,
				null
			};
			if ((bool)methodInfo.Invoke(null, array) && array[1] is Item { } item2 && (Object)item2 != (Object)null)
			{
				item = item2;
				return true;
			}
			ushort[] array2 = new ushort[11]
			{
				6, 1, 2, 3, 4, 5, 10, 15, 20, 25,
				30
			};
			foreach (ushort itemID in array2)
			{
				array[0] = itemID;
				array[1] = null;
				if (!(bool)methodInfo.Invoke(null, array) || !(array[1] is Item { } item3) || (Object)item3 == (Object)null)
				{
					continue;
				}
				item = item3;
				string text = item.GetName()?.ToLowerInvariant();
				if (text != null && (text.Contains("first aid") || text.Contains("medkit") || text.Contains("bandage") || text.Contains("heal") || text.Contains("急救") || text.Contains("医疗")))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		item = null;
		return false;
	}

	private void CheckSpawnWeaponKey()
	{
		if (IsWeaponFeatureEnabled() && SpawnWeaponKey != null && (int)SpawnWeaponKey.Value != 0 && Input.GetKeyDown(SpawnWeaponKey.Value))
		{
			SpawnWeaponAtPlayer();
		}
	}

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
			Component[] components = ((Component)heldBlowgunItem).GetComponents(_actionRaycastDartType);
			if (components == null || components.Length <= 1)
			{
				return;
			}
			for (int i = 1; i < components.Length; i++)
			{
				if ((Object)components[i] != (Object)null)
				{
					Object.Destroy((Object)components[i]);
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
			if (!flag && !(num2 > 0f))
			{
				ZombieSpawner.DestroyZombie(val2);
			}
		}
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
