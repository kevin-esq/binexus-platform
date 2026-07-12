namespace Binexus.Platform.Ids;

/// <summary>Central UUID v7 generation for persistent identifiers.</summary>
public interface IIdGenerator
{
    Guid NewId();
}
