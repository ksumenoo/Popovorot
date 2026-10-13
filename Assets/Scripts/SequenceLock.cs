using UnityEngine;

/// Замок на последовательность кнопок. Верная последовательность включает флаг.
public class SequenceLock : MonoBehaviour
{
    [Tooltip("Номера кнопок в правильном порядке")]
    public int[] expected = new int[0];
    public string flag;
    public string doneMessage = "Замок открыт";

    int progress;

    public void Press(int index)
    {
        if (GameState.Has(flag)) return;

        if (index == expected[progress]) progress++;
        else progress = index == expected[0] ? 1 : 0;

        if (progress >= expected.Length)
        {
            GameState.Set(flag, true);
            Interactor.Say(doneMessage);
        }
    }
}
