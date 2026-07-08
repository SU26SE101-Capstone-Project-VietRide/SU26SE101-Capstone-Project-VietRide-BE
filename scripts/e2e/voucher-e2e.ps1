param(
    [string]$GatewayBaseUrl = $(if ($env:GATEWAY_BASE_URL) { $env:GATEWAY_BASE_URL } else { "http://localhost:3000" })
)

$ErrorActionPreference = "Stop"

function Get-Data([object]$Response) {
    if ($null -ne $Response -and $Response.PSObject.Properties.Name -contains "data") { return $Response.data }
    return $Response
}

function Get-FirstProperty([object]$Object, [string[]]$Names) {
    if ($null -eq $Object) { return $null }
    foreach ($name in $Names) {
        if ($Object.PSObject.Properties.Name -contains $name) { return $Object.$name }
    }
    return $null
}

function Write-Step([string]$Message) {
    "[voucher-e2e] $Message"
}

function Invoke-Json {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [string]$Token = $null,
        [hashtable]$Headers = @{}
    )

    $allHeaders = @{}
    foreach ($key in $Headers.Keys) { $allHeaders[$key] = $Headers[$key] }
    if ($Token) { $allHeaders["Authorization"] = "Bearer $Token" }

    $uri = "$GatewayBaseUrl$Path"
    if ($Body -ne $null) {
        $json = $Body | ConvertTo-Json -Depth 20
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $allHeaders -ContentType "application/json" -Body $json
    }

    return Invoke-RestMethod -Method $Method -Uri $uri -Headers $allHeaders
}

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Test-Health([string]$Name, [string]$Path) {
    Write-Step "health $Name"
    Invoke-Json -Method GET -Path $Path | Out-Null
}

function Assert-LocalDbSeedAllowed {
    if ($env:E2E_ALLOW_DB_SEED -ne "true") {
        throw "DB seed fallback disabled. Set E2E_ALLOW_DB_SEED=true only for local E2E."
    }

    $cs = $env:E2E_DATABASE_URL
    if (-not $cs) { throw "E2E_DATABASE_URL is required for DB fallback." }
    if ($cs -notmatch "localhost|127\.0\.0\.1|host\.docker\.internal|postgres") {
        throw "Refusing DB fallback because E2E_DATABASE_URL is not local/docker-local."
    }
}

function ConvertFrom-Base64Url([string]$Value) {
    $normalized = $Value.Replace('-', '+').Replace('_', '/')
    switch ($normalized.Length % 4) {
        2 { $normalized += "==" }
        3 { $normalized += "=" }
    }
    return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($normalized))
}

function Get-JwtClaim([string]$Token, [string[]]$Names) {
    if (-not $Token) { return $null }
    $parts = $Token.Split('.')
    if ($parts.Length -lt 2) { return $null }
    $payload = ConvertFrom-Json (ConvertFrom-Base64Url $parts[1])
    foreach ($name in $Names) {
        if ($payload.PSObject.Properties.Name -contains $name) { return $payload.$name }
    }
    return $null
}

