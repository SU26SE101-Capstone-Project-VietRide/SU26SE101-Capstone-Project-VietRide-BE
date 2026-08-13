# Cẩm nang VietRide dành cho phụ xe

> Knowledge base dùng để trả lời phụ xe bằng tiếng Việt tự nhiên. Tài liệu tập trung vào chuyến được phân công, hành khách và bưu kiện.

## Metadata upload

| Trường | Giá trị |
|---|---|
| Access level | `OPERATOR` |
| Category | `OPERATOR_POLICY` |
| Document type | `GUIDE` |
| Operator | Để trống để dùng chung cho các nhà xe |
| Language | `vi` |
| Audience roles | Chỉ `ASSISTANT` |

## Quy tắc trả lời bắt buộc

- Chỉ hướng dẫn công việc phụ xe được phân công và dữ liệu thuộc đúng nhà xe/chuyến.
- Dịch trạng thái thành lời nói như “đang chờ nhận hàng”, “sẵn sàng xếp lên xe”, “đang chờ người nhận xác nhận”.
- Không đọc mã trạng thái, API, event, service, database, handler hoặc source path.
- Không cấp quyền của tài xế hoặc quản trị viên cho phụ xe.
- Không tiết lộ thông tin riêng của hành khách/người nhận ngoài dữ liệu cần cho vận hành.
- Khi thiếu dữ liệu, xin mã chuyến, mã đặt chỗ/mã bưu kiện và thời điểm; không xin token đăng nhập hoặc token giao hàng.
- Không hiển thị chunk ID, UUID, document ID, đường dẫn source hoặc tự thêm mục “Nguồn”; ứng dụng hiển thị nguồn thân thiện riêng.

## Tài khoản và quyền truy cập

- Phụ xe do nhà xe tạo nhận link đặt mật khẩu lần đầu có hiệu lực mặc định 48 giờ.
- Mật khẩu mới dài ít nhất 8 ký tự, có chữ và số.
- Chỉ đăng nhập bình thường khi tài khoản hoạt động và nhà xe đã được duyệt, không bị tạm ngưng.
- Khi nhà xe bị tạm ngưng, phụ xe không được đăng nhập hoặc làm mới phiên.
- Phiên cũ có thể còn hiệu lực ngắn tối đa 15 phút vì trạng thái nhà xe được chụp khi phát phiên.
- Phụ xe được tải ảnh đại diện, ảnh bằng chứng bưu kiện và ảnh báo sự cố đúng phạm vi.
- Phụ xe có thể đăng ký thiết bị nhận thông báo.

## Chuyến và lịch được phân công

- Phụ xe chỉ xem hoặc thao tác chuyến mình được phân công.
- Biết mã chuyến không tự tạo quyền.
- Lịch tương lai có thể thay đổi mà không viết lại chuyến đã bắt đầu.
- Tài xế, phụ xe và xe được giữ theo cửa sổ vận hành, thời gian quay đầu 30 phút và thời gian di chuyển giữa hai nhiệm vụ.
- Màn hình kiểm tra rảnh chỉ là ảnh chụp; một phân công khác có thể xuất hiện trước lúc tạo/bắt đầu chuyến.

Nếu phụ xe bị báo đang bận, cần kiểm tra nhiệm vụ đang hoạt động hoặc lịch trước/sau. Không hướng dẫn bỏ qua kiểm tra xung đột.

## Vận hành chuyến chính

### Bắt đầu chuyến

Phụ xe không phải người bắt đầu chuyến chính. Chỉ tài xế được phân công thực hiện bước bắt đầu khi chuyến đang cho khách lên xe.

Nếu tài xế hỏi vì sao không bắt đầu được, nguyên nhân có thể là tài xế, phụ xe hoặc xe vẫn đang hoạt động ở nhiệm vụ khác. Phụ xe không được tự dùng quyền khác để vượt qua.

### Ghi nhận điểm dừng

Phụ xe được phân công có thể tham gia thao tác tại điểm dừng theo quyền crew:

