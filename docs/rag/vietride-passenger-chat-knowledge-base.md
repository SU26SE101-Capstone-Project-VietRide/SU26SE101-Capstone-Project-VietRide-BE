# Cẩm nang VietRide dành cho hành khách

> Knowledge base dùng để trả lời hành khách bằng tiếng Việt tự nhiên. Không dùng tài liệu này để trả lời cho vai trò vận hành hoặc quản trị.

## Metadata upload

| Trường | Giá trị |
|---|---|
| Access level | `PUBLIC` |
| Category | `CUSTOMER_SUPPORT` |
| Document type | `GUIDE` |
| Operator | Để trống |
| Language | `vi` |
| Audience roles | Chỉ `PASSENGER` |

## Quy tắc trả lời bắt buộc

- Trả lời thẳng điều hành khách muốn biết, sau đó mới giải thích điều kiện liên quan.
- Dùng các từ như “vé đang chờ thanh toán”, “vé đã được giữ chỗ”, “chuyến chưa bắt đầu chạy”; không đưa mã trạng thái nội bộ vào câu trả lời.
- Không nhắc tên service, controller, API, database, event, queue, handler hoặc đường dẫn source.
- Không hướng dẫn hành khách thực hiện thao tác dành cho tài xế, phụ xe, nhà xe hoặc quản trị viên.
- Không tự khẳng định số dư, vị trí xe, ghế trống, trạng thái vé hay bưu kiện hiện tại nếu chưa có dữ liệu thực tế.
- Khi chưa đủ thông tin, nói “Mình chưa có đủ thông tin để xác định trường hợp của bạn” và xin đúng mã đặt chỗ, mã vé, mã bưu kiện hoặc thời điểm liên quan.
- Không xin mật khẩu, mã OTP, access token, refresh token, chữ ký thanh toán hoặc token xác nhận giao hàng.
- Chỉ nói mã lỗi hoặc thuật ngữ kỹ thuật khi hành khách chủ động gửi mã đó và muốn được giải thích.
- Không hiển thị chunk ID, UUID, document ID, đường dẫn source hoặc tự thêm mục “Nguồn”; ứng dụng hiển thị nguồn thân thiện riêng.

## Tài khoản và đăng nhập

### Đăng ký bằng email

Hành khách đăng ký bằng email, tên hiển thị, mật khẩu và số điện thoại hợp lệ. Email và số điện thoại không được trùng tài khoản khác.

- Mật khẩu đăng ký dài từ 8 đến 128 ký tự.
- Hệ thống gửi mã xác minh gồm 6 chữ số, có hiệu lực 5 phút.
- Nhập sai từ 5 lần khiến mã không còn dùng được.
- Chỉ được yêu cầu gửi mã tối đa ba lần trong một giờ cho cùng email.
- Gửi lại mã làm mã cũ đang còn hiệu lực bị thu hồi.
- Đăng ký thành công nghĩa là yêu cầu gửi email đã được ghi nhận, không bảo đảm email đến ngay lập tức.

### Đăng nhập bằng mật khẩu

- Tài khoản hợp lệ và mật khẩu đúng sẽ được đăng nhập.
- Tài khoản còn chờ xác minh email vẫn có thể đăng nhập, nhưng thông tin tài khoản vẫn thể hiện chưa xác minh.
- Nhập sai nhiều lần có thể làm tài khoản bị khóa; hết thời gian đếm lỗi không tự mở khóa tài khoản.
- Email không tồn tại và mật khẩu sai đều được trả lời theo cách không làm lộ tài khoản nào đang tồn tại.
- Phiên đăng nhập ngắn hạn có hiệu lực 15 phút; phiên dùng để làm mới đăng nhập có hiệu lực 30 ngày.

### Đăng nhập bằng Google

- Email Google đã liên kết sẽ đăng nhập vào tài khoản hiện có.
- Email đã có tài khoản nhưng chưa liên kết Google sẽ được liên kết rồi đăng nhập.
- Email mới tạo một tài khoản hành khách đang hoạt động mà không cần xác minh email bằng mã.
- Tài khoản Google mới có thể chưa có số điện thoại. Khi đó người dùng vẫn đăng nhập được nhưng phần lớn chức năng sẽ yêu cầu hoàn tất hồ sơ trước.
- Đăng nhập lại không tạo thêm tài khoản hoặc thêm một ví mới.

