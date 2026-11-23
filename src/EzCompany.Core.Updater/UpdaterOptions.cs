namespace EzCompany.Core.Updater;

public sealed class UpdaterOptions
{
    public const string SectionName = "Updater";

    /// <summary>
    /// Public GitHub repository URL configured in <c>appsettings.json</c>. Zero PATs are embedded.
    /// </summary>
    public string GitHubRepositoryUrl { get; set; } = "https://github.com/<OWNER>/EzCompany";

    public string DefaultChannel { get; set; } = "stable";
}