- chuyến phải đang chạy;
- điểm dừng chưa được ghi nhận đến;
- muốn rời điểm phải ghi nhận đến trước;
- trước khi rời, hệ thống kiểm tra còn hành khách đang chờ;
- không xác minh được danh sách hành khách thì thao tác rời điểm bị từ chối;
- ghi nhận đến điểm cuối không tự hoàn tất chuyến.

### Hoàn tất chuyến

- Phụ xe hoặc tài xế được phân công có thể hoàn tất chuyến đang chạy.
- Hoàn tất giải phóng lịch giữ tổ phục vụ và xe.
- Chuyến đã kết thúc không thể hoàn tất lần hai như một thao tác mới.

## Danh sách hành khách, quét mã và xác nhận lên xe

### Xem danh sách

- Chỉ tổ phục vụ được phân công mới xem danh sách.
- Danh sách gồm những vé đã được xác nhận.
- Thứ tự ưu tiên theo điểm đón, ghế và mã vé.
- Dữ liệu trả về nhằm phục vụ vận hành, không phải toàn bộ hồ sơ riêng tư.

### Quét mã

- Nhập đúng một mã vé hoặc mã đặt chỗ.
- Mã vé trả đúng hành khách của vé.
- Mã đặt chỗ có thể trả nhiều vé trong cùng đơn.
- Quét chỉ để tìm thông tin, không tự xác nhận đã lên xe.
- Mã không thuộc chuyến hoặc vé không còn hợp lệ bị từ chối.

### Xác nhận hành khách lên xe

- Phụ xe xác nhận theo từng bản ghi hành khách.
- Hành khách chuyển từ đang chờ sang đã lên xe.
- Vé chuyển sang đã sử dụng.
- Người đã được xác nhận không bị đánh dấu vắng mặt.
- Xác nhận lại người đã lên xe bị từ chối.

Nếu hai thao tác khác nhau chạy đồng thời, không nên bấm lặp bằng yêu cầu mới; hãy đọc lại danh sách để biết kết quả cuối.

## Hành khách vắng mặt

- Hệ thống kiểm tra theo đợt mỗi 5 phút.
- Với điểm dọc tuyến, chỉ xét sau thời điểm xe đến điểm đó hơn 15 phút.
- Với bến đầu, chỉ xét sau khi xe bắt đầu chạy hơn 15 phút.
- Đúng mốc 15 phút chưa đánh dấu ngay.
- Chỉ người vẫn đang chờ mới bị đánh dấu vắng.
- Một đơn có thể vắng toàn bộ hoặc vắng một phần.

Thông báo vắng mặt hiện nêu vé không được hoàn tiền.

## GPS chuyến chính

- Phụ xe được phân công có thể gửi GPS cho chuyến chính.
- Phụ xe không được gửi GPS cho xe trung chuyển; quyền đó thuộc tài xế được phân công.
- Tọa độ, tốc độ, hướng và thời điểm phải hợp lệ.
- Gửi lại đúng cùng điểm/thời điểm được coi là lặp an toàn.
- Cùng định danh nhưng nội dung khác bị từ chối.
- Lỗi tính thời gian dự kiến sau khi điểm đã được nhận không làm mất xác nhận GPS.

Điểm nhiễu nhẹ có thể được đặt lên hình tuyến để hiển thị ổn định, trong khi điểm gốc vẫn dùng để phát hiện lệch tuyến.

## Khi chuyến bị trễ hơn 30 phút

- Hệ thống so ETA mới với thời gian dự kiến của điểm dừng kế tiếp.
- Chỉ trễ trên 30 phút mới được đánh dấu; đúng 30 phút chưa vượt ngưỡng.
- Hành khách và Nhà xe được thông báo khi hệ thống xác định đủ người nhận.
- Phụ xe tiếp tục gửi GPS để ETA và trạng thái trễ được cập nhật.
- Nếu có ùn tắc, xe hỏng, tai nạn hoặc thời tiết, phụ xe báo sự cố.
- Phụ xe có thể đề xuất tuyến thay thế; Nhà xe quyết định áp dụng và hệ thống không tự đổi tuyến.

## Báo sự cố

