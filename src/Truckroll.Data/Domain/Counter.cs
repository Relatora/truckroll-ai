namespace Truckroll.Data.Domain;

/// <summary>
/// A named, ever-increasing number the database hands out one at a time. Today there's one, the
/// job number (<see cref="JobNumbers"/>); the table is shaped for more (invoice numbers, say).
/// </summary>
/// <remarks>
/// MySQL has no sequences. A row updated with <c>LAST_INSERT_ID(Value + 1)</c> is the documented
/// stand-in: the row lock makes each increment atomic, and the new value comes back on the same
/// connection, so two callers can never be given the same number.
/// </remarks>
public sealed class Counter
{
    /// <summary>The sequence's name, for example <see cref="JobNumbers.CounterName"/>.</summary>
    public required string Name { get; set; }

    /// <summary>The last value handed out. The next caller gets one more.</summary>
    public long Value { get; set; }
}
