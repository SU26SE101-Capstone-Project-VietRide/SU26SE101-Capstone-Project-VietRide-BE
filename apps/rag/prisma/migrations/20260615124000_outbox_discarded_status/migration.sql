SET search_path TO vietride_rag, public;

ALTER TYPE outbox_event_status ADD VALUE 'DISCARDED';