- Phụ xe được phân công chỉ báo sự cố khi chuyến đang chạy.
- Nhóm sự cố gồm ùn tắc, xe hỏng, tai nạn, thời tiết và nguyên nhân khác.
- Mô tả tối đa 500 ký tự.
- Có tối đa ba ảnh đã tải lên đúng phạm vi tài khoản.
- Vị trí phải có đủ cả vĩ độ và kinh độ hoặc không có cả hai.
- Báo cáo được lưu cho nhà xe xem và có thể tạo thông báo.

Hiện chưa xác định được thao tác đánh dấu sự cố đã xử lý xong. Không hướng dẫn phụ xe tự đóng sự cố.

## Đề xuất đổi tuyến

- Phụ xe được phân công có thể gửi đề xuất tuyến thay thế như tài xế.
- Chuyến chưa được kết thúc.
- Sự cố đính kèm phải thuộc đúng chuyến.
- Có thể chọn tuyến thay thế đang có hoặc cung cấp phương án tùy chỉnh đầy đủ.
- Quản trị viên nhà xe là người duyệt.
- Tuyến nguồn thay đổi có thể làm đề xuất hết hiệu lực.
- Một đề xuất khác được duyệt có thể thay thế đề xuất đang chờ.

Phụ xe không tự phê duyệt đề xuất.

## Xe trung chuyển

- Phụ xe không bắt đầu, gửi GPS hoặc vận hành đón/trả cho xe trung chuyển nếu không có vai trò tài xế.
- Thông tin xe trung chuyển có thể xuất hiện trong thông báo vận hành, nhưng không tạo thêm quyền.
- Nếu hành khách cần hỗ trợ xe trung chuyển, hướng dẫn họ theo thông tin chuyến/nhà xe; không dùng dữ liệu điểm đón của hành khách khác.

## Bưu kiện: trách nhiệm chính của phụ xe

### Nguyên tắc chung

- Chỉ xử lý bưu kiện của đúng nhà xe và đúng chuyến được phân công.
- Mã bưu kiện, trạng thái và thời hạn luôn được kiểm tra lại.
- Ảnh bằng chứng phải được tải lên đúng phạm vi của phụ xe.
- Thao tác quan trọng dùng cơ chế chỉ một người thắng khi xử lý đồng thời; bên thua cần đọc lại trạng thái.
- Không tự chọn số tiền thanh toán hoặc hoàn tiền.

## Nhận bưu kiện tại điểm gửi

- Bưu kiện phải đã được giữ chỗ sau thanh toán cọc.
- Phụ xe nhập đúng mã bưu kiện và mã chuyến.
- Việc nhận hàng phải hoàn tất trước giờ khởi hành 30 phút.
- Có thể gửi tối đa ba ảnh bằng chứng.
- Thành công lưu người nhận hàng, thời điểm và bằng chứng.
- Sai chuyến, sai mã, quá hạn hoặc trạng thái không phù hợp bị từ chối.

Bưu kiện đã giữ chỗ nhưng quá hạn nhận có thể bị hệ thống từ chối, mất khoản cọc theo cách xử lý hiện tại và giải phóng sức chứa.

## Cân đo lại

- Chỉ cân lại sau khi bưu kiện đã được nhận.
- Phải thực hiện trước hạn xếp hàng, mặc định trước giờ khởi hành 10 phút.
- Phụ xe nhập số đo thực; hệ thống tự tính lại trọng lượng tính cước và tiền cuối.
- Không tự sửa giá, voucher hoặc số tiền cọc đã khóa.

Kết quả có thể là:

- không còn tiền phải trả và kiện sẵn sàng xếp;
- người gửi cần thanh toán phần còn lại;
- người gửi được hoàn phần cọc dư;
- kiện vượt sức chứa và phải chờ nhà xe quyết định.

Nếu nhà xe cho phép vượt sức chứa nhưng đơn quay lại bước thanh toán phần còn lại, thời hạn cũ có thể đã hết và chưa đủ thông tin để xác định việc gia hạn hoặc thông báo lại. Không khẳng định người gửi chắc chắn nhận được hạn mới.

## Xếp bưu kiện lên xe

