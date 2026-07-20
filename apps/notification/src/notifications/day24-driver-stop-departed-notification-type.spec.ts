import { NotificationType } from '../generated/notification-prisma-client';

describe('Day 24 notification enum migration:', () => {
  it('exposes DRIVER_STOP_DEPARTED_WITH_PENDING through the generated Prisma client', () => {
    expect(NotificationType.DRIVER_STOP_DEPARTED_WITH_PENDING).toBe(
      'DRIVER_STOP_DEPARTED_WITH_PENDING'
    );
  });
});
