using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Truckroll.Api.Common;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Knowledge;

/// <summary>
/// Turns CRM records into passages: documents by heading, one passage per piece of equipment,
/// one (or a few) per job. All text is masked here, before it goes anywhere else.
/// </summary>
/// <remarks>
/// Equipment and job passages are written as plain sentences, not field dumps. That's what both
/// rankers want: the embedding model was trained on prose, and keyword search scores real words.
/// They state dates, never "still under warranty", because the index is not rebuilt every day and
/// a passage that's true today would be wrong next month.
/// </remarks>
public static partial class PassageBuilder
{
    public static string RecordFor(KnowledgeDocument document) => $"document:{document.Id}";

    public static string RecordFor(Equipment equipment) => $"equipment:{equipment.Id}";

    public static string RecordFor(Job job) => $"job:{job.Id}";

    public static IEnumerable<Passage> FromDocument(KnowledgeDocument document)
    {
        var record = RecordFor(document);
        var known = new[] { document.Code, document.AppliesToModel };
        foreach (var section in MarkdownSections.Split(document.Title, document.Body))
        {
            var text = PiiMasker.Mask(section.Text);
            yield return new Passage(
                record,
                PassageSource.Document,
                PiiMasker.Mask(section.Title),
                text,
                document.BranchId,
                CustomerId: null,
                IdentifiersIn(text, known));
        }
    }

    /// <summary>Needs the equipment's property and customer loaded.</summary>
    public static Passage FromEquipment(Equipment equipment)
    {
        var property = equipment.Property;
        var customer = property.Customer;
        var installer = equipment.InstalledByUs ? "by us" : "by another company";
        var warranty = equipment.WarrantyExpiresOn is { } expires
            ? $"Parts warranty until {Day(expires)}."
            : "Parts warranty unknown.";

        var text =
            $"{equipment.Brand} {equipment.Model} {Words(equipment.Kind)} at {property.ShortAddress}, " +
            $"owned by {customer.FullName}. Serial number {equipment.SerialNumber}. " +
            $"Installed {Day(equipment.InstalledOn)} {installer}. {warranty}";

        return new Passage(
            RecordFor(equipment),
            PassageSource.Equipment,
            $"{Capitalised(Words(equipment.Kind))} at {property.ShortAddress}",
            PiiMasker.Mask(text),
            customer.BranchId,
            customer.Id,
            IdentifiersIn(text, [equipment.Model, ModelFamily(equipment.Model), equipment.SerialNumber]));
    }

    /// <summary>
    /// Needs the job's customer, property, equipment, technician and notes loaded. A job with many
    /// notes becomes several passages, each starting with the same header so it stands alone.
    /// </summary>
    public static IEnumerable<Passage> FromJob(Job job)
    {
        var day = Day(LocalDay(job.WindowStart));
        var title = $"{job.Number}: {job.Type} at {job.Property.ShortAddress}, {day}";

        var header = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"{job.Type} job {job.Number} for {job.Customer.FullName} at {job.Property.ShortAddress} on {day}, {Words(job.Status)}.");
        if (job.Equipment is { } equipment)
        {
            header.Append(CultureInfo.InvariantCulture, $" Equipment: {equipment.Brand} {equipment.Model} {Words(equipment.Kind)}, serial {equipment.SerialNumber}.");
        }

        if (job.Technician is { } technician)
        {
            header.Append(CultureInfo.InvariantCulture, $" Technician: {technician.FullName}.");
        }

        var blocks = new List<string> { $"Reported: {job.Summary}" };
        if (!string.IsNullOrWhiteSpace(job.Resolution))
        {
            blocks.Add($"Found and done: {job.Resolution}");
        }

        blocks.AddRange(job.Notes
            .OrderBy(n => n.CreatedAt)
            .Select(n => $"Note from {n.Author}, {Day(LocalDay(n.CreatedAt))}: {n.Body}"));

        var known = new[] { job.Number, job.Equipment?.Model, ModelFamily(job.Equipment?.Model), job.Equipment?.SerialNumber };
        foreach (var piece in MarkdownSections.Pack(blocks))
        {
            var text = PiiMasker.Mask($"{header}\n\n{piece}");
            yield return new Passage(RecordFor(job), PassageSource.Job, title, text, job.BranchId, job.CustomerId, IdentifiersIn(text, known));
        }
    }

    /// <summary>
    /// Identifiers found in the text (<c>IGN-80H</c>, <c>SB-2024-07</c>, <c>CAL80-19-10234</c>) plus
    /// the ones the record states outright, upper-cased for exact matching.
    /// </summary>
    public static IReadOnlySet<string> IdentifiersIn(string text, IEnumerable<string?> known) =>
        IdentifierPattern().Matches(text).Select(m => m.Value)
            .Concat(known.OfType<string>())
            .Where(id => id.Length >= 3)
            .Select(id => id.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

    /// <summary><c>CAL-80-96</c> belongs to the <c>CAL-80</c> family, the prefix its manual covers.</summary>
    public static string? ModelFamily(string? model) =>
        model is not null && model.Count(c => c == '-') >= 2 ? model[..model.LastIndexOf('-')] : null;

    // Upper-case letters, optional digits, then one or more hyphenated parts: the shape of every
    // job, model, serial, part, policy and bulletin number in the system.
    [GeneratedRegex(@"\b[A-Z]{1,4}\d*(?:-[A-Z0-9]+)+\b")]
    private static partial Regex IdentifierPattern();

    /// <summary>
    /// The calendar day in Ontario. Timestamps are stored in UTC, where a note written at 8 pm
    /// already falls on tomorrow.
    /// </summary>
    private static DateOnly LocalDay(DateTimeOffset moment) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, CompanyClock.Zone).Date);

    private static string Day(DateOnly day) => day.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary><c>TanklessWaterHeater</c> → "tankless water heater".</summary>
    private static string Words<T>(T value) where T : struct, Enum =>
        SpacedWords().Replace(value.ToString(), " $1").ToLowerInvariant();

    private static string Capitalised(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    [GeneratedRegex(@"(?<=[a-z])([A-Z])")]
    private static partial Regex SpacedWords();
}
