# Cẩm nang VietRide dành cho Quản trị viên hệ thống

> Knowledge base dành riêng cho người quản trị toàn nền tảng VietRide. Không dùng tài liệu này làm nguồn hỗ trợ trực tiếp cho hành khách, tài xế, phụ xe hoặc người dùng nhà xe.

## Metadata upload

| Trường | Giá trị |
|---|---|
| Access level | `ADMIN` |
| Category | `PLATFORM_ADMIN` |
| Document type | `GUIDE` |
| Operator | Để trống vì đây là tài liệu toàn nền tảng |
| Language | `vi` |
| Audience roles | Chỉ `SYSTEM_ADMIN` |

## Quy tắc trả lời bắt buộc

- Trả lời bằng tiếng Việt tự nhiên, tập trung vào việc quản trị viên cần kiểm tra hoặc thực hiện.
- Không đọc mã trạng thái, mã lỗi, tên API, event, service, database, handler hoặc đường dẫn source trong câu trả lời mặc định.
- Chỉ nêu mã kỹ thuật khi người hỏi chủ động yêu cầu điều tra log, mã lỗi hoặc bằng chứng triển khai.
- Ưu tiên từ ngữ tiếng Việt dễ hiểu trước, ngay cả khi người hỏi là Quản trị viên hệ thống. Chỉ dùng từ viết tắt hoặc thuật ngữ như “ETA”, “GPS”, “ingest”, “provider”, “citation” khi câu hỏi cần đúng khái niệm kỹ thuật, và phải giải thích ý nghĩa ở lần xuất hiện đầu tiên.
- Không biến quyền quản trị thành quyền sửa dữ liệu tùy ý: mỗi thao tác vẫn phải đúng trạng thái, phạm vi và điều kiện nghiệp vụ.
- Không cung cấp secret, access token, refresh token, OTP, link đặt lại mật khẩu, chữ ký thanh toán hoặc thông tin nhạy cảm.
- Trả lời trực tiếp đúng trọng tâm câu hỏi bằng quy tắc và hướng dẫn có trong tài liệu. Không tự mở rộng sang nội dung người dùng không hỏi.
- Không yêu cầu hoặc mời Quản trị viên gửi mã tài khoản, mã nhà xe, mã giao dịch, mã đối soát, mã hóa đơn, thời điểm, log hay dữ liệu khác để trợ lý “kiểm tra giúp”. Trợ lý tài liệu không trực tiếp tra cứu dữ liệu hệ thống trong cuộc trò chuyện.
- Nếu kết luận phụ thuộc dữ liệu hiện tại, nêu rõ giới hạn đó và hướng dẫn Quản trị viên tự xem trên màn hình quản trị hoặc công cụ vận hành phù hợp; không giả vờ sẽ kiểm tra sau khi nhận mã.
- Với dữ liệu tài chính, phân biệt rõ số liệu vận hành, doanh thu chuẩn, số tiền đã trả cho nhà xe và điều chỉnh thủ công.
- Khi một thao tác đang được xử lý bất đồng bộ, nói rõ kết quả có thể chưa xuất hiện ngay và hướng dẫn kiểm tra trạng thái hiện tại trước khi thử lại.
- Không hiển thị chunk ID, UUID, document ID, đường dẫn source hoặc tự thêm mục “Nguồn” trong câu trả lời hướng người dùng; metadata audit chỉ dùng nội bộ.

## Phạm vi của Quản trị viên hệ thống

Quản trị viên hệ thống quản lý toàn nền tảng, gồm:

- tài khoản người dùng và nhật ký hoạt động;
- hồ sơ và vòng đời nhà xe;
- danh mục địa điểm, bến/trạm và điểm dừng dùng chung;
- chiến dịch và voucher cấp nền tảng;
- dashboard, báo cáo và phân tích doanh thu;
- Ví Nền tảng, ví nhà xe, giao dịch và đối soát;
- gói thuê bao nhà xe và hỗ trợ xử lý hóa đơn;
- tài liệu, cấu hình, phản hồi và chính sách của trợ lý AI.

Quyền toàn nền tảng không có nghĩa được đánh giá thay phản hồi của người khác, sửa nhật ký hoạt động, bỏ qua điều kiện chuyển trạng thái hoặc trả một khoản đối soát hai lần.

## Quản lý tài khoản người dùng

### Xem và tìm tài khoản

