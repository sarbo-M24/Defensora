
using System.Collections;
using UnityEngine;

public enum WeaponFireMode
{
    SemiAutomatic,
    Automatic
}

public class WeaponController : MonoBehaviour
{
    [Header("Weapon Configuration")]
    [SerializeField] private WeaponFireMode fireMode =
        WeaponFireMode.Automatic;

    [SerializeField, Min(1)] private int magazineCapacity = 30;
    [SerializeField, Min(1)] private int pelletsPerShot = 1;
    [SerializeField, Min(1f)] private float roundsPerMinute = 600f;
    [SerializeField, Min(0.01f)] private float reloadDuration = 1.5f;

    [Header("Ammunition")]
    [SerializeField, Min(0)] private int startingReserveAmmo = 90;

    [Tooltip("Reload automatically when the magazine runs dry.")]
    [SerializeField] private bool autoReload = true;

    [Header("Hitscan Settings")]
    [SerializeField, Min(0f)] private float damagePerPellet = 10f;
    [SerializeField, Min(0.1f)] private float weaponRange = 100f;
    [SerializeField, Range(0f, 45f)] private float spreadAngle = 0.5f;

    private HitscanShooter hitscanShooter;

    private int currentAmmo;
    private int reserveAmmo;
    private bool isFiring;
    private bool isReloading;
    private float nextShotTime;
    private Coroutine reloadCoroutine;

    public WeaponFireMode FireMode => fireMode;
    public int CurrentAmmo => currentAmmo;
    public int ReserveAmmo => reserveAmmo;
    public int MagazineCapacity => magazineCapacity;
    public bool IsReloading => isReloading;

    private float FireInterval => 60f / roundsPerMinute;

    private void Awake()
    {
        magazineCapacity = Mathf.Max(1, magazineCapacity);
        pelletsPerShot = Mathf.Max(1, pelletsPerShot);
        roundsPerMinute = Mathf.Max(1f, roundsPerMinute);
        reloadDuration = Mathf.Max(0.01f, reloadDuration);
        startingReserveAmmo = Mathf.Max(0, startingReserveAmmo);

        currentAmmo = magazineCapacity;
        reserveAmmo = startingReserveAmmo;

        hitscanShooter = GetComponentInParent<HitscanShooter>();

        if (hitscanShooter == null)
        {
            Debug.LogError(
                $"{name}: No HitscanShooter found on this object or its parents.",
                this
            );
        }
    }

    private void Update()
    {
        if (!isFiring || isReloading)
            return;

        if (currentAmmo <= 0)
        {
            StopFiring();
            Debug.Log($"{name}: Magazine empty. Reload required.");
            TryAutoReload();
            return;
        }

        if (Time.time >= nextShotTime)
        {
            FireOneRound();
            nextShotTime = Time.time + FireInterval;
        }
    }

    public void TriggerPressed()
    {
        if (isReloading || isFiring)
            return;

        if (currentAmmo <= 0)
        {
            Debug.Log($"{name}: Magazine empty. Press R to reload.");
            TryAutoReload();
            return;
        }

        if (fireMode == WeaponFireMode.SemiAutomatic)
        {
            FireOneRound();
        }
        else
        {
            isFiring = true;
            nextShotTime = Time.time;
        }
    }

    private void TryAutoReload()
    {
        if (autoReload && reserveAmmo > 0)
            Reload();
    }

    public void TriggerReleased()
    {
        StopFiring();
    }

    private void StopFiring()
    {
        isFiring = false;
    }

    private void FireOneRound()
    {
        if (isReloading || currentAmmo <= 0)
            return;

        currentAmmo--;

        Debug.Log(
            $"{name}: Shot fired. Ammo: {currentAmmo}/{magazineCapacity}. " +
            $"Reserve: {reserveAmmo}"
        );

        if (hitscanShooter != null)
        {
            hitscanShooter.Fire(
                pelletsPerShot,
                damagePerPellet,
                weaponRange,
                spreadAngle
            );
        }

        if (currentAmmo <= 0)
            TryAutoReload();
    }

    public void Reload()
    {
        if (isReloading ||
            currentAmmo >= magazineCapacity ||
            reserveAmmo <= 0)
        {
            if (reserveAmmo <= 0)
                Debug.Log($"{name}: No reserve ammunition.");

            return;
        }

        StopFiring();
        reloadCoroutine = StartCoroutine(ReloadRoutine());
    }

    private IEnumerator ReloadRoutine()
    {
        isReloading = true;

        Debug.Log($"{name}: Reloading...");

        yield return new WaitForSeconds(reloadDuration);

        int spaceInMagazine = magazineCapacity - currentAmmo;
        int ammoToLoad = Mathf.Min(spaceInMagazine, reserveAmmo);

        currentAmmo += ammoToLoad;
        reserveAmmo -= ammoToLoad;

        isReloading = false;
        reloadCoroutine = null;

        Debug.Log(
            $"{name}: Reload complete. Ammo: {currentAmmo}/{magazineCapacity}. " +
            $"Reserve: {reserveAmmo}"
        );
    }

    private void OnDisable()
    {
        StopFiring();

        if (reloadCoroutine != null)
        {
            StopCoroutine(reloadCoroutine);
            reloadCoroutine = null;
        }

        isReloading = false;
    }
}
