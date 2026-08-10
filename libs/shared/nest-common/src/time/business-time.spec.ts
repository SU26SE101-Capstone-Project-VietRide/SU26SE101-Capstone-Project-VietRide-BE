import {
  BUSINESS_TIME_ZONE,
  formatVietnamDateTime,
  toUtcIso,
  toVietnamIso,
  transformFrontendTimestamps,
  transformUtcTimestamps,
} from './business-time';

describe('business time', () => {
  it('normalizes an explicit offset to UTC Z', () => {
    expect(toUtcIso('2026-08-10T17:00:00+07:00')).toBe('2026-08-10T10:00:00.000Z');
  });

  it('rejects a timestamp without an offset', () => {
    expect(() => toUtcIso('2026-08-10T17:00:00')).toThrow(
      'Timestamp must be RFC 3339 with Z or an explicit offset.',
    );
  });

  it.each([
    '2026-02-30T12:00:00Z',
    '2026-99-99T99:99:99Z',
    '2026-08-10 12:00:00+07:00',
  ])('rejects invalid or non-RFC3339 input %s', (value) => {
    expect(() => toUtcIso(value)).toThrow('Timestamp must be RFC 3339');
  });

  it('formats a UTC instant for Vietnamese human-readable text', () => {
    expect(BUSINESS_TIME_ZONE).toBe('Asia/Ho_Chi_Minh');
    expect(formatVietnamDateTime('2026-08-10T10:00:00Z')).toBe('17:00 ngày 10/08/2026');
  });

  it('serializes an instant with the Vietnam offset', () => {
    expect(toVietnamIso('2026-08-10T05:00:00Z')).toBe('2026-08-10T12:00:00.000+07:00');
  });

  it('converts nested frontend timestamps without mutating the source', () => {
    const source = {
      departureDateTime: '2026-08-10T05:00:00Z',
      nested: [{ updatedAt: '2026-08-10T05:30:00+00:00' }],
      date: '2026-08-10',
      time: '12:00:00',
    };

    const result = transformFrontendTimestamps(source);

    expect(result).toEqual({
      departureDateTime: '2026-08-10T12:00:00.000+07:00',
      nested: [{ updatedAt: '2026-08-10T12:30:00.000+07:00' }],
      date: '2026-08-10',
      time: '12:00:00',
    });
    expect(source.departureDateTime).toBe('2026-08-10T05:00:00Z');
  });

  it('does not rewrite timestamp-looking user text or throw for invalid text', () => {
    const source = {
      message: '2026-08-10T05:00:00Z',
      description: '2026-99-99T99:99:99Z',
      occurredAt: '2026-08-10T05:00:00Z',
    };

    expect(transformFrontendTimestamps(source)).toEqual({
      message: source.message,
      description: source.description,
      occurredAt: '2026-08-10T12:00:00.000+07:00',
    });
  });

  it('normalizes nested cache and event timestamps to UTC Z', () => {
    expect(transformUtcTimestamps({ nested: { occurredAt: '2026-08-10T12:00:00+07:00' } }))
      .toEqual({ nested: { occurredAt: '2026-08-10T05:00:00.000Z' } });
  });
});
