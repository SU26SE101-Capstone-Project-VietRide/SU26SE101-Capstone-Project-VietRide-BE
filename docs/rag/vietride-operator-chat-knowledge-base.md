# Cẩm nang VietRide dành cho nhà xe

> Knowledge base dùng chung hoàn toàn cho nhóm Nhà xe. `OPERATOR_STAFF` và `OPERATOR_ADMIN` có cùng phạm vi tri thức và đều được gọi là “Nhà xe”.

## Metadata upload

| Trường | Giá trị |
|---|---|
| Access level | `OPERATOR` |
| Category | `OPERATOR_POLICY` |
| Document type | `GUIDE` |
| Operator | Để trống để dùng chung cho các nhà xe |
| Language | `vi` |
| Audience roles | `OPERATOR_STAFF`, `OPERATOR_ADMIN` |

## Quy tắc trả lời bắt buộc

- Luôn gọi người dùng là “Nhà xe”; không phân chia Nhân viên và Quản trị viên trong câu trả lời.
- Chỉ dùng dữ liệu và quy trình của đúng nhà xe người hỏi.
- Dùng tiếng Việt tự nhiên; không đọc mã trạng thái, event, API, service, database, handler hoặc source path.
- Không cung cấp thao tác chỉ dành cho System Admin.
- Không tiết lộ dữ liệu riêng của nhà xe khác, hành khách khác hoặc tài khoản ngoài phạm vi.
- Khi chưa đủ dữ liệu, xin mã nhà xe/chuyến/đặt chỗ/bưu kiện và thời điểm phù hợp; không xin access token, refresh token, OTP hoặc secret.
- Chỉ chuyển sang thông tin kỹ thuật khi người dùng chủ động debug hoặc hỏi mã lỗi.
- Không hiển thị chunk ID, UUID, document ID, đường dẫn source hoặc tự thêm mục “Nguồn”; ứng dụng hiển thị nguồn thân thiện riêng.

## Phạm vi chung của Nhà xe

Nhà xe được hướng dẫn thống nhất về hồ sơ, nhân sự, thuê bao, tuyến, điểm dừng, phương tiện,
lịch, chuyến, đặt chỗ, voucher, báo cáo, ví, đối soát, hóa đơn, bưu kiện, sự cố, đổi tuyến,
chính sách và thông báo. Không hỏi lại người dùng đang dùng role Nhà xe nào và không chia nội dung
thành quyền xem/quyền sửa giữa hai enum kỹ thuật.

## Tài khoản và trạng thái nhà xe

### Khi nhà xe hoạt động bình thường

- Người dùng nhà xe chỉ đăng nhập khi tài khoản hoạt động và nhà xe đã được duyệt.
- Người dùng vận hành, tài xế và phụ xe phải thuộc đúng nhà xe.
- Phiên đăng nhập mang phạm vi nhà xe; thay ID trên yêu cầu không cho phép truy cập dữ liệu nhà xe khác.

### Khi nhà xe bị tạm ngưng

- Người dùng nhà xe có thể bị từ chối đăng nhập hoặc chỉ nhận phiên hạn chế khi nhà xe bị tạm ngưng.
- Phiên hạn chế chỉ dùng để xem trạng thái, hồ sơ, thuê bao, làm mới phiên và đăng xuất.
- Phiên hạn chế không cho sửa hồ sơ hoặc vận hành nghiệp vụ.
- Khi nhà xe bị tạm ngưng, các phiên làm mới của toàn bộ người dùng nhà xe bị thu hồi.
- Phiên truy cập cũ có thể còn quyền trước đó tối đa khoảng 15 phút vì trạng thái được chụp khi phát phiên.
- Sau khi nhà xe được kích hoạt lại, người dùng có thể cần đăng nhập/làm mới để nhận quyền mới.

Khi hỗ trợ, cần phân biệt tài khoản cá nhân bị khóa với cả nhà xe bị tạm ngưng.

## Hồ sơ và nhân sự

### Hồ sơ nhà xe

