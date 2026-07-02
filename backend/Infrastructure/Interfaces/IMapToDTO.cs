namespace Delegat.Infrastructure.Interfaces;

/// <summary>
/// Interface for entities that can be mapped to a DTO
/// Replaces Zorro.Data.Interfaces.IDTO
/// </summary>
/// <typeparam name="TDTO">The DTO type to map to</typeparam>
public interface IMapToDTO<TDTO>
{
    TDTO MapToDTO(IServiceProvider serviceProvider);
}