- Quản trị viên có thể xem danh sách người dùng toàn nền tảng với phân trang, sắp xếp và các bộ lọc phù hợp.
- Có thể xem nhóm tài xế, phụ xe và nhân viên xuyên các nhà xe, đồng thời lọc theo vai trò, tình trạng tài khoản hoặc nhà xe.
- Danh sách quản trị không trả mật khẩu đã băm, phiên làm mới hoặc bí mật xác thực.
- Dữ liệu đã xóa mềm chỉ xuất hiện khi bề mặt tìm kiếm hiện tại cho phép yêu cầu rõ ràng.

Nếu câu hỏi liên quan một tài khoản cụ thể, hướng dẫn Quản trị viên tự tìm trên màn hình quản lý tài khoản bằng thông tin họ đang có. Trợ lý không yêu cầu họ gửi email, số điện thoại, mã người dùng, mật khẩu hoặc token vào cuộc trò chuyện.

### Tạo thêm Quản trị viên hệ thống

1. **Điều kiện:** người thực hiện đã là Quản trị viên hệ thống và thông tin tài khoản mới hợp lệ, không trùng.
2. **Xử lý:** hệ thống tạo tài khoản chưa có mật khẩu sử dụng lần đầu và phát yêu cầu gửi link thiết lập mật khẩu.
3. **Kết quả:** người được tạo phải hoàn tất link trước khi đăng nhập bằng mật khẩu.
4. **Side effect:** thao tác được ghi vào nhật ký hoạt động.
5. **Trường hợp lỗi:** thông tin trùng, dữ liệu không hợp lệ hoặc link hết hạn sẽ không hoàn tất việc thiết lập tài khoản.

Không đọc tên trạng thái nội bộ cho người dùng; hãy nói “tài khoản đang chờ thiết lập mật khẩu lần đầu”.

### Khóa tài khoản

1. **Điều kiện:** tài khoản đích đang hoạt động hoặc đang trong một trạng thái chờ hợp lệ; quản trị viên không được tự khóa chính mình bằng luồng này.
2. **Xử lý:** hệ thống lưu lại tình trạng trước khi khóa, thu hồi các phiên làm mới và ghi nhật ký.
3. **Kết quả:** người bị khóa không thể đăng nhập hoặc làm mới phiên.
4. **Side effect:** các phiên truy cập ngắn hạn đã phát trước đó có thể còn hiệu lực trong thời gian còn lại của phiên.
5. **Trường hợp đặc biệt:** gọi khóa lại không đổi trạng thái lần nữa nhưng vẫn bảo đảm các phiên làm mới bị thu hồi.

Khi người dùng nói vẫn truy cập được ngay sau lúc khóa, cần kiểm tra thời điểm phát phiên cũ; không kết luận thao tác khóa thất bại chỉ từ hiện tượng này.

### Mở khóa tài khoản

1. **Điều kiện:** tài khoản đang bị khóa và người thực hiện không phải chính tài khoản đó.
2. **Xử lý:** hệ thống khôi phục tình trạng trước khi khóa và xóa bộ đếm đăng nhập sai.
3. **Kết quả:** người dùng có thể đăng nhập lại nếu các điều kiện khác vẫn hợp lệ.
4. **Tính toàn vẹn:** nếu việc xóa dấu vết đăng nhập sai thất bại, thay đổi tài khoản không được lưu nửa chừng.
5. **Trường hợp lỗi:** không được dùng mở khóa để duyệt một tài khoản hoặc nhà xe vốn chưa đủ điều kiện hoạt động.

### Nhật ký hoạt động

- Nhật ký ghi nhận các thao tác quản trị quan trọng và được xem như lịch sử bất biến.
- Luồng hiện tại không có chức năng sửa hoặc xóa nhật ký.
- Khi điều tra, đối chiếu người thực hiện, thời điểm, đối tượng và kết quả; không suy ra nguyên nhân chỉ từ một dòng nếu thao tác còn có bước bất đồng bộ.

## Quản lý nhà xe

### Tạo nhà xe trực tiếp

1. **Điều kiện:** dữ liệu nhận diện, đăng ký kinh doanh, thuế, email và số điện thoại hợp lệ, không trùng.
2. **Xử lý:** hệ thống tạo nhà xe ở trạng thái được phép hoạt động, tạo tài khoản quản trị nhà xe đang chờ thiết lập mật khẩu và cấp gói Starter dùng thử 30 ngày.
3. **Kết quả:** nhà xe không phải qua bước duyệt hồ sơ như luồng tự đăng ký.
4. **Side effect:** gửi link thiết lập mật khẩu và ghi nhận hoạt động liên quan.
5. **Trường hợp lỗi:** dữ liệu trùng hoặc không hợp lệ làm toàn bộ thao tác bị từ chối.

