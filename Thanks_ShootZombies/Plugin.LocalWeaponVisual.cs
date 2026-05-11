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
}
