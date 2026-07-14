import { z } from 'zod';

export const InvoiceIssuedEventSchema = z.object({
  eventId: z.string().uuid(),
  occurredAt: z.string().datetime({ offset: true }),
  invoiceId: z.string().uuid(),
  invoiceNumber: z.string().regex(/^VR-INV-\d{6}-\d{6}$/),
  operatorId: z.string().uuid(),
  amount: z.number().int().nonnegative(),
  invoiceWebUrl: z.string().url(),
  downloadApiUrl: z.string().url(),
});
export type InvoiceIssuedEvent = z.infer<typeof InvoiceIssuedEventSchema>;
export const INVOICE_ISSUED_ROUTING_KEY = 'payment.invoice.issued';