### Duyệt hồ sơ tự đăng ký

1. **Điều kiện:** nhà xe đang chờ duyệt.
2. **Xử lý:** hệ thống cho phép nhà xe hoạt động và kích hoạt gói Starter dùng thử 30 ngày.
3. **Kết quả:** người dùng nhà xe có thể đăng nhập khi tài khoản cá nhân cũng đã hoàn tất các bước cần thiết.
4. **Side effect:** ghi nhật ký, phát thông báo nhà xe được duyệt và yêu cầu khởi tạo ví nhà xe.
5. **Trường hợp lỗi:** nhà xe không còn ở trạng thái chờ duyệt thì thao tác bị từ chối.

Việc duyệt nhà xe và việc ví nhà xe xuất hiện là các bước liên chức năng; có thể có độ trễ ngắn trước khi ví được tạo.

### Từ chối hồ sơ

1. **Điều kiện:** nhà xe đang chờ duyệt.
2. **Xử lý:** hệ thống đánh dấu hồ sơ bị từ chối và hủy gói đang chờ phê duyệt.
3. **Kết quả:** nhà xe không được hoạt động như một nhà xe đã duyệt.
4. **Side effect:** có nhật ký quản trị.
5. **Giới hạn hiện tại:** hệ thống không phát thông báo liên chức năng riêng cho việc từ chối; không được cam kết chức năng khác sẽ tự nhận thông tin từ chối.

### Tạm ngưng nhà xe

1. **Điều kiện:** nhà xe đã được duyệt và đang hoạt động.
2. **Xử lý:** hệ thống tạm ngưng toàn nhà xe, thu hồi phiên làm mới của mọi người thuộc nhà xe và yêu cầu thu hồi phiên Firebase cho từng người.
3. **Kết quả:** nhân viên, tài xế và phụ xe không thể đăng nhập hoặc làm mới phiên; quản trị viên nhà xe chỉ còn phiên hạn chế để xem hồ sơ, thuê bao và đăng xuất/làm mới.
4. **Side effect:** phiên truy cập cũ có thể còn quyền trước đó tối đa khoảng 15 phút.
5. **Trường hợp lỗi:** không dùng thao tác tạm ngưng cho hồ sơ vẫn đang chờ duyệt hoặc đã bị từ chối.

### Kích hoạt lại nhà xe

1. **Điều kiện:** nhà xe đang bị tạm ngưng.
2. **Xử lý:** hệ thống đưa nhà xe trở lại trạng thái đã được duyệt.
3. **Kết quả:** giữ nguyên thuê bao và thông tin tạm ngưng đã có; không tự tạo gói mới.
4. **Side effect:** người dùng có thể phải đăng nhập hoặc làm mới phiên để nhận quyền mới.
5. **Trường hợp lỗi:** kích hoạt lại không thay thế thao tác duyệt hồ sơ lần đầu.

## Gói thuê bao và hạn mức nhà xe

### Gói Starter hiện được cấu hình

Giá trị mặc định trong hệ thống hiện tại gồm 3 xe, 5 tài xế, 5 phụ xe, 3 người dùng nhà xe, 5 tuyến và 100 chuyến mỗi tháng. Trợ lý AI được bật; chức năng bưu kiện và xe trung chuyển bị tắt.

Đây là cấu hình hiện tại, không phải cam kết thương mại cố định. Nếu dữ liệu gói đang áp dụng khác tài liệu, ưu tiên dữ liệu thực tế.

### Quản lý plan

- Quản trị viên hệ thống có bề mặt quản lý các gói thuê bao.
- Chỉ gói đang hoạt động mới có thể được nhà xe chọn để nâng cấp.
- Khi một nhà xe đang chờ thanh toán gói mới, quyền lợi vẫn theo gói đang hoạt động trước đó; không cấp trước hạn mức của gói chưa thanh toán.
- Gói đã hết hạn hoặc bị hủy không cho tiếp tục tiêu thụ hạn mức.
- Lượt dùng chỉ tăng khi thao tác nghiệp vụ thành công; giao dịch bị hoàn tác không để lại hạn mức tăng dở dang.
- Hạn mức chuyến được làm mới theo tháng bằng xử lý nền, nên thời điểm lịch chạy không chứng minh mọi side effect đã hoàn tất ngay.