- Nhà xe được xem và cập nhật hồ sơ của đúng nhà xe.
- Nhà xe phải đang được duyệt và hoạt động để sửa.
- Hồ sơ có thông tin liên hệ, nhận diện và các chính sách vận hành.
- Khi nhà xe bị tạm ngưng, chỉ xem lý do/thời điểm tạm ngưng và không sửa hồ sơ.

### Tạo nhân sự

Nhà xe được tạo tài xế, phụ xe hoặc người dùng vận hành.

- Email, số điện thoại và vai trò phải hợp lệ.
- Email/số điện thoại không được trùng.
- Người mới bắt đầu bằng link đặt mật khẩu lần đầu có hiệu lực 48 giờ.
- Việc tạo phải còn hạn mức tương ứng của gói thuê bao.
- Khi mức sử dụng chạm hoặc vượt 80% lần đầu trong kỳ, hệ thống tạo cảnh báo và không gửi lặp cho cùng nguồn lực/kỳ.
- Hai yêu cầu đồng thời khi chỉ còn một chỗ được xử lý để không vượt hạn mức.


## Gói thuê bao và hạn mức

### Gói Starter hiện tại

Giá trị mặc định trong hệ thống hiện tại:

- 3 xe;
- 5 tài xế;
- 5 phụ xe;
- 3 tài khoản nhân viên nhà xe;
- 5 tuyến;
- 100 chuyến mỗi tháng;
- có trợ lý RAG;
- chưa bật bưu kiện và xe trung chuyển.

Đây là cấu hình hiện tại, không phải cam kết thương mại vĩnh viễn. Khi có dữ liệu gói thực tế, phải ưu tiên dữ liệu đó.

### Nâng cấp gói

Nhà xe thực hiện:

- chọn gói trả phí đang hoạt động;
- chọn chu kỳ tháng hoặc năm;
- thanh toán bằng Ví nhà xe hoặc VNPay;
- mỗi lần nâng cấp có thời hạn xử lý khoảng 15 phút;
- chỉ một yêu cầu nâng cấp đang hoạt động được phép;
- gửi lại cùng yêu cầu có thể nhận lại kết quả cũ; đổi nội dung với cùng mã thao tác bị từ chối.

Thanh toán Ví nhà xe thành công có thể kích hoạt ngay. Với VNPay, quyền lợi cũ tiếp tục được dùng trong lúc chờ xác nhận; không được dùng hạn mức cao hơn của gói đích trước khi thanh toán hoàn tất.

Thanh toán, kích hoạt gói và tạo file hóa đơn là ba mốc riêng. Chưa có PDF hóa đơn không tự nghĩa là nâng cấp thất bại.

### Hết hạn và cảnh báo

- Hệ thống gửi cảnh báo gói dùng thử khi còn không quá 3 ngày.
- Gói hết hạn không tiếp tục cấp hạn mức mới.
- Số chuyến theo tháng được đặt lại theo kỳ.
- Các bước kiểm tra chạy theo lịch nên trạng thái/thông báo có thể không thay đổi đúng từng giây.

## Bến, điểm dừng và tuyến

### Liên kết bến

- Bến chính là danh mục dùng chung toàn hệ thống.
- Nhà xe liên kết vào bến chính thay vì tự sở hữu một bản sao riêng.
- Nếu đề nghị bến mới nhưng có bến hiện hữu trong phạm vi 100 mét, hệ thống trả gợi ý gần trùng.
- Ngừng liên kết chỉ tắt quan hệ của nhà xe, không xóa bến toàn nền tảng.
- Bến bị Quản trị viên hệ thống gộp có thể được chuyển sang mã bến chuẩn mới; không tiếp tục coi mã cũ là một bến độc lập.

### Điểm dừng

- Điểm dừng thuộc một nhà xe và có thể cho đón, cho trả hoặc cả hai.
- Nhà xe chỉ thao tác điểm dừng thuộc đúng nhà xe mình.
- Điểm dùng trong tuyến phải cho ít nhất một mục đích đón hoặc trả.
- Khi ngừng một điểm, có thể chọn điểm thay thế đang hoạt động cùng nhà xe.
- Không được tạo chuỗi thay thế vòng.
- Ngừng lại cùng phương án là thao tác lặp an toàn; đổi sang phương án khác sau đó có thể bị từ chối.
- Vé bị ảnh hưởng được xử lý bất đồng bộ nên không phải mọi hành khách cập nhật ngay cùng lúc.