- Chỉ xếp kiện đã sẵn sàng.
- Mã bưu kiện phải khớp và phụ xe phải thuộc đúng chuyến.
- Hệ thống cập nhật sức chứa chuyến cùng với trạng thái kiện.
- Nếu cập nhật sức chứa thất bại, thay đổi bưu kiện được hoàn tác.
- Thao tác xếp hàng hiện không tự kiểm tra lại hạn xếp; việc chặn muộn phụ thuộc trạng thái hoặc cơ chế quét liên quan.

Khi chuyến bắt đầu chạy, bưu kiện đã xếp được chuyển sang đang vận chuyển.

## Dỡ bưu kiện

- Kiện phải đang vận chuyển.
- Nếu trả tại điểm dọc tuyến, điểm đó phải thuộc chuyến, cho phép trả và xe đã được ghi nhận đến.
- Nếu trả tại bến cuối, xe phải được ghi nhận đã đến điểm cuối.
- Thành công chuyển kiện sang đã dỡ và giải phóng sức chứa.

Không dỡ kiện ở điểm chưa đến hoặc không thuộc hành trình.

## Bàn giao cho người nhận

- Chỉ bàn giao kiện đã dỡ.
- Phụ xe đúng chuyến có thể gửi tối đa ba ảnh bằng chứng.
- Sau bàn giao, kiện chờ người nhận xác nhận.
- Nếu người nhận có email, hệ thống gửi link có hiệu lực 48 giờ.
- Nếu không có email, không tạo link và cần xác nhận thủ công.
- Link cũ bị thu hồi khi phát hành lại hoặc khi giao dịch đã kết thúc.

Email hiện được gửi trước khi bước lưu dữ liệu hoàn tất. Nếu bước lưu thất bại, người nhận có thể nhận một link không dùng được. Không nói việc gửi link là chính xác tuyệt đối một lần.

## Xác nhận giao hàng thủ công

Phụ xe được phân công có thể xác nhận thủ công khi:

- bưu kiện đang chờ người nhận xác nhận;
- thuộc đúng chuyến và nhà xe;
- ghi chú không quá 500 ký tự.

Lặp lại cùng người xác nhận và cùng nội dung chuẩn hóa có thể được coi là thao tác lặp an toàn. Nội dung khác không được giả định là lặp.

Không dùng mã bưu kiện để tự tạo link xác nhận cho người nhận.

## Người nhận từ chối

- Người nhận có thể từ chối bằng link và phải nhập lý do.
- Trong 15 phút, cùng link có thể hoàn tác quyết định từ chối.
- Đúng mốc 15 phút đã hết hạn hoàn tác.
- Sau cửa sổ này, hệ thống bắt đầu nhánh hoàn hàng.
- Chưa đủ thông tin để xác định đầy đủ bước tự động từ “bắt đầu hoàn” đến “đã trả xong”. Không hứa hàng chắc chắn tự về người gửi nếu chưa có cập nhật.

## Chuyển bưu kiện sang chuyến khác

Khi bưu kiện đã lên xe hoặc đang vận chuyển:

- nhà xe chọn chuyến đích phù hợp;
- tổ phục vụ chuyến đích phải xác nhận trong 30 phút;
- tại đúng hạn, nhánh hết hạn có thể thắng;
- hai người xác nhận đồng thời chỉ một người thành công;
- sau khi chuyển, kiện giữ trạng thái đã xếp để chờ chuyến mới bắt đầu.

Khi bưu kiện đang chờ nhà xe xử lý, việc chuyển dùng một yêu cầu phục hồi bền vững để tránh chuyển và hoàn cùng lúc.

Phụ xe không tự chọn chuyến đích nếu không có thao tác nhà xe tương ứng.

## Khi chuyến bị hủy, gián đoạn hoặc thay xe

- Kiện chưa xếp thường được hủy, hoàn khoản còn phải hoàn và giải phóng sức chứa.
- Kiện đã xếp/đang vận chuyển chuyển sang chờ nhà xe chọn chuyển chuyến hoặc hoàn.
- Thay xe có thể chuyển dữ liệu hàng sang chuyến thay thế.
- Không tự kết luận tiền đã hoàn chỉ vì kiện bị hủy; tiền về ví là bước riêng.

