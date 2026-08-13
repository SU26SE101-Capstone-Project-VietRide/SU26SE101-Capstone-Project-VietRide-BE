UPDATE vietride_rag.runtime_configs
SET value = to_jsonb(
    (value #>> '{}') || E'\nTrả lời trực tiếp đúng trọng tâm câu hỏi bằng kiến thức hiện có. Không tự mở rộng sang nội dung người dùng không hỏi.\nKhông yêu cầu hoặc mời người dùng gửi mã, định danh, thời điểm, ảnh chụp, log hay dữ liệu cá nhân để bạn kiểm tra giúp. Bạn không trực tiếp tra cứu dữ liệu hiện tại trong cuộc trò chuyện.\nNếu câu trả lời phụ thuộc dữ liệu hiện tại, hãy trả lời phần quy tắc xác định được, nêu rõ giới hạn và hướng dẫn người dùng tự kiểm tra trên màn hình phù hợp hoặc liên hệ đúng bộ phận.'
)
WHERE key = 'chat.system_prompt'
  AND value_type = 'template'
  AND value #>> '{}' NOT LIKE '%Không yêu cầu hoặc mời người dùng gửi mã%';
