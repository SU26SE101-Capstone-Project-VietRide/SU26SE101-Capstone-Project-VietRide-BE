# Cẩm nang VietRide dành cho tài xế

> Knowledge base dùng để trả lời tài xế bằng tiếng Việt tự nhiên. Không dùng tài liệu này để cấp quyền quản trị nhà xe hoặc giải thích dữ liệu riêng của hành khách.

## Metadata upload

| Trường | Giá trị |
|---|---|
| Access level | `OPERATOR` |
| Category | `OPERATOR_POLICY` |
| Document type | `GUIDE` |
| Operator | Để trống để dùng chung cho các nhà xe |
| Language | `vi` |
| Audience roles | Chỉ `DRIVER` |

## Quy tắc trả lời bắt buộc

- Chỉ hướng dẫn công việc tài xế được phân công và dữ liệu thuộc đúng nhà xe/chuyến.
- Dùng lời nói tự nhiên như “chuyến đang cho khách lên xe”, “chuyến đã bắt đầu”, “xe hoặc tài xế còn bận ở chuyến khác”.
- Không đọc mã trạng thái, mã event, tên API, service, database hoặc source path.
- Không cung cấp thao tác dành riêng cho phụ xe, nhân viên nhà xe, quản trị viên nhà xe hoặc Quản trị viên hệ thống.
- Không tiết lộ thông tin liên hệ riêng của hành khách ngoài dữ liệu vận hành được phép.
- Khi chưa đủ dữ liệu, xin mã chuyến, thời điểm và hành động tài xế đang thực hiện; không xin token đăng nhập.
- Chỉ chuyển sang thuật ngữ kỹ thuật khi tài xế đang gửi log/mã lỗi để được hỗ trợ.
- Ưu tiên từ ngữ vận hành dễ hiểu. Không dùng từ viết tắt hoặc thuật ngữ nội bộ như “ETA”, “GPS”, “delayed alert”, “route proposal” trong câu trả lời thông thường; dùng “thời gian dự kiến đến”, “định vị”, “cảnh báo chuyến trễ” và “đề xuất đường đi khác”. Nếu cần nhắc thuật ngữ trên màn hình, giải thích tiếng Việt trước rồi mới đặt tên kỹ thuật trong ngoặc.
- Không hiển thị chunk ID, UUID, document ID, đường dẫn source hoặc tự thêm mục “Nguồn”; ứng dụng hiển thị nguồn thân thiện riêng.

## Tài khoản và quyền truy cập

- Tài xế do nhà xe tạo sẽ nhận link đặt mật khẩu lần đầu, mặc định có hiệu lực 48 giờ và chỉ dùng một lần.
- Mật khẩu mới dài ít nhất 8 ký tự, có chữ và số.
- Tài xế chỉ đăng nhập bình thường khi tài khoản đang hoạt động và nhà xe đã được duyệt, không bị tạm ngưng.
- Khi nhà xe bị tạm ngưng, tài xế không được phát phiên đăng nhập mới và các phiên làm mới hiện có bị thu hồi.
- Một phiên đăng nhập cũ có thể còn hiệu lực trong thời gian ngắn tối đa 15 phút vì trạng thái nhà xe được chụp lúc phát phiên.
- Tài xế có thể cập nhật ảnh đại diện của mình và tải ảnh báo sự cố đúng phạm vi được cấp.
- Tài xế có thể đăng ký thiết bị để nhận thông báo đẩy.

## Lịch và chuyến được phân công

- Tài xế chỉ xem hoặc thao tác chuyến mình được phân công.
- Việc biết mã chuyến không tự tạo quyền.
- Lịch có thể được tạo theo ngày lặp và sinh chuyến tự động.
- Thay đổi lịch tương lai không viết lại những chuyến đã bắt đầu vận hành.
- Một lịch bị ngừng hoạt động sẽ không sinh thêm chuyến mới.

Khi trả lời “Tại sao tôi chưa thấy chuyến?”, cần phân biệt:

- lịch chưa hoạt động;
- tuyến hoặc xe không còn hợp lệ;
- nhà xe hết hạn mức số chuyến;
- tài xế/xe bị trùng lịch;
- job sinh chuyến chưa đến lượt quét.

Không hứa một chuyến sẽ xuất hiện đúng từng giây vì việc sinh chuyến chạy theo lịch và có thể bị bỏ qua khi không thỏa điều kiện.

## Vì sao tài xế hoặc xe bị báo không rảnh

Hệ thống kiểm tra cả chuyến chính và xe trung chuyển. Hai nhiệm vụ có thể không chồng giờ nhưng vẫn xung đột nếu:

- chưa đủ 30 phút quay đầu;
- điểm kết thúc trước và điểm bắt đầu sau khác nhau, không đủ thời gian di chuyển;
- tài xế hoặc xe vẫn đang hoạt động ở một nhiệm vụ khác;
- một yêu cầu khác đã giữ tài nguyên sau lúc xem trước.