### Quên mật khẩu

- Yêu cầu quên mật khẩu luôn trả lời trung tính để không làm lộ email có tồn tại hay không.
- Chỉ tài khoản đang hoạt động mới thực sự nhận mã đặt lại mật khẩu.
- Mã có 6 chữ số, hiệu lực 5 phút và chỉ dùng một lần.
- Mật khẩu mới dài từ 8 đến 128 ký tự, có ít nhất một chữ và một chữ số.
- Tối đa ba yêu cầu đặt lại trong một giờ cho cùng email.

### Hồ sơ cá nhân

- Hành khách chỉ xem và sửa hồ sơ của chính mình.
- Hoàn tất hồ sơ chỉ dùng để thêm số điện thoại khi tài khoản chưa có số; đây không phải chức năng đổi số điện thoại hiện có.
- Số điện thoại phải hợp lệ và chưa thuộc tài khoản khác.
- Ảnh đại diện phải là ảnh đã tải lên đúng khu vực lưu trữ của chính người dùng.
- Tài khoản bị khóa không được xin quyền tải ảnh mới.

## Tìm chuyến và xem ghế

### Tìm chuyến

- Ngày tìm kiếm được hiểu theo giờ Việt Nam.
- Người dùng phải chọn đủ nơi đi và nơi đến.
- Điểm đón phải đứng trước điểm trả trên hành trình.
- Chỉ những chuyến chưa bắt đầu đón khách và còn đủ số ghế yêu cầu mới xuất hiện.
- Giá hiển thị đã gồm phụ thu đang có hiệu lực.
- Kết quả tìm kiếm chỉ là thông tin tại thời điểm xem, chưa giữ ghế.

Nếu vừa thấy chuyến hoặc ghế trống nhưng lúc đặt không còn, nguyên nhân thường là người khác đã giữ hoặc đặt trước khi thao tác của người dùng hoàn tất.

### Trạm và điểm đón/trả

VietRide phân biệt bến chính và điểm đón/trả dọc tuyến. Một điểm dừng có thể chỉ cho đón, chỉ cho trả hoặc cho cả hai. Khi một điểm bị ngừng sử dụng, hệ thống có thể đề xuất chuyển sang bến đầu/cuối hoặc một phương án khác tùy trường hợp.

### Sơ đồ ghế

- Ghế có thể đang trống, đang được giữ tạm, đã được đặt hoặc bị nhà xe ngừng bán.
- Xem sơ đồ không giữ ghế.
- Khi bắt đầu đặt, hệ thống mới cố giữ toàn bộ ghế đã chọn trong 10 phút.
- Nếu một ghế không còn khả dụng, toàn bộ yêu cầu giữ ghế liên quan có thể thất bại để tránh tạo đơn thiếu ghế.

## Đặt vé

### Đặt vé một chiều

Hành khách có thể chọn từ 1 đến 5 ghế trong một lần đặt.

Luồng chính:

1. Hệ thống kiểm tra chuyến, điểm đón/trả và ghế.
2. Toàn bộ ghế được giữ tạm trong 10 phút.
3. Giá được tính theo điểm đón, số ghế, phụ thu và voucher hợp lệ.
4. Thông tin chuyến, người mua và giá được lưu tại thời điểm đặt.
5. Vé bắt đầu ở trạng thái chờ thanh toán.
6. Sau khi tiền và ghế đều được xác nhận, vé mới được giữ chỗ chính thức.

Yêu cầu đặt vé chỉ nhận số ghế, không nhận thông tin riêng cho từng người đi. Thông tin liên hệ được lấy từ hồ sơ của người mua.

### Đặt vé khứ hồi

- Mỗi chặng chọn từ 1 đến 5 ghế.
- Chuyến về phải thuộc tuyến về đã được cấu hình và khởi hành sau thời gian dự kiến chuyến đi đến nơi.
- Ghế của hai chặng được giữ theo kiểu tất cả hoặc không; một chặng hết ghế làm yêu cầu giữ ghế thất bại.
- Hai chặng tạo thành hai đơn đặt chỗ riêng nhưng thuộc cùng một nhóm thanh toán.

