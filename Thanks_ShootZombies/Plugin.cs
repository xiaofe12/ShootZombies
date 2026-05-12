﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
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

[BepInPlugin("com.github.Thanks.ShootZombies", "ShootZombies", "1.3.6")]
[BepInDependency("com.github.PEAKModding.PEAKLib.Core", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.github.PEAKModding.PEAKLib.Items", BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.github.PEAKModding.PEAKLib.ModConfig", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("PEAKModding.ModConfig", BepInDependency.DependencyFlags.SoftDependency)]
public partial class Plugin : BaseUnityPlugin
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

	public const string Version = "1.3.6";

	private const string CanonicalConfigFileName = "Thanks.ShootZombies.cfg";

	private const string PreviousCanonicalConfigFileName = "com.github.Thanks.ShootZombies.cfg";

	private const string LegacyCanonicalConfigFileName = "com.github.PeakTest.ShootZombies.cfg";

	private const string LocalizedConfigMirrorFileName = "Thanks.ShootZombies.localized.cfg";

	private const string PreviousLocalizedConfigMirrorFileName = "com.github.Thanks.ShootZombies.localized.cfg";

	private const string LegacyLocalizedConfigMirrorFileName = "com.github.PeakTest.ShootZombies.localized.cfg";

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

	private const float DefaultZombieKnockbackForce = 200f;

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

	private const byte ZombieHealthEventCode = 184;

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

	private const float ScheduledZombieHealthBarInterval = 0.25f;

	private const float ScheduledLocalCharacterRefreshInterval = 0.1f;

	private const float ScheduledPendingRemoteGrantInterval = 0.25f;

	private const float ScheduledWeaponOwnershipInterval = 0.25f;

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

	private bool _lastModEnabled;

	private bool _lastWeaponEnabled;

	private bool _restoredVanillaBlowgunFunctionalityForDisabledFeature;

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

	private static readonly bool DisableModConfigRuntimePatches = false;

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

	private float _nextZombieHealthBarRefreshTime = -10f;

	private float _nextLocalCharacterRefreshTime = -10f;

	private float _nextPendingRemoteGrantProcessTime = -10f;

	private float _nextWeaponOwnershipUpdateTime = -10f;

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
			PatchModConfigProcessEntriesBug(val);
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

	private void PatchModConfigProcessEntriesBug(Harmony harmony)
	{
		if (harmony == null)
		{
			return;
		}
		try
		{
			Type type = ResolveLoadedType("PEAKLib.ModConfig.ModConfigPlugin", "com.github.PEAKModding.PEAKLib.ModConfig");
			MethodInfo methodInfo = type?.GetMethod("ProcessModEntries", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo methodInfo2 = AccessTools.Method(typeof(Plugin), "ModConfigProcessModEntriesPrefix", Type.EmptyTypes, (Type[])null);
			if (methodInfo != null && methodInfo2 != null)
			{
				harmony.Patch((MethodBase)methodInfo, new HarmonyMethod(methodInfo2), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning((object)("[ShootZombies] PatchModConfigProcessEntriesBug failed: " + DescribeReflectionException(ex)));
		}
	}

	private static bool ModConfigProcessModEntriesPrefix()
	{
		return !TryProcessModConfigEntriesWithoutEarlyReturn();
	}

	private static bool TryProcessModConfigEntriesWithoutEarlyReturn()
	{
		try
		{
			Type type = ResolveLoadedType("PEAKLib.ModConfig.ModConfigPlugin", "com.github.PEAKModding.PEAKLib.ModConfig");
			if (type == null)
			{
				return false;
			}
			Type type2 = type.Assembly.GetType("PEAKLib.ModConfig.Components.ModSectionNames");
			Type type3 = type.Assembly.GetType("PEAKLib.ModConfig.SettingsHandlerUtility");
			Type type4 = type.Assembly.GetType("PEAKLib.ModConfig.Components.ModKeyToName");
			if (type2 == null || type3 == null)
			{
				return false;
			}
			List<ConfigEntryBase> modConfigProcessedEntries = GetModConfigProcessedEntries(type);
			if (modConfigProcessedEntries == null)
			{
				return false;
			}
			IList modConfigModdedKeys = GetModConfigModdedKeys(type);
			MethodInfo method = type2.GetMethod("SetMod", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			MethodInfo methodInfo = type2.GetMethod("CheckSectionName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null || methodInfo == null)
			{
				return false;
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
			foreach (PluginInfo item in Chainloader.PluginInfos.Values.OrderBy((PluginInfo p) => p.Metadata?.Name))
			{
				if (item?.Instance == null || item.Instance.Config == null)
				{
					continue;
				}
				string text = FixModConfigPluginName(item.Metadata?.Name);
				if (string.IsNullOrWhiteSpace(text) || !hashSet.Add(text))
				{
					continue;
				}
				object obj = method.Invoke(null, new object[1] { text });
				foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> item2 in item.Instance.Config)
				{
					ConfigEntryBase value = item2.Value;
					if (value == null || !ShouldExposeModConfigEntry(value) || modConfigProcessedEntries.Contains(value))
					{
						continue;
					}
					try
					{
						methodInfo.Invoke(obj, new object[1] { value.Definition.Section });
						RegisterModConfigEntry(type, type3, type4, modConfigModdedKeys, value, text);
						modConfigProcessedEntries.Add(value);
					}
					catch (Exception ex)
					{
						Log?.LogWarning((object)("[ShootZombies] ModConfig entry registration failed for [" + text + "] " + value.Definition.Key + ": " + DescribeReflectionException(ex)));
					}
				}
			}
			return true;
		}
		catch (Exception ex2)
		{
			Log?.LogWarning((object)("[ShootZombies] TryProcessModConfigEntriesWithoutEarlyReturn failed: " + DescribeReflectionException(ex2)));
			return false;
		}
	}

	private static List<ConfigEntryBase> GetModConfigProcessedEntries(Type modConfigPluginType)
	{
		return GetStaticModConfigMemberValue(modConfigPluginType, "EntriesProcessed", "<EntriesProcessed>k__BackingField") as List<ConfigEntryBase>;
	}

	private static IList GetModConfigModdedKeys(Type modConfigPluginType)
	{
		return GetStaticModConfigMemberValue(modConfigPluginType, "ModdedKeys", "<ModdedKeys>k__BackingField") as IList;
	}

	private static object GetStaticModConfigMemberValue(Type type, string propertyName, string fieldName)
	{
		if (type == null)
		{
			return null;
		}
		PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if (property != null)
		{
			return property.GetValue(null);
		}
		return type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
	}

	private static bool ShouldExposeModConfigEntry(ConfigEntryBase entry)
	{
		object[] tags = entry?.Description?.Tags;
		return tags == null || !tags.Contains("Hidden");
	}

	private static void RegisterModConfigEntry(Type modConfigPluginType, Type settingsUtilityType, Type modKeyToNameType, IList moddedKeys, ConfigEntryBase entry, string tabName)
	{
		Type settingType = entry.SettingType;
		if (settingType == typeof(bool))
		{
			InvokeModConfigSettingMethod(settingsUtilityType, "AddBoolToTab", entry, tabName, new Action<bool>(delegate(bool newVal)
			{
				entry.BoxedValue = newVal;
			}));
			return;
		}
		if (settingType == typeof(float))
		{
			InvokeModConfigSettingMethod(settingsUtilityType, "AddFloatToTab", entry, tabName, new Action<float>(delegate(float newVal)
			{
				entry.BoxedValue = newVal;
			}));
			return;
		}
		if (settingType == typeof(double))
		{
			InvokeModConfigSettingMethod(settingsUtilityType, "AddDoubleToTab", entry, tabName, new Action<double>(delegate(double newVal)
			{
				entry.BoxedValue = newVal;
			}));
			return;
		}
		if (settingType == typeof(int))
		{
			InvokeModConfigSettingMethod(settingsUtilityType, "AddIntToTab", entry, tabName, new Action<int>(delegate(int newVal)
			{
				entry.BoxedValue = newVal;
			}));
			return;
		}
		if (settingType == typeof(string))
		{
			string text = (entry.DefaultValue as string) ?? string.Empty;
			if (text.Length > 4 && IsValidModConfigInputPath(modConfigPluginType, text))
			{
				TryAddModConfigModdedKey(modKeyToNameType, moddedKeys, entry, tabName);
			}
			Action<string> action = delegate(string newVal)
			{
				entry.BoxedValue = newVal;
			};
			if (entry.Description?.AcceptableValues is AcceptableValueList<string>)
			{
				InvokeModConfigSettingMethod(settingsUtilityType, "AddEnumToTab", entry, tabName, false, action);
			}
			else
			{
				InvokeModConfigSettingMethod(settingsUtilityType, "AddStringToTab", entry, tabName, action);
			}
			return;
		}
		if (settingType == typeof(KeyCode))
		{
			TryAddModConfigModdedKey(modKeyToNameType, moddedKeys, entry, tabName);
			InvokeModConfigSettingMethod(settingsUtilityType, "AddKeybindToTab", entry, tabName, new Action<KeyCode>(delegate(KeyCode newVal)
			{
				entry.BoxedValue = newVal;
			}));
			return;
		}
		if (settingType.IsEnum)
		{
			InvokeModConfigSettingMethod(settingsUtilityType, "AddEnumToTab", entry, tabName, true, new Action<string>(delegate(string newVal)
			{
				try
				{
					entry.BoxedValue = Enum.Parse(settingType, newVal);
				}
				catch
				{
				}
			}));
			return;
		}
		Log?.LogWarning((object)$"[ShootZombies] Missing ModConfig SettingType: [Mod: {tabName}] {entry.Definition.Key} (Type: {entry.SettingType})");
	}

	private static void InvokeModConfigSettingMethod(Type settingsUtilityType, string methodName, params object[] args)
	{
		MethodInfo method = settingsUtilityType?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
		{
			throw new MissingMethodException(settingsUtilityType?.FullName ?? "PEAKLib.ModConfig.SettingsHandlerUtility", methodName);
		}
		try
		{
			method.Invoke(null, args);
		}
		catch (TargetInvocationException ex)
		{
			throw ex.InnerException ?? ex;
		}
	}

	private static void TryAddModConfigModdedKey(Type modKeyToNameType, IList moddedKeys, ConfigEntryBase entry, string tabName)
	{
		if (modKeyToNameType == null || moddedKeys == null || entry == null)
		{
			return;
		}
		try
		{
			ConstructorInfo constructor = modKeyToNameType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[2]
			{
				typeof(ConfigEntryBase),
				typeof(string)
			}, null);
			if (constructor != null)
			{
				moddedKeys.Add(constructor.Invoke(new object[2] { entry, tabName }));
			}
		}
		catch
		{
		}
	}

	private static bool IsValidModConfigInputPath(Type modConfigPluginType, string path)
	{
		try
		{
			MethodInfo method = modConfigPluginType?.GetMethod("IsValidPath", BindingFlags.Static | BindingFlags.NonPublic);
			return method != null && method.Invoke(null, new object[1] { path }) is bool result && result;
		}
		catch
		{
			return false;
		}
	}

	private static string FixModConfigPluginName(string input)
	{
		input = input ?? string.Empty;
		input = System.Text.RegularExpressions.Regex.Replace(input, "([a-z])([A-Z])", "$1 $2");
		input = System.Text.RegularExpressions.Regex.Replace(input, "([A-Z])([A-Z][a-z])", "$1 $2");
		input = System.Text.RegularExpressions.Regex.Replace(input, "\\s+", " ");
		input = System.Text.RegularExpressions.Regex.Replace(input, "([A-Z]\\.)\\s([A-Z]\\.)", "$1$2");
		return input.Trim();
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
		return BlowgunInfiniteUsePatch.IsBlowgunItem(item);
	}
}
