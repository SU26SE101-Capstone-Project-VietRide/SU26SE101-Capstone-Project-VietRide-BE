UPDATE vietride_rag.runtime_configs
SET value = to_jsonb(
    replace(
        value #>> '{}',
        'Only cite chunk IDs included in the retrieved context.',
        E'Không hiển thị chunk ID, UUID, document ID, đường dẫn source hoặc mã kỹ thuật cho người dùng.\nKhông tự thêm mục “Nguồn” vào nội dung câu trả lời. Nguồn tham khảo được hệ thống cung cấp riêng dưới dạng metadata thân thiện.'
    )
)
WHERE key = 'chat.system_prompt'
  AND value_type = 'template'
  AND value #>> '{}' LIKE '%Only cite chunk IDs included in the retrieved context.%';