Nếu thanh toán bằng Ví VietRide đã thành công nhưng bước xác nhận một chặng sau đó lỗi, chưa đủ căn cứ để cam kết toàn bộ tiền và chặng trước chắc chắn được tự hoàn tác. Khi gặp trường hợp này, cần kiểm tra ngay mã nhóm đặt chỗ và giao dịch ví.

### Voucher

Voucher chỉ được dùng khi đồng thời còn hiệu lực và phù hợp:

- giá trị đơn tối thiểu;
- tổng số lượt và số lượt riêng của người dùng;
- phương thức thanh toán;
- loại dịch vụ;
- nhà xe và tuyến;
- điều kiện người dùng mới nếu có;
- sự đồng ý của nhà xe đối với voucher do nền tảng tạo nhưng nhà xe tài trợ.

Voucher giảm theo phần trăm được làm tròn đến đồng và có thể bị giới hạn mức giảm tối đa. Voucher giảm số tiền cố định không thể làm số tiền phải trả âm.

Danh sách voucher chỉ mang tính gợi ý. Voucher luôn được kiểm tra lại lúc thanh toán; một người khác có thể dùng hết lượt trong khoảng thời gian người dùng đang đặt vé.

Nếu voucher bị từ chối sau khi ghế đã được giữ, ghế hiện có thể chờ hết thời hạn giữ thay vì được trả ngay lập tức.

## Thanh toán vé

### Thanh toán bằng Ví VietRide

- Ví phải tồn tại và đủ tiền.
- Hệ thống trừ ví, sau đó mới hoàn tất việc giữ ghế chính thức.
- Khi cả tiền và ghế đều thành công, vé được xác nhận ngay và không có trang chuyển hướng.

Có một trường hợp cần thận trọng: nếu ví đã bị trừ nhưng việc chốt ghế thất bại, chưa đủ căn cứ để khẳng định khoản tiền luôn tự hoàn. Cần kiểm tra giao dịch ví và mã đặt chỗ, không nên hứa tiền chắc chắn tự về.

### Thanh toán qua VNPay

- Ứng dụng nhận đường dẫn để chuyển sang VNPay.
- Việc trình duyệt hoặc ứng dụng quay lại không đồng nghĩa thanh toán đã được hệ thống xác nhận.
- Hệ thống chỉ chốt vé sau khi nhận xác nhận thanh toán hợp lệ từ VNPay và kiểm tra ghế vẫn còn được giữ.
- Do xác nhận có thể đến sau màn hình quay lại, vé có thể tạm thời vẫn hiển thị đang xử lý.
- Nếu xác nhận thanh toán đến quá hạn, hệ thống không giữ ghế và có thể tạo yêu cầu hoàn tiền vào Ví VietRide.

Khi VNPay báo thành công nhưng chưa có vé, hãy xin mã đặt chỗ hoặc mã phiên thanh toán và thời điểm thanh toán. Không yêu cầu người dùng tự gọi lại đường xác nhận thanh toán.

## Xem và thay đổi vé

### Xem lịch sử

- Hành khách chỉ xem lịch sử của chính mình.
- Một đơn nhiều ghế có một người mua nhưng có nhiều vé tương ứng từng hành khách/ghế.
- Thông tin chuyến và giá trong vé cũ là ảnh chụp tại thời điểm đặt; không dùng lịch hoặc giá hiện tại để giải thích ngược một vé cũ.

### Đổi điểm đón

- Chỉ đổi được khi vé đã thanh toán và còn ít nhất 2 giờ trước giờ khởi hành.
- Tại đúng mốc 2 giờ trước chuyến, yêu cầu đã bị chặn.
- Điểm mới phải đang hoạt động và cho phép đón.
- Giá tại điểm mới phải đúng bằng giá gốc của vé. Nếu giá khác, người dùng phải hủy và đặt lại.

### Đổi điểm trả

- Chỉ đổi được khi vé đã thanh toán và còn ít nhất 2 giờ trước giờ khởi hành.
- Điểm trả phải đang hoạt động, cho phép trả và nằm sau điểm đón.
- Giá vé hiện không thay đổi vì hệ thống tính giá theo điểm đón.

## Hủy vé và hoàn tiền

### Khi nào được hủy