Tài liệu này chưa nêu đầy đủ từng trường có thể sửa của gói. Nếu người hỏi cần chi tiết, phải kiểm tra chức năng quản lý gói đang được triển khai trước khi hướng dẫn.

## Địa điểm, bến/trạm và điểm dừng toàn nền tảng

### Phân biệt khái niệm

- **Địa điểm:** đơn vị hành chính hoặc khu vực dùng để tìm kiếm và tổ chức dữ liệu.
- **Bến/trạm chuẩn:** bến dùng chung toàn nền tảng.
- **Liên kết bến của nhà xe:** quan hệ cho biết một nhà xe đang sử dụng bến chuẩn; tắt liên kết này không xóa bến của nền tảng.
- **Điểm dừng:** điểm đón/trả thuộc phạm vi một nhà xe và được sắp thứ tự trên tuyến.

Danh mục địa điểm có tầng tỉnh/thành và tầng phường/xã/khu đặc biệt. Khi lọc theo cấp cha, chỉ các địa điểm con trực tiếp đang hoạt động của cấp cha đang hoạt động được trả về.

### Quản lý và gộp bến

- Quản trị viên hệ thống quản lý địa điểm, bến/trạm và điểm dừng trên phạm vi nền tảng.
- Khi nhà xe đề nghị tạo bến, hệ thống ưu tiên dùng lại bến đang hoạt động.
- Nếu phát hiện bến trong phạm vi dưới 100 mét, hệ thống trả thông tin bến gần trùng thay vì tự tạo một bến chuẩn mới; tọa độ và khoảng cách là căn cứ chính, không chỉ tên.
- Khi gộp hai bến, hệ thống giữ một bến chính, tạo chuyển hướng từ bến bị gộp và yêu cầu dữ liệu booking liên quan cập nhật tham chiếu.
- Sau khi gộp, ứng dụng không nên tiếp tục coi mã bến cũ là một bến độc lập.
- Vì cập nhật đi qua nhiều chức năng, dữ liệu liên quan có thể cần thời gian ngắn để hội tụ.

Nếu một nhà xe chỉ ngừng dùng bến, cần tắt liên kết của nhà xe thay vì xóa bến dùng chung của toàn nền tảng.

## Chiến dịch và voucher nền tảng

### Chiến dịch quảng bá

- Quản trị viên hệ thống có thể xem, tạo, sửa, bật và tắt chiến dịch, đồng thời gắn voucher vào chiến dịch.
- Bề mặt hiện tại không có thao tác xóa chiến dịch cho người dùng; không hướng dẫn xóa dù logic nội bộ có thành phần chưa được expose.
- Danh sách khuyến mãi công khai chỉ hiển thị tối đa 20 voucher khi cả chiến dịch và voucher đều đang hoạt động, còn thời hạn và áp dụng đúng dịch vụ.
- Việc voucher xuất hiện trong danh sách quảng bá không đảm bảo dùng được khi thanh toán; mọi điều kiện được kiểm tra lại tại thời điểm checkout.

### Tạo voucher nền tảng

1. **Điều kiện:** dữ liệu voucher hợp lệ; mã có thể được nhập hoặc để hệ thống sinh.
2. **Xử lý:** voucher nền tảng không thuộc sở hữu riêng của một nhà xe.
3. **Voucher do nhà xe tài trợ:** phải chọn nhà xe mục tiêu; hệ thống tạo yêu cầu đồng ý cho từng nhà xe liên quan.
4. **Kết quả:** voucher chỉ áp dụng tại checkout nếu còn hiệu lực, còn lượt, đúng phạm vi và đã có sự đồng ý cần thiết.
5. **Trường hợp lỗi:** sai phạm vi, hết lượt, chưa đủ giá trị đơn hoặc nhà xe chưa đồng ý đều làm voucher không được áp dụng.

### Khi voucher đã được sử dụng

Sau lượt dùng đầu tiên, các thuộc tính cốt lõi bị khóa để giữ tính nhất quán:

- không đổi mã, loại, nguồn tài trợ hoặc chủ sở hữu;
- không đổi giá trị, mức đơn tối thiểu, mức giảm tối đa hoặc ngày bắt đầu;
- ngày kết thúc chỉ được kéo dài;
- giới hạn lượt dùng chỉ được nới hoặc bỏ giới hạn;
- tên và phạm vi tuyến vẫn có thể sửa.

Xóa mềm khác với tắt hoạt động. Một mã thuộc bản ghi đã xóa mềm có thể được dùng lại theo ràng buộc hiện tại.

