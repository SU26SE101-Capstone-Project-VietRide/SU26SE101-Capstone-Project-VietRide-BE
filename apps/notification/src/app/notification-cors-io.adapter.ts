import type { INestApplicationContext } from '@nestjs/common';
import { IoAdapter } from '@nestjs/platform-socket.io';
import type { Server, ServerOptions } from 'socket.io';

export class NotificationCorsIoAdapter extends IoAdapter {
  constructor(
    app: INestApplicationContext,
    private readonly corsOrigin: string,
  ) {
    super(app);
  }

  override createIOServer(port: number, options?: ServerOptions): Server {
    const allowedOrigins = this.corsOrigin === '*'
      ? '*'
      : this.corsOrigin.split(',').map((origin) => origin.trim());

    return super.createIOServer(port, {
      ...options,
      cors: {
        origin: allowedOrigins,
        credentials: allowedOrigins !== '*',
      },
    });
  }
}
