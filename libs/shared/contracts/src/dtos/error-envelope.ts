/**
 * @deprecated Use `ApiResponseErrorSchema` / `ApiResponseError` from
 * `./api-response` instead.  The RFC 7807 ProblemDetails shape is superseded
 * by the unified `ApiResponse<T>` envelope per ADR 0004.
 *
 * This file is kept only as a re-export anchor so existing importers
 * (`import { ProblemDetailsSchema } from '@vietride/contracts'`) resolve
 * without a hard break.  Remove once all consumers are migrated.
 */
export {
  ApiResponseErrorSchema as ProblemDetailsSchema,
  type ApiResponseError as ProblemDetails,
} from './api-response';