Màn hình kiểm tra rảnh chỉ là ảnh chụp tại thời điểm xem và không giữ tài xế/xe. Khi tạo hoặc bắt đầu chuyến, hệ thống kiểm tra lại nên kết quả có thể thay đổi.

Cách trả lời:

> Ngoài giờ chạy, hệ thống còn tính thời gian quay đầu và thời gian di chuyển giữa hai nhiệm vụ. Lịch xem trước cũng có thể thay đổi nếu một chuyến khác được phân công trước khi bạn bắt đầu.

## Bắt đầu chuyến chính

- Chỉ tài xế được phân công mới bắt đầu chuyến.
- Chuyến phải đang trong thời gian cho khách lên xe, chưa bắt đầu chạy và chưa kết thúc.
- Khi bắt đầu thành công, hệ thống ghi thời điểm rời bến thực tế và thông báo cho các chức năng liên quan.
- Nếu tài xế, phụ xe hoặc xe vẫn đang hoạt động ở nhiệm vụ khác, chuyến không bắt đầu và giữ nguyên trạng thái chờ.
- Trường hợp bị chặn do tài nguyên còn bận được ghi nhận một lần để quản trị viên nhà xe xử lý; thử lại liên tục không tạo vô hạn thông báo giống nhau.
- Hệ thống có thể tự thử bắt đầu chuyến bị trễ, nhưng vẫn kiểm tra tài nguyên như thao tác thủ công.

Khi bị chặn, hãy hướng dẫn tài xế liên hệ quản trị viên nhà xe để kết thúc/giải phóng nhiệm vụ đang giữ tài nguyên hoặc điều chỉnh phân công. Không hướng dẫn bỏ qua kiểm tra.

## Đến và rời điểm dừng

### Ghi nhận đến điểm dừng

- Tài xế phải được phân công.
- Chuyến phải đang chạy.
- Điểm dừng phải chưa được ghi nhận đến.
- Thành công lưu thời điểm đến thực tế.

### Ghi nhận rời điểm dừng

- Điểm dừng phải đã được ghi nhận đến và chưa rời.
- Hệ thống kiểm tra còn hành khách đang chờ lên xe hay không.
- Nếu chức năng kiểm tra hành khách không phản hồi, hệ thống từ chối ghi rời điểm thay vì giả định không còn ai.
- Nếu vẫn còn hành khách chưa lên xe, hệ thống ghi cảnh báo cho tổ phục vụ/nhà xe.

### Đến điểm cuối

- Ghi nhận đến điểm cuối chỉ lưu thời điểm xe đến.
- Thao tác này không tự hoàn tất chuyến.
- Chuyến chỉ hoàn tất khi tài xế hoặc phụ xe được phân công thực hiện bước hoàn tất, hoặc cơ chế tự hoàn tất chạy sau mốc dự kiến.

## Hoàn tất chuyến

- Tài xế hoặc phụ xe được phân công có thể hoàn tất chuyến đang chạy.
- Hoàn tất giải phóng lịch giữ tài xế, phụ xe và xe.
- Hệ thống có thể tự hoàn tất khi đã quá thời gian đến dự kiến 30 phút; việc quét không xảy ra đúng từng giây.
- Chuyến đã hoàn tất, bị hủy hoặc gián đoạn không quay lại trạng thái đang chạy bằng thao tác thường.

## Danh sách hành khách và lên xe

- Tài xế được phân công có thể xem danh sách hành khách của chuyến.
- Danh sách chỉ gồm vé đã được xác nhận và sắp theo điểm đón, ghế, mã vé.
- Dữ liệu phục vụ vận hành không phải toàn bộ hồ sơ riêng tư của hành khách.
- Quét mã vé hoặc mã đặt chỗ chỉ để tìm thông tin, không tự xác nhận hành khách đã lên xe.
- Tài xế phải thực hiện bước xác nhận lên xe theo từng hành khách.
- Sau khi xác nhận, vé được ghi nhận đã sử dụng.
- Hành khách đã được xác nhận lên xe không bị đánh dấu vắng mặt.

Nếu mã không thuộc chuyến, vé không còn dùng được hoặc hành khách đã được xác nhận trước đó, thao tác bị từ chối.

## Hành khách vắng mặt

- Hệ thống kiểm tra theo đợt mỗi 5 phút.
- Với điểm đón dọc tuyến, chỉ xét sau khi xe đến điểm đó hơn 15 phút.
- Với bến đầu, chỉ xét sau khi xe bắt đầu chạy hơn 15 phút.
- Đúng mốc 15 phút chưa bị đánh dấu ngay.
- Chỉ những hành khách vẫn chưa được xác nhận lên xe mới bị đánh dấu vắng.
- Một đơn có thể vắng toàn bộ hoặc vắng một phần.

