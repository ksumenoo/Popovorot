using UnityEngine;

/// Кнопка, рычаг, замок: по нажатию E включает флаг.
/// Может требовать предмет в руках (он тратится) и другой флаг (например, питание).
public class FlagSwitch : MonoBehaviour, IInteractable
{
    public string prompt = "ЛКМ — нажать";
    public string flag;
    [Tooltip("Предмет, который нужен в руках. Пусто — не нужен")]
    public string requiredItem;
    [Tooltip("Флаг, который должен быть включён. Пусто — не нужен")]
    public string requiredFlag;
    public string failHint = "Чего-то не хватает";
    [Tooltip("Подсказка, когда не включён нужный флаг. Пусто — показывается failHint")]
    public string flagHint = "";
    public string doneMessage;
    [Tooltip("Больше нуля — флаг включается на это число секунд")]
    public float duration;
    [Tooltip("Предмет не тратится: нужен только в сумке (рукоятка, которой крутят несколько механизмов)")]
    public bool keepItem;

    public string Prompt
    {
        get { return duration <= 0f && GameState.Has(flag) ? "" : prompt; }
    }

    public void Interact(Interactor who)
    {
        if (duration <= 0f && GameState.Has(flag)) return;

        if (!string.IsNullOrEmpty(requiredFlag) && !GameState.Has(requiredFlag))
        {
            Interactor.Say(string.IsNullOrEmpty(flagHint) ? failHint : flagHint);
            Sfx.Play("deny");
            return;
        }
        if (!string.IsNullOrEmpty(requiredItem) && !(keepItem ? who.Holds(requiredItem) : who.Consume(requiredItem)))
        {
            // Нужный предмет есть, но в руке другой — подсказываем, как его выбрать.
            string name = who.NameOf(requiredItem);
            Interactor.Say(name != null ? "Возьми в руку: " + name + "  (Q / E — сменить предмет)" : failHint);
            Sfx.Play("deny");
            return;
        }

        Sfx.Play("click");
        if (duration > 0f) GameState.SetTimed(flag, duration);
        else GameState.Set(flag, true);

        if (!string.IsNullOrEmpty(doneMessage)) Interactor.Say(doneMessage);
    }
}