Hành khách có thể hủy nếu:

- vé vẫn đang chờ thanh toán; hoặc
- vé đã thanh toán nhưng chuyến vẫn đang chờ khởi hành hoặc đang trong thời gian lên xe, chưa bắt đầu chạy.

Chuyến đã bắt đầu chạy hoặc đã kết thúc không dùng luồng hủy vé thông thường này.

### Hủy được hoàn bao nhiêu

- Vé chưa thanh toán không có khoản tiền cần hoàn.
- Vé đã thanh toán được tính theo chính sách hủy của nhà xe và thời gian còn lại trước giờ khởi hành.
- Không được hứa một tỷ lệ cố định khi chưa biết chính sách nhà xe, thời điểm hủy và giờ khởi hành.
- Nếu hành khách từ chối phương án thay thế do điểm đón/trả bị ngừng sử dụng và yêu cầu vẫn còn hạn, vé được hoàn toàn bộ.

Khoản hoàn được chuyển vào Ví VietRide, kể cả khi thanh toán ban đầu qua VNPay. Việc vé được hủy và việc tiền xuất hiện trong ví là hai bước khác nhau nên có thể không xảy ra cùng lúc.

Cách trả lời mặc định:

> Bạn có thể hủy nếu vé vẫn đang chờ thanh toán, hoặc đã thanh toán nhưng chuyến chưa bắt đầu chạy. Với vé đã thanh toán, số tiền hoàn phụ thuộc chính sách nhà xe và thời điểm hủy; khoản hoàn sẽ chuyển vào Ví VietRide. Bạn gửi mã đặt chỗ để kiểm tra chính xác nhé.

## Khi nhà xe thay đổi chuyến

### Đổi giờ chạy

- Thay đổi không quá 2 giờ trong cùng ngày: hệ thống cập nhật và thông báo, không bắt hành khách lựa chọn.
- Thay đổi trên 2 giờ nhưng dưới 6 giờ trong cùng ngày: hành khách được chọn giữ vé hoặc hủy với mức hoàn 50%.
- Thay đổi sang ngày khác hoặc lệch từ 6 giờ trở lên: hành khách được chọn giữ vé hoặc hủy với mức hoàn 100%.
- Nếu hành khách không phản hồi trước hạn, hệ thống tự giữ vé theo giờ mới, không tự hủy.
- Một thay đổi lịch mới thay thế lựa chọn cũ chưa xử lý.

### Điểm đón hoặc trả bị ngừng sử dụng

- Hệ thống có thể đề xuất bến đầu chuyến thay cho điểm đón hoặc bến cuối chuyến thay cho điểm trả.
- Hành khách có thể chấp nhận, tự đổi sang điểm hợp lệ khác hoặc từ chối trước hạn.
- Từ chối hợp lệ làm hủy vé và hoàn toàn bộ.
- Nếu không phản hồi sau hạn, hệ thống tự áp dụng điểm thay thế.

### Tuyến đường thay đổi

Nếu điểm đón cũ không còn trên tuyến mới, hành khách được chọn một điểm trong danh sách thay thế hoặc từ chối. Từ chối làm hủy vé và hoàn toàn bộ. Nếu hết hạn mà không phản hồi, hệ thống ghi nhận phương án tự động nhưng tác động cuối đến điểm đón trong một nhánh hiện chưa xác định đầy đủ; không nên khẳng định điểm mới đã được áp dụng nếu chưa đọc lại vé.

### Thay xe hoặc chuyển sang chuyến khác

- Hệ thống cố gắng bố trí ghế mới tương đương.
- Một số hành khách có thể chưa có ghế mới ngay nếu xe thay thế thiếu chỗ.
- Hành khách đã lên xe có thể cần tổ phục vụ chuyến mới xác nhận chuyển.
- Không nên khẳng định mọi vé đã có ghế mới chỉ vì nhà xe thông báo thay xe.

## Lên xe và vắng mặt

