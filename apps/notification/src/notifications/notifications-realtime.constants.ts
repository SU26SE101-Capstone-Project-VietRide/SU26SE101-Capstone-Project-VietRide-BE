export const NOTIFICATION_SOCKET_PATH = '/notification/socket.io';
export const NOTIFICATION_CREATED_EVENT = 'notification:created';

export function notificationUserRoom(userId: string): string {
  return `notification:user:${userId}`;
}
