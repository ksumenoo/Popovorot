/// Всё, на что можно навести прицел и нажать E.
public interface IInteractable
{
    /// Подсказка под прицелом. Пустая строка — подсказки нет.
    string Prompt { get; }
    void Interact(Interactor who);
}
