/**
 * @deprecated Use `ApiResponseExceptionFilter` from
 * `./api-response-exception.filter` instead.  The RFC 7807 ProblemDetails
 * shape is superseded by the unified `ApiResponse<T>` envelope per ADR 0004.
 *
 * This file is kept only as a re-export anchor so existing importers
 * (`import { ProblemDetailsExceptionFilter } from '@vietride/nest-common'`)
 * resolve without a hard break.  Remove once all consumers are migrated.
 */
export { ApiResponseExceptionFilter as ProblemDetailsExceptionFilter } from './api-response-exception.filter';