### Tạo và cập nhật tuyến

- Nơi đi và nơi đến phải là hai bến đang hoạt động khác nhau.
- Nhà xe phải có liên kết đang hoạt động tới cả hai bến.
- Tuyến không được trùng theo ràng buộc hiện tại.
- Tạo tuyến phải còn hạn mức thuê bao.
- Không đổi nơi đi/nơi đến bằng cập nhật toàn bộ tuyến.
- Điểm dừng không được trùng và thứ tự phải liên tục từ 1.
- Thay cấu trúc điểm có thể làm hình tuyến cũ không còn phù hợp.

Nhà xe còn có thể quản lý hình tuyến, giá theo điểm và tuyến thay thế.

## Phụ thu

- Nhà xe dùng cấu hình của đúng nhà xe mình.
- Phần trăm phụ thu từ 1 đến 100.
- Ngày kết thúc không trước ngày bắt đầu.
- Hai khoảng đang hoạt động của cùng nhà xe không được chồng nhau.
- Khoảng bị tắt, đã xóa hoặc ngoài ngày hiệu lực không áp vào giá.
- Giá sau phụ thu được làm tròn đến đồng gần nhất.
- Tìm chuyến, chi tiết chuyến và giá đặt vé dùng giá đã gồm phụ thu phù hợp.

## Xe và loại ghế

- Biển số được chuẩn hóa và không trùng trên toàn hệ thống đối với xe chưa xóa.
- Loại xe, số ghế, sơ đồ và sức chứa hàng phải hợp lệ.
- Sức chứa hành khách dùng được không tính ghế bị vô hiệu hóa hoặc khu vực tài xế.
- Các loại ghế hành khách gồm ghế thường, giường tầng trên, giường tầng dưới và VIP.
- Sơ đồ được chụp vào chuyến để việc sửa xe sau đó không viết lại chuyến cũ.
- Trạng thái xe và cờ hoạt động là hai điều kiện riêng; chỉ nhìn một tên trạng thái chưa đủ kết luận xe có thể phân công.
- Nhà xe xem được danh mục loại xe.

Danh sách xe có thể hiển thị nhiệm vụ đang hoạt động và nhiệm vụ tiếp theo. Nhiệm vụ được giữ trước không tự nghĩa là chuyến đã bắt đầu.

## Lịch tài xế và sinh chuyến

### Điều kiện lịch

- Tài xế, phụ xe, tuyến và xe phải thuộc đúng nhà xe và còn hoạt động.
- Vai trò tài xế/phụ xe phải đúng.
- Thời gian và thời lượng phải hợp lệ.
- Tài xế và xe không được xung đột với lịch khác.
- Tạo hoặc kích hoạt lịch yêu cầu sinh chuyến ngay.
- Ngừng lịch ngăn sinh chuyến mới.
- Chỉ xóa được lịch chưa từng sinh chuyến; nếu đã có lịch sử nên ngừng hoạt động.

### Sinh chuyến

Hệ thống:

1. kiểm tra lại lịch, tuyến và xe;
2. chặn chuyến trùng;
3. kiểm tra hạn mức chuyến tháng;
4. tạo chuyến, điểm dừng, sơ đồ ghế và giá;
5. giữ tài xế, phụ xe và xe trong cùng quá trình.

Nếu xung đột hoặc hết hạn mức, chuyến bị bỏ qua và không giữ hạn mức cho chuyến không được tạo. Việc sinh theo lịch chạy định kỳ, không cam kết xuất hiện đúng từng giây.

## Kiểm tra tài xế, phụ xe và xe

Nhà xe dùng hai chức năng xem trước khả dụng cho lịch và xe trung chuyển.

Kết quả tính:

- thời gian chồng lấn;
- 30 phút quay đầu;
- thời gian lái xe giữa điểm kết thúc cũ và điểm bắt đầu mới;
- nhiệm vụ khác vẫn đang hoạt động.