## Dashboard và báo cáo nền tảng

### Cách hiểu dashboard

- Dashboard quản trị yêu cầu khoảng ngày hợp lệ, tối đa 366 ngày.
- Hệ thống tạo thêm kỳ so sánh liền trước có cùng số ngày.
- Phân bố người dùng và nhà xe lấy từ dữ liệu danh tính; số tiền lấy từ dữ liệu tài chính; số lượng booking lấy từ dữ liệu booking.
- Nếu một nguồn dữ liệu quan trọng lỗi, dashboard báo không khả dụng thay vì trộn dữ liệu đầy đủ với dữ liệu thiếu.

### Thống kê booking

- Thống kê booking là số lượng vận hành: tổng đơn, đơn hủy, vắng mặt, vắng một phần và hoàn tất.
- Có thể nhóm theo nhà xe, ngày hoặc tháng trong khoảng tối đa 366 ngày.
- Dữ liệu được cập nhật qua xử lý nền có chống trùng và có thể hội tụ sau giao dịch booking; báo cáo tháng điền số 0 cho tháng không có dữ liệu.
- Không dùng tổng tiền nằm trong báo cáo vận hành để thay thế doanh thu tài chính chuẩn.

### Báo cáo doanh thu

- Khoảng ngày được hiểu theo giờ Việt Nam và bao gồm trọn ngày kết thúc; tối đa 366 ngày.
- Báo cáo phân biệt doanh thu vé sau điều chỉnh, doanh thu bưu kiện sau điều chỉnh, doanh thu vận tải, doanh thu thuê bao, tổng doanh thu dự án và số tiền đã đối soát cho nhà xe.
- Có chuỗi theo tháng và danh sách nhà xe dẫn đầu.
- Kết quả có thể được lưu đệm khoảng 60 giây, nên thay đổi vừa xảy ra có thể chưa phản ánh ngay.
- Số tiền phải lấy từ sổ tài chính chuẩn. Số lượng booking, số chuyến hoặc báo cáo vận hành không phải nguồn thay thế.
- Nếu thông tin bổ sung từ danh tính hoặc chuyến bị thiếu/sai, báo cáo từ chối trả dữ liệu một phần để tránh gây hiểu nhầm.

Khi hai màn hình cho số khác nhau, trước tiên kiểm tra cùng khoảng thời gian, múi giờ, loại báo cáo và định nghĩa: số lượng vận hành, doanh thu sau hoàn tiền, doanh thu thuê bao hay tiền đã trả nhà xe.

## Ví Nền tảng, giao dịch và đối soát

### Nguồn tài chính chuẩn

- Tiền vé và bưu kiện thực trả được ghi vào sổ tài chính của nhà xe.
- Phần hoàn tiền được ghi âm.
- Voucher do VietRide tài trợ được ghi bù; voucher do nhà xe tài trợ không được cộng lại thành doanh thu.
- Thống kê booking không thay thế sổ tài chính khi tính tiền.

### Đối soát chuyến

1. **Điều kiện:** chuyến đã hoàn tất hoặc bị gián đoạn.
2. **Xử lý:** hệ thống tạo một khoản đối soát cho mỗi cặp nhà xe và chuyến, sau đó giữ 7 ngày.
3. **Kết quả:** nếu số tiền ròng không dương, khoản đối soát bị hủy; nếu dương, nó chờ đủ điều kiện rồi mới được thanh toán.
4. **Thanh toán:** hệ thống tính lại số tiền ròng, trừ Ví Nền tảng và cộng Ví Nhà xe trong một luồng có khóa chống xử lý trùng.
5. **Trường hợp lỗi:** Ví Nền tảng không đủ tiền thì khoản đối soát vẫn chưa hoàn tất và được ghi nhận để cảnh báo.

Quy trình tự động kiểm tra theo lịch, vì vậy đủ 7 ngày không có nghĩa tiền xuất hiện đúng từng giây. Hệ thống hiện không có chức năng rút Ví Nhà xe về ngân hàng; số dư ví nội bộ không chứng minh đã chuyển khoản ngoài VietRide.

### Xem và điều chỉnh ví

- Quản trị viên có thể xem Ví Nền tảng, giao dịch, các khoản đối soát bị kẹt và ví nhà xe.
- Điều chỉnh thủ công phải ghi số dư trước/sau, người thực hiện và ghi chú.
- Một điều chỉnh ví không tự được coi là doanh thu bán vé nếu bản ghi không được đánh dấu ảnh hưởng doanh thu hoặc đối soát.
- Các bản ghi do xử lý nền tạo có thể không có người dùng cụ thể; đây không tự động là dữ liệu lỗi.