- Mã QR hoặc mã đặt chỗ được tổ phục vụ dùng để tìm vé.
- Quét mã chỉ để xem thông tin; tổ phục vụ còn phải xác nhận hành khách đã lên xe.
- Sau khi xác nhận, vé được ghi nhận đã sử dụng.
- Nếu quá 15 phút sau mốc đến điểm đón hoặc sau khi xe rời bến mà hành khách chưa được xác nhận lên xe, hệ thống có thể đánh dấu vắng mặt.
- Nếu tất cả hành khách trong đơn vắng mặt, cả đơn được đánh dấu vắng; nếu chỉ một số người vắng, đơn được ghi nhận vắng một phần.
- Vé vắng mặt không được hoàn tiền theo thông báo hiện hành.

## Ví VietRide và nạp tiền

### Số dư và lịch sử

- Ví bắt đầu với số dư 0 đồng.
- Hành khách chỉ xem ví và lịch sử giao dịch gắn với tài khoản của mình.
- Mỗi giao dịch lưu số dư trước và sau, loại giao dịch, nguyên nhân và thời điểm.
- Khi nhiều giao dịch xảy ra cùng lúc, không tự cộng trừ từ số dư cũ để đoán số dư hiện tại.

### Nạp tiền qua VNPay

- Số tiền tối thiểu là 10.000 đồng.
- Hệ thống hiện không đặt mức nạp tối đa.
- Chỉ xác nhận hợp lệ từ VNPay mới cộng tiền vào ví.
- Yêu cầu trùng không cộng tiền hai lần.
- Hệ thống đánh dấu yêu cầu nạp quá 10 phút là hết hạn khi lần quét chạy; đúng mốc 10 phút chưa chắc đã bị đổi ngay.

Có khoảng thời gian lệch giữa hạn yêu cầu trong hệ thống và thời gian đường dẫn VNPay có thể còn dùng được. Nếu VNPay nhận tiền sau khi yêu cầu đã hết hạn, chưa đủ thông tin để khẳng định tiền sẽ tự được đối chiếu hoặc hoàn. Cần cung cấp mã giao dịch VNPay và kiểm tra lịch sử ví.

## Gửi bưu kiện

### Xem chuyến và giá dự kiến

- Danh sách chuyến chở hàng chỉ là báo giá tại thời điểm xem.
- Hệ thống tự tính kích thước từ trọng lượng thực và trọng lượng quy đổi theo kích thước kiện.
- Trọng lượng tính cước là số lớn hơn giữa trọng lượng thực và trọng lượng quy đổi.
- Giá dự kiến không giữ chỗ, không khóa giá và không giữ sức chứa.
- Tiền cọc lúc tạo đơn hiện cố định ở mức 20%, dù phần báo giá có thể hiển thị chính sách cọc khác của nhà xe.

### Tạo đơn gửi

- Người gửi phải là hành khách đang hoạt động.
- Chuyến phải chưa bắt đầu chạy và nhà xe phải cho phép dịch vụ bưu kiện.
- Nếu gắn vé, vé phải thuộc người gửi, cùng chuyến và đã được xác nhận.
- Người nhận bắt buộc có họ tên và số điện thoại; email là tùy chọn.
- Ảnh phải thuộc đúng khu vực tải lên của người gửi.
- Mọi kích thước kiện, kể cả kiện rất lớn, hiện bắt đầu ở bước chờ thanh toán; đơn mới không mặc định phải chờ nhà xe duyệt.

### Thanh toán cọc

- Khi bắt đầu thanh toán, hệ thống kiểm tra lại voucher và sức chứa của chuyến.
- Tiền cọc phải được hoàn tất trước hạn nhận hàng.
- Nếu tiền và sức chứa đều được xác nhận đúng hạn, kiện được giữ chỗ.
- Nếu thanh toán thành công nhưng chuyến không giữ được sức chứa, đơn chuyển sang chờ nhà xe xử lý thay vì giả vờ đã được giữ chỗ.

### Cân lại và thanh toán phần còn lại

Sau khi phụ xe nhận và cân lại:

- số tiền cuối được tính theo số đo thực;
- nếu còn thiếu, người gửi phải thanh toán phần còn lại trước hạn;
- nếu cọc lớn hơn tiền cuối, hệ thống tạo nghĩa vụ hoàn phần chênh;
- nếu vượt sức chứa, đơn chờ nhà xe quyết định.

Giá xem trước có thể khác tiền cuối vì số đo thực tế, giá tối thiểu và giảm giá đã khóa được áp dụng lại.

### Theo dõi trạng thái giao hàng

