import { CanActivate, ExecutionContext, Injectable } from '@nestjs/common';
import type { Request, Response } from 'express';
import { TripShareAccessService, type TripShareAccessContext } from './trip-share-access.service';

const SHARE_TOKEN_HEADER = 'x-trip-share-token';

export interface AuthorizedTripShareRequest extends Request {
  tripShareAccess: TripShareAccessContext;
}

@Injectable()
export class TripShareTokenGuard implements CanActivate {
  constructor(private readonly access: TripShareAccessService) {}

  async canActivate(context: ExecutionContext): Promise<boolean> {
    const http = context.switchToHttp();
    const request = http.getRequest<AuthorizedTripShareRequest>();
    const response = http.getResponse<Response>();
    response.setHeader('Cache-Control', 'no-store');
    response.setHeader('Pragma', 'no-cache');
    response.setHeader('Referrer-Policy', 'no-referrer');
    const header = request.headers[SHARE_TOKEN_HEADER];
    const rawToken = typeof header === 'string' ? header : undefined;
    request.tripShareAccess = await this.access.authorize(rawToken);
    return true;
  }
}