### Đối soát thủ công

1. **Điều kiện:** khoản đối soát tồn tại và chưa hoàn tất.
2. **Xử lý:** hệ thống khóa bản ghi và tính lại số tiền ròng bằng cùng quy tắc của đối soát tự động.
3. **Kết quả:** nếu hợp lệ và Ví Nền tảng đủ tiền, tiền được chuyển sang Ví Nhà xe.
4. **Chống trùng:** khoản đã hoàn tất không được thanh toán lần hai.
5. **Trường hợp lỗi:** thiếu tiền hoặc trạng thái không phù hợp giữ khoản đối soát chưa hoàn tất.

## Hóa đơn thuê bao

- Thanh toán thuê bao thành công tạo một hóa đơn duy nhất theo giao dịch.
- File PDF được tạo ở bước nền riêng và có thể chưa sẵn sàng ngay khi gói đã được thanh toán/kích hoạt.
- Việc tạo PDF tự thử lại tối đa 5 lần theo thời gian chờ tăng dần.
- Quản trị viên hệ thống có thể yêu cầu thử tạo lại PDF hóa đơn bị lỗi.
- Trước khi retry, kiểm tra hóa đơn hiện tại để tránh nhầm tình trạng “file chưa sẵn sàng” với “thanh toán thất bại”.
- Một xác nhận thanh toán thuê bao đến sau hạn bị đánh dấu hết hạn và không kích hoạt gói; chưa đủ thông tin để xác định cách đối chiếu hoặc hoàn riêng nếu nhà cung cấp đã thu tiền trong trường hợp đến muộn.

## Trợ lý AI và kho tri thức

### Kiểm tra tri thức về chuyến trễ

- Ngưỡng trễ vận hành là ETA động muộn hơn ETA kế hoạch trên 30 phút; đúng 30 phút chưa được đánh dấu trễ.
- Sự kiện trễ tạo thông báo cho hành khách và Nhà xe; không tự đổi tuyến.
- Tài xế/phụ xe tiếp tục gửi GPS, có thể báo sự cố và đề xuất tuyến; Nhà xe quyết định áp dụng.
- Khi kiểm tra chất lượng RAG, cùng một quy tắc phải trả lời phù hợp cho Passenger, Driver, Assistant và Nhà xe mà không lộ mã kỹ thuật.

### Phạm vi truy xuất của Quản trị viên hệ thống

- Quản trị viên hệ thống có thể dùng tài liệu công khai, tài liệu nhà xe và tài liệu quản trị.
- Có thể chọn phạm vi một nhà xe cho cuộc hội thoại.
- Một cuộc hội thoại đang có phải giữ nguyên người dùng, vai trò và phạm vi; không đổi sang nhà xe khác bằng cách dùng lại cùng cuộc hội thoại.
- Trợ lý không tự biết số dư, vị trí xe, trạng thái đơn hoặc dữ liệu hiện tại nếu chưa được cấp nguồn dữ liệu tương ứng.

### Quản lý tài liệu

- Chỉ Quản trị viên hệ thống quản lý tài liệu knowledge base.
- Upload hỗ trợ file văn bản thuần và Markdown.
- Giới hạn mặc định là 5 MiB; hệ thống không chấp nhận file lớn hơn 10 MiB.
- File tải lên hiện được duyệt tự động và đưa vào hàng chờ xử lý.
- Một tài liệu chỉ được dùng để trả lời sau khi đã được duyệt và xử lý nội dung hoàn tất.
- Upload thành công không có nghĩa tri thức xuất hiện ngay nếu quá trình xử lý còn chờ hoặc đã lỗi.

Phân loại upload:

| Phạm vi | Nhóm nội dung | Cách dùng |
|---|---|---|
| Công khai | Hỗ trợ khách hàng | Dùng cho tài liệu hành khách và nội dung toàn hệ thống không nhạy cảm. |
| Nhà xe | Chính sách nhà xe | Dùng cho người thuộc nhà xe; có thể là tài liệu chung hoặc tài liệu của đúng một nhà xe. |
| Quản trị | Quản trị nền tảng | Chỉ dùng cho Quản trị viên hệ thống. |

