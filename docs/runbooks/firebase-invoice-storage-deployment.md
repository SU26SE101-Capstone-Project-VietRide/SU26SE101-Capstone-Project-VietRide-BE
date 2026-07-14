# Triển khai Firebase Storage cho Invoice PDF

## Mục đích

Tài liệu này hướng dẫn người deploy cấu hình Payment Service lưu Invoice PDF vào Firebase Storage của project `vietride-204c0` khi hệ thống chạy trên VPS, phía trước là Nginx và Cloudflare.

Firebase Storage dùng hạ tầng Google Cloud Storage. Backend .NET truy cập bucket bằng `Google.Cloud.Storage.V1`, nhưng không cần tạo thêm bucket GCS riêng.

## Giá trị đã chốt

| Cấu hình | Giá trị |
|---|---|
| Backend public URL | `https://api.vietride.online` |
| Firebase project | `vietride-204c0` |
| Firebase Storage bucket | `vietride-204c0.firebasestorage.app` |
| Service account | `firebase-adminsdk-fbsvc@vietride-204c0.iam.gserviceaccount.com` |
| Object path | `invoices/{operatorId}/{invoiceId}.pdf` |
| Credential path trong container | `/run/secrets/firebase/service-account.json` |
| Signed URL TTL | 60 phút |

`OPERATOR_WEB_INVOICE_DETAIL_BASE_URL` phải được thay bằng URL trang Invoice của frontend production khi domain đó được chốt.

## Secret cần nhận

Người deploy cần nhận file private-key JSON của service account qua kênh bảo mật. Không nhận qua Git, issue tracker, chat công khai hoặc email không mã hóa.

Không in nội dung JSON vào log. Chỉ kiểm tra cấu trúc bằng lệnh:

```bash
jq -e '
  .type == "service_account" and
  .project_id == "vietride-204c0" and
  .client_email == "firebase-adminsdk-fbsvc@vietride-204c0.iam.gserviceaccount.com" and
  (.private_key | type == "string" and length > 0)
' firebase-service-account.json >/dev/null
```

Exit code `0` nghĩa là cấu trúc hợp lệ. Lệnh không xuất private key.

## Quyền Firebase Storage

Service account phải có role `Storage Object User` (`roles/storage.objectUser`) trên bucket `vietride-204c0.firebasestorage.app`. Role này cho phép đọc, tạo và ghi đè object; ghi đè cần thiết khi retry upload cùng đường dẫn Invoice.

Ưu tiên cấp role ở phạm vi bucket, không cấp quyền toàn project nếu không cần. Firebase client Security Rules không thay thế IAM của service account backend.

## Cài secret trên VPS

Tạo thư mục và cài file với quyền hạn chế:

```bash
sudo install -d -m 700 /opt/vietride/secrets
sudo install -o root -g root -m 600 \
  firebase-service-account.json \
  /opt/vietride/secrets/firebase-service-account.json
```

Xác nhận quyền mà không đọc nội dung:

```bash
sudo stat -c '%U %G %a %n' /opt/vietride/secrets/firebase-service-account.json
```

Kết quả mong đợi:

```text
root root 600 /opt/vietride/secrets/firebase-service-account.json
```

## Cấu hình môi trường production

Thêm vào `/opt/vietride/infra/docker/.env`, là file mà workflow `.github/workflows/deploy.yml` đọc trên server:

```env
INVOICE_STORAGE_BUCKET=vietride-204c0.firebasestorage.app
INVOICE_STORAGE_STABLE_BASE_URL=https://api.vietride.online
OPERATOR_WEB_INVOICE_DETAIL_BASE_URL=https://<operator-web-domain>/invoices
```

`GOOGLE_APPLICATION_CREDENTIALS` đã được production Compose khóa ở `/run/secrets/firebase/service-account.json`; không cần khai báo lại trong `.env` server.

Không đặt nội dung JSON hoặc private key vào `.env`. Giới hạn quyền file môi trường:

```bash
sudo chown root:root /opt/vietride/infra/docker/.env
sudo chmod 600 /opt/vietride/infra/docker/.env
```

## Mount secret vào Payment container

`infra/docker/docker-compose.prod.yml` đã có mount production sau; workflow deploy không cần Compose override riêng:

```yaml
services:
  payment:
    volumes:
      - /opt/vietride/secrets/firebase-service-account.json:/run/secrets/firebase/service-account.json:ro
```

