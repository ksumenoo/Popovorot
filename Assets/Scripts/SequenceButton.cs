using UnityEngine;

/// Одна кнопка замка SequenceLock.
public class SequenceButton : MonoBehaviour, IInteractable
{
    public SequenceLock target;
    public int index;

    public string Prompt
    {
        get { return target != null && GameState.Has(target.flag) ? "" : "ЛКМ — нажать"; }
    }

    public void Interact(Interactor who)
    {
        Sfx.Play("click");
        if (target != null) target.Press(index);
    }
}
