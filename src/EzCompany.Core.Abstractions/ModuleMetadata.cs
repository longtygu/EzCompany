namespace EzCompany.Core.Abstractions;

/// <summary>
/// Declarative descriptor exposed by every <see cref="IAppModule"/> plugin.
/// </summary>
public sealed record ModuleMetadata(
    string ModuleId,
    string DisplayName,
    string Description,
    string IconGlyph,
    string Version,
    string Category,
    int DisplayOrder,
    IReadOnlyList<string> RequiredPermissions);