Xem trước:

- không giữ tài nguyên;
- không khóa lịch;
- có thể trả tối đa 100 xung đột và báo còn kết quả khác;
- lịch lặp chỉ kiểm mẫu đại diện tối đa 15 ngày;
- có thể khác kết quả lúc tạo vì yêu cầu khác đã thắng trước.

Nếu hai nhiệm vụ ở khác nơi và không xác định được thời gian lái xe, hệ thống từ chối theo hướng an toàn, không tự thay bằng khoảng cách đường chim bay.

## Vận hành chuyến

### Trạng thái bằng ngôn ngữ người dùng

Một chuyến thường đi qua:

1. đang chờ;
2. đang cho khách lên xe;
3. đang chạy;
4. đã hoàn tất.

Nhánh khác là bị hủy trước khi chạy hoặc bị gián đoạn khi đang chạy. Không đọc mã nội bộ nếu người dùng không hỏi kỹ thuật.

### Bắt đầu và hoàn tất

- Chỉ tài xế được phân công bắt đầu.
- Tài xế hoặc phụ xe được phân công có thể hoàn tất chuyến đang chạy.
- Trước khi bắt đầu, hệ thống kiểm tra tài xế, phụ xe và xe không còn hoạt động ở nhiệm vụ khác.
- Bị chặn do tài nguyên còn bận sẽ tạo cảnh báo cho Nhà xe nhưng không gửi lặp vô hạn.
- Hệ thống có cơ chế tự chuyển sang cho khách lên xe, thử bắt đầu khi trễ và hoàn tất sau mốc dự kiến; các bước chạy theo đợt.

### Đến/rời điểm

- Ghi nhận đến yêu cầu đúng crew, chuyến đang chạy và điểm chưa đến.
- Rời điểm yêu cầu đã ghi nhận đến.
- Hệ thống kiểm tra hành khách còn chờ; không gọi được dữ liệu Booking thì không cho rời.
- Đến điểm cuối không tự hoàn tất chuyến.

### Khi chuyến bị trễ hơn 30 phút

- Hệ thống so ETA mới với thời gian dự kiến tại điểm dừng kế tiếp.
- Chỉ trễ trên 30 phút mới được đánh dấu; đúng 30 phút chưa vượt ngưỡng.
- Nhà xe và hành khách được thông báo khi hệ thống xác định đủ người nhận.
- Tracking tiếp tục cập nhật ETA và gỡ trạng thái trễ khi cùng điểm dừng trở lại trong ngưỡng.
- Nhà xe có thể theo dõi, gửi thông báo bổ sung hoặc quyết định đổi tuyến nếu crew đề xuất.
- Không có hành động tự động đổi tuyến chỉ vì chuyến bị trễ.

### Hủy chuyến

- Chỉ hủy chuyến chưa bắt đầu chạy.
- Xem trước hủy chỉ tổng hợp ảnh hưởng tại thời điểm đọc, không thực hiện hủy.
- Hủy thành công cập nhật vé, bưu kiện và thông báo theo cơ chế bất đồng bộ.
- Kết quả tiền cuối phụ thuộc xử lý hoàn tiền sau đó.
- Chuyến đang chạy phải dùng luồng gián đoạn/thay xe, không dùng hủy trước khởi hành.

### Sửa chuyến

- Giá hoặc tuyến chỉ sửa trước khi bắt đầu cho khách lên xe.
- Xe được đổi khi chuyến còn chờ hoặc đang cho khách lên xe.
- Ghi chú có thể sửa khi chuyến chưa kết thúc và còn trong giai đoạn cho phép.
- Không đổi tuyến trực tiếp nếu đã có vé đang hoạt động.
- Đổi xe phải kiểm tra ghế đang được giữ/đặt còn ánh xạ được.

### Gián đoạn và thay xe

Không có xe thay:

- chỉ áp dụng chuyến đang chạy;
- chuyến chuyển sang bị gián đoạn;
- vé, bưu kiện và thông báo được xử lý tiếp.

Có xe thay:

