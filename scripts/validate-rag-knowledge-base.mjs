import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

const ROOT = resolve(import.meta.dirname, '..');
const RAG_DOCS = resolve(ROOT, 'docs', 'rag');
const EXPECTED_FILES = [
  ['vietride-passenger-chat-knowledge-base.md', ['PASSENGER']],
  ['vietride-driver-chat-knowledge-base.md', ['DRIVER']],
  ['vietride-assistant-chat-knowledge-base.md', ['ASSISTANT']],
  ['vietride-operator-chat-knowledge-base.md', ['OPERATOR_STAFF', 'OPERATOR_ADMIN']],
  ['vietride-system-admin-chat-knowledge-base.md', ['SYSTEM_ADMIN']],
];
const REQUIRED_BY_FILE = new Map([
  ['vietride-passenger-chat-knowledge-base.md', ['Tài khoản', 'Tìm chuyến', 'Đặt vé', 'Thanh toán vé', 'Hủy vé', 'Gửi bưu kiện', 'Theo dõi xe', 'Thông báo']],
  ['vietride-driver-chat-knowledge-base.md', ['Tài khoản', 'Lịch và chuyến', 'Bắt đầu chuyến', 'Đến và rời điểm', 'Hành khách vắng mặt', 'GPS', 'Báo sự cố', 'Đề xuất đổi tuyến', 'Xe trung chuyển', 'Bưu kiện', 'Thông báo']],
  ['vietride-assistant-chat-knowledge-base.md', ['Tài khoản', 'Chuyến và lịch', 'Vận hành chuyến', 'Danh sách hành khách', 'Hành khách vắng mặt', 'GPS', 'Báo sự cố', 'Đề xuất đổi tuyến', 'Xe trung chuyển', 'Bưu kiện', 'Nhận bưu kiện', 'Cân đo lại', 'Xếp bưu kiện', 'Dỡ bưu kiện', 'Bàn giao', 'Chuyển bưu kiện', 'Hoàn tiền', 'Thông báo']],
  ['vietride-operator-chat-knowledge-base.md', ['Tài khoản', 'Hồ sơ', 'Gói thuê bao', 'Bến, điểm dừng và tuyến', 'Xe và loại ghế', 'Lịch tài xế', 'Kiểm tra tài xế', 'Vận hành chuyến', 'Đề xuất đổi tuyến', 'Xe trung chuyển', 'Booking', 'Bưu kiện', 'Ví nhà xe', 'Chính sách RAG', 'Thông báo']],
  ['vietride-system-admin-chat-knowledge-base.md', ['Quản lý tài khoản', 'Quản lý nhà xe', 'Gói thuê bao', 'Địa điểm', 'Chiến dịch', 'Dashboard', 'Ví Nền tảng', 'Hóa đơn', 'Trợ lý AI', 'Chính sách nền tảng']],
]);
const SUGGESTED_QUESTIONS = [
  'Nếu chuyến trễ hơn 30 phút thì sao?',
  'Khi nào được dỡ hàng tại bến đích?',
  'Cần kiểm tra gì trước khi rời điểm dừng?',
];
const OPERATOR_DIVISION_PATTERNS = [
  /Chỉ Quản trị viên/i,
  /Nhân viên chỉ xem/i,
  /Staff chỉ/i,
  /Chỉ Admin/i,
  /phân biệt quyền Nhân viên/i,
];
const PLAIN_LANGUAGE_RULE = 'Ưu tiên từ ngữ';
const DIRECT_ANSWER_RULE = 'Trả lời trực tiếp đúng trọng tâm câu hỏi';
const NO_LOOKUP_REQUEST_RULE = 'Không yêu cầu hoặc mời';
const DELAY_QUESTION = '### “Nếu chuyến trễ hơn 30 phút thì sao?”';
const UNEXPLAINED_DELAY_TERMS = /\b(?:ETA|GPS|delayed alert|route proposal)\b/i;
const LOOKUP_INVITATION_PATTERNS = [
  /(?:bạn|vui lòng|hãy)\s+(?:gửi|cung cấp|đưa)\s+(?:cho\s+(?:tôi|mình)\s+)?mã[^.\n]*(?:kiểm tra|tra cứu)/i,
  /(?:gửi|cung cấp|đưa)\s+mã[^.\n]*(?:để|rồi)\s+(?:tôi|mình|trợ lý)?\s*(?:kiểm tra|tra cứu)/i,
  /xin\s+(?:đúng\s+)?mã[^.\n]*(?:kiểm tra|tra cứu)?/i,
];

const errors = [];
const docs = new Map();

for (const [file, roles] of EXPECTED_FILES) {
  const content = await readUtf8(resolve(RAG_DOCS, file));
  docs.set(file, content);
  assert(content.includes('| Language | `vi` |'), `${file}: thiếu language vi`);
  assert(content.includes('| Document type | `GUIDE` |'), `${file}: thiếu document type GUIDE`);
  for (const role of roles) assert(content.includes(`\`${role}\``), `${file}: thiếu audience ${role}`);
  assert(!content.includes('\r'), `${file}: phải dùng LF`);
  assert(content.includes('Không hiển thị chunk ID, UUID'), `${file}: thiếu quy tắc ẩn identifier nội bộ`);
  assert(content.includes(PLAIN_LANGUAGE_RULE), `${file}: thiếu quy tắc dùng tiếng Việt dễ hiểu`);
  assert(content.includes(DIRECT_ANSWER_RULE), `${file}: thiếu quy tắc trả lời đúng trọng tâm`);
  assert(content.includes(NO_LOOKUP_REQUEST_RULE), `${file}: thiếu quy tắc không xin mã để tra cứu`);
  const answerContent = content
    .split('\n')
    .filter((line) => !line.includes(NO_LOOKUP_REQUEST_RULE) && !line.includes('Không yêu cầu họ cung cấp mã'))
    .join('\n');
  for (const pattern of LOOKUP_INVITATION_PATTERNS) {
    assert(!pattern.test(answerContent), `${file}: còn mời người dùng gửi mã để trợ lý kiểm tra: ${pattern}`);
  }
  for (const domain of REQUIRED_BY_FILE.get(file) ?? []) {
    assert(content.toLocaleLowerCase('vi').includes(domain.toLocaleLowerCase('vi')), `${file}: thiếu domain ${domain}`);
  }
}

