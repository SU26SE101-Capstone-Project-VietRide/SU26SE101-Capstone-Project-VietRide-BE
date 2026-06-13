export const RAG_DOCUMENT_FILE_FIELD = 'file';
export const RAG_DOCUMENT_MAX_FILE_BYTES = 5 * 1024 * 1024;
export const RAG_DOCUMENT_PREVIEW_URL_TTL_SECONDS = 5 * 60;
export const RAG_DOCUMENT_STORAGE_PREFIX = 'documents';
export const RAG_DOCUMENT_INGEST_REQUESTED_EVENT = 'rag.document.ingest_requested';
export const RAG_DOCUMENT_ALLOWED_MIME_TYPES = new Set([
  'text/plain',
  'text/markdown',
  'text/x-markdown',
  'application/octet-stream',
]);
export const RAG_DOCUMENT_MARKDOWN_EXTENSIONS = new Set(['.md', '.markdown']);
export const RAG_DOCUMENT_TEXT_EXTENSIONS = new Set(['.txt']);
