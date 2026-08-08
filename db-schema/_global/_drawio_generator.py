#!/usr/bin/env python3
"""
drawio generator for VietRide db-schema.

Reads a Python dict spec of {service -> [Table(name, columns, color)]} and emits
draw.io mxGraph XML with table-shape boxes (no edges) per service.

Run from repo root:
  python3 db-schema/_global/_drawio_generator.py
  python3 db-schema/_global/_drawio_generator.py rag-ai

Each row is rendered as a tableRow with 3 cells:
  c1 (50px): "PK" / "FK" / "" marker
  c2 (180px): column name (camelCase, PK underlined, FK italic)
  c3 (120px): SQL type label

Tables are arranged in a 4-column grid with 60px gaps.
"""

import argparse
import os
from dataclasses import dataclass, field
from typing import List, Tuple

OUT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.normpath(os.path.join(OUT_DIR, "..", ".."))
SCHEMA_ROOT = os.path.join(REPO_ROOT, "db-schema")

# Layout constants
TABLE_W = 350
ROW_H = 30
HEADER_H = 30
C1_W = 50
C2_W = 180
C3_W = 120
COL_GAP = 50
ROW_GAP = 60
GRID_COLS = 4


@dataclass
class Col:
    name: str       # camelCase
    type: str       # SQL type label
    pk: bool = False
    fk: bool = False


@dataclass
class Table:
    name: str       # PascalCase
    cols: List[Col]
    fill: str
    stroke: str


# =============================================================================
# Style cheat sheet per service (matches task spec)
# =============================================================================
COLORS = {
    "identity-user-user":     ("#dae8fc", "#6c8ebf"),  # User group
    "identity-user-operator": ("#d5e8d4", "#82b366"),  # Operator group
    "booking":            ("#fff2cc", "#d6b656"),
    "trip-route-vehicle": ("#f8cecc", "#b85450"),
    "payment-wallet":     ("#e1d5e7", "#9673a6"),
    "parcel":             ("#ffe6cc", "#d79b00"),
    "tracking":           ("#f5f5f5", "#666666"),
    "notification":       ("#dae8fc", "#6c8ebf"),
    "rag-ai":             ("#fad9d5", "#ae4132"),
}


def xml_escape(s: str) -> str:
    return (
        s.replace("&", "&amp;")
         .replace("<", "&lt;")
         .replace(">", "&gt;")
         .replace('"', "&quot;")
    )


def render_table(tbl: Table, x: int, y: int) -> Tuple[str, int]:
    """Return (xml_fragment, table_height)."""
    n_rows = len(tbl.cols)
    height = HEADER_H + n_rows * ROW_H
    tid = "tbl_" + tbl.name.lower()
    parts = []
    # Header / container
    parts.append(
        f'        <mxCell id="{tid}" value="{xml_escape(tbl.name)}" '
        f'style="shape=table;startSize=30;container=1;collapsible=0;childLayout=tableLayout;'
        f'fontSize=14;fontStyle=1;fillColor={tbl.fill};strokeColor={tbl.stroke};align=center;" '
        f'vertex="1" parent="1">\n'
        f'          <mxGeometry x="{x}" y="{y}" width="{TABLE_W}" height="{height}" as="geometry" />\n'
        f'        </mxCell>\n'
    )
    # Rows
    for i, col in enumerate(tbl.cols, start=1):
        row_id = f"{tid}_r{i}"
        marker = "PK" if col.pk else ("FK" if col.fk else "")
        # name with underline for PK, italic for FK
        if col.pk:
            name_val = f"&lt;u&gt;{xml_escape(col.name)}&lt;/u&gt;"
            name_font = "fontStyle=4;"  # underline
        elif col.fk:
            name_val = f"&lt;i&gt;{xml_escape(col.name)}&lt;/i&gt;"
            name_font = "fontStyle=2;"  # italic
        else:
            name_val = xml_escape(col.name)
            name_font = ""

        parts.append(
            f'        <mxCell id="{row_id}" value="" '
            f'style="shape=tableRow;horizontal=0;startSize=0;swimlaneHead=0;swimlaneBody=0;'
            f'strokeColor=inherit;top=0;left=0;bottom=0;right=0;collapsible=0;dropTarget=0;'
            f'fillColor=none;points=[[0,0.5],[1,0.5]];portConstraint=eastwest;fontSize=12;" '
            f'vertex="1" parent="{tid}">\n'
            f'          <mxGeometry y="{HEADER_H + (i-1) * ROW_H}" width="{TABLE_W}" height="{ROW_H}" as="geometry" />\n'
            f'        </mxCell>\n'
        )
        # c1: marker
        parts.append(
            f'        <mxCell id="{row_id}_c1" value="{marker}" '
            f'style="shape=partialRectangle;html=1;whiteSpace=wrap;connectable=0;strokeColor=inherit;'
            f'overflow=hidden;fillColor=none;top=0;left=0;bottom=0;right=0;pointerEvents=1;fontSize=11;'
            f'fontStyle=5;align=center;" vertex="1" parent="{row_id}">\n'
            f'          <mxGeometry width="{C1_W}" height="{ROW_H}" as="geometry" />\n'
            f'        </mxCell>\n'
        )
        # c2: column name
        parts.append(
            f'        <mxCell id="{row_id}_c2" value="{name_val}" '
            f'style="shape=partialRectangle;html=1;whiteSpace=wrap;connectable=0;strokeColor=inherit;'
            f'overflow=hidden;fillColor=none;top=0;left=0;bottom=0;right=0;pointerEvents=1;fontSize=12;'
            f'{name_font}align=left;spacingLeft=8;" vertex="1" parent="{row_id}">\n'
            f'          <mxGeometry x="{C1_W}" width="{C2_W}" height="{ROW_H}" as="geometry" />\n'
            f'        </mxCell>\n'
        )
        # c3: type
        parts.append(
            f'        <mxCell id="{row_id}_c3" value="{xml_escape(col.type)}" '
            f'style="shape=partialRectangle;html=1;whiteSpace=wrap;connectable=0;strokeColor=inherit;'
            f'overflow=hidden;fillColor=none;top=0;left=0;bottom=0;right=0;pointerEvents=1;fontSize=12;'
            f'align=left;spacingLeft=8;" vertex="1" parent="{row_id}">\n'
            f'          <mxGeometry x="{C1_W + C2_W}" width="{C3_W}" height="{ROW_H}" as="geometry" />\n'
            f'        </mxCell>\n'
        )
    return "".join(parts), height


