# Runbook kiểm tra truy vấn retrieval của RAG

Mục tiêu của runbook này là xác minh bằng số liệu thay vì suy đoán:

- Truy vấn vector có dùng được HNSW index hay không.
- Truy vấn hybrid có bị `CTE Scan` + `Sort` làm mất lợi thế HNSW hay không.
- Có hiện tượng trả ít hơn `LIMIT` dù vẫn còn chunk hợp lệ sau khi lọc tenant/status hay không.

## Chuẩn bị

Chọn một embedding query mẫu có đúng số chiều `halfvec(2048)`. Có thể lấy từ log dev hoặc tạo bằng provider embedding rồi thay vào biến `<QUERY_VECTOR>`.

Các tham số mẫu:

- `LIMIT`: `5`
- `ACCESS_LEVELS`: `ARRAY['PUBLIC']::vietride_rag.knowledge_document_access[]`
- `OPERATOR_ID`: `NULL`

## Vector Search

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT
  c.id,
  c.document_id,
  c.content,
  c.token_count,
  d.access_level,
  c.operator_id,
  c.embedding <=> '<QUERY_VECTOR>'::halfvec AS distance
FROM vietride_rag.knowledge_chunks c
INNER JOIN vietride_rag.knowledge_documents d ON d.id = c.document_id
WHERE d.status = 'APPROVED'::vietride_rag.knowledge_document_status
  AND d.ingest_status = 'COMPLETED'::vietride_rag.knowledge_document_ingest_status
  AND d.access_level = ANY(ARRAY['PUBLIC']::vietride_rag.knowledge_document_access[])
  AND (c.operator_id IS NULL OR (NULL::uuid IS NOT NULL AND c.operator_id = NULL::uuid))
ORDER BY c.embedding <=> '<QUERY_VECTOR>'::halfvec
LIMIT 5;
```

Tín hiệu tốt:

- Có `Index Scan using idx_knowledge_chunks_embedding_hnsw`.
- Số dòng trả về đạt `LIMIT` khi corpus còn đủ chunk hợp lệ.

Tín hiệu xấu:

- `Seq Scan` + `Sort` theo distance.
- Trả ít hơn `LIMIT` dù dữ liệu hợp lệ còn nhiều. Đây có thể là under-return do HNSW lấy ứng viên rồi mới lọc.

## Hybrid Search

```sql
EXPLAIN (ANALYZE, BUFFERS)
WITH scoped_chunks AS (
  SELECT
    c.id,
    c.document_id,
    c.content,
    c.token_count,
    c.search_vector,
    c.embedding,
    d.access_level,
    c.operator_id
  FROM vietride_rag.knowledge_chunks c
  INNER JOIN vietride_rag.knowledge_documents d ON d.id = c.document_id
  WHERE d.status = 'APPROVED'::vietride_rag.knowledge_document_status
    AND d.ingest_status = 'COMPLETED'::vietride_rag.knowledge_document_ingest_status
    AND d.access_level = ANY(ARRAY['PUBLIC']::vietride_rag.knowledge_document_access[])
    AND (c.operator_id IS NULL OR (NULL::uuid IS NOT NULL AND c.operator_id = NULL::uuid))
),
vector_ranked AS (
  SELECT id, row_number() OVER (ORDER BY embedding <=> '<QUERY_VECTOR>'::halfvec) AS vector_rank
  FROM scoped_chunks
  LIMIT 10
),
fts_ranked AS (
  SELECT id, row_number() OVER (ORDER BY ts_rank_cd(search_vector, plainto_tsquery('simple', 'hành lý')) DESC) AS fts_rank
  FROM scoped_chunks
  WHERE search_vector @@ plainto_tsquery('simple', 'hành lý')
  LIMIT 10
)
SELECT s.id
FROM scoped_chunks s
LEFT JOIN vector_ranked v ON v.id = s.id
LEFT JOIN fts_ranked f ON f.id = s.id
WHERE v.id IS NOT NULL OR f.id IS NOT NULL
ORDER BY
  COALESCE(1.0 / (60 + v.vector_rank), 0) +
  COALESCE(1.0 / (60 + f.fts_rank), 0) DESC
LIMIT 5;
```

Tín hiệu cần chú ý:

- Nếu thấy `CTE Scan` rồi `Sort` theo `<=>`, HNSW gần như không tham gia cho nhánh vector của hybrid.
- Nếu thấy `Index Scan using idx_knowledge_chunks_embedding_hnsw`, ghi lại query plan vì đó là tín hiệu tốt.

## Ghi kết quả

Khi chạy xong, dán kết quả chính vào phần này:

- Ngày chạy:
- Dữ liệu test:
- Vector search dùng HNSW: có/không
- Hybrid search dùng HNSW: có/không
- Có under-return: có/không
- Quyết định tiếp theo: giữ nguyên / bật `hnsw.iterative_scan` / refactor hybrid query.