Thông báo vắng mặt hiện cho biết vé không được hoàn tiền.

## GPS và theo dõi

### Gửi vị trí chuyến chính

- Tài xế được phân công có thể gửi vị trí.
- Tọa độ, tốc độ, hướng và thời điểm phải hợp lệ.
- Gửi lại đúng cùng một điểm và thời điểm được coi là lặp an toàn, không phát cập nhật lần hai.
- Gửi cùng định danh nhưng thay nội dung bị từ chối.
- Lỗi tính thời gian dự kiến hoặc cảnh báo sau khi điểm đã được nhận không làm mất xác nhận gửi vị trí.

### Hiển thị vị trí

- Điểm GPS nhiễu nhẹ trong phạm vi 50 mét có thể được đặt lên hình tuyến để bản đồ ổn định hơn.
- Điểm gốc vẫn được dùng để phát hiện xe thực sự đi lệch.
- Nếu xe cách tuyến trên 500 mét liên tục quá 2 phút, hệ thống có thể phát cảnh báo lệch tuyến.
- Trở lại tuyến trước khi hết thời gian chờ sẽ không phát cảnh báo.

### Thời gian dự kiến

- Hệ thống ưu tiên dữ liệu đường đi có giao thông.
- Khi nguồn ngoài không dùng được, hệ thống dùng hình tuyến và tốc độ cục bộ.
- Nếu vẫn thiếu dữ liệu, có thể dùng khoảng cách trực tiếp hoặc không trả thời gian dự kiến.
- Vị trí quá cũ có thể làm thời gian dự kiến không khả dụng.

Không tự đoán thời gian đến khi hệ thống không có đủ dữ liệu.

### Khi chuyến bị trễ hơn 30 phút

- Hệ thống so ETA mới với thời gian dự kiến của điểm dừng kế tiếp.
- Trễ trên 30 phút mới được đánh dấu; đúng 30 phút chưa vượt ngưỡng.
- Hành khách và Nhà xe được thông báo khi hệ thống xác định đủ người nhận.
- Tài xế tiếp tục gửi GPS để ETA và trạng thái trễ được cập nhật.
- Nếu nguyên nhân là ùn tắc, xe hỏng, tai nạn hoặc thời tiết, tài xế báo sự cố và có thể đề xuất tuyến thay thế.
- Nhà xe quyết định áp dụng tuyến thay thế; hệ thống không tự đổi tuyến chỉ vì phát hiện trễ.

## Báo sự cố

- Tài xế được phân công chỉ báo sự cố khi chuyến đang chạy.
- Các nhóm sự cố gồm ùn tắc, xe hỏng, tai nạn, thời tiết và nguyên nhân khác.
- Có thể gửi tối đa ba ảnh đã tải lên đúng phạm vi tài khoản.
- Vị trí phải có đủ cả vĩ độ và kinh độ hoặc bỏ trống cả hai.
- Mô tả tối đa 500 ký tự.
- Báo cáo thành công được lưu để nhà xe xem và có thể tạo thông báo.

Hiện chưa xác định được thao tác để nhà xe đánh dấu một sự cố đã xử lý xong. Không hứa rằng tài xế có thể tự đóng sự cố.

## Đề xuất đổi tuyến

- Tài xế được phân công có thể đề xuất dùng tuyến thay thế có sẵn hoặc gửi phương án tùy chỉnh.
- Chuyến chưa được kết thúc.
- Nếu gắn sự cố, sự cố phải thuộc đúng chuyến.
- Phương án có sẵn được chụp phiên bản để phát hiện khi dữ liệu nguồn đã thay đổi.
- Phương án tùy chỉnh phải có đầy đủ hình tuyến và điểm dừng cần thiết.
- Quản trị viên nhà xe là người phê duyệt.
- Tuyến nguồn bị sửa hoặc ngừng hoạt động có thể làm đề xuất hết hiệu lực.
- Một phương án khác được duyệt có thể thay thế đề xuất đang chờ.

Tài xế không tự phê duyệt đề xuất của mình.

## Xe trung chuyển

### Quyền của tài xế

- Chỉ tài xế được phân công mới bắt đầu, gửi GPS và vận hành đón/trả trên xe trung chuyển.
- Phụ xe không được dùng quyền của tài xế để vận hành xe trung chuyển.

### Bắt đầu và hoàn tất

- Chuyến trung chuyển bắt đầu từ trạng thái đã lên lịch.
- Khi bắt đầu, tài xế và xe được đánh dấu đang hoạt động.
- Nếu tài xế/xe còn hoạt động ở nhiệm vụ khác, không thể bắt đầu.
- Chỉ hoàn tất khi không còn hành khách chờ đón hoặc đang ở trên xe.
- Hủy chuyến trung chuyển làm các hành khách chưa kết thúc chuyển sang đã hủy.

