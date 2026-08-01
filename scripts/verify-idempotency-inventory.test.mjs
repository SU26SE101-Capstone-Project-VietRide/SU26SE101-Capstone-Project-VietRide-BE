import assert from 'node:assert/strict';
import test from 'node:test';
import {
  combineDotNetRoute,
  countNotificationBindingCollection,
  outboundCallsiteHasIdempotencyKey,
  scanControllerSource,
  scanDotNetOutboundHttpSource,
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

test('outbound HTTP discovery includes explicit methods and JSON convenience methods', () => {
  const callsites = scanDotNetOutboundHttpSource(`
    using var first = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/mutate");
    using var second = await client.PostAsJsonAsync("/internal/v1/read", body);
    using var third = await client.PutAsync("/internal/v1/replace", body);
    using var ignored = await client.GetAsync("/internal/v1/query");
  `);

  assert.deepEqual(
    callsites.map(({ method, style }) => ({ method, style })),
    [
      { method: 'POST', style: 'http-method' },
      { method: 'POST', style: 'json-extension' },
      { method: 'PUT', style: 'http-extension' },
    ],
  );
});

test('outbound idempotency validation is scoped to the enclosing client method', () => {
  const source = `
    public async Task SendCoveredAsync(Guid idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/v1/covered");
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey.ToString("D"));
        await client.SendAsync(request);
    }

    public async Task SendMissingAsync(Guid idempotencyKey)
    {
        await client.PostAsync("/internal/v1/missing", content);
    }
  `;
  const callsites = scanDotNetOutboundHttpSource(source);

  assert.deepEqual(
    callsites.map((callsite) => outboundCallsiteHasIdempotencyKey(source, callsite)),
    [true, false],
  );
});

test('outbound idempotency validation follows a header-owning request helper argument', () => {
  const source = `
    public async Task SendDerivedAsync(Guid operationId)
    {
        using var request = BuildRequest(
            HttpMethod.Post,
            "/internal/v1/derived",
            operationId.ToString("D"));
        await client.SendAsync(request);
    }

    private static HttpRequestMessage BuildRequest(
        HttpMethod method,
        string path,
        string idempotencyKey)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
    }
  `;
  const [callsite] = scanDotNetOutboundHttpSource(source);

  assert.equal(outboundCallsiteHasIdempotencyKey(source, callsite), true);
});

test('Notification binding collection counts mapped runtime registrations', () => {
  const source = `
    export const BINDINGS = [
      { queue: 'notification:first', routingKey: 'first.created' },
      { queue: 'notification:second', routingKey: 'second.created' },
    ] as const;
  `;

  assert.equal(countNotificationBindingCollection(source, 'BINDINGS'), 2);
  assert.equal(countNotificationBindingCollection(source, 'MISSING'), null);
});