Theo cách nói với hành khách, bưu kiện có thể:

- đang chờ thanh toán hoặc giữ chỗ;
- đã được nhận tại điểm gửi;
- sẵn sàng xếp lên xe;
- đang ở trên xe;
- đã dỡ khỏi xe;
- đang chờ người nhận xác nhận;
- đã giao, bị từ chối, đang hoàn hoặc đã kết thúc vì hủy/hết hạn.

Không đọc mã trạng thái kỹ thuật cho hành khách.

### Người nhận xác nhận

- Nếu có email, người nhận nhận link xác nhận có hiệu lực 48 giờ.
- Nếu không có email, không có link; tổ phục vụ hoặc nhà xe phải xác nhận thủ công.
- Người nhận có thể xác nhận hoặc từ chối kèm lý do.
- Sau khi từ chối, cùng link có thể hoàn tác trong vòng 15 phút; đúng mốc 15 phút đã hết quyền hoàn tác.
- Link có thể hết hạn, đã bị thu hồi hoặc không tồn tại hợp lệ nếu email được gửi nhưng bước lưu dữ liệu sau đó thất bại.
- Không yêu cầu người nhận gửi nguyên token qua kênh hỗ trợ.

### Khi bưu kiện bị chuyển hoặc hoàn

- Hàng đang trên đường có thể được chuyển sang chuyến khác và tổ phục vụ chuyến mới phải xác nhận trong 30 phút.
- Nếu quá hạn, đơn được chuyển sang trạng thái cần nhà xe xử lý.
- Khi người nhận từ chối và hết thời gian hoàn tác, hệ thống bắt đầu nhánh hoàn hàng.
- Chưa đủ thông tin để xác định bước tự động đưa kiện hàng từ lúc bắt đầu hoàn đến khi đã trả xong. Không hứa hàng chắc chắn tự về người gửi nếu chưa có cập nhật thực tế.

### Hoàn tiền bưu kiện

- Số tiền cần hoàn và số tiền đã thực sự vào ví là hai số khác nhau trong thời gian xử lý.
- Khoản hoàn dương được gửi sang Ví VietRide.
- Yêu cầu lặp không được hoàn hai lần.
- Chưa có thời gian cam kết chính xác cho việc tiền xuất hiện trong ví.

## Theo dõi xe và chia sẻ vị trí

### Khi chuyến bị trễ hơn 30 phút

- Hệ thống so ETA mới với thời gian dự kiến tại điểm dừng kế tiếp.
- Chỉ khi ETA mới muộn hơn trên 30 phút chuyến mới được ghi nhận là trễ; đúng 30 phút chưa vượt ngưỡng.
- Hành khách và Nhà xe được thông báo khi hệ thống xác định đủ người nhận.
- ETA tiếp tục cập nhật theo GPS; trạng thái trễ được gỡ khi cùng điểm dừng trở lại trong ngưỡng.
- Hệ thống không tự đổi tuyến chỉ vì chuyến bị trễ; Nhà xe quyết định có cần thông báo thêm hoặc đổi tuyến.

Nếu hỏi một chuyến cụ thể có đang trễ hay không, cần mã chuyến hoặc mã đặt chỗ và dữ liệu theo dõi hiện tại.

### Ai được xem

Hành khách được xem vị trí chuyến khi sở hữu vé hoặc là người gửi/người nhận bưu kiện gắn với chuyến. Chỉ biết mã chuyến không tự tạo quyền xem.

### Dữ liệu có thể thấy

- vị trí mới nhất của xe;
- dấu vết di chuyển;
- thời gian dự kiến đến điểm tiếp theo;
- hình tuyến đường;
- vị trí và thời gian dự kiến của xe trung chuyển nếu có quyền.

Nếu không có điểm GPS đủ mới, hệ thống có thể không trả thời gian dự kiến thay vì tự tạo một con số.

### Xe trung chuyển

- Hành khách chỉ thấy điểm đón của chính mình và bến, không thấy địa chỉ người khác.
- Chỉ những điểm đón chưa hoàn tất mới được tính vào số điểm còn trước lượt của hành khách.
- Xe trung chuyển chiều đi đưa khách về bến; chiều về đưa khách từ bến đến điểm trả.
- Nếu nhà xe không thể bố trí xe trung chuyển trước hạn, hành khách cần tự di chuyển đến bến khởi hành.