- xe và crew thay thế phải hoạt động, cùng nhà xe và không xung đột;
- hệ thống tạo chuyến phục hồi;
- ưu tiên ghế cùng loại rồi mới dùng loại khác;
- thiếu chỗ có thể khiến một số hành khách chưa có ghế mới;
- không nói mọi vé đã có ghế mới chỉ vì thay xe thành công.

## Đề xuất đổi tuyến và sự cố

### Đề xuất đổi tuyến

- Driver/Assistant được phân công có thể gửi phương án.
- Chỉ Nhà xe sở hữu chuyến được duyệt đề xuất.
- Phương án dựa trên tuyến nguồn đã thay đổi có thể hết hiệu lực.
- Duyệt phương án tùy chỉnh tạo tuyến thay thế chính thức và áp vào chuyến.
- Từ chối chỉ áp dụng đề xuất đang chờ.
- Một phương án được duyệt có thể thay thế các đề xuất khác.

### Sự cố

- Driver/Assistant chỉ báo khi chuyến đang chạy.
- Có tối đa ba ảnh và vị trí hợp lệ.
- Nhà xe xem được danh sách/chi tiết sự cố của mình, lọc theo đang mở hoặc đã xử lý.
- Hiện không tìm thấy thao tác thực tế để chuyển sự cố sang đã xử lý. Không hướng dẫn người dùng một nút resolve chưa tồn tại.

## Xe trung chuyển

### Điều kiện bố trí

- Chuyến chính còn chờ khởi hành.
- Gói thuê bao có dịch vụ xe trung chuyển.
- Tài xế/xe trung chuyển hoạt động, đúng nhà xe và không xung đột.
- Danh sách booking không thay đổi.
- Sức chứa đủ.
- Khoảng cách đường đi có dữ liệu và không vượt ngưỡng mặc định 10 km.

Xe đón khách về bến phải hoàn tất trước giờ chuyến chính 30 phút. Xe trả khách chỉ bắt đầu ít nhất 30 phút sau thời gian dự kiến chuyến chính đến nơi.

### Cảnh báo chưa bố trí được

- Còn từ hơn 60 đến 120 phút: cảnh báo sớm.
- Còn trên 30 đến 60 phút: cảnh báo gần hạn.
- Còn không quá 30 phút: hệ thống hủy các yêu cầu chưa được bố trí và báo hành khách tự đến bến.
- Mỗi mức cảnh báo chỉ được tạo một lần cho cùng chuyến.
- Job chạy theo đợt nên thông báo không đảm bảo đến đúng chính xác phút 120, 60 hoặc 30.

### Vòng đời

- Tài xế được phân công bắt đầu và vận hành.
- Đón xử lý nhóm có cùng thứ tự.
- Chỉ người đã đón mới được đánh dấu đã trả.
- Người vắng mặt cần lý do.
- Chỉ hoàn tất khi không còn người đang chờ hoặc đang trên xe.

## Booking, voucher và báo cáo

### Xem booking

Nhà xe xem danh sách/chi tiết đặt chỗ của mình. Dữ liệu chuyến, người mua, giá và tổ phục vụ trong đặt chỗ cũ là ảnh chụp lịch sử; không thay bằng lịch hiện tại.

### Thống kê và báo cáo

- Nhà xe xem và xuất báo cáo đặt chỗ, hủy vé.
- Khoảng mặc định thường là 30 ngày theo giờ Việt Nam, tối đa 92 ngày cho file xuất.
- Thống kê Booking chủ yếu là số lượng, không phải nguồn doanh thu chuẩn.
- Không cộng tiền trong file booking để thay báo cáo tài chính.

### Voucher

Nhà xe tạo, sửa, bật/tắt hoặc xóa mềm voucher của mình.

- Voucher nhà xe luôn do nhà xe tài trợ và chỉ dùng tại nhà xe đó.
- Sau lượt dùng đầu tiên, các trường tiền và điều kiện quan trọng bị khóa hoặc chỉ được nới theo hướng an toàn.
- Nhà xe xem, chấp nhận hoặc từ chối yêu cầu đồng ý tài trợ voucher nền tảng.
- Từ chối sau khi từng chấp nhận chỉ ảnh hưởng booking tương lai, không đảo giảm giá đã xác nhận.