### Đón và trả hành khách

- Một thao tác đón xử lý cả nhóm có cùng thứ tự đón.
- Chỉ hành khách đang chờ mới được đánh dấu đã đón.
- Chỉ hành khách đã đón mới được đánh dấu đã trả.
- Hành khách vắng mặt phải có lý do.

### GPS xe trung chuyển

- Chỉ tài xế được phân công gửi GPS.
- Thời gian dự kiến ưu tiên dữ liệu đường đi, sau đó dùng phép tính cục bộ.
- Vị trí mới nhất được giữ trong khoảng 5 phút; kết quả thời gian dự kiến thường được giữ khoảng 1 phút.

## Bưu kiện: phần việc của tài xế

Tài xế không thực hiện các bước nhận hàng, cân lại, xếp hàng, dỡ hàng hoặc bàn giao vốn thuộc trách nhiệm chính của phụ xe.

Tài xế có thể tham gia:

- xác nhận chuyển bưu kiện sang chuyến mới khi được phân công đúng chuyến đích;
- xác nhận giao hàng thủ công trong phạm vi crew được cấp quyền;
- phối hợp xử lý khi chuyến bị hủy, gián đoạn hoặc thay xe.

Khi xác nhận chuyển:

- bưu kiện phải đang chờ xác nhận chuyển;
- chuyến đích và tổ phục vụ phải đúng;
- thời hạn xác nhận là 30 phút;
- tại đúng thời hạn, nhánh hết hạn có thể thắng;
- hai người xác nhận đồng thời chỉ một người thành công.

Không hướng dẫn tài xế tự đổi trạng thái bưu kiện hoặc tự hoàn tiền.

## Thông báo dành cho tài xế

Tài xế có thể nhận thông báo về:

- chuyến được phân công hoặc thay đổi tổ phục vụ;
- bắt đầu cho khách lên xe, đổi giờ, đổi tuyến, đổi xe hoặc chuyến trễ;
- báo cáo sự cố và cảnh báo lệch tuyến;
- hành khách còn chờ khi xe rời điểm;
- xe trung chuyển được phân công, cảnh báo hoặc không thể đáp ứng;
- chuyển bưu kiện và các hành động crew liên quan.

Thông báo trong ứng dụng được lưu trước khi thử gửi thông báo đẩy. Không thấy banner không đồng nghĩa thao tác chưa được ghi nhận.

## Khi cần hỗ trợ

Xin tối thiểu:

- mã chuyến;
- thời điểm xảy ra;
- hành động tài xế đang thực hiện;
- nội dung thông báo dễ hiểu trên màn hình.

Chỉ xin mã lỗi kỹ thuật nếu đang chuyển cho đội phát triển. Không xin access token hoặc dữ liệu riêng của hành khách.

## Mẫu trả lời nhanh

### “Tôi không bắt đầu được chuyến”

Chuyến chỉ bắt đầu khi đang trong thời gian cho khách lên xe và tài xế, phụ xe, xe không còn bận ở nhiệm vụ khác. Bạn gửi mã chuyến và thời điểm thao tác để nhà xe kiểm tra phân công đang giữ tài nguyên.

### “Tôi đã đến điểm cuối nhưng chuyến chưa hoàn tất”

Ghi nhận đến điểm cuối không tự kết thúc chuyến. Tài xế hoặc phụ xe được phân công vẫn cần thực hiện bước hoàn tất chuyến.

### “Tại sao rời điểm dừng bị chặn?”

Điểm dừng phải được ghi nhận đã đến trước. Hệ thống còn kiểm tra hành khách đang chờ; nếu không xác minh được dữ liệu này, thao tác rời điểm sẽ bị từ chối để tránh bỏ sót khách.

### “GPS gửi thành công nhưng chưa thấy thời gian dự kiến”

Điểm vị trí có thể đã được nhận trong khi bước tính thời gian dự kiến đang chậm hoặc thiếu dữ liệu tuyến. Việc này không nhất thiết có nghĩa vị trí bị mất.

### “Nếu chuyến trễ hơn 30 phút thì sao?”

Bạn hãy tiếp tục bật định vị và cập nhật vị trí xe. Khi thời gian dự kiến đến nơi chậm hơn lịch ban đầu trên 30 phút, hệ thống ghi nhận chuyến bị trễ và thông báo cho hành khách cùng Nhà xe. Nếu có ùn tắc, xe hỏng, tai nạn hoặc thời tiết xấu, hãy báo sự cố. Bạn có thể đề xuất đường đi khác, nhưng Nhà xe là bên quyết định; hệ thống không tự đổi đường đi. Nếu chỉ trễ đúng 30 phút thì chưa được tính là vượt ngưỡng.
