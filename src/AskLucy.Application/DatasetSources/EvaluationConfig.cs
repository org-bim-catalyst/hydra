using AskLucy.Application.Abstractions;
using FluentValidation;

namespace AskLucy.Application.DatasetSources;

/// <summary>
/// Configuration class for data source evaluation rules and supported domains.
/// </summary>
public class EvaluationConfig
{
    /// <summary>
    /// Gets or sets the supported open data source domains.
    /// </summary>
    public IList<string> SupportedDomains { get; set; } = new List<string>
    {
        "ea.gov.om",
        "arcgis.com",
        "humdata.org",
        "opendata.gov.om",
        "opaz.gov.om",
        "geoportal.un.org"
    };

    /// <summary>
    /// Gets or sets the maximum age (in days) for source data to be considered recent.
    /// </summary>
    public int MaxDataAgeDays { get; set; } = 365;

    /// <summary>
    /// Gets or sets the maximum URL length allowed for a data source.
    /// </summary>
    public int MaxUrlLength { get; set; } = 2048;

    /// <summary>
    /// Gets or sets the allowed query parameter characters for data source URLs.
    /// </summary>
    public string AllowedQueryCharacters { get; set; } = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~:/?#[]@!";

    /// <summary>
    /// Validates that a source URL is well-formed and belongs to a supported domain.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <returns>True if the URL is valid, false otherwise.</returns>
    public bool IsValidDomain(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            var uri = new Uri(url, UriKind.Absolute);
            return SupportedDomains.Any(d => uri.Host.Contains(d, System.StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates the source configuration model.
    /// </summary>
    public class EvaluationConfigValidator : AbstractValidator<EvaluationConfig>
    {
        public EvaluationConfigValidator()
        {
            RuleFor(x => x.SupportedDomains)
                .NotEmpty()
                .Must(domains => domains.Count >= 1)
                .WithMessage("At least one supported domain must be configured.");

            RuleForEach(x => x.SupportedDomains)
                .Cascade(CascadeMode.Stop)
                .Must(BeAValidUrlFormat)
                .WithMessage("Each supported domain must be a valid URL format.");

            RuleFor(x => x.MaxDataAgeDays)
                .GreaterThan(0)
                .WithMessage("MaxDataAgeDays must be greater than 0.");

            RuleFor(x => x.MaxUrlLength)
                .GreaterThan(0)
                .WithMessage("MaxUrlLength must be greater than 0.");
        }

        private static bool BeAValidUrlFormat(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                return false;

            // SupportedDomains holds bare host names ("ea.gov.om"), not absolute URLs.
            return Uri.CheckHostName(domain.Trim()) != UriHostNameType.Unknown;
        }
    }
}
