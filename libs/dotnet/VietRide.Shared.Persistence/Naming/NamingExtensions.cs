using System.Text;
using Microsoft.EntityFrameworkCore;

namespace VietRide.Shared.Persistence.Naming;

/// PascalCase → snake_case mapping for EF Core table/column/key/index names.
/// Applied in VietRideDbContextBase.OnModelCreating so every aggregate inherits the convention
/// (matches BACKEND_SOURCE_OF_TRUTH 3.5 — DB: snake_case, code: PascalCase).
public static class NamingExtensions
{
    public static ModelBuilder ApplySnakeCaseNaming(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (!string.IsNullOrEmpty(tableName))
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName() ?? string.Empty));
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName() ?? string.Empty));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName() ?? string.Empty));
            }
        }

        return modelBuilder;
    }

    public static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var sb = new StringBuilder(input.Length + 8);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && input[i - 1] != '_' && !char.IsUpper(input[i - 1]))
                {
                    sb.Append('_');
                }
                else if (i > 0 && char.IsUpper(input[i - 1]) && i + 1 < input.Length && !char.IsUpper(input[i + 1]) && input[i + 1] != '_')
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
