using Truckroll.Api.Knowledge;

namespace Truckroll.Api.Tests.Knowledge;

/// <summary>
/// Masking is the line between "the index has customer notes" and "the index has customers' phone
/// numbers". Both sides are pinned: what must go, and what must survive.
/// </summary>
public sealed class PiiMaskerTests
{
    [Theory]
    [InlineData("519-555-0142")]
    [InlineData("(519) 555-0142")]
    [InlineData("(519)555-0142")]
    [InlineData("519.555.0142")]
    [InlineData("519 555 0142")]
    [InlineData("5195550142")]
    [InlineData("+1 519 555 0142")]
    [InlineData("+1-519-555-0142")]
    [InlineData("1-519-555-0142")]
    [InlineData("1 (226) 555-0118")]
    [InlineData("+15195550142")]
    [InlineData("519-555-0142 ext. 23")]
    [InlineData("519-555-0142 x23")]
    [InlineData("(800) 555-0199 extension 4")]
    public void Canadian_phone_formats_are_masked(string phone)
    {
        var masked = PiiMasker.Mask($"Customer asked us to call {phone} before arriving.");

        Assert.Equal("Customer asked us to call [phone] before arriving.", masked);
    }

    [Theory]
    [InlineData("priya.raman@mail.test")]
    [InlineData("doug+furnace@inbox.test")]
    [InlineData("MEI_CHEN@Example.co.uk")]
    public void Emails_are_masked(string email)
    {
        Assert.Equal("Send the quote to [email] today.", PiiMasker.Mask($"Send the quote to {email} today."));
    }

    [Theory]
    [InlineData("Serial CAL80-19-10234, installed 2019.")]
    [InlineData("Job J-24051 on 2026-11-25.")]
    [InlineData("Postal code N1H 4K8.")]
    [InlineData("Input 100,000 BTU/h, filter 16 x 25 x 1.")]
    [InlineData("Manifold pressure 3.5 in. w.c., rise 35 to 65 F.")]
    [InlineData("Part PS-80-0.55 and bulletin SB-2024-07.")]
    [InlineData("Invoice total 1250.00 on order 20261125.")]
    public void Identifiers_dates_and_measurements_survive(string text)
    {
        Assert.Equal(text, PiiMasker.Mask(text));
    }

    [Fact]
    public void Several_details_in_one_note_are_all_masked()
    {
        var masked = PiiMasker.Mask("Wife's cell 226-555-0177, his work (519) 555-0100 x12, email them at home@family.test.");

        Assert.Equal("Wife's cell [phone], his work [phone], email them at [email].", masked);
    }
}