for (const [file, content] of docs) {
  if (file === 'vietride-system-admin-chat-knowledge-base.md') continue;
  const delayStart = content.indexOf(DELAY_QUESTION);
  if (delayStart < 0) continue;
  const nextHeading = content.indexOf('\n### ', delayStart + DELAY_QUESTION.length);
  const delayAnswer = content.slice(delayStart, nextHeading < 0 ? undefined : nextHeading);
  assert(!UNEXPLAINED_DELAY_TERMS.test(delayAnswer), `${file}: câu trả lời chuyến trễ còn thuật ngữ kỹ thuật chưa giải thích`);
  assert(delayAnswer.includes('thời gian dự kiến'), `${file}: câu trả lời chuyến trễ thiếu cách nói phổ thông`);
}

const operator = docs.get('vietride-operator-chat-knowledge-base.md');
for (const pattern of OPERATOR_DIVISION_PATTERNS) {
  assert(!pattern.test(operator), `Operator knowledge còn phân chia Staff/Admin: ${pattern}`);
}
assert(operator.includes('dùng chung hoàn toàn'), 'Operator knowledge chưa xác nhận mô hình Nhà xe gộp');

const coverage = await readUtf8(resolve(RAG_DOCS, 'vietride-knowledge-coverage.md'));
for (const domain of ['Tài khoản', 'Nhà xe', 'Bến', 'chuyến', 'Shuttle', 'Booking', 'Payment', 'Parcel', 'GPS', 'Thông báo', 'doanh thu', 'RAG', 'Idempotency']) {
  assert(coverage.toLocaleLowerCase('vi').includes(domain.toLocaleLowerCase('vi')), `Coverage matrix thiếu domain ${domain}`);
}

const allRoleDocs = [...docs.values()].join('\n');
for (const question of SUGGESTED_QUESTIONS) {
  assert(allRoleDocs.includes(question), `Thiếu câu gợi ý: ${question}`);
}

const regression = JSON.parse(
  await readUtf8(resolve(RAG_DOCS, 'vietride-knowledge-regression-cases.json')),
);
assert(regression.version === 1, 'Regression version phải là 1');
assert(Array.isArray(regression.scenarios), 'Regression scenarios phải là array');
const questions = regression.scenarios.flatMap((scenario) => scenario.questions ?? []);
assert(questions.length >= 185, `Cần ít nhất 185 regression cases, hiện có ${questions.length}`);
const scopedQuestions = regression.scenarios.flatMap((scenario) =>
  (scenario.questions ?? []).map((question) => `${scenario.role}:${question}`),
);
assert(new Set(scopedQuestions).size === scopedQuestions.length, 'Regression có câu hỏi bị trùng trong cùng role');
for (const scenario of regression.scenarios) {
  for (const field of ['role', 'topic', 'questions', 'mustInclude', 'mustNotInclude', 'requiresLiveData', 'askFor']) {
    assert(Object.hasOwn(scenario, field), `Scenario ${scenario.topic ?? '<unknown>'} thiếu ${field}`);
  }
  assert(Array.isArray(scenario.askFor) && scenario.askFor.length === 0, `Scenario ${scenario.topic}: askFor phải rỗng vì trợ lý không nhận mã để tra cứu`);
}

const promptRegistry = await readUtf8(
  resolve(ROOT, 'apps', 'rag', 'src', 'config', 'runtime-config.registry.ts'),
);
const chatTypes = await readUtf8(resolve(ROOT, 'apps', 'rag', 'src', 'chat', 'chat.types.ts'));
assert(!promptRegistry.includes('Only cite chunk IDs included in the retrieved context.'), 'Prompt còn yêu cầu cite chunk ID');
assert(promptRegistry.includes('Không hiển thị chunk ID, UUID'), 'Prompt thiếu quy tắc ẩn UUID');
assert(promptRegistry.includes(DIRECT_ANSWER_RULE), 'Prompt thiếu quy tắc trả lời đúng trọng tâm');
assert(promptRegistry.includes('Không yêu cầu hoặc mời người dùng gửi mã'), 'Prompt còn thiếu quy tắc không nhận mã để tra cứu');
assert(!/event: 'done'[\s\S]{0,300}citedChunkIds/.test(chatTypes), 'Public SSE done còn citedChunkIds');
assert(/event: 'done'[\s\S]{0,300}citations/.test(chatTypes), 'Public SSE done thiếu citations');

if (errors.length > 0) {
  errors.forEach((error) => process.stderr.write(`FAIL: ${error}\n`));
  process.exitCode = 1;
} else {
  process.stdout.write(`PASS: 5 role documents, ${regression.scenarios.length} scenarios, ${questions.length} regression cases.\n`);
}

async function readUtf8(path) {
  const content = await readFile(path, 'utf8');
  assert(!content.startsWith('\uFEFF'), `${path}: không được có BOM`);
  return content;
}

function assert(condition, message) {
  if (!condition) errors.push(message);
}
