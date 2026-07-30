import { EmailTemplateKey } from '../generated/notification-prisma-client';
import {
  decryptEmailTemplateData,
  encryptEmailSendJob,
} from './email-job-payload.crypto';

const SECRET = 'test-secret-min-32-chars-aaaaaaaaaaaaaaaa';

describe('email queue payload encryption', () => {
  const plaintextJob = {
    emailDeliveryId: '11111111-1111-4111-8111-111111111111',
    toEmail: 'recipient@vietride.local',
    templateKey: EmailTemplateKey.PARCEL_DELIVERY_LINK,
    templateData: {
      parcelCode: 'VRP-001',
      deliveryUrl:
        'https://app.vietride.local/parcels/delivery/11111111-2222-4333-8444-555555555555',
    },
  };

  it('round-trips without persisting the raw token or URL', () => {
    const encryptedJob = encryptEmailSendJob(plaintextJob, SECRET);

    expect(encryptedJob).not.toHaveProperty('templateData');
    expect(JSON.stringify(encryptedJob)).not.toContain(
      '11111111-2222-4333-8444-555555555555',
    );
    expect(JSON.stringify(encryptedJob)).not.toContain('deliveryUrl');
    expect(decryptEmailTemplateData(encryptedJob, SECRET)).toEqual(
      plaintextJob.templateData,
    );
  });

  it('fails authentication when Redis payload metadata is tampered', () => {
    const encryptedJob = encryptEmailSendJob(plaintextJob, SECRET);

    expect(() =>
      decryptEmailTemplateData(
        { ...encryptedJob, toEmail: 'attacker@vietride.local' },
        SECRET,
      ),
    ).toThrow();
  });

  it('fails closed when no encryption secret is configured', () => {
    expect(() => encryptEmailSendJob(plaintextJob, undefined)).toThrow(
      'INTERNAL_JWT_SECRET_REQUIRED_FOR_EMAIL_QUEUE_ENCRYPTION',
    );
  });
});
