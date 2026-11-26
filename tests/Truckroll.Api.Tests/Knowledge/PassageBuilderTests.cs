using Truckroll.Api.Knowledge;
using Truckroll.Data.Domain;

namespace Truckroll.Api.Tests.Knowledge;

/// <summary>How records become passages: sections, sizes, identifiers and masking.</summary>
public sealed class PassageBuilderTests
{
    private const string Manual = """
        # Calderon CAL-80 Series Gas Furnace

        Mid-efficiency gas furnace. Gas work requires a G2 licence.

        ## Troubleshooting

        ### Blower runs but burners never light

        Check the ignitor IGN-80H glows. See service bulletin SB-2024-07.

        ### Burners light then shut off

        Almost always the flame sensor.

        ## Parts

        | Part | Number |
        | ---- | ------ |
        | Ignitor | IGN-80H |
        """;

    [Fact]
    public void A_document_splits_along_its_headings_with_the_path_as_title()
    {
        var titles = MarkdownSections.Split("CAL-80 manual", Manual).Select(s => s.Title).ToList();

        Assert.Equal(
            [
                "CAL-80 manual",
                "CAL-80 manual > Troubleshooting > Blower runs but burners never light",
                "CAL-80 manual > Troubleshooting > Burners light then shut off",
                "CAL-80 manual > Parts",
            ],
            titles);
    }

    [Fact]
    public void A_long_table_splits_between_rows_and_repeats_its_header()
    {
        var rows = Enumerable.Range(1, 80).Select(i => $"| Part {i:00} | PN-{i:000} |");
        var table = "| Part | Number |\n| ---- | ------ |\n" + string.Join('\n', rows);

        var pieces = MarkdownSections.Split("Catalogue", $"## Parts\n\n{table}").Select(s => s.Text).ToList();

        Assert.True(pieces.Count > 1);
        Assert.All(pieces, piece =>
        {
            Assert.True(piece.Length <= MarkdownSections.MaxCharacters, $"{piece.Length} characters");
            Assert.StartsWith("| Part | Number |\n| ---- | ------ |", piece, StringComparison.Ordinal);
        });
        Assert.Equal(80, pieces.Sum(p => p.Split('\n').Count(line => line.StartsWith("| Part ", StringComparison.Ordinal) && !line.StartsWith("| Part |", StringComparison.Ordinal))));
    }

    [Fact]
    public void Document_passages_carry_their_code_model_and_the_identifiers_they_mention()
    {
        var document = new KnowledgeDocument { Id = 7, Code = "M-CAL-80", Title = "CAL-80 manual", AppliesToModel = "CAL-80", Body = Manual };

        var passage = PassageBuilder.FromDocument(document).Single(p => p.Title.EndsWith("never light", StringComparison.Ordinal));

        Assert.Equal("document:7", passage.Record);
        Assert.Null(passage.BranchId);
        Assert.Superset(new HashSet<string> { "M-CAL-80", "CAL-80", "IGN-80H", "SB-2024-07" }, passage.Identifiers.ToHashSet());
    }

    [Fact]
    public void A_unit_becomes_one_sentence_passage_with_dates_not_relative_states()
    {
        var passage = PassageBuilder.FromEquipment(Furnace());

        Assert.Equal("Furnace at 14 Maple St, Guelph", passage.Title);
        Assert.Equal(
            "Calderon CAL-80-80 furnace at 14 Maple St, Guelph, owned by Priya Raman. Serial number CAL80-19-10234. " +
            "Installed June 3, 2019 by us. Parts warranty until June 3, 2029.",
            passage.Text);
        Assert.Superset(new HashSet<string> { "CAL-80-80", "CAL-80", "CAL80-19-10234" }, passage.Identifiers.ToHashSet());
        Assert.Equal(3, passage.BranchId);
    }