function New-DevJwt([string]$RoleName) {
    if ($env:E2E_ALLOW_DEV_JWT -ne "true") { return $null }

    $role = if ($RoleName -eq "ADMIN") { "SYSTEM_ADMIN" } else { $RoleName }
    $userIdEnv = "E2E_${RoleName}_USER_ID"
    $userId = [Environment]::GetEnvironmentVariable($userIdEnv)
    if (-not $userId) { $userId = [Guid]::NewGuid().ToString() }

    $nodeScript = @'
const fs = require('fs');
const crypto = require('crypto');
const jose = require('jose');

async function main() {
  const role = process.env.E2E_JWT_ROLE;
  const sub = process.env.E2E_JWT_SUB || crypto.randomUUID();
  const source = fs.readFileSync('scripts/generate-dev-token.js', 'utf8');
  const match = source.match(/`(-----BEGIN PRIVATE KEY-----[\s\S]*?-----END PRIVATE KEY-----)`/);
  if (!match) throw new Error('Unable to locate dev private key in scripts/generate-dev-token.js');
  const key = await jose.importPKCS8(match[1], 'RS256');
  const claims = { role, email: `${role.toLowerCase()}@e2e.local`, hasPhone: true };
  if (process.env.E2E_OPERATOR_ID) claims.operatorId = process.env.E2E_OPERATOR_ID;
  const token = await new jose.SignJWT(claims)
    .setProtectedHeader({ alg: 'RS256', kid: process.env.USER_JWT_KID || 'dev-2026-05' })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(sub)
    .setIssuedAt()
    .setExpirationTime('30m')
    .sign(key);
  process.stdout.write(token);
}

main().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.stack : String(error)}\n`);
  process.exit(1);
});
'@

    $env:E2E_JWT_ROLE = $role
    $env:E2E_JWT_SUB = $userId
    try {
        $token = node -e $nodeScript
        if ($LASTEXITCODE -ne 0 -or -not $token) { throw "Dev JWT generation failed." }
        return $token.Trim()
    }
    finally {
        Remove-Item Env:E2E_JWT_ROLE -ErrorAction SilentlyContinue
        Remove-Item Env:E2E_JWT_SUB -ErrorAction SilentlyContinue
    }
}

function Get-TestToken([string]$RoleName) {
    $envName = "E2E_${RoleName}_TOKEN"
    $token = [Environment]::GetEnvironmentVariable($envName)
    if ($token) { return $token }

    $devToken = New-DevJwt $RoleName
    if ($devToken) { return $devToken }

    $emailEnv = "E2E_${RoleName}_EMAIL"
    $passwordEnv = "E2E_${RoleName}_PASSWORD"
    $email = [Environment]::GetEnvironmentVariable($emailEnv)
    $password = [Environment]::GetEnvironmentVariable($passwordEnv)
    if (-not $email -or -not $password) {
        throw "Missing $envName or $emailEnv/$passwordEnv. This script uses real Identity login when tokens are not supplied."
    }

    $login = Invoke-Json -Method POST -Path "/v1/auth/login" -Body @{ email = $email; password = $password }
    $data = if ($login.data) { $login.data } else { $login }
    if ($data.accessToken) { return $data.accessToken }
    if ($data.token) { return $data.token }
    throw "Login for $RoleName did not return accessToken/token."
}

function New-IdempotencyKey([string]$Prefix) {
    return "$Prefix-$([Guid]::NewGuid().ToString('N'))"
}

function Invoke-Psql([string]$Sql) {
    Assert-LocalDbSeedAllowed
    $psql = Get-Command psql -ErrorAction SilentlyContinue
    if (-not $psql) { throw "psql is required for guarded DB fallback but was not found on PATH." }

    $path = [IO.Path]::Combine([IO.Path]::GetTempPath(), "voucher-e2e-$([Guid]::NewGuid().ToString('N')).sql")
    try {
        [IO.File]::WriteAllText($path, $Sql, [Text.Encoding]::UTF8)
        & $psql.Source $env:E2E_DATABASE_URL -v ON_ERROR_STOP=1 -f $path | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "psql exited with code $LASTEXITCODE." }
    }
    finally {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
}

function Invoke-PsqlScalar([string]$Sql) {
    Assert-LocalDbSeedAllowed
    $psql = Get-Command psql -ErrorAction SilentlyContinue
    if (-not $psql) { throw "psql is required for DB assertions but was not found on PATH." }

    $output = & $psql.Source $env:E2E_DATABASE_URL -v ON_ERROR_STOP=1 -t -A -c $Sql
    if ($LASTEXITCODE -ne 0) { throw "psql exited with code $LASTEXITCODE." }
    return ($output | Select-Object -First 1).Trim()
}

function Assert-VoucherUsage([string]$ReferenceType, [string]$ReferenceId, [string]$VoucherCode) {
    $count = Invoke-PsqlScalar "SELECT count(*) FROM vietride_booking.voucher_usages vu JOIN vietride_booking.vouchers v ON v.id = vu.voucher_id WHERE vu.reference_type = '$ReferenceType' AND vu.reference_id = '$ReferenceId' AND v.code = '$VoucherCode';"
    Assert-True ([int]$count -eq 1) "Expected one voucher_usage for $ReferenceType/$ReferenceId and voucher $VoucherCode, found $count."
}

function New-LocalTripSeed([string]$ParcelSize) {
    $suffix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $operatorId = if ($env:E2E_OPERATOR_ID) { $env:E2E_OPERATOR_ID } else { [Guid]::NewGuid().ToString() }
    $driverUserId = if ($env:E2E_DRIVER_USER_ID) { $env:E2E_DRIVER_USER_ID } else { [Guid]::NewGuid().ToString() }
    $vehicleTypeId = [Guid]::NewGuid().ToString()
    $vehicleId = [Guid]::NewGuid().ToString()
    $originStationId = [Guid]::NewGuid().ToString()
    $destinationStationId = [Guid]::NewGuid().ToString()
    $dropoffStopId = [Guid]::NewGuid().ToString()
    $routeId = [Guid]::NewGuid().ToString()
    $tripId = [Guid]::NewGuid().ToString()
    $departure = [DateTimeOffset]::UtcNow.AddDays(1).ToString("O")
    $arrival = [DateTimeOffset]::UtcNow.AddDays(1).AddHours(4).ToString("O")
    $license = "E2E$($suffix % 10000000000000000)"

    Write-Step "seed local trip/fare via DB fallback"
    Invoke-Psql @"
INSERT INTO vietride_trip.vehicle_types (id, code, display_name, estimated_passenger_luggage_kg_per_seat, default_seat_count, is_system_defined, is_active)
VALUES ('$vehicleTypeId', 'E2E_TYPE_$suffix', 'E2E Vehicle Type', 10, 40, false, true)
ON CONFLICT (code) DO NOTHING;

INSERT INTO vietride_trip.stations (id, name, slug, city, province, latitude, longitude, supports_shuttle, is_active)
VALUES
('$originStationId', 'E2E Origin $suffix', 'e2e-origin-$suffix', 'Ho Chi Minh', 'Ho Chi Minh', 10.7700000, 106.7000000, false, true),
('$destinationStationId', 'E2E Destination $suffix', 'e2e-destination-$suffix', 'Da Lat', 'Lam Dong', 11.9400000, 108.4400000, false, true);

INSERT INTO vietride_trip.stops (id, operator_id, name, latitude, longitude, address, is_active)
VALUES ('$dropoffStopId', '$operatorId', 'E2E Dropoff $suffix', 11.5000000, 107.9000000, 'E2E dropoff', true);

INSERT INTO vietride_trip.routes (id, operator_id, name, origin_station_id, destination_station_id, base_fare, total_distance_km, estimated_duration_minutes, is_active)
VALUES ('$routeId', '$operatorId', 'E2E Route $suffix', '$originStationId', '$destinationStationId', 200000, 300.00, 240, true);

INSERT INTO vietride_trip.route_stops (route_id, stop_id, order_index, estimated_duration_from_origin_minutes, distance_from_origin_km, allow_pickup, allow_dropoff)
VALUES ('$routeId', '$dropoffStopId', 1, 180, 220.00, false, true);

INSERT INTO vietride_trip.vehicles (id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats, max_cargo_weight_kg, max_cargo_volume_m3, status, is_active)
VALUES ('$vehicleId', '$operatorId', '$vehicleTypeId', '$license', '{"version":1,"vehicleTypeCode":"E2E","totalSeats":40,"rows":10,"cols":4,"decks":1,"aisles":[],"seats":[]}'::jsonb, 40, 500.00, 10.00, 'ACTIVE', true);

INSERT INTO vietride_trip.trips (id, operator_id, route_id, vehicle_id, driver_user_id, departure_date_time, estimated_arrival_time, status, source, base_fare, max_cargo_weight_kg, estimated_passenger_luggage_kg)
VALUES ('$tripId', '$operatorId', '$routeId', '$vehicleId', '$driverUserId', '$departure', '$arrival', 'SCHEDULED', 'MANUAL', 200000, 500.00, 400.00);

INSERT INTO vietride_trip.trip_seats (trip_id, seat_number, seat_type, status)
VALUES
('$tripId', 'A01', 'STANDARD', 'AVAILABLE'),
('$tripId', 'A02', 'STANDARD', 'AVAILABLE'),
('$tripId', 'A03', 'STANDARD', 'AVAILABLE'),
('$tripId', 'A04', 'STANDARD', 'AVAILABLE')
ON CONFLICT (trip_id, seat_number) DO NOTHING;

INSERT INTO vietride_trip.trip_stops (trip_id, stop_id, order_index, estimated_arrival_time, status, allow_pickup, allow_dropoff, distance_from_origin_km)
VALUES ('$tripId', '$dropoffStopId', 1, '$arrival', 'PENDING', false, true, 220.00)
ON CONFLICT (trip_id, stop_id) DO NOTHING;

INSERT INTO vietride_parcel.parcel_route_fares (route_id, size_category, operator_id, price_vnd, effective_from)
VALUES ('$routeId', '$ParcelSize', '$operatorId', 50000, now() - interval '1 day')
ON CONFLICT (route_id, size_category) DO UPDATE
SET operator_id = EXCLUDED.operator_id,
    price_vnd = EXCLUDED.price_vnd,
    effective_from = EXCLUDED.effective_from,
    effective_until = NULL,
    updated_at = now();
"@

    return [pscustomobject]@{
        TripId = $tripId
        PickupStationId = $originStationId
        DropoffStopId = $dropoffStopId
    }
}

function Invoke-ExpectFailure([scriptblock]$Action, [string]$Message) {
    $failed = $false
    try {
        & $Action | Out-Null
    }
    catch {
        $failed = $true
        Write-Step "negative passed: $Message"
    }
    if (-not $failed) { throw $Message }
}

Write-Step "Gateway: $GatewayBaseUrl"

Test-Health "gateway" "/health"
Test-Health "identity" "/v1/identity/health"
Test-Health "trip" "/v1/trip/health"
Test-Health "booking" "/v1/booking/health"
Test-Health "parcel" "/v1/parcel/health"
Test-Health "payment" "/v1/payment/health"

$adminToken = Get-TestToken "ADMIN"
$passengerToken = Get-TestToken "PASSENGER"

$tripId = $env:E2E_TRIP_ID
$pickupStationId = $env:E2E_PICKUP_STATION_ID
$dropoffStopId = $env:E2E_DROPOFF_STOP_ID
$seat1 = if ($env:E2E_SEAT_1) { $env:E2E_SEAT_1 } else { "A01" }
$seat2 = if ($env:E2E_SEAT_2) { $env:E2E_SEAT_2 } else { "A02" }
$parcelSize = if ($env:E2E_PARCEL_SIZE) { $env:E2E_PARCEL_SIZE } else { "SMALL" }

if (-not $tripId -or -not $pickupStationId -or -not $dropoffStopId) {
    $seed = New-LocalTripSeed $parcelSize
    $tripId = $seed.TripId
    $pickupStationId = $seed.PickupStationId
    $dropoffStopId = $seed.DropoffStopId
}

$now = [DateTimeOffset]::UtcNow
$bookingVoucherCode = "E2EBOOK$($now.ToUnixTimeSeconds())"
$parcelVoucherCode = "E2EPARC$($now.ToUnixTimeSeconds())"

Write-Step "create BOOKING voucher $bookingVoucherCode"
$bookingVoucher = Invoke-Json -Method POST -Path "/v1/admin/vouchers" -Token $adminToken -Headers @{ "Idempotency-Key" = New-IdempotencyKey "voucher-booking" } -Body @{
    code = $bookingVoucherCode
    name = "E2E Booking Voucher"
    type = "FIXED_AMOUNT"
    value = 10000
    minOrderAmount = 0
    maxDiscountAmount = $null
    totalUsageLimit = 10
    perUserLimit = 10
    validFrom = $now.AddMinutes(-5).ToString("O")
    validUntil = $now.AddDays(1).ToString("O")
    applicableServices = @("BOOKING")
    applicablePaymentMethods = @("WALLET")
    fundingType = "VIETRIDE_FUNDED"
}
$bookingVoucherData = Get-Data $bookingVoucher
$bookingVoucherId = Get-FirstProperty $bookingVoucherData @("id", "voucherId")
Assert-True ($null -ne $bookingVoucherId) "BOOKING voucher creation did not return voucher id."

Write-Step "create PARCEL voucher $parcelVoucherCode"
$parcelVoucher = Invoke-Json -Method POST -Path "/v1/admin/vouchers" -Token $adminToken -Headers @{ "Idempotency-Key" = New-IdempotencyKey "voucher-parcel" } -Body @{
    code = $parcelVoucherCode
    name = "E2E Parcel Voucher"
    type = "FIXED_AMOUNT"
    value = 5000
    minOrderAmount = 0
    maxDiscountAmount = $null
    totalUsageLimit = 10
    perUserLimit = 10
    validFrom = $now.AddMinutes(-5).ToString("O")
    validUntil = $now.AddDays(1).ToString("O")
    applicableServices = @("PARCEL")
    applicablePaymentMethods = @("WALLET")
    fundingType = "VIETRIDE_FUNDED"
}
$parcelVoucherData = Get-Data $parcelVoucher
$parcelVoucherId = Get-FirstProperty $parcelVoucherData @("id", "voucherId")
Assert-True ($null -ne $parcelVoucherId) "PARCEL voucher creation did not return voucher id."

Write-Step "create campaign for promotion vouchers"
$campaignName = "E2E Campaign $($now.ToUnixTimeSeconds())"
Invoke-Json -Method POST -Path "/v1/admin/campaigns" -Token $adminToken -Headers @{ "Idempotency-Key" = New-IdempotencyKey "campaign" } -Body @{
    name = $campaignName
    description = "E2E campaign-backed promotions"
    ownerOperatorId = $null
    validFrom = $now.AddMinutes(-5).ToString("O")
    validUntil = $now.AddDays(1).ToString("O")
    voucherIds = @($bookingVoucherId, $parcelVoucherId)
} | Out-Null

Write-Step "promotions BOOKING"
$promotionsBooking = Invoke-Json -Method GET -Path "/v1/promotions?service=BOOKING"
$promotionsBookingData = Get-Data $promotionsBooking
Assert-True (($promotionsBookingData | Where-Object { $_.code -eq $bookingVoucherCode }).Count -ge 1) "BOOKING public promotion not found."

Write-Step "available BOOKING vouchers"
$availableBooking = Invoke-Json -Method GET -Path "/v1/vouchers/available?service=BOOKING&tripId=$tripId&paymentMethod=WALLET&orderAmount=200000" -Token $passengerToken
$availableBookingData = Get-Data $availableBooking
Assert-True (($availableBookingData | Where-Object { $_.code -eq $bookingVoucherCode }).Count -ge 1) "BOOKING voucher not available."

Write-Step "create booking with two seats"
$booking = Invoke-Json -Method POST -Path "/v1/bookings" -Token $passengerToken -Headers @{ "Idempotency-Key" = New-IdempotencyKey "booking" } -Body @{
    tripId = $tripId
    pickup = @{ stationId = $pickupStationId }
    seats = @(
        @{ seatNumber = $seat1; passenger = @{ fullName = "E2E Passenger One"; phoneNumber = "0900000001"; idNumber = "E2E001" } },
        @{ seatNumber = $seat2; passenger = @{ fullName = "E2E Passenger Two"; phoneNumber = "0900000002"; idNumber = "E2E002" } }
    )
    voucherCode = $bookingVoucherCode
    paymentMethod = "WALLET"
}
$bookingData = Get-Data $booking
Assert-True ($bookingData.discountAmount -gt 0) "Booking discount must be > 0."
Assert-True ($bookingData.tickets.Count -eq 2) "Booking must contain exactly 2 tickets."
$ticketDiscount = ($bookingData.tickets | Measure-Object -Property discountAmount -Sum).Sum
Assert-True ($ticketDiscount -eq $bookingData.discountAmount) "Ticket discounts must sum to booking discount."
$bookingId = Get-FirstProperty $bookingData @("bookingId", "id")
Assert-True ($null -ne $bookingId) "Booking response did not return booking id."
Assert-VoucherUsage "BOOKING" $bookingId $bookingVoucherCode

Write-Step "available PARCEL vouchers"
$availableParcel = Invoke-Json -Method GET -Path "/v1/parcels/vouchers/available?tripId=$tripId&sizeCategory=$parcelSize&paymentMethod=WALLET&orderAmount=50000" -Token $passengerToken
$availableParcelData = Get-Data $availableParcel
Assert-True (($availableParcelData | Where-Object { $_.code -eq $parcelVoucherCode }).Count -ge 1) "PARCEL voucher not available."

Write-Step "create parcel with voucher"
$parcel = Invoke-Json -Method POST -Path "/v1/parcels" -Token $passengerToken -Headers @{ "Idempotency-Key" = New-IdempotencyKey "parcel" } -Body @{
    tripId = $tripId
    dropoffStopId = $dropoffStopId
    bookingId = $null
    itemName = "E2E Parcel"
    description = "E2E parcel voucher test"
    sizeCategory = $parcelSize
    estimatedWeightKg = 3.5
    photoUrl = "https://example.com/e2e-parcel.jpg"
    recipient = @{ fullName = "E2E Recipient"; phoneNumber = "0900000100"; email = "recipient@example.com" }
    deliveryMethod = "TERMINAL_PICKUP"
    paymentMethod = "WALLET"
    voucherCode = $parcelVoucherCode
}
$parcelData = Get-Data $parcel
Assert-True ($parcelData.discountAmount -gt 0) "Parcel discount must be > 0."
Assert-True ($parcelData.originalDepositAmount -gt $parcelData.totalAmount) "Parcel original deposit must be greater than discounted total."
Assert-True ($parcelData.voucherCode -eq $parcelVoucherCode) "Parcel response must echo voucher code."
$parcelId = Get-FirstProperty $parcelData @("parcelId", "id")
Assert-True ($null -ne $parcelId) "Parcel response did not return parcel id."
Assert-VoucherUsage "PARCEL" $parcelId $parcelVoucherCode

Write-Step "negative: PARCEL voucher on booking should fail"
Invoke-ExpectFailure {
    Invoke-Json -Method POST -Path "/v1/bookings" -Token $passengerToken -Headers @{ "Idempotency-Key" = New-IdempotencyKey "booking-neg" } -Body @{
        tripId = $tripId
        pickup = @{ stationId = $pickupStationId }
        seats = @(@{ seatNumber = "A03"; passenger = @{ fullName = "E2E Negative"; phoneNumber = "0900000099"; idNumber = "E2E099" } })
        voucherCode = $parcelVoucherCode
        paymentMethod = "WALLET"
    }
} "PARCEL voucher unexpectedly worked for booking."

Write-Step "negative: BOOKING voucher on parcel should fail"
Invoke-ExpectFailure {
    Invoke-Json -Method POST -Path "/v1/parcels" -Token $passengerToken -Headers @{ "Idempotency-Key" = New-IdempotencyKey "parcel-neg" } -Body @{
        tripId = $tripId
        dropoffStopId = $dropoffStopId
        bookingId = $null
        itemName = "E2E Negative Parcel"
        description = "Wrong voucher service"
        sizeCategory = $parcelSize
        estimatedWeightKg = 2.5
        photoUrl = $null
        recipient = @{ fullName = "E2E Recipient"; phoneNumber = "0900000101"; email = "recipient2@example.com" }
        deliveryMethod = "TERMINAL_PICKUP"
        paymentMethod = "WALLET"
        voucherCode = $bookingVoucherCode
    }
} "BOOKING voucher unexpectedly worked for parcel."

Write-Step "negative: PARCEL voucher unavailable for EXTRA_LARGE"
$availableExtraLarge = Invoke-Json -Method GET -Path "/v1/parcels/vouchers/available?tripId=$tripId&sizeCategory=EXTRA_LARGE&paymentMethod=WALLET&orderAmount=50000" -Token $passengerToken
$availableExtraLargeData = Get-Data $availableExtraLarge
Assert-True (($availableExtraLargeData | Where-Object { $_.code -eq $parcelVoucherCode }).Count -eq 0) "PARCEL voucher should not be available for EXTRA_LARGE."

Write-Step "PASS"
