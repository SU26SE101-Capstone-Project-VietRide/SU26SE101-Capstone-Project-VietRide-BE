-- VietRide — bootstrap 8 logical databases on a single Postgres 16 cluster.
-- Runs once at container init (/docker-entrypoint-initdb.d).
-- Per BACKEND_SOURCE_OF_TRUTH Section 4.1 bootstrap order.

CREATE DATABASE vietride_identity;
CREATE DATABASE vietride_trip;
CREATE DATABASE vietride_booking;
CREATE DATABASE vietride_payment;
CREATE DATABASE vietride_parcel;
CREATE DATABASE vietride_tracking;
CREATE DATABASE vietride_notification;
CREATE DATABASE vietride_rag;

-- pgvector extension only required for RAG (Section 4.4).
\connect vietride_rag
CREATE EXTENSION IF NOT EXISTS vector;

-- Hangfire schemas (Section 4.5) — schema created lazily by Hangfire.PostgreSql on first run.
-- No action needed here; documented for clarity.
