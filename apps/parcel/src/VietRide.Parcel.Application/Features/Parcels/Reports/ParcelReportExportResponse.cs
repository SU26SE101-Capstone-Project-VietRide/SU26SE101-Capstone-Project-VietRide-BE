namespace VietRide.Parcel.Application.Features.Parcels.Reports;

public sealed record ParcelReportExportResponse(
    string FileName,
    string ContentType,
    string Content);