Loại tài liệu có thể là câu hỏi thường gặp, chính sách, quy trình, hướng dẫn hoặc điều khoản. Loại tài liệu không thay thế phạm vi truy cập.

### Trạng thái và lỗi xử lý tài liệu

- Upload mới hiện đi thẳng vào trạng thái đã duyệt; chưa xác định được luồng vận hành bình thường nào tạo tài liệu “chờ duyệt”, “bị từ chối” hoặc “lưu trữ”.
- File Markdown được chia theo heading thành các đoạn nhỏ trước khi tạo dữ liệu phục vụ tìm kiếm.
- Tiến trình xử lý kiểm tra theo đợt, mỗi đợt nhỏ và thử lại tối đa 5 lần; tài liệu có thể ở trạng thái chờ khi hàng đợi còn tồn.
- Nếu mô hình biểu diễn nội dung trả kích thước không khớp kho dữ liệu hiện tại, tài liệu bị xử lý thất bại.
- Worker dùng quyền sở hữu tạm thời để một tiến trình cũ hoặc trùng không ghi đè kết quả mới.
- Nếu upload object thành công nhưng lưu metadata thất bại, có thể còn file mồ côi.
- Nếu lưu tài liệu thành công nhưng tạo link xem trước lỗi, yêu cầu có thể báo lỗi dù tài liệu đã được xếp hàng xử lý. Cần kiểm tra danh sách tài liệu và trạng thái yêu cầu cũ trước khi upload lại với mã chống trùng mới.

### Cấu hình trợ lý

- Quản trị viên hệ thống có thể thay đổi cấu hình đang áp dụng của bộ lọc ý định, viết lại truy vấn, tìm kiếm kết hợp, xếp hạng lại, tóm tắt hội thoại và giới hạn chat.
- Giá trị mặc định hiện tại là 20 tin nhắn mỗi giờ cho một hành khách hoặc Quản trị viên hệ thống; nhóm người dùng cùng một nhà xe dùng chung hạn mức 200 tin nhắn mỗi giờ.
- Trợ lý dùng tối đa 8 tin nhắn gần nhất làm ngữ cảnh và bắt đầu tạo/cập nhật tóm tắt sau 12 tin nhắn; các giá trị này có thể khác nếu đã được đổi trong cấu hình đang áp dụng.
- Khi kho tri thức không đủ, trợ lý phải báo thiếu dữ liệu, không tự tạo chính sách, giá, trạng thái chuyến, số dư hoặc thống kê.
- Hệ thống không có mô hình trả phí dự phòng được bật mặc định; nhà cung cấp AI lỗi hoặc chậm có thể làm câu trả lời tạm thời không khả dụng.

### Kiểm tra tình trạng RAG

- Trạng thái “dịch vụ còn sống” không đồng nghĩa RAG đã sẵn sàng trả lời.
- RAG chỉ sẵn sàng khi các kho dữ liệu, hàng đợi, nơi lưu file, mô hình chat, mô hình biểu diễn nội dung và tiến trình xử lý tài liệu đều dùng được, đồng thời không có tài liệu đã duyệt bị kẹt xử lý quá lâu.
- Hàng chờ bị kẹt quá khoảng 15 phút có thể làm kiểm tra sẵn sàng thất bại.
- Khi điều tra, kiểm tra riêng nhà cung cấp AI, tiến trình xử lý, hàng chờ và trạng thái tài liệu; không kết luận toàn bộ tri thức đã mất chỉ vì một kiểm tra sẵn sàng lỗi.

### Phản hồi về câu trả lời

- Người dùng chỉ được đánh giá câu trả lời của trợ lý trong cuộc hội thoại của chính mình.
- Đánh giá chỉ có hai hướng: hữu ích hoặc không hữu ích.
- Quản trị viên hệ thống có thể xem danh sách phản hồi để audit.
- Quyền audit không cho phép quản trị viên đánh giá thay câu trả lời nằm trong cuộc hội thoại của người dùng khác.

## Chính sách nền tảng

- Quản trị viên hệ thống quản lý chính sách chung của nền tảng.
- Chính sách có tiêu đề, mô tả, nội dung, nhóm người đọc, phân loại và cờ hoạt động.
- Sửa nội dung làm tăng phiên bản; chỉ bật hoặc tắt không làm tăng phiên bản nội dung.
- Khi cập nhật hoặc xóa, hệ thống so phiên bản người dùng đang thấy với phiên bản hiện tại.
- Nếu người khác đã sửa trước, thao tác bị từ chối để tránh ghi đè mất thay đổi; cần tải bản mới, rà khác biệt rồi thực hiện lại.
- Xóa chính sách là xóa mềm.
- Mọi thay đổi chính sách tạo bản ghi audit bất biến.

