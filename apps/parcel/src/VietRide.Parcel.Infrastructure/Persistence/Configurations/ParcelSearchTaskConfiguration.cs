using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VietRide.Parcel.Domain.Entities;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Infrastructure.Persistence.Configurations;

internal sealed class ParcelSearchTaskConfiguration : IEntityTypeConfiguration<ParcelSearchTask>
{
    public void Configure(EntityTypeBuilder<ParcelSearchTask> builder)
    {
        builder.ToTable("parcel_search_tasks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasColumnType("uuid").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(x => x.IncidentId).HasColumnName("incident_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.ParcelId).HasColumnName("parcel_id").HasColumnType("uuid").IsRequired();
        builder.Property(x => x.TaskType).HasColumnName("task_type").HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(x => x.Location).HasColumnName("location").HasMaxLength(500);
        builder.Property(x => x.AssigneeId).HasColumnName("assignee_id").HasColumnType("uuid");
        builder.Property(x => x.Deadline).HasColumnName("deadline").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Result).HasColumnName("result").HasColumnType("text");
        builder.Property(x => x.EvidenceJson).HasColumnName("evidence_json").HasColumnType("jsonb");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Ignore(x => x.RowVersion);

        builder.HasOne<ParcelIncident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ParcelEntity>().WithMany().HasForeignKey(x => x.ParcelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.IncidentId, x.Status });
        builder.HasIndex(x => new { x.AssigneeId, x.Status, x.Deadline });
    }
}
