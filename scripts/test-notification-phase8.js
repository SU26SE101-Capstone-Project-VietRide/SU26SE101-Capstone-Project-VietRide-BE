const sendGridMail = require('@sendgrid/mail');
require('dotenv').config();

const DEFAULT_BASE_URL = 'http://localhost:3002';
const baseUrl = process.env.BASE_URL ?? process.env.NOTIFICATION_URL ?? DEFAULT_BASE_URL;
const sendGridApiKey = process.env.SENDGRID_API_KEY;
const fromEmail = process.env.SENDGRID_FROM_EMAIL;
const fromName = process.env.SENDGRID_FROM_NAME ?? 'VietRide';
const toEmail = process.env.SENDGRID_TEST_TO_EMAIL ?? fromEmail;

async function main() {
  const results = [];

  results.push(await runCase('health endpoint reachable', checkHealth));
  results.push(await runCase('SendGrid env configured', checkSendGridEnv));
  results.push(await runCase('SendGrid live AUTH_OTP test email', sendLiveEmail));

  const failed = results.filter((result) => !result.pass);
  for (const result of results) {
    const prefix = result.pass ? 'PASS' : 'FAIL';
    console.log(`${prefix} ${result.name}${result.detail ? ` - ${result.detail}` : ''}`);
  }

  process.exit(failed.length === 0 ? 0 : 1);
}

async function runCase(name, fn) {
  try {
    const detail = await fn();
    return { name, pass: true, detail };
  } catch (error) {
    return { name, pass: false, detail: error instanceof Error ? error.message : String(error) };
  }
}

async function checkHealth() {
  const response = await fetch(new URL('/health', baseUrl));
  if (response.status !== 200) {
    throw new Error(`expected 200, got ${response.status}`);
  }

  return baseUrl;
}

async function checkSendGridEnv() {
  const missing = [];
  if (!sendGridApiKey) missing.push('SENDGRID_API_KEY');
  if (!fromEmail) missing.push('SENDGRID_FROM_EMAIL');
  if (!toEmail) missing.push('SENDGRID_TEST_TO_EMAIL');
  if (missing.length > 0) {
    throw new Error(`missing ${missing.join(', ')}`);
  }

  return 'all required SendGrid env vars are present';
}

async function sendLiveEmail() {
  sendGridMail.setApiKey(sendGridApiKey);
  const [response] = await sendGridMail.send({
    to: toEmail,
    from: {
      email: fromEmail,
      name: fromName,
    },
    subject: 'VietRide Notification Phase 8 Test',
    text: 'Test email cho Notification Service Phase 8. Ma test da duoc redacted trong logs.',
    html: '<p>Test email cho Notification Service Phase 8.</p>',
  });

  const messageId = response.headers['x-message-id'];
  return `status ${response.statusCode}, message ${typeof messageId === 'string' ? messageId : 'n/a'}`;
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
