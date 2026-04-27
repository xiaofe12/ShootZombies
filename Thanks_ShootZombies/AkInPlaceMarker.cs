using UnityEngine;

namespace ShootZombies;

public sealed class AkInPlaceMarker : MonoBehaviour
{
	public bool hasBaseRotation;

	public bool hasBaseTransform;

	public bool hasOriginalVisual;

	public Vector3 baseLocalPosition = Vector3.zero;

	public Quaternion baseLocalRotation = Quaternion.identity;

	public Vector3 baseLocalScale = Vector3.one;

	public Mesh originalMesh;

	public Material[] originalMaterials;
}
