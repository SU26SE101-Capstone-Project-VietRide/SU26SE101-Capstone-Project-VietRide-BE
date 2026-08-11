import type { Server, Socket } from 'socket.io';
import type { UserJwtVerifier } from '../auth/user-jwt.verifier';
import { NotificationsRealtimeGateway } from './notifications-realtime.gateway';

const USER_ID = '11111111-1111-4111-8111-111111111111';
const NOTIFICATION_ID = '22222222-2222-4222-8222-222222222222';

describe('NotificationsRealtimeGateway', () => {
  let verifier: jest.Mocked<UserJwtVerifier>;
  let gateway: NotificationsRealtimeGateway;
  let middleware: (socket: Socket, next: (error?: Error) => void) => void;
  let emit: jest.Mock;

  beforeEach(() => {
    verifier = { verify: jest.fn() };
    gateway = new NotificationsRealtimeGateway(verifier);
    emit = jest.fn();
    const server = {
      use: jest.fn((registered) => {
        middleware = registered;
      }),
      to: jest.fn(() => ({ emit })),
    } as unknown as Server;
    gateway.afterInit(server);
  });

  it('authenticates auth.token and auto-joins the verified user room', async () => {
    verifier.verify.mockResolvedValue({ userId: USER_ID, role: 'PASSENGER' });
    const socket = createSocket({ token: 'valid-token' });

    await runMiddleware(socket);
    await gateway.handleConnection(socket);

    expect(verifier.verify).toHaveBeenCalledWith('valid-token');
    expect(socket.data.user).toEqual({ userId: USER_ID, role: 'PASSENGER' });
    expect(socket.join).toHaveBeenCalledWith(`notification:user:${USER_ID}`);
  });

  it('supports Authorization Bearer fallback and rejects invalid tokens without leaking details', async () => {
    verifier.verify.mockResolvedValueOnce({ userId: USER_ID, role: 'DRIVER' });
    const bearerSocket = createSocket(undefined, 'Bearer fallback-token');
    await runMiddleware(bearerSocket);
    expect(verifier.verify).toHaveBeenCalledWith('fallback-token');

    verifier.verify.mockRejectedValueOnce(new Error('signature details'));
    await expect(runMiddleware(createSocket({ token: 'bad-token' }))).rejects.toThrow('UNAUTHORIZED');
    await expect(runMiddleware(createSocket())).rejects.toThrow('UNAUTHORIZED');
  });

  it('emits a user-scoped payload without userId and with frontend timestamps', () => {
    gateway.publishCreated({
      id: NOTIFICATION_ID,
      userId: USER_ID,
      type: 'BOOKING_CONFIRMED',
      title: 'Đặt vé thành công',
      body: 'Vé của bạn đã được xác nhận.',
      data: null,
      action: { type: 'NONE', params: {} },
      readAt: null,
      createdAt: '2026-08-11T08:30:00.000Z',
    });

    expect(emit).toHaveBeenCalledWith('notification:created', {
      id: NOTIFICATION_ID,
      type: 'BOOKING_CONFIRMED',
      title: 'Đặt vé thành công',
      body: 'Vé của bạn đã được xác nhận.',
      data: null,
      action: { type: 'NONE', params: {} },
      readAt: null,
      createdAt: '2026-08-11T15:30:00.000+07:00',
    });
  });

  function createSocket(
    auth?: Record<string, unknown>,
    authorization?: string,
  ): Socket {
    return {
      data: {},
      handshake: {
        auth: auth ?? {},
        headers: authorization ? { authorization } : {},
      },
      join: jest.fn(async () => undefined),
    } as unknown as Socket;
  }

  function runMiddleware(socket: Socket): Promise<void> {
    return new Promise((resolve, reject) => {
      middleware(socket, (error?: Error) => {
        if (error) reject(error);
        else resolve();
      });
    });
  }
});