def render_service(service_id: str, diagram_name: str, tables: List[Table]) -> str:
    """Lay out tables in a 4-column grid, return full drawio XML."""
    body = []
    col_heights = [0] * GRID_COLS
    col_x = [40 + i * (TABLE_W + COL_GAP) for i in range(GRID_COLS)]

    for idx, tbl in enumerate(tables):
        # Pick the shortest column
        col_idx = col_heights.index(min(col_heights))
        x = col_x[col_idx]
        y = 40 + col_heights[col_idx]
        frag, h = render_table(tbl, x, y)
        body.append(frag)
        col_heights[col_idx] += h + ROW_GAP

    page_w = 40 + GRID_COLS * (TABLE_W + COL_GAP) + 40
    page_h = max(col_heights) + 200

    xml = (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        f'<mxfile host="app.diagrams.net" agent="VietRide-DB-Schema" version="24.0.0">\n'
        f'  <diagram name="{xml_escape(diagram_name)}" id="{service_id}">\n'
        f'    <mxGraphModel dx="1422" dy="757" grid="1" gridSize="10" guides="1" tooltips="1" '
        f'connect="1" arrows="1" fold="1" page="1" pageScale="1" pageWidth="{page_w}" '
        f'pageHeight="{page_h}" math="0" shadow="0">\n'
        '      <root>\n'
        '        <mxCell id="0" />\n'
        '        <mxCell id="1" parent="0" />\n'
        + "".join(body) +
        '      </root>\n'
        '    </mxGraphModel>\n'
        '  </diagram>\n'
        '</mxfile>\n'
    )
    return xml


# =============================================================================
# Service specs — column lists mirror schema.sql (PascalCase entity / camelCase col)
# =============================================================================