Không bake JSON vào Docker image. Cloudflare, Nginx, Gateway và các service khác không cần mount file này.

## Validate Compose trước khi deploy

```bash
cd /opt/vietride/infra/docker

docker compose \
  --env-file .env \
  -f docker-compose.prod.yml \
  config --quiet
```

Lệnh phải kết thúc với exit code `0`. Không chạy `docker compose config` không có `--quiet` trong log CI vì output render có thể chứa các biến môi trường nhạy cảm khác.

## Deploy

```bash
cd /opt/vietride/infra/docker

docker compose \
  --env-file .env \
  -f docker-compose.prod.yml \
  pull payment

docker compose \
  --env-file .env \
  -f docker-compose.prod.yml \
  up -d payment
```

Compose tự khởi động các dependency của Payment nếu chúng chưa chạy.

## Xác minh sau deploy

### 1. Container đọc được credential

```bash
docker exec vietride_payment sh -c \
  'test -r /run/secrets/firebase/service-account.json && echo FIREBASE_CREDENTIAL_OK'
```

Không dùng `cat` hoặc lệnh nào in nội dung file.

### 2. Payment healthy

```bash
docker inspect --format '{{json .State.Health.Status}}' vietride_payment
curl -fsS https://api.vietride.online/health >/dev/null
```

### 3. Chạy flow Invoice thật

1. Thực hiện một subscription payment test bằng WALLET hoặc VNPay.
2. Chờ Invoice chuyển sang `ISSUED`.
3. Trong Firebase Console, xác nhận object xuất hiện tại `invoices/{operatorId}/{invoiceId}.pdf`.
4. Gọi authenticated endpoint `GET /v1/operator/invoices/{invoiceId}/download`.
5. Xác nhận response `200`, URL tải mới được sinh và `expiresAt` cách thời điểm hiện tại tối đa 60 phút.
6. Xác nhận database chỉ lưu `storage_object_path`, không lưu signed URL.

### 4. Kiểm tra log an toàn

```bash
docker logs vietride_payment --since 15m 2>&1 \
  | grep -E 'PRIVATE KEY|private_key|X-Goog-Signature'
```

Lệnh không được trả về secret hoặc signed URL. Không đưa toàn bộ log chứa dữ liệu production vào ticket công khai.

## Cloudflare và Nginx

- Cloudflare và Nginx chỉ proxy `https://api.vietride.online` tới Gateway.
- Credential JSON không được đặt trong Cloudflare environment hoặc Nginx filesystem public.
- Sau khi API xác thực operator và trả signed URL, trình duyệt tải PDF trực tiếp từ Firebase Storage.
- Không cần tạo Nginx route để serve thư mục Invoice.

## Rotate key

1. Tạo key mới cho cùng service account hoặc service account thay thế.
2. Validate JSON mới bằng `jq` nhưng không in nội dung.
3. Cài tạm thành `/opt/vietride/secrets/firebase-service-account.next.json` với mode `600`.
4. Atomically thay `/opt/vietride/secrets/firebase-service-account.json` bằng file mới và recreate Payment container.
5. Chạy lại flow upload/download.
6. Chỉ revoke key cũ sau khi kiểm tra thành công.

Nếu nghi ngờ key bị lộ, revoke ngay trên Google Cloud IAM, thay secret trên VPS và recreate Payment. Một key dùng chung local/production đồng nghĩa việc revoke sẽ làm cả hai môi trường mất quyền cho đến khi cập nhật key mới.

## Rollback

Nếu credential hoặc quyền bucket lỗi:

1. Giữ nguyên schema và Invoice đã tạo.
2. Khôi phục key/override đã hoạt động trước đó.
3. Recreate Payment container.
4. Invoice đang `DRAFT/FAILED` sẽ được reconciliation/retry xử lý; không tạo Invoice hoặc wallet movement thủ công để bù.

Không chuyển bucket sang public để chữa lỗi quyền. Không xóa Invoice row, object PDF hoặc attempt history trong quá trình rollback.

## Bàn giao tối thiểu

Người deploy phải ghi nhận, không kèm private key:

- Thời điểm deploy và image tag Payment.
- Firebase project, bucket và service-account email.
- Key ID đang dùng, không ghi private key.
- Kết quả `config --quiet`, health check và flow upload/download.
- Người chịu trách nhiệm rotate/revoke key.
