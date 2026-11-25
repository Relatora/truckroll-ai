using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Truckroll.Data.Configurations;

internal static class PropertyBuilderExtensions
{
    /// <summary>
    /// Opts a string column out of the 200-character default set in
    /// <see cref="TruckrollDbContext"/>, for text that's long by nature (notes, transcripts,
    /// documents). Pair it with <c>HasColumnType</c> to pick TEXT, MEDIUMTEXT or JSON.
    /// </summary>
    public static PropertyBuilder<TProperty> IsLongText<TProperty>(this PropertyBuilder<TProperty> property)
    {
        property.Metadata.SetMaxLength(null);
        return property;
    }
}
