
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public sealed class ObjectPlacementManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera placementCamera;
    [SerializeField] private GameObject bulletTurretPrefab;
    [SerializeField] private GameObject fireballTurretPrefab;
    [SerializeField] private GameObject laserTurretPrefab;
    [SerializeField] private Transform placedObjectsParent;

    [Header("Surface Detection")]
    [SerializeField, Min(0.1f)]
    private float maxPlacementDistance = 100f;

    [SerializeField]
    private LayerMask placementSurfaceLayers = ~0;

    [Header("Placement Validation")]
    [Tooltip("Buildable/obstacle layers. Exclude the ground layer.")]
    [SerializeField]
    private LayerMask blockingLayers = ~0;

    [Tooltip("Half-size of the placement overlap box.")]
    [SerializeField]
    private Vector3 footprintHalfExtents =
        new Vector3(0.5f, 0.5f, 0.5f);

    [Tooltip("Offset of the validation box from the object's pivot.")]
    [SerializeField]
    private Vector3 footprintOffset =
        new Vector3(0f, 0.5f, 0f);

    [Tooltip("Raises the object pivot above the surface.")]
    [SerializeField]
    private float placementHeightOffset = 0f;

    [Header("Grid Snapping")]
    [SerializeField]
    private bool gridSnappingEnabled = false;

    [SerializeField, Min(0.1f)]
    private float gridSize = 1f;

    [Header("Rotation")]
    [SerializeField, Min(1f)]
    private float rotationStep = 15f;

    [Header("Preview Appearance")]
    [SerializeField, Range(0.1f, 0.9f)]
    private float previewOpacity = 0.45f;

    [SerializeField]
    private Color validColor = Color.green;

    [SerializeField]
    private Color invalidColor = Color.red;

    private GameObject selectedPrefab;
    private GameObject previewInstance;

    private Renderer[] previewRenderers;
    private Material[][] previewMaterials;

    private bool isPlacing;
    private bool isValidPlacement;
    private float previewYaw;

    public bool IsPlacing => isPlacing;
    public bool IsValidPlacement => isValidPlacement;

    private void Awake()
    {
        if (placementCamera == null)
            placementCamera = Camera.main;

        selectedPrefab = bulletTurretPrefab;
    }

    private void Update()
    {
        if (placementCamera == null)
            return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard == null)
            return;

        // Outside placement mode, only B starts building.
        // Weapon switching can use 1 and 2 normally.
        if (!isPlacing)
        {
            if (keyboard.bKey.wasPressedThisFrame)
                BeginPlacement();

            return;
        }

        // Number keys select turrets only while placing.
        if (keyboard.digit1Key.wasPressedThisFrame ||
            keyboard.numpad1Key.wasPressedThisFrame)
        {
            SelectTurret(bulletTurretPrefab);
            return;
        }

        if (keyboard.digit2Key.wasPressedThisFrame ||
            keyboard.numpad2Key.wasPressedThisFrame)
        {
            SelectTurret(fireballTurretPrefab);
            return;
        }

        if (keyboard.digit3Key.wasPressedThisFrame ||
            keyboard.numpad3Key.wasPressedThisFrame)
        {
            SelectTurret(laserTurretPrefab);
            return;
        }

        if (mouse == null)
            return;

        // Cancel placement.
        if (keyboard.escapeKey.wasPressedThisFrame ||
            mouse.rightButton.wasPressedThisFrame)
        {
            CancelPlacement();
            return;
        }

        // Rotate preview.
        if (keyboard.qKey.wasPressedThisFrame)
            previewYaw -= rotationStep;

        if (keyboard.eKey.wasPressedThisFrame)
            previewYaw += rotationStep;

        // Toggle grid snapping.
        if (keyboard.gKey.wasPressedThisFrame)
            gridSnappingEnabled = !gridSnappingEnabled;

        // Find the surface under the mouse.
        if (!TryGetPlacementPoint(out Vector3 point))
        {
            isValidPlacement = false;

            if (previewInstance != null)
                previewInstance.SetActive(false);

            return;
        }

        // Snap position to grid if enabled.
        if (gridSnappingEnabled)
        {
            point.x =
                Mathf.Round(point.x / gridSize) * gridSize;

            point.z =
                Mathf.Round(point.z / gridSize) * gridSize;
        }

        point.y += placementHeightOffset;

        if (previewInstance == null)
        {
            isValidPlacement = false;
            return;
        }

        previewInstance.SetActive(true);

        Quaternion rotation =
            Quaternion.Euler(0f, previewYaw, 0f);

        previewInstance.transform.SetPositionAndRotation(
            point,
            rotation
        );

        // Check whether the position is valid.
        isValidPlacement = IsLocationValid(point, rotation);

        UpdatePreviewColor(
            isValidPlacement ? validColor : invalidColor
        );

        // Place only at a valid position.
        if (mouse.leftButton.wasPressedThisFrame &&
            isValidPlacement)
        {
            PlaceObject(point, rotation);
        }
    }

    private void SelectTurret(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning(
                "Assign all turret prefabs in ObjectPlacementManager.",
                this
            );
            return;
        }

        selectedPrefab = prefab;
        BeginPlacement();
    }

    public void BeginPlacement()
    {
        if (placementCamera == null)
        {
            Debug.LogWarning(
                "Assign the Placement Camera.",
                this
            );
            return;
        }

        if (selectedPrefab == null)
        {
            Debug.LogWarning(
                "Assign the turret prefabs in ObjectPlacementManager.",
                this
            );
            return;
        }

        CancelPlacement();

        // Create the preview from the selected prefab.
        previewInstance = Instantiate(selectedPrefab);

        // IMPORTANT: Disable turret behaviour on the preview.
        // The actual placed turret is instantiated separately.
        TurretController[] turretControllers =
            previewInstance.GetComponentsInChildren<TurretController>(true);

        foreach (TurretController controller in turretControllers)
        {
            controller.enabled = false;
        }

        previewInstance.name = selectedPrefab.name + "_Preview";

        // Keep the preview from blocking placement raycasts.
        SetLayerRecursively(
            previewInstance,
            LayerMask.NameToLayer("Ignore Raycast")
        );

        previewYaw = selectedPrefab.transform.eulerAngles.y;

        previewRenderers =
            previewInstance.GetComponentsInChildren<Renderer>(true);

        previewMaterials =
            new Material[previewRenderers.Length][];

        for (int i = 0; i < previewRenderers.Length; i++)
        {
            previewMaterials[i] = previewRenderers[i].materials;

            foreach (Material material in previewMaterials[i])
            {
                ConfigureTransparentMaterial(material);
            }
        }

        isPlacing = true;
        isValidPlacement = false;

        previewInstance.SetActive(false);
    }

    private bool TryGetPlacementPoint(out Vector3 point)
    {
        point = default;

        if (Mouse.current == null || placementCamera == null)
            return false;

        Ray ray = placementCamera.ScreenPointToRay(
            Mouse.current.position.ReadValue()
        );

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            maxPlacementDistance,
            placementSurfaceLayers,
            QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            return true;
        }

        return false;
    }

    private bool IsLocationValid(
        Vector3 position,
        Quaternion rotation)
    {
        Vector3 boxCenter =
            position + rotation * footprintOffset;

        Collider[] overlaps = Physics.OverlapBox(
            boxCenter,
            footprintHalfExtents,
            rotation,
            blockingLayers,
            QueryTriggerInteraction.Ignore
        );

        return overlaps.Length == 0;
    }

    private void UpdatePreviewColor(Color stateColor)
    {
        if (previewMaterials == null)
            return;

        foreach (Material[] materials in previewMaterials)
        {
            foreach (Material material in materials)
            {
                if (material == null)
                    continue;

                Color color = stateColor;
                color.a = previewOpacity;

                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", color);
                }
                else if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", color);
                }
            }
        }
    }

    private void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);

            material.SetInt(
                "_SrcBlend",
                (int)BlendMode.SrcAlpha
            );

            material.SetInt(
                "_DstBlend",
                (int)BlendMode.OneMinusSrcAlpha
            );

            material.SetInt("_ZWrite", 0);

            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);

        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);

        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void PlaceObject(
        Vector3 position,
        Quaternion rotation)
    {
        if (!isValidPlacement || selectedPrefab == null)
            return;

        // Instantiate the original prefab.
        // Its TurretController remains enabled.
        Instantiate(
            selectedPrefab,
            position,
            rotation,
            placedObjectsParent
        );

        CancelPlacement();
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        if (layer < 0)
            return;

        obj.layer = layer;

        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    public void CancelPlacement()
    {
        isPlacing = false;
        isValidPlacement = false;

        if (previewInstance != null)
            Destroy(previewInstance);

        previewInstance = null;
        previewRenderers = null;
        previewMaterials = null;
    }

    private void OnDisable()
    {
        CancelPlacement();
    }

    private void OnDrawGizmosSelected()
    {
        if (!isPlacing || previewInstance == null)
            return;

        Gizmos.color =
            isValidPlacement ? validColor : invalidColor;

        Vector3 center =
            previewInstance.transform.position +
            previewInstance.transform.rotation * footprintOffset;

        Gizmos.matrix = Matrix4x4.TRS(
            center,
            previewInstance.transform.rotation,
            Vector3.one
        );

        Gizmos.DrawWireCube(
            Vector3.zero,
            footprintHalfExtents * 2f
        );
    }
}