## Bưu kiện của nhà xe

### Quyền chung

- Nhà xe xem bưu kiện, thống kê tổng hợp và xuất báo cáo bưu kiện của mình.
- Nhà xe tạo, sửa hoặc cập nhật hàng loạt giá bưu kiện.

### Cấu hình giá

- Giá gắn với tuyến, nhóm kích thước và khoảng hiệu lực.
- Tuyến phải thuộc nhà xe.
- Tạo/sửa đơn lẻ yêu cầu giá ít nhất 1.000 đồng.
- Batch hiện chỉ yêu cầu giá dương, khác quy tắc tối thiểu của thao tác đơn lẻ.
- Tra cứu giá hiện không lọc đầy đủ cửa sổ hiệu lực; không khẳng định giá ngoài hạn chắc chắn bị loại.

### Kiện lớn

Đơn mới, kể cả kiện rất lớn, đi thẳng vào chờ thanh toán. Quy trình chờ duyệt chỉ dành cho dữ liệu cũ; không hướng dẫn hành khách hiện tại rằng kiện lớn luôn phải chờ duyệt.

### Khi vượt sức chứa

Nhà xe xử lý trường hợp vượt sức chứa của đúng chuyến thuộc mình.

- Đơn phải thực sự đang chờ vì vượt sức chứa hoặc giữ sức chứa thất bại.
- Hệ thống gọi Trip kiểm tra lại; nếu vẫn bị từ chối, đơn không tiếp tục.
- Hai thao tác đồng thời chỉ một bên thắng.
- Không dùng chức năng vượt sức chứa cho mọi loại “chờ nhà xe xử lý”.

### Chuyển hoặc hoàn bưu kiện

- Hàng đã lên xe có thể chuyển sang chuyến khác cùng nhà xe.
- Crew chuyến đích xác nhận trong 30 phút.
- Quá hạn chuyển sang cần nhà xe xử lý.
- Transfer và return không được chạy cạnh tranh cho cùng kiện.
- Nhà xe có thể hoàn bưu kiện đang chờ xử lý hoặc chuyển quá hạn.
- Nhánh tự bắt đầu hoàn sau khi người nhận từ chối hiện chưa có bước hoàn tất vật lý rõ ràng; không hứa tự chuyển thành “đã trả”.

### Hủy trước khi xếp

Nhà xe có thể chọn:

- hoàn toàn bộ;
- hoàn theo chính sách;
- không hoàn.

Chỉ áp dụng trước khi hàng được xếp. Sau khi hàng đã lên xe phải dùng chuyển hoặc hoàn. Thành công hủy giải phóng sức chứa và tạo yêu cầu hoàn khi có tiền phải trả.

### Xác nhận hoàn tiền

Có một chức năng xác nhận hoàn cho dữ liệu đang chờ đúng loại, nhưng chưa xác định được luồng vận hành bình thường nào tạo trường hợp chờ này. Không hướng dẫn đây là bước bắt buộc của mọi khoản hoàn.

## Ví nhà xe, doanh thu và đối soát

### Nguồn tiền chuẩn

- Báo cáo tài chính chuẩn lấy từ sổ giao dịch Payment, không lấy từ số booking.
- Doanh thu ghi nhận tiền vé, tiền bưu kiện, hoàn tiền và phần voucher theo nguồn tài trợ.
- Hoàn tiền tạo dòng âm.
- Voucher do nhà xe tài trợ không được cộng lại như doanh thu mới.

### Đối soát chuyến

- Khi chuyến hoàn tất hoặc bị gián đoạn, khoản đối soát được giữ 7 ngày.
- Khoản ròng không dương bị hủy; khoản dương chờ đủ điều kiện.
- Sau thời gian giữ, hệ thống có thể chuyển tiền từ Ví nền tảng sang Ví nhà xe.
- Thiếu tiền ở Ví nền tảng làm đối soát chưa hoàn tất và có thể tạo cảnh báo.
- Số dư Ví nhà xe là tiền nội bộ; hệ thống hiện không có chức năng rút về ngân hàng.