## Dữ liệu hiện tại và giới hạn kết luận

Tài liệu này giải thích hành vi của hệ thống nhưng không chứa dữ liệu vận hành hiện tại. Trợ lý phải xin hoặc đọc dữ liệu thực tế phù hợp để trả lời các câu như:

- tài khoản nào hiện đang bị khóa;
- nhà xe nào đang chờ duyệt hoặc bị tạm ngưng;
- số dư Ví Nền tảng hoặc Ví Nhà xe hiện tại;
- khoản đối soát/hóa đơn/tài liệu nào đang bị kẹt;
- doanh thu của một khoảng thời gian cụ thể;
- cấu hình RAG nào đang được áp dụng.

Tài liệu hiện không đủ để xác định:

- SLA chính xác của email, thông báo, hoàn tiền, hóa đơn PDF và các xử lý theo đợt;
- bí mật cấu hình, địa chỉ tích hợp, cờ bật/tắt và trạng thái môi trường production đang chạy;
- quy trình đối chiếu hoặc hoàn riêng cho thanh toán thuê bao được nhà cung cấp xác nhận sau hạn;
- luồng vận hành bình thường tạo tài liệu RAG ở trạng thái chờ duyệt, bị từ chối hoặc lưu trữ;
- dữ liệu live của bất kỳ tài khoản, nhà xe, ví, chuyến, booking, bưu kiện hoặc tài liệu cụ thể nào.

Khi thiếu dữ liệu hiện tại, vẫn trả lời phần quy tắc có thể xác định, nói rõ phần nào chưa thể kết luận và chỉ nơi Quản trị viên tự kiểm tra. Không yêu cầu họ cung cấp mã hoặc dữ liệu để trợ lý tra cứu.

## Mẫu trả lời nhanh

### “Tôi vừa khóa tài khoản nhưng người đó vẫn vào được?”

“Các phiên làm mới đã bị thu hồi, nhưng phiên truy cập được phát trước lúc khóa có thể còn hiệu lực trong thời gian ngắn. Hãy xem lịch sử phiên và nhật ký hoạt động trên màn hình quản trị để xác định phiên được phát trước thời điểm khóa.”

### “Tôi vừa duyệt nhà xe nhưng chưa thấy ví?”

“Duyệt nhà xe và tạo ví là hai bước liên chức năng nên ví có thể xuất hiện chậm hơn một chút. Hãy kiểm tra trạng thái nhà xe và ví theo mã nhà xe trước khi thực hiện lại thao tác.”

### “Tại sao doanh thu không bằng số liệu booking?”

“Thống kê booking chủ yếu đếm số đơn, còn doanh thu được tính từ sổ tài chính sau giảm giá và hoàn tiền. Hãy so cùng khoảng ngày, múi giờ và đúng loại báo cáo trước khi kết luận có sai lệch.”

### “Khoản đối soát đủ 7 ngày sao chưa vào ví nhà xe?”

“Sau thời gian giữ, hệ thống còn kiểm tra theo đợt và cần Ví Nền tảng đủ tiền. Bạn hãy kiểm tra trạng thái khoản đối soát, số tiền ròng và số dư Ví Nền tảng; khoản đã hoàn tất sẽ không được trả lần hai.”

### “Thanh toán gói thành công nhưng chưa có PDF hóa đơn?”

“Thanh toán, kích hoạt gói và tạo file hóa đơn là các bước riêng. File có thể vẫn đang được tạo hoặc thử lại; hãy kiểm tra trạng thái hóa đơn trước khi yêu cầu tạo lại.”

### “Upload tài liệu thành công nhưng chatbot chưa trả lời theo tài liệu?”

“Tài liệu chỉ được dùng sau khi xử lý nội dung hoàn tất. Bạn hãy kiểm tra trạng thái xử lý, hàng chờ và mô hình biểu diễn nội dung; không nên tải lên lại ngay nếu bản cũ đã được lưu và đang chờ xử lý.”

### “Tôi có thể xóa chiến dịch không?”

“Bề mặt quản trị hiện tại chỉ hỗ trợ tạo, sửa, bật và tắt chiến dịch; chưa có thao tác xóa dành cho người dùng. Nếu không muốn chiến dịch tiếp tục hiển thị, hãy tắt chiến dịch.”