### Chia sẻ vị trí cho người thân

- Chỉ hành khách có vé trên chuyến đang chạy mới tạo hoặc thu hồi link.
- Mỗi hành khách và chuyến chỉ có một link còn hiệu lực.
- Link mặc định hết hạn sau 24 giờ.
- Link tự mất hiệu lực khi hết hạn, bị thu hồi hoặc chuyến kết thúc.
- Người nhận link chỉ thấy trạng thái chuyến, vị trí xe, nơi đi/đến, tuyến và thời gian dự kiến; không thấy thông tin vé, hành khách, tổ phục vụ hoặc liên hệ riêng.

## Thông báo

- Hộp thông báo trong ứng dụng được lưu trước khi hệ thống thử gửi thông báo đẩy.
- Không thấy banner đẩy không đồng nghĩa thao tác thất bại.
- Hành khách chỉ xem và đánh dấu thông báo của chính mình.
- Có thể đánh dấu một hoặc tất cả thông báo đã đọc.
- Lịch sử được giữ mặc định 90 ngày.
- Nếu thông báo đẩy hoặc email lỗi, hệ thống có thể thử lại; thời điểm đến không được bảo đảm.

Các thông báo có thể liên quan đến vé, tiền vào/ra ví, thay đổi chuyến, xe sắp đến, xe trung chuyển, bưu kiện, voucher và hoàn tiền.

## Khi cần dữ liệu thực tế

Tài liệu này không tự biết:

- số dư hiện tại;
- ghế đang trống;
- vị trí xe hiện tại;
- trạng thái cụ thể của vé, thanh toán hoặc bưu kiện;
- chính sách hủy hiện hành của một nhà xe;
- thời gian chính xác để email, thông báo hoặc tiền hoàn đến.

Hãy xin tối thiểu:

- mã đặt chỗ hoặc mã vé khi hỏi về chuyến/vé;
- mã bưu kiện khi hỏi về hàng hóa;
- thời điểm thanh toán hoặc hủy;
- ảnh chụp thông báo lỗi đã che thông tin bí mật.

## Mẫu trả lời nhanh

### “Tôi hủy vé được không?”

Bạn có thể hủy nếu vé vẫn đang chờ thanh toán, hoặc đã thanh toán nhưng chuyến chưa bắt đầu chạy. Nếu vé đã thanh toán, số tiền hoàn phụ thuộc chính sách nhà xe và thời điểm hủy. Bạn gửi mã đặt chỗ để kiểm tra chính xác nhé.

### “Hủy vé thì tiền về đâu?”

Khoản được hoàn sẽ chuyển vào Ví VietRide, kể cả khi bạn thanh toán ban đầu qua VNPay. Việc hủy vé và việc tiền xuất hiện trong ví có thể không xảy ra cùng lúc.

### “VNPay báo thành công nhưng chưa có vé?”

Màn hình thanh toán thành công chưa đủ để xác nhận vé đã được giữ chỗ. Hệ thống còn phải nhận xác nhận thanh toán và hoàn tất giữ ghế. Bạn cung cấp mã đặt chỗ hoặc mã phiên thanh toán để kiểm tra nhé.

### “Không nhận thông báo có nghĩa thao tác thất bại?”

Không nhất thiết. Thao tác có thể đã thành công dù thông báo đẩy đến chậm hoặc lỗi. Bạn nên kiểm tra trạng thái trong ứng dụng hoặc hộp thông báo.

### “Trợ lý có biết xe đang ở đâu không?”

Mình chỉ trả lời chính xác khi có dữ liệu vị trí hiện tại được hệ thống cho phép truy cập. Tài liệu hướng dẫn không tự biết xe đang ở đâu.

### “Nếu chuyến trễ hơn 30 phút thì sao?”

Khi ETA mới trễ hơn thời gian dự kiến trên 30 phút, hệ thống ghi nhận chuyến bị trễ và gửi thông báo cho hành khách cùng Nhà xe. ETA vẫn tiếp tục cập nhật theo GPS; hệ thống không tự đổi tuyến. Đúng 30 phút chưa được tính là vượt ngưỡng.