Nhà xe xem ví, giao dịch, đối soát, sổ tiền, phân tích doanh thu tổng hợp và hóa đơn thuê bao của mình.

### Vì sao hai báo cáo lệch nhau

- Booking dashboard đếm hoạt động.
- Sổ tài chính tính tiền sau giảm giá/hoàn tiền.
- Đối soát là khoản đã hoặc sắp trả sau thời gian giữ.
- Phải so cùng định nghĩa và cùng khoảng ngày theo giờ Việt Nam.

## Chính sách RAG và thông báo

### Chính sách nhà xe

Nhà xe quản lý chính sách của mình:

- nội dung, tiêu đề, mô tả, đối tượng áp dụng và nhóm;
- thay đổi nội dung làm tăng phiên bản;
- bật/tắt không làm tăng phiên bản nội dung;
- cập nhật/xóa yêu cầu phiên bản đang thấy để tránh ghi đè thay đổi của người khác;
- xóa là xóa mềm và vẫn giữ lịch sử kiểm toán.

### Gửi thông báo chủ động

Nhà xe có thể gửi thông báo:

- cho crew của một chuyến;
- cho crew đang hoạt động trong nhà xe.

Tiêu đề dài 1–120 ký tự, nội dung 1–500 ký tự. Không có người nhận phù hợp thì thao tác bị từ chối. Gửi lại cùng yêu cầu không tạo thông báo trùng.

### Hộp thông báo

- Thông báo được lưu trước khi thử gửi push.
- Push/email/realtime có thể đến khác thời điểm.
- Không thấy banner không đồng nghĩa nghiệp vụ thất bại.
- Người dùng chỉ xem hộp thư của chính mình.

## Khi cần hỗ trợ

Xin tối thiểu:

- tài khoản Nhà xe đang thao tác;
- mã nhà xe và mã đối tượng: chuyến, lịch, booking hoặc bưu kiện;
- thời điểm thao tác;
- mô tả dễ hiểu của lỗi.

Chỉ xin mã lỗi/trace khi chuyển sang debug kỹ thuật. Không xin token hoặc secret.

## Mẫu trả lời nhanh

### “Nhà xe cập nhật hồ sơ thế nào?”

Nhà xe mở hồ sơ của chính mình và cập nhật khi tài khoản cùng nhà xe đang hoạt động. Nếu bị từ chối, hãy kiểm tra trạng thái tài khoản, trạng thái nhà xe và dữ liệu đang gửi.

### “Xem trước báo xe rảnh nhưng lúc tạo lại xung đột?”

Kết quả xem trước không giữ xe hoặc tài xế. Một yêu cầu khác có thể được tạo trước; lúc lưu, hệ thống kiểm tra lại cả giờ chạy, 30 phút quay đầu và thời gian di chuyển.

### “Chuyến bị chặn lúc bắt đầu”

Tài xế, phụ xe hoặc xe có thể vẫn đang hoạt động ở nhiệm vụ khác. Chuyến được giữ ở trạng thái chưa chạy và Nhà xe nhận cảnh báo để kiểm tra nhiệm vụ đang giữ tài nguyên.

### “Nếu chuyến trễ hơn 30 phút thì sao?”

Khi ETA mới trễ hơn thời gian dự kiến trên 30 phút, hệ thống đánh dấu chuyến bị trễ và thông báo cho hành khách cùng Nhà xe. Nhà xe có thể tiếp tục theo dõi ETA, gửi thông báo bổ sung hoặc quyết định đổi tuyến khi có đề xuất; hệ thống không tự đổi tuyến.

### “Đã thanh toán nâng cấp nhưng chưa có hóa đơn”

Thanh toán, kích hoạt gói và tạo file hóa đơn là các bước riêng. Gói có thể đã được xử lý trong khi PDF hóa đơn vẫn đang được tạo.

### “Doanh thu không bằng thống kê booking”

Thống kê booking chủ yếu đếm số đơn. Doanh thu chuẩn còn tính giảm giá, nguồn tài trợ voucher và hoàn tiền, nên hai con số không bắt buộc bằng nhau.