## Hoàn tiền bưu kiện

- Phụ xe không xác nhận hoặc thực hiện hoàn tiền.
- Số tiền cần hoàn và số tiền đã thực sự vào ví có thể khác nhau trong lúc xử lý.
- Khoản hoàn được chuyển vào Ví VietRide.
- Yêu cầu lặp không được hoàn hai lần.
- Hệ thống hiện không có thời gian xử lý được cam kết chính xác.

Nếu người gửi hỏi, xin mã bưu kiện và hướng dẫn kiểm tra ví; không yêu cầu người gửi cung cấp bí mật thanh toán.

## Thông báo dành cho phụ xe

Phụ xe có thể nhận:

- phân công hoặc thay đổi chuyến;
- bắt đầu cho khách lên xe, đổi giờ/tuyến/xe;
- hành khách còn chờ, vắng mặt hoặc đã lên xe;
- báo cáo sự cố và lệch tuyến;
- bưu kiện cần nhận, cân, xếp, dỡ, bàn giao hoặc chuyển;
- thông báo xe trung chuyển liên quan vận hành.

Hộp thông báo được lưu trước khi thử gửi thông báo đẩy. Không nhận banner không đồng nghĩa thao tác thất bại.

## Khi cần hỗ trợ

Xin tối thiểu:

- mã chuyến;
- mã đặt chỗ hoặc mã bưu kiện;
- thời điểm thao tác;
- bước đang thực hiện: nhận, cân, xếp, dỡ, bàn giao hay chuyển.

Không xin token đăng nhập, link giao hàng nguyên bản, ảnh giấy tờ không cần thiết hoặc dữ liệu riêng ngoài phạm vi hỗ trợ.

## Mẫu trả lời nhanh

### “Cần kiểm tra gì trước khi rời điểm dừng?”

Chuyến phải đang chạy và điểm dừng phải được ghi nhận đã đến. Trước khi rời, hệ thống kiểm tra còn hành khách đang chờ hay không; nếu không xác minh được danh sách, thao tác bị từ chối để tránh bỏ sót khách.

### “Khi nào được dỡ hàng tại bến đích?”

Xe phải được ghi nhận đã đến bến đích và bưu kiện phải đang vận chuyển. Sau khi dỡ thành công, kiện chuyển sang đã dỡ và sức chứa chuyến được giải phóng.

### “Tại sao tôi không nhận được bưu kiện?”

Bưu kiện phải thuộc đúng chuyến, đã thanh toán cọc và còn trước hạn nhận hàng. Bạn gửi mã chuyến, mã bưu kiện và thời điểm thao tác để kiểm tra điều kiện nào chưa đạt.

### “Cân xong sao chưa được xếp lên xe?”

Sau khi cân, bưu kiện có thể còn chờ người gửi thanh toán phần còn lại hoặc chờ nhà xe xử lý sức chứa. Chỉ kiện đã hoàn tất các bước này mới sẵn sàng xếp.

### “Người nhận không có email thì xác nhận thế nào?”

Nếu người nhận không có email, hệ thống không tạo link. Phụ xe, tài xế hoặc nhà xe có quyền phù hợp cần xác nhận giao hàng thủ công.

### “Đã đến điểm cuối nhưng chưa dỡ được hàng?”

Xe phải được ghi nhận đã đến điểm cuối và kiện phải đang trên đường vận chuyển. Bạn kiểm tra lại mã chuyến, mã bưu kiện và bước ghi nhận đến nơi.

### “Nếu chuyến trễ hơn 30 phút thì sao?”

Khi ETA mới trễ hơn thời gian dự kiến trên 30 phút, hệ thống ghi nhận chuyến bị trễ và thông báo cho hành khách cùng Nhà xe. Bạn tiếp tục gửi GPS; nếu có ùn tắc, xe hỏng, tai nạn hoặc thời tiết xấu thì báo sự cố. Bạn có thể đề xuất tuyến thay thế nhưng Nhà xe là bên quyết định.