def get_specs():
    op_fill, op_stroke = COLORS["identity-user-operator"]
    us_fill, us_stroke = COLORS["identity-user-user"]
    bk_fill, bk_stroke = COLORS["booking"]
    tr_fill, tr_stroke = COLORS["trip-route-vehicle"]
    pw_fill, pw_stroke = COLORS["payment-wallet"]
    pc_fill, pc_stroke = COLORS["parcel"]
    tk_fill, tk_stroke = COLORS["tracking"]
    nt_fill, nt_stroke = COLORS["notification"]
    rg_fill, rg_stroke = COLORS["rag-ai"]

    return {
        # =====================================================================
        # IDENTITY & USER
        # =====================================================================
        "identity-user": [
            Table("Operator", [
                Col("id", "uuid", pk=True),
                Col("name", "varchar(255)"),
                Col("businessRegistrationNumber", "varchar(50)"),
                Col("taxCode", "varchar(50)"),
                Col("contactEmail", "varchar(255)"),
                Col("contactPhone", "varchar(20)"),
                Col("logoUrl", "text"),
                Col("addressStreet", "varchar(255)"),
                Col("addressWard", "varchar(100)"),
                Col("addressDistrict", "varchar(100)"),
                Col("addressProvince", "varchar(100)"),
                Col("representativeName", "varchar(255)"),
                Col("representativePhone", "varchar(20)"),
                Col("registrationStatus", "enum"),
                Col("approvedAt", "timestamptz"),
                Col("approvedByUserId", "uuid"),
                Col("rejectedAt", "timestamptz"),
                Col("rejectedByUserId", "uuid"),
                Col("rejectReason", "text"),
                Col("suspendedAt", "timestamptz"),
                Col("suspendReason", "text"),
                Col("cancellationPolicy", "jsonb"),
                Col("parcelNoShowPolicy", "jsonb"),
                Col("luggagePolicy", "jsonb"),
                Col("bankAccountName", "varchar(100)"),
                Col("bankAccountNumber", "varchar(20)"),
                Col("bankName", "varchar(200)"),
                Col("isActive", "boolean"),
                Col("deletedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], op_fill, op_stroke),
            Table("User", [
                Col("id", "uuid", pk=True),
                Col("email", "varchar(255)"),
                Col("phone", "varchar(20)"),
                Col("passwordHash", "varchar(255)"),
                Col("displayName", "varchar(255)"),
                Col("avatarUrl", "text"),
                Col("role", "enum"),
                Col("status", "enum"),
                Col("operatorId", "uuid", fk=True),
                Col("failedLoginAttempts", "int"),
                Col("lastFailedLoginAt", "timestamptz"),
                Col("lastLoginAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
                Col("deletedAt", "timestamptz"),
            ], us_fill, us_stroke),
            Table("OAuthIdentity", [
                Col("id", "uuid", pk=True),
                Col("userId", "uuid", fk=True),
                Col("provider", "enum"),
                Col("providerSubject", "varchar(255)"),
                Col("providerEmail", "varchar(255)"),
                Col("linkedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], us_fill, us_stroke),
            Table("RefreshToken", [
                Col("id", "uuid", pk=True),
                Col("userId", "uuid", fk=True),
                Col("tokenHash", "varchar(255)"),
                Col("familyId", "uuid"),
                Col("parentTokenId", "uuid", fk=True),
                Col("issuedAt", "timestamptz"),
                Col("expiresAt", "timestamptz"),
                Col("revokedAt", "timestamptz"),
                Col("revokedReason", "enum"),
                Col("userAgent", "varchar(500)"),
                Col("ipAddress", "varchar(45)"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], us_fill, us_stroke),
            Table("EmailVerificationToken", [
                Col("id", "uuid", pk=True),
                Col("userId", "uuid", fk=True),
                Col("purpose", "enum"),
                Col("code", "varchar(255)"),
                Col("expiresAt", "timestamptz"),
                Col("failedAttempts", "int"),
                Col("usedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
            ], us_fill, us_stroke),
            Table("UserDevice", [
                Col("id", "uuid", pk=True),
                Col("userId", "uuid", fk=True),
                Col("fcmToken", "varchar(500)"),
                Col("platform", "enum"),
                Col("isActive", "boolean"),
                Col("lastActiveAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], us_fill, us_stroke),
            Table("ActivityLog", [
                Col("id", "uuid", pk=True),
                Col("userId", "uuid", fk=True),
                Col("action", "enum"),
                Col("metadata", "jsonb"),
                Col("ipAddress", "varchar(45)"),
                Col("userAgent", "varchar(500)"),
                Col("createdAt", "timestamptz"),
            ], us_fill, us_stroke),
            Table("SubscriptionPlan", [
                Col("id", "uuid", pk=True),
                Col("name", "varchar(100)"),
                Col("description", "text"),
                Col("pricePerMonth", "bigint"),
                Col("pricePerYear", "bigint"),
                Col("maxVehicles", "int"),
                Col("maxDrivers", "int"),
                Col("maxAssistants", "int"),
                Col("maxOperatorUsers", "int"),
                Col("maxRoutes", "int"),
                Col("maxTripsPerMonth", "int"),
                Col("enableParcel", "boolean"),
                Col("enableShuttle", "boolean"),
                Col("enableRag", "boolean"),
                Col("isActive", "boolean"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], op_fill, op_stroke),
            Table("OperatorSubscription", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("planId", "uuid", fk=True),
                Col("previousActivePlanId", "uuid", fk=True),
                Col("status", "enum"),
                Col("startedAt", "timestamptz"),
                Col("expiresAt", "timestamptz"),
                Col("paymentMethod", "enum"),
                Col("currentVehicles", "int"),
                Col("currentDrivers", "int"),
                Col("currentAssistants", "int"),
                Col("currentOperatorUsers", "int"),
                Col("currentRoutes", "int"),
                Col("currentTripsThisMonth", "int"),
                Col("lastResetAt", "timestamptz"),
                Col("warnSentAt", "timestamptz"),
                Col("trialExpiringWarnSentAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], op_fill, op_stroke),
        ],

        # =====================================================================
        # BOOKING
        # =====================================================================
        "booking": [
            Table("Booking", [
                Col("id", "uuid", pk=True),
                Col("bookingCode", "varchar(30)"),
                Col("passengerUserId", "uuid", fk=True),
                Col("tripId", "uuid", fk=True),
                Col("operatorId", "uuid", fk=True),
                Col("pickupStationId", "uuid", fk=True),
                Col("pickupStopId", "uuid", fk=True),
                Col("dropoffStationId", "uuid", fk=True),
                Col("dropoffStopId", "uuid", fk=True),
                Col("baseFare", "bigint"),
                Col("discountAmount", "bigint"),
                Col("totalAmount", "bigint"),
                Col("status", "enum"),
                Col("cancellationReason", "enum"),
                Col("refundOverride", "boolean"),
                Col("bookingGroupId", "uuid"),
                Col("tripDirection", "enum"),
                Col("tripSnapshotOriginName", "varchar(255)"),
                Col("tripSnapshotDestName", "varchar(255)"),
                Col("tripSnapshotDeparture", "timestamptz"),
                Col("tripSnapshotRouteName", "varchar(255)"),
                Col("confirmedAt", "timestamptz"),
                Col("cancelledAt", "timestamptz"),
                Col("refundedAt", "timestamptz"),
                Col("expiredAt", "timestamptz"),
                Col("completedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], bk_fill, bk_stroke),
            Table("Passenger", [
                Col("id", "uuid", pk=True),
                Col("bookingId", "uuid", fk=True),
                Col("seatNumber", "varchar(20)"),
                Col("boardingStatus", "enum"),
                Col("boardedAt", "timestamptz"),
                Col("boardedAtStopId", "uuid", fk=True),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], bk_fill, bk_stroke),
            Table("BookingPendingAction", [
                Col("id", "uuid", pk=True),
                Col("bookingId", "uuid", fk=True),
                Col("reason", "enum"),
                Col("severity", "enum"),
                Col("deadline", "timestamptz"),
                Col("resolvedAt", "timestamptz"),
                Col("resolvedAction", "enum"),
                Col("metadata", "jsonb"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], bk_fill, bk_stroke),
            Table("BookingTransfer", [
                Col("id", "uuid", pk=True),
                Col("bookingId", "uuid", fk=True),
                Col("passengerId", "uuid", fk=True),
                Col("originalTripId", "uuid", fk=True),
                Col("newTripId", "uuid", fk=True),
                Col("originalSeatNumber", "varchar(20)"),
                Col("newSeatNumber", "varchar(20)"),
                Col("transferredAt", "timestamptz"),
                Col("transferredByUserId", "uuid", fk=True),
                Col("note", "text"),
                Col("createdAt", "timestamptz"),
            ], bk_fill, bk_stroke),
            Table("BookingStats", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("statDate", "date"),
                Col("tripId", "uuid", fk=True),
                Col("totalBookings", "int"),
                Col("totalConfirmed", "int"),
                Col("totalCancelled", "int"),
                Col("totalNoShow", "int"),
                Col("totalCompleted", "int"),
                Col("totalRevenue", "bigint"),
                Col("totalRefunded", "bigint"),
                Col("totalSeatsBooked", "int"),
                Col("updatedAt", "timestamptz"),
            ], bk_fill, bk_stroke),
            Table("Voucher", [
                Col("id", "uuid", pk=True),
                Col("code", "varchar(50)"),
                Col("type", "enum"),
                Col("value", "bigint"),
                Col("minOrderAmount", "bigint"),
                Col("maxDiscountAmount", "bigint"),
                Col("totalUsageLimit", "int"),
                Col("perUserLimit", "int"),
                Col("validFrom", "timestamptz"),
                Col("validUntil", "timestamptz"),
                Col("applicableOperatorIds", "uuid[]"),
                Col("applicableRouteIds", "uuid[]"),
                Col("fundingType", "enum"),
                Col("isActive", "boolean"),
                Col("createdByUserId", "uuid", fk=True),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], bk_fill, bk_stroke),
            Table("VoucherUsage", [
                Col("id", "uuid", pk=True),
                Col("voucherId", "uuid", fk=True),
                Col("userId", "uuid", fk=True),
                Col("bookingId", "uuid", fk=True),
                Col("bookingGroupId", "uuid"),
                Col("discountAmount", "bigint"),
                Col("fundedBy", "enum"),
                Col("createdAt", "timestamptz"),
            ], bk_fill, bk_stroke),
            Table("OperatorVoucherConsent", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("voucherId", "uuid", fk=True),
                Col("status", "enum"),
                Col("requestedAt", "timestamptz"),
                Col("respondedAt", "timestamptz"),
                Col("respondedByUserId", "uuid", fk=True),
                Col("rejectReason", "text"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], bk_fill, bk_stroke),
            Table("OutboxEvent", [
                Col("id", "uuid", pk=True),
                Col("eventType", "varchar(100)"),
                Col("payload", "jsonb"),
                Col("status", "enum"),
                Col("retryCount", "int"),
                Col("lastError", "text"),
                Col("createdAt", "timestamptz"),
                Col("publishedAt", "timestamptz"),
            ], bk_fill, bk_stroke),
        ],

        # =====================================================================
        # TRIP-ROUTE-VEHICLE
        # =====================================================================
        "trip-route-vehicle": [
            Table("Station", [
                Col("id", "uuid", pk=True),
                Col("name", "varchar(255)"),
                Col("slug", "varchar(100)"),
                Col("addressStreet", "varchar(255)"),
                Col("city", "varchar(100)"),
                Col("province", "varchar(100)"),
                Col("latitude", "decimal(10,7)"),
                Col("longitude", "decimal(10,7)"),
                Col("contactPhone", "varchar(20)"),
                Col("contactEmail", "varchar(255)"),
                Col("operatingHours", "jsonb"),
                Col("facilities", "jsonb"),
                Col("supportsShuttle", "boolean"),
                Col("isActive", "boolean"),
                Col("deletedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("OperatorStation", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("stationId", "uuid", fk=True),
                Col("displayNameOverride", "varchar(255)"),
                Col("counterLocation", "varchar(255)"),
                Col("contactPhone", "varchar(20)"),
                Col("instructions", "text"),
                Col("isActive", "boolean"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("Stop", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("name", "varchar(255)"),
                Col("description", "text"),
                Col("latitude", "decimal(10,7)"),
                Col("longitude", "decimal(10,7)"),
                Col("address", "varchar(500)"),
                Col("googlePlaceId", "varchar(255)"),
                Col("sharedSuggestion", "boolean"),
                Col("replacedByStopId", "uuid", fk=True),
                Col("isActive", "boolean"),
                Col("deletedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("Route", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("name", "varchar(255)"),
                Col("originStationId", "uuid", fk=True),
                Col("destinationStationId", "uuid", fk=True),
                Col("returnRouteId", "uuid", fk=True),
                Col("baseFare", "bigint"),
                Col("totalDistanceKm", "decimal(8,2)"),
                Col("estimatedDurationMinutes", "int"),
                Col("isActive", "boolean"),
                Col("deletedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("RouteStop", [
                Col("routeId", "uuid", pk=True),
                Col("stopId", "uuid", pk=True),
                Col("orderIndex", "int"),
                Col("estimatedDurationFromOriginMinutes", "int"),
                Col("distanceFromOriginKm", "decimal(8,2)"),
                Col("allowPickup", "boolean"),
                Col("allowDropoff", "boolean"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("RouteStopFareTemplate", [
                Col("id", "uuid", pk=True),
                Col("routeId", "uuid", fk=True),
                Col("stopId", "uuid", fk=True),
                Col("fareFromThisStop", "bigint"),
                Col("effectiveFrom", "timestamptz"),
                Col("effectiveUntil", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("AlternativeRoute", [
                Col("id", "uuid", pk=True),
                Col("routeId", "uuid", fk=True),
                Col("name", "varchar(255)"),
                Col("description", "text"),
                Col("destinationStationId", "uuid", fk=True),
                Col("totalDistanceKm", "decimal(8,2)"),
                Col("estimatedDurationMinutes", "int"),
                Col("isActive", "boolean"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("AlternativeRouteStop", [
                Col("alternativeRouteId", "uuid", pk=True),
                Col("stopId", "uuid", pk=True),
                Col("orderIndex", "int"),
                Col("estimatedDurationFromOriginMinutes", "int"),
                Col("distanceFromOriginKm", "decimal(8,2)"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("VehicleType", [
                Col("id", "uuid", pk=True),
                Col("code", "varchar(50)"),
                Col("displayName", "varchar(255)"),
                Col("estimatedPassengerLuggageKgPerSeat", "int"),
                Col("defaultSeatCount", "int"),
                Col("isSystemDefined", "boolean"),
                Col("isActive", "boolean"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("Vehicle", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("vehicleTypeId", "uuid", fk=True),
                Col("licensePlate", "varchar(20)"),
                Col("seatLayoutJson", "jsonb"),
                Col("totalSeats", "int"),
                Col("maxCargoWeightKg", "decimal(8,2)"),
                Col("maxCargoVolumeM3", "decimal(8,2)"),
                Col("status", "enum"),
                Col("isActive", "boolean"),
                Col("deletedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("Trip", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("routeId", "uuid", fk=True),
                Col("vehicleId", "uuid", fk=True),
                Col("driverUserId", "uuid", fk=True),
                Col("assistantUserId", "uuid", fk=True),
                Col("driverScheduleId", "uuid", fk=True),
                Col("departureDateTime", "timestamptz"),
                Col("estimatedArrivalTime", "timestamptz"),
                Col("actualDepartureTime", "timestamptz"),
                Col("completedAt", "timestamptz"),
                Col("disruptedAt", "timestamptz"),
                Col("disruptionReason", "text"),
                Col("cancelledAt", "timestamptz"),
                Col("cancelledByUserId", "uuid", fk=True),
                Col("cancelReason", "text"),
                Col("completedByUserId", "uuid", fk=True),
                Col("status", "enum"),
                Col("source", "enum"),
                Col("hasSubstitution", "boolean"),
                Col("baseFare", "bigint"),
                Col("maxCargoWeightKg", "decimal(8,2)"),
                Col("estimatedPassengerLuggageKg", "decimal(8,2)"),
                Col("reservedParcelWeightKg", "decimal(8,2)"),
                Col("totalLoadedWeightKg", "decimal(8,2)"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("TripSeat", [
                Col("id", "uuid", pk=True),
                Col("tripId", "uuid", fk=True),
                Col("seatNumber", "varchar(20)"),
                Col("seatType", "enum"),
                Col("status", "enum"),
                Col("disabledReason", "text"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("TripStop", [
                Col("tripId", "uuid", pk=True),
                Col("stopId", "uuid", pk=True),
                Col("orderIndex", "int"),
                Col("estimatedArrivalTime", "timestamptz"),
                Col("actualArrivalTime", "timestamptz"),
                Col("status", "enum"),
                Col("allowPickup", "boolean"),
                Col("allowDropoff", "boolean"),
                Col("distanceFromOriginKm", "decimal(8,2)"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("TripStopFare", [
                Col("tripId", "uuid", pk=True),
                Col("stopId", "uuid", pk=True),
                Col("fareFromThisStop", "bigint"),
                Col("createdAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("DriverSchedule", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("routeId", "uuid", fk=True),
                Col("vehicleId", "uuid", fk=True),
                Col("driverUserId", "uuid", fk=True),
                Col("assistantUserId", "uuid", fk=True),
                Col("dayOfWeek", "jsonb"),
                Col("departureTime", "time"),
                Col("validFrom", "date"),
                Col("validUntil", "date"),
                Col("isActive", "boolean"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("TripGenerationSkipLog", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("driverScheduleId", "uuid", fk=True),
                Col("skippedDate", "date"),
                Col("reason", "enum"),
                Col("message", "text"),
                Col("createdAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("ShuttleTrip", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("mainTripId", "uuid", fk=True),
                Col("stationId", "uuid", fk=True),
                Col("direction", "enum"),
                Col("driverUserId", "uuid", fk=True),
                Col("vehicleId", "uuid", fk=True),
                Col("status", "enum"),
                Col("scheduledDepartureTime", "timestamptz"),
                Col("actualDepartureTime", "timestamptz"),
                Col("completedAt", "timestamptz"),
                Col("notes", "text"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("ShuttlePassenger", [
                Col("id", "uuid", pk=True),
                Col("shuttleTripId", "uuid", fk=True),
                Col("mainTripId", "uuid", fk=True),
                Col("bookingId", "uuid", fk=True),
                Col("direction", "enum"),
                Col("pickupAddress", "text"),
                Col("pickupLat", "decimal(10,7)"),
                Col("pickupLng", "decimal(10,7)"),
                Col("scheduledPickupTime", "timestamptz"),
                Col("status", "enum"),
                Col("pickedUpAt", "timestamptz"),
                Col("deliveredAt", "timestamptz"),
                Col("cancelReason", "text"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("Incident", [
                Col("id", "uuid", pk=True),
                Col("tripId", "uuid", fk=True),
                Col("reportedByUserId", "uuid", fk=True),
                Col("category", "enum"),
                Col("description", "text"),
                Col("photoUrls", "jsonb"),
                Col("latitude", "decimal(10,7)"),
                Col("longitude", "decimal(10,7)"),
                Col("reportedAt", "timestamptz"),
                Col("resolvedAt", "timestamptz"),
                Col("resolvedByUserId", "uuid", fk=True),
                Col("resolutionNote", "text"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
            Table("OutboxEvent", [
                Col("id", "uuid", pk=True),
                Col("eventType", "varchar(100)"),
                Col("payload", "jsonb"),
                Col("status", "enum"),
                Col("retryCount", "int"),
                Col("lastError", "text"),
                Col("createdAt", "timestamptz"),
                Col("publishedAt", "timestamptz"),
            ], tr_fill, tr_stroke),
        ],

        # =====================================================================
        # PAYMENT & WALLET (v1 wallet model — no bank withdrawal)
        # =====================================================================
        "payment-wallet": [
            Table("Payment", [
                Col("id", "uuid", pk=True),
                Col("referenceType", "enum"),
                Col("referenceId", "uuid"),
                Col("userId", "uuid", fk=True),
                Col("operatorId", "uuid", fk=True),
                Col("amount", "bigint"),
                Col("method", "enum"),
                Col("status", "enum"),
                Col("vnpayTxnRef", "varchar(100)"),
                Col("vnpayResponseCode", "varchar(10)"),
                Col("idempotencyKey", "varchar(100)"),
                Col("paymentRedirectUrl", "text"),
                Col("succeededAt", "timestamptz"),
                Col("failedAt", "timestamptz"),
                Col("expiredAt", "timestamptz"),
                Col("refundedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("TopUpRequest", [
                Col("id", "uuid", pk=True),
                Col("userId", "uuid", fk=True),
                Col("amount", "bigint"),
                Col("status", "enum"),
                Col("vnpayTxnRef", "varchar(100)"),
                Col("vnpayResponseCode", "varchar(10)"),
                Col("paymentRedirectUrl", "text"),
                Col("succeededAt", "timestamptz"),
                Col("expiredAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("Wallet", [
                # userId is natural PK (1-1 with identity.users) — same pattern as OperatorWallet.
                # No synthetic id; bootstrap via identity.user.created event (UPSERT idempotent).
                Col("userId", "uuid", pk=True),
                Col("balance", "bigint"),
                Col("currency", "varchar(3)"),
                Col("rowVersion", "int"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("WalletTransaction", [
                Col("id", "uuid", pk=True),
                # Logical FK to wallets.user_id (= identity.users.id). No hard DB FK —
                # mirrors operator_wallet_transactions.operator_id pattern.
                Col("userId", "uuid", fk=True),
                Col("type", "enum"),
                Col("amount", "bigint"),
                Col("balanceBefore", "bigint"),
                Col("balanceAfter", "bigint"),
                Col("referenceType", "enum"),
                Col("referenceId", "uuid"),
                Col("note", "text"),
                Col("createdAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("Invoice", [
                Col("id", "uuid", pk=True),
                Col("invoiceNumber", "varchar(50)"),
                Col("operatorId", "uuid", fk=True),
                Col("operatorSubscriptionId", "uuid", fk=True),
                Col("paymentId", "uuid", fk=True),
                Col("amount", "bigint"),
                Col("periodFrom", "timestamptz"),
                Col("periodTo", "timestamptz"),
                Col("status", "enum"),
                Col("issuedAt", "timestamptz"),
                Col("pdfUrl", "text"),
                Col("eInvoiceProviderRef", "varchar(255)"),
                Col("metadata", "jsonb"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("PlatformWallet", [
                Col("id", "uuid", pk=True),
                Col("balance", "bigint"),
                Col("currency", "varchar(3)"),
                Col("rowVersion", "int"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("PlatformWalletTransaction", [
                Col("id", "uuid", pk=True),
                Col("type", "enum"),
                Col("amount", "bigint"),
                Col("balanceBefore", "bigint"),
                Col("balanceAfter", "bigint"),
                Col("referenceType", "enum"),
                Col("referenceId", "uuid"),
                Col("note", "text"),
                Col("createdAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("OperatorWallet", [
                Col("operatorId", "uuid", pk=True),
                Col("balance", "bigint"),
                Col("currency", "varchar(3)"),
                Col("rowVersion", "int"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("OperatorWalletTransaction", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("type", "enum"),
                Col("amount", "bigint"),
                Col("balanceBefore", "bigint"),
                Col("balanceAfter", "bigint"),
                Col("referenceType", "enum"),
                Col("referenceId", "uuid"),
                Col("note", "text"),
                Col("createdAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("OperatorTripSettlement", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("tripId", "uuid", fk=True),
                Col("netAmount", "bigint"),
                Col("tripTerminalAt", "timestamptz"),
                Col("eligibleAt", "timestamptz"),
                Col("status", "enum"),
                Col("settlementMethod", "enum"),
                Col("settledAt", "timestamptz"),
                Col("settledByUserId", "uuid", fk=True),
                Col("walletTransactionId", "uuid", fk=True),
                Col("rowVersion", "int"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("OperatorLedgerEntry", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("tripId", "uuid", fk=True),
                Col("entryType", "enum"),
                Col("amount", "bigint"),
                Col("referenceType", "enum"),
                Col("referenceId", "uuid"),
                Col("note", "text"),
                Col("createdAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("RefundFailureLog", [
                Col("id", "uuid", pk=True),
                Col("bookingId", "uuid", fk=True),
                Col("parcelId", "uuid", fk=True),
                Col("triggerEventType", "varchar(100)"),
                Col("failureReason", "text"),
                Col("retryCount", "int"),
                Col("lastAttemptAt", "timestamptz"),
                Col("resolvedAt", "timestamptz"),
                Col("resolvedByUserId", "uuid", fk=True),
                Col("createdAt", "timestamptz"),
            ], pw_fill, pw_stroke),
            Table("OutboxEvent", [
                Col("id", "uuid", pk=True),
                Col("eventType", "varchar(100)"),
                Col("payload", "jsonb"),
                Col("status", "enum"),
                Col("retryCount", "int"),
                Col("lastError", "text"),
                Col("createdAt", "timestamptz"),
                Col("publishedAt", "timestamptz"),
            ], pw_fill, pw_stroke),
        ],

        # =====================================================================
        # PARCEL
        # =====================================================================
        "parcel": [
            Table("Parcel", [
                Col("id", "uuid", pk=True),
                Col("parcelCode", "varchar(30)"),
                Col("senderUserId", "uuid", fk=True),
                Col("recipientUserId", "uuid", fk=True),
                Col("operatorId", "uuid", fk=True),
                Col("tripId", "uuid", fk=True),
                Col("recipientName", "varchar(255)"),
                Col("recipientPhone", "varchar(20)"),
                Col("recipientEmail", "varchar(255)"),
                Col("description", "text"),
                Col("photoUrl", "text"),
                Col("sizeCategory", "enum"),
                Col("estimatedWeightKg", "decimal(8,2)"),
                Col("actualWeightKg", "decimal(8,2)"),
                Col("dropoffStopId", "uuid", fk=True),
                Col("deliveryMethod", "enum"),
                Col("depositAmount", "bigint"),
                Col("additionalAmount", "bigint"),
                Col("additionalPaymentId", "uuid", fk=True),
                Col("additionalPaymentDeadline", "timestamptz"),
                Col("status", "enum"),
                Col("rejectionReason", "text"),
                Col("cancellationReason", "text"),
                Col("reviewDecision", "enum"),
                Col("reviewedAt", "timestamptz"),
                Col("reviewedByUserId", "uuid", fk=True),
                Col("loadedAt", "timestamptz"),
                Col("unloadedAt", "timestamptz"),
                Col("deliveredPendingConfirmAt", "timestamptz"),
                Col("confirmedAt", "timestamptz"),
                Col("confirmedByUserId", "uuid", fk=True),
                Col("confirmedByIp", "varchar(45)"),
                Col("confirmNote", "text"),
                Col("rejectedAt", "timestamptz"),
                Col("lastReminderAt", "timestamptz"),
                Col("transferTargetTripId", "uuid", fk=True),
                Col("transferRequestedAt", "timestamptz"),
                Col("transferConfirmedAt", "timestamptz"),
                Col("transferConfirmedByUserId", "uuid", fk=True),
                Col("transferConfirmationClaimId", "uuid"),
                Col("transferConfirmationClaimedAt", "timestamptz"),
                Col("transferConfirmationClaimedByUserId", "uuid", fk=True),
                Col("returnReason", "text"),
                Col("returnedAt", "timestamptz"),
                Col("returnedByUserId", "uuid", fk=True),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pc_fill, pc_stroke),
            Table("ParcelDeliveryToken", [
                Col("id", "uuid", pk=True),
                Col("parcelId", "uuid", fk=True),
                Col("tokenHash", "char(64)"),
                Col("expiresAt", "timestamptz"),
                Col("revokedAt", "timestamptz"),
                Col("issuedByUserId", "uuid", fk=True),
                Col("issueReason", "varchar(32)"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pc_fill, pc_stroke),
            Table("ParcelCargoRecoveryOperation", [
                Col("id", "uuid", pk=True),
                Col("parcelId", "uuid", fk=True),
                Col("operatorId", "uuid", fk=True),
                Col("operationType", "varchar(16)"),
                Col("status", "varchar(16)"),
                Col("sourceTripId", "uuid", fk=True),
                Col("targetTripId", "uuid", fk=True),
                Col("targetState", "varchar(16)"),
                Col("actorUserId", "uuid", fk=True),
                Col("reason", "varchar(500)"),
                Col("refundAmountVnd", "bigint"),
                Col("refundDueVnd", "bigint"),
                Col("sourceStatus", "varchar(40)"),
                Col("isStatusOverride", "boolean"),
                Col("claimedAt", "timestamptz"),
                Col("completedAt", "timestamptz"),
                Col("failureCode", "varchar(64)"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pc_fill, pc_stroke),
            Table("PlatformParcelStats", [
                Col("parcelId", "uuid", pk=True, fk=True),
                Col("operatorId", "uuid", fk=True),
                Col("confirmedAt", "timestamptz"),
                Col("parcelRevenueVnd", "bigint"),
                Col("projectedAt", "timestamptz"),
            ], pc_fill, pc_stroke),
            Table("ParcelRouteFare", [
                Col("routeId", "uuid", pk=True),
                Col("sizeCategory", "enum", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("priceVnd", "bigint"),
                Col("effectiveFrom", "timestamptz"),
                Col("effectiveUntil", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pc_fill, pc_stroke),
            Table("SystemConfig", [
                Col("id", "uuid", pk=True),
                Col("key", "varchar(100)"),
                Col("decimalValue", "decimal(12,4)"),
                Col("version", "int"),
                Col("isActive", "boolean"),
                Col("effectiveFrom", "timestamptz"),
                Col("effectiveTo", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pc_fill, pc_stroke),
            Table("OperatorDepositPolicy", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("routeId", "uuid", fk=True),
                Col("depositPercent", "decimal(5,2)"),
                Col("effectiveFrom", "timestamptz"),
                Col("effectiveTo", "timestamptz"),
                Col("isActive", "boolean"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], pc_fill, pc_stroke),
            Table("ParcelStats", [
                Col("id", "uuid", pk=True),
                Col("operatorId", "uuid", fk=True),
                Col("statDate", "date"),
                Col("totalParcels", "int"),
                Col("totalLoaded", "int"),
                Col("totalDelivered", "int"),
                Col("totalRejected", "int"),
                Col("totalReturned", "int"),
                Col("totalRevenue", "bigint"),
                Col("totalRefunded", "bigint"),
                Col("updatedAt", "timestamptz"),
            ], pc_fill, pc_stroke),
            Table("IntegrationInbox", [
                Col("id", "uuid", pk=True),
                Col("consumerName", "varchar(200)"),
                Col("messageId", "uuid"),
                Col("payloadHash", "char(64)"),
                Col("processedAt", "timestamptz"),
            ], pc_fill, pc_stroke),
            Table("OutboxEvent", [
                Col("id", "uuid", pk=True),
                Col("eventType", "varchar(100)"),
                Col("payload", "jsonb"),
                Col("status", "enum"),
                Col("retryCount", "int"),
                Col("lastError", "text"),
                Col("createdAt", "timestamptz"),
                Col("publishedAt", "timestamptz"),
            ], pc_fill, pc_stroke),
            Table("OutboxDlq", [
                Col("id", "uuid", pk=True),
                Col("eventId", "uuid"),
                Col("eventType", "varchar(100)"),
                Col("payload", "jsonb"),
                Col("retryCount", "int"),
                Col("lastError", "text"),
                Col("createdAt", "timestamptz"),
                Col("terminalAt", "timestamptz"),
            ], pc_fill, pc_stroke),
        ],

        # =====================================================================
        # TRACKING
        # =====================================================================
        "tracking": [
            Table("GpsTrail", [
                Col("id", "uuid", pk=True),
                Col("tripId", "uuid", fk=True),
                Col("latitude", "decimal(10,7)"),
                Col("longitude", "decimal(10,7)"),
                Col("speedKmh", "decimal(6,2)"),
                Col("headingDeg", "decimal(5,2)"),
                Col("recordedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
            ], tk_fill, tk_stroke),
            Table("OutboxEvent", [
                Col("id", "uuid", pk=True),
                Col("eventType", "varchar(100)"),
                Col("payload", "jsonb"),
                Col("status", "enum"),
                Col("retryCount", "int"),
                Col("lastError", "text"),
                Col("createdAt", "timestamptz"),
                Col("publishedAt", "timestamptz"),
            ], tk_fill, tk_stroke),
        ],

        # =====================================================================
        # NOTIFICATION
        # =====================================================================
        "notification": [
            Table("Notification", [
                Col("id", "uuid", pk=True),
                Col("userId", "uuid", fk=True),
                Col("type", "enum"),
                Col("title", "varchar(255)"),
                Col("body", "text"),
                Col("data", "jsonb"),
                Col("readAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
            ], nt_fill, nt_stroke),
            Table("NotificationDelivery", [
                Col("id", "uuid", pk=True),
                Col("notificationId", "uuid", fk=True),
                Col("fcmToken", "varchar(500)"),
                Col("platform", "enum"),
                Col("status", "enum"),
                Col("retryCount", "int"),
                Col("lastError", "text"),
                Col("sentAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], nt_fill, nt_stroke),
        ],

        # =====================================================================
        # RAG AI
        # =====================================================================
        "rag-ai": [
            Table("KnowledgeDocument", [
                Col("id", "uuid", pk=True),
                Col("title", "varchar(500)"),
                Col("description", "text"),
                Col("storageProvider", "rag_storage_provider"),
                Col("storagePath", "text"),
                Col("fileType", "enum"),
                Col("accessLevel", "enum"),
                Col("status", "enum"),
                Col("uploadedByUserId", "uuid", fk=True),
                Col("approvedByUserId", "uuid", fk=True),
                Col("approvedAt", "timestamptz"),
                Col("archivedAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
                Col("updatedAt", "timestamptz"),
            ], rg_fill, rg_stroke),
            Table("KnowledgeChunk", [
                Col("id", "uuid", pk=True),
                Col("documentId", "uuid", fk=True),
                Col("chunkIndex", "int"),
                Col("content", "text"),
                Col("tokenCount", "int"),
                Col("embedding", "halfvec(2048)"),
                Col("createdAt", "timestamptz"),
            ], rg_fill, rg_stroke),
            Table("RagConversation", [
                Col("id", "uuid", pk=True),
                Col("userId", "uuid", fk=True),
                Col("role", "enum"),
                Col("startedAt", "timestamptz"),
                Col("lastMessageAt", "timestamptz"),
                Col("createdAt", "timestamptz"),
            ], rg_fill, rg_stroke),
            Table("RagMessage", [
                Col("id", "uuid", pk=True),
                Col("conversationId", "uuid", fk=True),
                Col("role", "enum"),
                Col("content", "text"),
                Col("citedChunkIds", "uuid[]"),
                Col("tokensUsed", "int"),
                Col("createdAt", "timestamptz"),
            ], rg_fill, rg_stroke),
            Table("OutboxEvent", [
                Col("id", "uuid", pk=True),
                Col("eventType", "varchar(100)"),
                Col("payload", "jsonb"),
                Col("status", "enum"),
                Col("retryCount", "int"),
                Col("lastError", "text"),
                Col("createdAt", "timestamptz"),
                Col("publishedAt", "timestamptz"),
            ], rg_fill, rg_stroke),
        ],
    }


SERVICE_DIAGRAM_NAMES = {
    "identity-user": "Identity-User-Schema",
    "booking": "Booking-Schema",
    "trip-route-vehicle": "Trip-Route-Vehicle-Schema",
    "payment-wallet": "Payment-Wallet-Schema",
    "parcel": "Parcel-Schema",
    "tracking": "Tracking-Schema",
    "notification": "Notification-Schema",
    "rag-ai": "Rag-AI-Schema",
}


def main():
    parser = argparse.ArgumentParser(description="Generate VietRide draw.io schema diagrams.")
    parser.add_argument(
        "service",
        nargs="?",
        choices=SERVICE_DIAGRAM_NAMES,
        help="Generate only one service diagram; omit to generate every diagram.",
    )
    args = parser.parse_args()
    specs = get_specs()
    selected_specs = {args.service: specs[args.service]} if args.service else specs
    for service, tables in selected_specs.items():
        out_path = os.path.join(SCHEMA_ROOT, service, "schema.drawio")
        xml = render_service(service, SERVICE_DIAGRAM_NAMES[service], tables)
        with open(out_path, "w", encoding="utf-8", newline="\n") as f:
            f.write(xml)
        print(f"Wrote {out_path}: {len(tables)} tables, {len(xml)} bytes")


if __name__ == "__main__":
    main()
