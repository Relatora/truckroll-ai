using System.Text.RegularExpressions;

namespace Truckroll.Api.Tests.Access;

/// <summary>
/// Keeps the Access layer the only way to CRM data. A reviewer can miss one
/// <c>db.Customers</c> in a 400-line PR; this can't.
/// </summary>
public sealed partial class AccessConventionTests
{
    /// <summary>
    /// Folders allowed to touch the DbSets directly: the Access layer itself, data setup, and the
    /// development-only reset. Anything else that needs an exception should argue for it in review.
    /// </summary>
    private static readonly string[] Allowed = ["Access", "Data", Path.Combine("Features", "Dev")];

    [Fact]
    public void Feature_code_never_reads_crm_tables_except_through_the_access_scope()
    {
        var api = Path.Combine(RepoRoot(), "src", "Truckroll.Api");
        var offenders = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !Allowed.Any(folder => Path.GetRelativePath(api, file).StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, number: index + 1)))
            .Where(x => DirectDbSetAccess().IsMatch(x.line))
            .Select(x => $"{Path.GetRelativePath(api, x.file)}:{x.number}: {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Read CRM data through AccessScope, not the DbSet:\n" + string.Join('\n', offenders));
    }

    [GeneratedRegex(@"\bdb\.(Customers|Properties|Equipment|Memberships|Jobs|JobNotes|CallRecords|KnowledgeDocuments|Technicians|Branches)\b")]
    private static partial Regex DirectDbSetAccess();

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Truckroll.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Couldn't find the repo root (Truckroll.slnx).");
    }
}
