import assert from 'node:assert/strict';
import test from 'node:test';
import {
  combineDotNetRoute,
  scanControllerSource,
  scanNestControllerSource,
} from './verify-idempotency-inventory.mjs';

function scan(source, file) {
  const errors = [];
  const result = scanControllerSource(source, 'booking', file, [], (error) => errors.push(error));

  assert.deepEqual(errors, []);
  return result.mutations;
}

test('relative action routes combine with the controller prefix', () => {
  assert.equal(
    combineDotNetRoute(
      'v1/[controller]/{bookingId}',
      'pending-actions/{actionId}/resolve',
      'BookingsController',
    ),
    '/v1/Bookings/{bookingId}/pending-actions/{actionId}/resolve',
  );
});

test('absolute action routes replace the controller prefix', () => {
  assert.equal(
    combineDotNetRoute('v1/bookings', '/internal/v1/bookings/history', 'BookingsController'),
    '/internal/v1/bookings/history',
  );
  assert.equal(
    combineDotNetRoute('v1/bookings', '~/internal/v1/[controller]/history', 'BookingsController'),
    '/internal/v1/Bookings/history',
  );
});

test("@Controller('v1/rag') and @Post('/feedback') preserve the Nest controller prefix", () => {
  const errors = [];
  const mutations = scanNestControllerSource(
    `
      @Controller('v1/rag')
      export class RagController {
        @Post('/feedback')
        @RequireIdempotency()
        submitFeedback() {}
      }
    `,
    'rag',
    'RequireIdempotency',
    'rag.controller.ts',
    (error) => errors.push(error),
  );

  assert.deepEqual(errors, []);
  assert.deepEqual(
    mutations.map(({ actionName, path }) => ({ actionName, path })),
    [{ actionName: 'submitFeedback', path: '/v1/rag/feedback' }],
  );
});

test('NonAction mutation methods are omitted', () => {
  const mutations = scan(
    `
      [ApiController]
      [Route("v1/bookings")]
      public sealed class BookingsController : ControllerBase
      {
          [NonAction]
          [HttpPost("{bookingId}/pending-actions/{actionId}/resolve")]
          [RequireIdempotency]
          public Task<IActionResult> ResolvePendingAction() => throw new NotImplementedException();

          [HttpPost("{bookingId}/cancel")]
          [RequireIdempotency]
          public Task<IActionResult> Cancel() => throw new NotImplementedException();
      }
    `,
    'BookingsController.cs',
  );

  assert.deepEqual(
    mutations.map(({ actionName, path }) => ({ actionName, path })),
    [{ actionName: 'Cancel', path: '/v1/bookings/{bookingId}/cancel' }],
  );
});

test('pending-action endpoint is discovered exactly once across legacy and active controllers', () => {
  const legacyMutations = scan(
    `
      [ApiController]
      [Route("v1/bookings")]
      public sealed class BookingsController : ControllerBase
      {
          [NonAction]
          [HttpPost("{bookingId}/pending-actions/{actionId}/resolve")]
          [RequireIdempotency]
          public Task<IActionResult> ResolvePendingAction() => throw new NotImplementedException();
      }
    `,
    'BookingsController.cs',
  );
  const activeMutations = scan(
    `
      [ApiController]
      [Route("v1/bookings/{bookingId}/pending-actions")]
      public sealed class PendingActionsController : ControllerBase
      {
          [HttpPost("{actionId}/resolve")]
          [RequireIdempotency]
          public Task<IActionResult> Resolve() => throw new NotImplementedException();
      }
    `,
    'PendingActionsController.cs',
  );

  const pendingActionMutations = [...legacyMutations, ...activeMutations].filter(
    ({ path }) => path === '/v1/bookings/{bookingId}/pending-actions/{actionId}/resolve',
  );

  assert.equal(pendingActionMutations.length, 1);
  assert.equal(pendingActionMutations[0].actionName, 'Resolve');
});
