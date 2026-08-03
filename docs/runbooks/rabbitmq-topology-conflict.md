# Runbook — RabbitMQ topology conflict (406 inequivalent arg)

> A consumer is not running because the broker refused its queue declaration.
> Background and the rule that prevents this: `docs/developer-guides/nest/nest-event-handling.md`.

## Symptoms

- `GET /ready` on notification / tracking / rag returns 503 with `failedConsumers`.
- RabbitMQ log:
  ```
  operation queue.declare caused a channel exception precondition_failed:
  inequivalent arg 'x-dead-letter-routing-key' for queue '<queue>' in vhost '/':
  received '<new key>' but current is '<old key>'
  ```
- Service log: `Topology assertion failed on queue=<queue> rk=<key> ... Consumer NOT started`.
- `/health` still returns 200 and `docker ps` still shows `(healthy)` — by design. The
  container does not restart, so container status is never the signal here.

## Cause

Durable queue arguments are immutable. A release renamed a routing key, so the argument
the code now declares no longer matches the argument the existing queue was created with.

## 1. Enumerate every conflict at once

Fixing one at a time only reveals the next. Dump all queue arguments:

```bash
docker exec vietride_rabbitmq rabbitmqctl list_queues name arguments --formatter=json | python3 -c "
import json,sys
def dlrk(a):
    if isinstance(a,dict): return a.get('x-dead-letter-routing-key')
    for it in (a or []):
        if isinstance(it,(list,tuple)) and it and it[0]=='x-dead-letter-routing-key': return it[-1]
for q in json.load(sys.stdin):
    if q['name'].endswith(('.retry','.dlq')): continue
    k=dlrk(q.get('arguments'))
    if k: print(q['name'],k)
"
```

`rabbitmqctl` renders each argument as a `[key, type, value]` triple, not a pair — a naive
`dict(...)` raises `too many values to unpack`.

Save the output to `keys.txt`, then check each key against the code:

```bash
while read -r q k; do
  grep -rqF "'$k'" apps/notification/src libs/shared/contracts/src || echo "STALE $q -> $k"
done < keys.txt
```

Only `notification:*` queues are affected. The `payment.*` / `booking.*` / `parcel.*` /
`identity.*` / `trip.*` service queues derive their dead-letter key from the queue name
(`<queue>.dead`), so event renames never touch them.

## 2. Delete the stale queues

Confirm each is empty first — normally it is, since nothing publishes the old key anymore.

```bash
docker exec vietride_rabbitmq rabbitmqctl list_queues name messages | grep '<queue>'
docker exec vietride_rabbitmq rabbitmqctl delete_queue '<queue>'
```

Delete only the main queue. `.retry` keys off the queue name (`__retry__.<queue>`) and
`.dlq` has no arguments, so neither conflicts after a routing-key rename.

## 3. Restart and verify

```bash
docker restart vietride_notification
docker exec vietride_rabbitmq rabbitmqctl list_queues name consumers arguments | grep '<queue>'
curl -fsS https://api.vietride.online/api/v1/ready
```

The queue must show the new key and at least one consumer, and `/ready` must return 200.

## Related: orphan queues after a queue rename

A renamed *queue* produces no 406 — the new name declares cleanly — but the old queue
keeps its binding and silently accumulates messages nobody consumes. Find them:

```bash
docker exec vietride_rabbitmq rabbitmqctl list_queues name consumers \
  | awk -F'\t' '$2==0 && $1 !~ /\.(dlq|retry)$/'
```

`list_queues` is TAB-separated; `grep ' 0$'` matches nothing and reads as a clean result.

Confirm the queue name no longer appears in the code (`grep -rnF "'<queue>'" apps/ libs/`
— queue names are always literals, never built from templates), then delete the queue
together with its `.retry` and `.dlq`. Deleting a queue drops its bindings too.
