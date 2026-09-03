using System.Text.RegularExpressions;

namespace HybridDeviceTagger.Services;

internal static partial class ActiveDirectorySearchPath
{
    internal static bool IsForestRelative(string path) =>
        !DomainComponent().IsMatch(path);

    internal static bool IsOuPath(string path) =>
        SplitComponents(path).All(component =>
            component.TrimStart().StartsWith("OU=", StringComparison.OrdinalIgnoreCase));

    internal static string CombineWithDomain(string ouPath, string domainNamingContext) =>
        $"{ouPath.Trim().TrimEnd(',')},{domainNamingContext.Trim()}";

    internal static string GetDomainDnsName(string distinguishedName) =>
        string.Join(
            '.',
            DomainComponentValue().Matches(distinguishedName)
                .Select(match => match.Groups[1].Value.Trim()));

    private static string[] SplitComponents(string path) =>
        UnescapedComma().Split(path.Trim().TrimEnd(','));

    [GeneratedRegex(@"(?:^|(?<!\\),)\s*DC\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex DomainComponent();

    [GeneratedRegex(@"(?:^|(?<!\\),)\s*DC\s*=\s*([^,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DomainComponentValue();

    [GeneratedRegex(@"(?<!\\),")]
    private static partial Regex UnescapedComma();
}