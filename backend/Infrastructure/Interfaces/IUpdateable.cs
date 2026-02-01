namespace PrintO.Infrastructure.Interfaces;

/// <summary>
/// Interface for entities that can be updated from a form
/// Replaces Zorro.Data.Interfaces.IUpdateable
/// </summary>
/// <typeparam name="TForm">The form type for updating the entity</typeparam>
public interface IUpdateable<TForm>
{
    bool UpdateFill(TForm form);
}