    [Fact]
    public void A_job_passage_holds_its_notes_with_contact_details_masked()
    {
        var job = Job(notes: ["Customer says call 519-555-0142 or priya.raman@mail.test first.", "Ignitor cracked again."]);

        var passage = Assert.Single(PassageBuilder.FromJob(job));

        Assert.Equal("job:40", passage.Record);
        Assert.Contains("Repair job J-24051 for Priya Raman at 14 Maple St, Guelph", passage.Text, StringComparison.Ordinal);
        Assert.Contains("Customer says call [phone] or [email] first.", passage.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0142", passage.Text, StringComparison.Ordinal);
        Assert.Contains("J-24051", passage.Identifiers);
        Assert.Contains("CAL80-19-10234", passage.Identifiers);
    }

    [Fact]
    public void A_job_with_many_notes_splits_and_every_piece_keeps_the_header()
    {
        var notes = Enumerable.Range(1, 30).Select(i => $"Visit {i}: checked the flame sensor, cleaned it, tested the ignitor, all within spec.").ToArray();

        var passages = PassageBuilder.FromJob(Job(notes)).ToList();

        Assert.True(passages.Count > 1);
        Assert.All(passages, p => Assert.StartsWith("Repair job J-24051", p.Text, StringComparison.Ordinal));
        Assert.Equal(30, passages.Sum(p => p.Text.Split("Note from").Length - 1));
    }

    [Fact]
    public void An_evening_note_is_dated_by_the_Ontario_calendar_not_UTC()
    {
        var job = Job(notes: ["Left a voicemail."]);
        // 8:30 pm on October 14 in Guelph is already October 15 in UTC.
        job.Notes[0].CreatedAt = new DateTimeOffset(2026, 10, 15, 0, 30, 0, TimeSpan.Zero);

        var passage = Assert.Single(PassageBuilder.FromJob(job));

        Assert.Contains("Note from Nadia Kowalczyk, October 14, 2026: Left a voicemail.", passage.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CAL-80-96", "CAL-80")]
    [InlineData("AVT-199-50", "AVT-199")]
    [InlineData("FL-16", null)]
    [InlineData(null, null)]
    public void A_model_family_is_the_model_without_its_size(string? model, string? family)
    {
        Assert.Equal(family, PassageBuilder.ModelFamily(model));
    }

    private static Equipment Furnace()
    {
        var customer = new Customer { Id = 9, BranchId = 3, FirstName = "Priya", LastName = "Raman", Email = "priya.raman@mail.test", Phone = "519-555-0142" };
        var property = new Property { Id = 5, Customer = customer, CustomerId = 9, Street = "14 Maple St", City = "Guelph", PostalCode = "N1H 4K8" };
        return new Equipment
        {
            Id = 11,
            Property = property,
            Kind = EquipmentKind.Furnace,
            Brand = "Calderon",
            Model = "CAL-80-80",
            SerialNumber = "CAL80-19-10234",
            InstalledOn = new DateOnly(2019, 6, 3),
            WarrantyExpiresOn = new DateOnly(2029, 6, 3),
            InstalledByUs = true,
        };
    }

    private static Job Job(string[] notes)
    {
        var furnace = Furnace();
        var when = new DateTimeOffset(2026, 10, 14, 9, 0, 0, TimeSpan.FromHours(-4));
        return new Job
        {
            Id = 40,
            Number = "J-24051",
            BranchId = 3,
            CustomerId = 9,
            Customer = furnace.Property.Customer,
            Property = furnace.Property,
            Equipment = furnace,
            Type = JobType.Repair,
            Status = JobStatus.Completed,
            WindowStart = when,
            WindowEnd = when.AddHours(2),
            Summary = "No heat, blower runs.",
            Resolution = "Replaced the ignitor.",
            Notes = notes.Select((body, i) => new JobNote { Author = "Nadia Kowalczyk", Body = body, CreatedAt = when.AddMinutes(i) }).ToList(),
        };
    }
}
