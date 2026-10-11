
using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponManager : MonoBehaviour
{
    [Header("Weapons")]
    [SerializeField] private WeaponController shotgun;
    [SerializeField] private WeaponController machineGun;

    [Header("Input Actions")]
    [SerializeField] private InputActionReference fireAction;
    [SerializeField] private InputActionReference reloadAction;
    [SerializeField] private InputActionReference shotgunAction;
    [SerializeField] private InputActionReference machineGunAction;

    private WeaponController currentWeapon;

    private void OnEnable()
    {
        Subscribe(fireAction, OnFire);
        Subscribe(reloadAction, OnReload);
        Subscribe(shotgunAction, OnShotgunSelected);
        Subscribe(machineGunAction, OnMachineGunSelected);

        SubscribeCanceled(fireAction, OnFireCanceled);

        EnableAction(fireAction);
        EnableAction(reloadAction);
        EnableAction(shotgunAction);
        EnableAction(machineGunAction);

        EquipWeapon(shotgun);
    }

    private void OnDisable()
    {
        Unsubscribe(fireAction, OnFire);
        Unsubscribe(reloadAction, OnReload);
        Unsubscribe(shotgunAction, OnShotgunSelected);
        Unsubscribe(machineGunAction, OnMachineGunSelected);

        UnsubscribeCanceled(fireAction, OnFireCanceled);

        if (currentWeapon != null)
            currentWeapon.TriggerReleased();
    }

    private void Subscribe(
        InputActionReference reference,
        System.Action<InputAction.CallbackContext> callback)
    {
        if (reference != null && reference.action != null)
        {
            reference.action.performed += callback;
        }
        else
        {
            Debug.LogError(
                "WeaponManager: An Input Action reference is missing.",
                this
            );
        }
    }

    private void SubscribeCanceled(
        InputActionReference reference,
        System.Action<InputAction.CallbackContext> callback)
    {
        if (reference != null && reference.action != null)
            reference.action.canceled += callback;
    }

    private void Unsubscribe(
        InputActionReference reference,
        System.Action<InputAction.CallbackContext> callback)
    {
        if (reference != null && reference.action != null)
            reference.action.performed -= callback;
    }

    private void UnsubscribeCanceled(
        InputActionReference reference,
        System.Action<InputAction.CallbackContext> callback)
    {
        if (reference != null && reference.action != null)
            reference.action.canceled -= callback;
    }

    private void EnableAction(InputActionReference reference)
    {
        if (reference != null && reference.action != null)
        {
            reference.action.Enable();
        }
    }

    private void OnFire(InputAction.CallbackContext context)
    {
        if (currentWeapon != null)
            currentWeapon.TriggerPressed();
    }

    private void OnFireCanceled(InputAction.CallbackContext context)
    {
        if (currentWeapon != null)
            currentWeapon.TriggerReleased();
    }

    private void OnReload(InputAction.CallbackContext context)
    {
        if (currentWeapon != null)
            currentWeapon.Reload();
    }

    private void OnShotgunSelected(InputAction.CallbackContext context)
    {
        EquipWeapon(shotgun);
    }

    private void OnMachineGunSelected(InputAction.CallbackContext context)
    {
        EquipWeapon(machineGun);
    }

    private void EquipWeapon(WeaponController weapon)
    {
        if (weapon == null)
        {
            Debug.LogError(
                "WeaponManager: Selected weapon is not assigned.",
                this
            );
            return;
        }

        if (currentWeapon == weapon)
            return;

        if (currentWeapon != null)
            currentWeapon.TriggerReleased();

        currentWeapon = weapon;

        Debug.Log($"Equipped: {currentWeapon.gameObject.name}");
    }
}
