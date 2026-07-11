namespace Delegat.Infrastructure.Interfaces;

/// <summary>
/// Interface for entities that can be created from a form
/// Replaces Zorro.Data.Interfaces.IAddable
/// </summary>
/// <typeparam name="TForm">The form type for adding the entity</typeparam>
public interface IAddable<TForm>
{
    bool AddFill(TForm form);
}
