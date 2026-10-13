using UnityEngine;

/// Предмет, который можно взять в руки.
[RequireComponent(typeof(Rigidbody))]
public class Pickup : MonoBehaviour, IInteractable
{
    public string itemId;
    public string displayName = "предмет";
    [Tooltip("Подсказка, которая появляется, когда предмет подбирают")]
    public string description = "";

    public string Prompt { get { return "ЛКМ — взять: " + displayName; } }

    public void Interact(Interactor who)
    {
        who.Take(this);
    }

    public void OnTaken(Transform hold, bool silent = false)
    {
        GetComponent<Rigidbody>().isKinematic = true;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        transform.SetParent(hold, true);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        if (!silent) Sfx.Play("pickup");
    }

    public void OnDropped(Vector3 position)
    {
        transform.SetParent(null, true);
        transform.position = position;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
        GetComponent<Rigidbody>().isKinematic = false;
        Sfx.Play("drop");
    }
}
