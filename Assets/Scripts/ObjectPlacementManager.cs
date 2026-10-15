
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public sealed class ObjectPlacementManager : MonoBehaviour
{
    public enum PlacementStatus
    {
        Valid,
        NoSurface,
        TooSteep,
        Blocked
    }

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

    [Tooltip("Steepest surface (degrees from flat) a turret can stand on.")]
    [SerializeField, Range(0f, 90f)]
    private float maxSurfaceAngle = 35f;

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

    [Header("Rotation")]
    [Tooltip("Degrees per second while holding Q / E.")]
    [SerializeField, Min(1f)]
    private float rotationSpeed = 120f;

    [Tooltip("Degrees per mouse-wheel notch.")]
    [SerializeField, Min(1f)]
    private float rotationStep = 15f;

    [Header("Preview Appearance")]
    [SerializeField, Range(0.1f, 0.9f)]
    private float previewOpacity = 0.45f;

    [SerializeField]
    private Color validColor = Color.green;

    [SerializeField]
    private Color invalidColor = Color.red;

    [Header("Range Indicator")]
    [SerializeField]
    private bool showRangeIndicator = true;

    [Tooltip("Optional. A Sprites/Default material is created if empty.")]
    [SerializeField]
    private Material rangeIndicatorMaterial;

    [SerializeField, Min(0.01f)]
    private float rangeIndicatorWidth = 0.06f;

    [SerializeField, Range(16, 128)]
    private int rangeIndicatorSegments = 64;

    private GameObject[] turretPrefabs;
    private int selectedIndex;

    private GameObject previewInstance;
    private Renderer[] previewRenderers;
    private Material[][] previewMaterials;

    private LineRenderer rangeIndicator;
    private Material runtimeRangeMaterial;
    private float previewRange;

    private readonly Collider[] overlapBuffer = new Collider[32];

    private bool isPlacing;
    private PlacementStatus status = PlacementStatus.NoSurface;
    private float previewYaw;

    public bool IsPlacing => isPlacing;
    public bool IsValidPlacement => isPlacing && status == PlacementStatus.Valid;
    public PlacementStatus Status => status;
    public float PreviewYaw => Mathf.Repeat(previewYaw, 360f);
    public int SelectedIndex => selectedIndex;
    public int TurretCount => turretPrefabs.Length;

    /// <summary>Raised with the new instance after a turret is placed.</summary>
    public event Action<GameObject> TurretPlaced;

    private void Awake()
    {
        if (placementCamera == null)
            placementCamera = Camera.main;

        turretPrefabs = new[]
        {
            bulletTurretPrefab,
            fireballTurretPrefab,
            laserTurretPrefab
        };
    }

    public GameObject GetTurretPrefab(int index)
    {
        return index >= 0 && index < turretPrefabs.Length
            ? turretPrefabs[index]
            : null;
    }

    private void Update()
    {
        if (placementCamera == null)
            return;

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard == null)
            return;

        // B toggles build mode. Weapon switching can use 1 and 2
        // normally while not placing.
        if (keyboard.bKey.wasPressedThisFrame)
        {
            if (isPlacing)
                CancelPlacement();
            else
                BeginPlacement();

            return;
        }

        if (!isPlacing)
            return;

        // Number keys select turrets only while placing.
        if (keyboard.digit1Key.wasPressedThisFrame ||
            keyboard.numpad1Key.wasPressedThisFrame)
        {
            SelectTurret(0);
            return;
        }

        if (keyboard.digit2Key.wasPressedThisFrame ||
            keyboard.numpad2Key.wasPressedThisFrame)
        {
            SelectTurret(1);
            return;
        }

        if (keyboard.digit3Key.wasPressedThisFrame ||
            keyboard.numpad3Key.wasPressedThisFrame)
        {
            SelectTurret(2);
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

        // Rotate preview: hold Q / E for smooth rotation,
        // or use the mouse wheel for fixed steps.
        if (keyboard.qKey.isPressed)
            previewYaw -= rotationSpeed * Time.deltaTime;

        if (keyboard.eKey.isPressed)
            previewYaw += rotationSpeed * Time.deltaTime;

        float scroll = mouse.scroll.ReadValue().y;

        if (scroll > 0.01f)
            previewYaw += rotationStep;
        else if (scroll < -0.01f)
            previewYaw -= rotationStep;

        if (previewInstance == null)
        {
            status = PlacementStatus.NoSurface;
            return;
        }

        // Find the surface under the mouse. Placement is free-form:
        // the turret goes exactly where the ray hits.
        if (!TryGetPlacementPoint(out Vector3 point, out Vector3 normal))
        {
            status = PlacementStatus.NoSurface;
            previewInstance.SetActive(false);
            return;
        }

        point.y += placementHeightOffset;

        previewInstance.SetActive(true);

        Quaternion rotation =
            Quaternion.Euler(0f, previewYaw, 0f);

        previewInstance.transform.SetPositionAndRotation(
            point,
            rotation
        );

        // Check whether the position is valid.
        status = EvaluatePlacement(point, normal, rotation);

        Color stateColor =
            status == PlacementStatus.Valid ? validColor : invalidColor;

        UpdatePreviewColor(stateColor);
        UpdateRangeIndicator(point, stateColor);

        // Place only at a valid position.
        if (mouse.leftButton.wasPressedThisFrame &&
            status == PlacementStatus.Valid)
        {
            PlaceObject(point, rotation);
        }
    }

    public void SelectTurret(int index)
    {
        GameObject prefab = GetTurretPrefab(index);

        if (prefab == null)
        {
            Debug.LogWarning(
                "Assign all turret prefabs in ObjectPlacementManager.",
                this
            );
            return;
        }

        selectedIndex = index;
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

        GameObject selectedPrefab = GetTurretPrefab(selectedIndex);

        if (selectedPrefab == null)
        {
            Debug.LogWarning(
                "Assign the turret prefabs in ObjectPlacementManager.",
                this
            );
            return;
        }

        // Keep the current rotation when switching turret type.
        float keptYaw = previewYaw;
        bool wasPlacing = isPlacing;

        CancelPlacement();

        // Create the preview from the selected prefab.
        previewInstance = Instantiate(selectedPrefab);

        // IMPORTANT: Disable turret behaviour on the preview.
        // The actual placed turret is instantiated separately.
        TurretController[] turretControllers =
            previewInstance.GetComponentsInChildren<TurretController>(true);

        previewRange = 0f;

        foreach (TurretController controller in turretControllers)
        {
            controller.enabled = false;
            previewRange = Mathf.Max(previewRange, controller.DetectionRange);
        }

        previewInstance.name = selectedPrefab.name + "_Preview";

        // Keep the preview from blocking placement raycasts.
        SetLayerRecursively(
            previewInstance,
            LayerMask.NameToLayer("Ignore Raycast")
        );

        previewYaw = wasPlacing
            ? keptYaw
            : selectedPrefab.transform.eulerAngles.y;

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

        CreateRangeIndicator();

        isPlacing = true;
        status = PlacementStatus.NoSurface;

        previewInstance.SetActive(false);
    }

    private bool TryGetPlacementPoint(
        out Vector3 point,
        out Vector3 normal)
    {
        point = default;
        normal = Vector3.up;

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
            normal = hit.normal;
            return true;
        }

        return false;
    }

    private PlacementStatus EvaluatePlacement(
        Vector3 position,
        Vector3 surfaceNormal,
        Quaternion rotation)
    {
        if (Vector3.Angle(surfaceNormal, Vector3.up) > maxSurfaceAngle)
            return PlacementStatus.TooSteep;

        Vector3 boxCenter =
            position + rotation * footprintOffset;

        if (Physics.CheckBox(
            boxCenter,
            footprintHalfExtents,
            rotation,
            blockingLayers,
            QueryTriggerInteraction.Ignore))
        {
            return PlacementStatus.Blocked;
        }

        // Turrets live on the Default layer, so check for them
        // separately to stop turrets being stacked on each other.
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        int mask = ignoreRaycastLayer >= 0
            ? ~(1 << ignoreRaycastLayer)
            : ~0;

        int count = Physics.OverlapBoxNonAlloc(
            boxCenter,
            footprintHalfExtents,
            overlapBuffer,
            rotation,
            mask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            if (overlapBuffer[i].GetComponentInParent<TurretController>() != null)
                return PlacementStatus.Blocked;
        }

        return PlacementStatus.Valid;
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

    private void CreateRangeIndicator()
    {
        if (!showRangeIndicator || previewRange <= 0f)
            return;

        Material material = rangeIndicatorMaterial;

        if (material == null)
        {
            if (runtimeRangeMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");

                if (shader == null)
                    return;

                runtimeRangeMaterial = new Material(shader);
            }

            material = runtimeRangeMaterial;
        }

        GameObject ring = new GameObject("RangeIndicator");
        ring.transform.SetParent(previewInstance.transform, false);
        ring.layer = previewInstance.layer;

        rangeIndicator = ring.AddComponent<LineRenderer>();
        rangeIndicator.sharedMaterial = material;
        rangeIndicator.useWorldSpace = true;
        rangeIndicator.loop = true;
        rangeIndicator.positionCount = rangeIndicatorSegments;
        rangeIndicator.widthMultiplier = rangeIndicatorWidth;
        rangeIndicator.shadowCastingMode = ShadowCastingMode.Off;
        rangeIndicator.receiveShadows = false;
    }

    private void UpdateRangeIndicator(Vector3 center, Color stateColor)
    {
        if (rangeIndicator == null)
            return;

        Color color = stateColor;
        color.a = 0.8f;
        rangeIndicator.startColor = color;
        rangeIndicator.endColor = color;

        // Lift the ring slightly so it doesn't z-fight with the ground.
        Vector3 origin = center + Vector3.up * 0.05f;
        float step = Mathf.PI * 2f / rangeIndicatorSegments;

        for (int i = 0; i < rangeIndicatorSegments; i++)
        {
            float angle = i * step;

            rangeIndicator.SetPosition(
                i,
                origin + new Vector3(
                    Mathf.Cos(angle) * previewRange,
                    0f,
                    Mathf.Sin(angle) * previewRange
                )
            );
        }
    }

    private void PlaceObject(
        Vector3 position,
        Quaternion rotation)
    {
        GameObject selectedPrefab = GetTurretPrefab(selectedIndex);

        if (status != PlacementStatus.Valid || selectedPrefab == null)
            return;

        // Instantiate the original prefab.
        // Its TurretController remains enabled.
        GameObject placed = Instantiate(
            selectedPrefab,
            position,
            rotation,
            placedObjectsParent
        );

        CancelPlacement();

        TurretPlaced?.Invoke(placed);
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
        status = PlacementStatus.NoSurface;

        // Renderer.materials made per-preview copies; free them.
        if (previewMaterials != null)
        {
            foreach (Material[] materials in previewMaterials)
            {
                foreach (Material material in materials)
                {
                    if (material != null)
                        Destroy(material);
                }
            }
        }

        if (previewInstance != null)
            Destroy(previewInstance);

        previewInstance = null;
        previewRenderers = null;
        previewMaterials = null;
        rangeIndicator = null;
    }

    private void OnDisable()
    {
        CancelPlacement();
    }

    private void OnDestroy()
    {
        if (runtimeRangeMaterial != null)
            Destroy(runtimeRangeMaterial);
    }

    private void OnDrawGizmosSelected()
    {
        if (!isPlacing || previewInstance == null)
            return;

        Gizmos.color =
            status == PlacementStatus.Valid ? validColor : invalidColor;

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
