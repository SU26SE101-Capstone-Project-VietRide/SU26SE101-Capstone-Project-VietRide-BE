export const BUSINESS_TIME_ZONE = 'Asia/Ho_Chi_Minh';

const ISO_INSTANT_PATTERN = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d+)?(Z|[+-]\d{2}:\d{2})$/;

export function toUtcIso(value: Date | string): string {
  if (typeof value === 'string' && !isValidInstantString(value)) {
    throw new RangeError('Timestamp must be RFC 3339 with Z or an explicit offset.');
  }

  const parsed = value instanceof Date ? new Date(value.getTime()) : new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    throw new RangeError('Timestamp is not a valid ISO-8601 value.');
  }

  return parsed.toISOString();
}

export function toVietnamIso(value: Date | string): string {
  const utcIso = toUtcIso(value);
  const instant = new Date(utcIso);
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: BUSINESS_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
    timeZoneName: 'longOffset',
  }).formatToParts(instant);
  const byType = Object.fromEntries(parts.map((part) => [part.type, part.value]));
  const offset = byType['timeZoneName']?.replace(/^GMT/, '');
  if (!offset || !/^[+-]\d{2}:\d{2}$/.test(offset)) {
    throw new RangeError(`Unable to resolve offset for ${BUSINESS_TIME_ZONE}.`);
  }

  return (
    `${byType['year']}-${byType['month']}-${byType['day']}T` +
    `${byType['hour']}:${byType['minute']}:${byType['second']}.` +
    `${String(instant.getUTCMilliseconds()).padStart(3, '0')}${offset}`
  );
}

export function transformFrontendTimestamps<T>(value: T): T {
  return transformValue(value, undefined, toVietnamIso) as T;
}

export function transformUtcTimestamps<T>(value: T): T {
  return transformValue(value, undefined, toUtcIso) as T;
}

function transformValue(
  value: unknown,
  propertyName: string | undefined,
  formatter: (value: Date | string) => string,
): unknown {
  if (value instanceof Date) return formatter(value);
  if (typeof value === 'string') {
    return isInstantPropertyName(propertyName) && isValidInstantString(value)
      ? formatter(value)
      : value;
  }
  if (Array.isArray(value)) {
    return value.map((item) => transformValue(item, propertyName, formatter));
  }
  if (!isPlainRecord(value)) return value;

  return Object.fromEntries(
    Object.entries(value).map(([key, item]) => [key, transformValue(item, key, formatter)]),
  );
}

function isInstantPropertyName(propertyName: string | undefined): boolean {
  if (!propertyName) return false;
  return ![
    'message',
    'description',
    'title',
    'body',
    'content',
    'note',
    'reason',
    'text',
  ].includes(propertyName.toLowerCase());
}

function isValidInstantString(value: string): boolean {
  const match = ISO_INSTANT_PATTERN.exec(value);
  if (!match) return false;

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const hour = Number(match[4]);
  const minute = Number(match[5]);
  const second = Number(match[6]);
  if (
    month < 1 || month > 12 ||
    day < 1 || day > daysInMonth(year, month) ||
    hour > 23 || minute > 59 || second > 59
  ) {
    return false;
  }

  const offset = match[7];
  if (!offset) return false;
  if (offset !== 'Z') {
    const offsetHour = Number(offset.slice(1, 3));
    const offsetMinute = Number(offset.slice(4, 6));
    if (offsetHour > 23 || offsetMinute > 59) return false;
  }

  return !Number.isNaN(new Date(value).getTime());
}

function daysInMonth(year: number, month: number): number {
  if (month === 2) {
    return year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0) ? 29 : 28;
  }
  return [4, 6, 9, 11].includes(month) ? 30 : 31;
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  if (value === null || typeof value !== 'object') return false;
  const prototype = Object.getPrototypeOf(value);
  return prototype === Object.prototype || prototype === null;
}

export function formatVietnamDateTime(value: Date | string): string {
  const utcIso = toUtcIso(value);
  const parts = new Intl.DateTimeFormat('vi-VN', {
    timeZone: BUSINESS_TIME_ZONE,
    hour: '2-digit',
    minute: '2-digit',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hourCycle: 'h23',
  }).formatToParts(new Date(utcIso));
  const byType = Object.fromEntries(parts.map((part) => [part.type, part.value]));

  return `${byType['hour']}:${byType['minute']} ngày ${byType['day']}/${byType['month']}/${byType['year']}`;
}
